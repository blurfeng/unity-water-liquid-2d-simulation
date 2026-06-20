using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体碰撞体注册表。所有启用的 <see cref="Liquid2DCollider"/> 在此登记；<see cref="Liquid2DSimulation"/> 每帧调用
    /// <see cref="BuildBuffer"/> 把它们扁平化为 Burst 可用的 <see cref="Liquid2DColliderBuffer"/>，并收集动态体的力接收者。
    /// Fluid collider registry. All enabled <see cref="Liquid2DCollider"/> register here; <see cref="Liquid2DSimulation"/>
    /// calls <see cref="BuildBuffer"/> each frame to flatten them into a Burst-usable <see cref="Liquid2DColliderBuffer"/>
    /// and collect dynamic bodies' force receivers.
    /// 流体コライダーレジストリ。有効な <see cref="Liquid2DCollider"/> をここに登録し、<see cref="Liquid2DSimulation"/> が
    /// 毎フレーム <see cref="BuildBuffer"/> で Burst 用バッファに平坦化し、動的体の力レシーバーを収集します。
    /// </summary>
    public static class Liquid2DColliderRegistry
    {
        private static readonly List<Liquid2DCollider> _active = new List<Liquid2DCollider>();

        // 重用的托管暂存，避免每帧 GC。 // Reused managed scratch to avoid per-frame GC. // 毎フレーム GC を避ける再利用バッファ。
        private static readonly List<Liquid2DColliderData> _dataScratch = new List<Liquid2DColliderData>(32);
        private static readonly List<float2> _pointScratch = new List<float2>(128);
        // 力接收者 → bodyIndex 去重：同一刚体（含多个子碰撞体）只占一个 bodyIndex，冲量汇总到同一条。
        // Force receiver → bodyIndex dedup: one rigidbody (with multiple child colliders) takes a single bodyIndex.
        // 力レシーバー → bodyIndex 去重：同一剛体は 1 つの bodyIndex を共有。
        private static readonly Dictionary<ILiquid2DForceReceiver, int> _receiverToBody =
            new Dictionary<ILiquid2DForceReceiver, int>();

        /// <summary>当前注册的碰撞体数。 // Number of registered colliders. // 登録コライダー数。</summary>
        public static int Count => _active.Count;

        public static void Register(Liquid2DCollider collider)
        {
            if (collider && !_active.Contains(collider)) _active.Add(collider);
        }

        public static void Unregister(Liquid2DCollider collider)
        {
            _active.Remove(collider);
        }

        /// <summary>
        /// 把当前所有碰撞体扁平化为 NativeArray 缓冲，并按顺序收集动态体力接收者（bodyIndex 即其在列表中的下标）。
        /// 返回的缓冲由调用方负责释放（建议 Allocator.TempJob）。
        /// Flatten all current colliders into NativeArray buffers and collect dynamic force receivers in order (bodyIndex
        /// is its list index). The caller owns the returned buffers (Allocator.TempJob recommended).
        /// 全コライダーを NativeArray バッファに平坦化し、動的体レシーバーを順に収集します。バッファは呼び出し側が解放。
        /// </summary>
        public static Liquid2DColliderBuffer BuildBuffer(Allocator allocator,
            List<ILiquid2DForceReceiver> dynamicReceivers, Func<string, int> groupResolver)
        {
            _dataScratch.Clear();
            _pointScratch.Clear();
            dynamicReceivers.Clear();
            _receiverToBody.Clear();

            for (int i = 0; i < _active.Count; i++)
            {
                var c = _active[i];
                if (!c || !c.isActiveAndEnabled) continue;

                // FillAll 支持多形状碰撞体（如 Liquid2DCustomCollider）；单形状碰撞体走默认实现调用 Fill()。
                // FillAll supports multi-shape colliders (e.g. Liquid2DCustomCollider); single-shape colliders use
                // the default implementation which calls Fill().
                // FillAll は多形状コライダーをサポート。単形状は Fill() を呼ぶデフォルト実装を使用。
                int startIdx = _dataScratch.Count;
                c.FillAll(_dataScratch, _pointScratch);
                int addedCount = _dataScratch.Count - startIdx;
                if (addedCount == 0) continue;

                // 同一碰撞体的所有子形状共享 dynamic/bodyIndex。bodyIndex 按力接收者去重：同一刚体的多个碰撞体
                // 复用同一条，冲量汇总到一起，避免重复施力。
                // All sub-shapes share dynamic/bodyIndex. bodyIndex is deduped by force receiver: multiple colliders of the
                // same rigidbody reuse one entry so impulse sums together (no double application).
                // サブ形状は dynamic/bodyIndex を共有。bodyIndex はレシーバーで去重。
                byte dynFlag = 0;
                int bodyIdx = -1;
                var receiver = c.IsDynamic ? c.ForceReceiver : null;
                if (receiver != null)
                {
                    dynFlag = 1;
                    if (!_receiverToBody.TryGetValue(receiver, out bodyIdx))
                    {
                        bodyIdx = dynamicReceivers.Count;
                        dynamicReceivers.Add(receiver);
                        _receiverToBody[receiver] = bodyIdx;
                    }
                }

                // nameTag → groupId 组过滤：空 nameTag 作用于全部（matchAll），否则仅作用于匹配组。
                // nameTag → groupId filter: empty nameTag affects all (matchAll); otherwise only the matching group.
                // nameTag → groupId 絞り込み：空は全作用、それ以外は一致グループのみ。
                string tag = c.NameTag;
                bool matchAll = string.IsNullOrEmpty(tag);
                int group = matchAll || groupResolver == null ? 0 : groupResolver(tag);

                for (int j = startIdx; j < _dataScratch.Count; j++)
                {
                    var d = _dataScratch[j];
                    d.Dynamic = dynFlag;
                    d.BodyIndex = bodyIdx;
                    d.GroupId = group;
                    d.MatchAll = (byte)(matchAll ? 1 : 0);
                    _dataScratch[j] = d;
                }
            }

            var buffer = new Liquid2DColliderBuffer();
            int n = _dataScratch.Count;
            buffer.Colliders = new NativeArray<Liquid2DColliderData>(n, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++) buffer.Colliders[i] = _dataScratch[i];

            int pcount = math.max(1, _pointScratch.Count); // 至少 1 长度避免零长 NativeArray 边角。 // at least length 1. // 最低長さ 1。
            buffer.Points = new NativeArray<float2>(pcount, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < _pointScratch.Count; i++) buffer.Points[i] = _pointScratch[i];

            return buffer;
        }
    }
}

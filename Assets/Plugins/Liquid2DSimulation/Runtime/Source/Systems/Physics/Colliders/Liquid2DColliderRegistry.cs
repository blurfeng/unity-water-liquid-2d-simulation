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
        public static Liquid2DColliderBuffer BuildBuffer(Allocator allocator, List<ILiquid2DForceReceiver> dynamicReceivers)
        {
            _dataScratch.Clear();
            _pointScratch.Clear();
            dynamicReceivers.Clear();

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

                // 同一碰撞体的所有子形状共享 dynamic/bodyIndex（同属一个物理体）。
                // All sub-shapes of the same collider share dynamic/bodyIndex (they belong to one physical body).
                // 同じコライダーのサブ形状は dynamic/bodyIndex を共有します。
                byte dynFlag = 0;
                int bodyIdx = -1;
                if (c.IsDynamic)
                {
                    dynFlag = 1;
                    bodyIdx = dynamicReceivers.Count;
                    dynamicReceivers.Add(c.ForceReceiver); // 可能为 null（seam，冲量丢弃）。 // may be null. // null 可。
                }

                for (int j = startIdx; j < _dataScratch.Count; j++)
                {
                    var d = _dataScratch[j];
                    d.Dynamic = dynFlag;
                    d.BodyIndex = bodyIdx;
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

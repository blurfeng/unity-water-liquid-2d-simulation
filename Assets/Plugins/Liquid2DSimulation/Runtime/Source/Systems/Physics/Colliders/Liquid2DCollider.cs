using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 自研 2D 流体碰撞体基类。碰撞体数量少、需在场景中编辑，故保留为 MonoBehaviour（与纯数据粒子相反）。
    /// 启用时注册到 <see cref="Liquid2DColliderRegistry"/>，由 <see cref="Liquid2DSimulation"/> 每帧扁平化为 Burst 缓冲供求解器投影。
    /// Base class for the custom 2D fluid colliders. Colliders are few and authored in scenes, so they stay as
    /// MonoBehaviours (unlike pure-data particles). On enable they register into <see cref="Liquid2DColliderRegistry"/>;
    /// <see cref="Liquid2DSimulation"/> flattens them into a Burst buffer each frame for the solver to project against.
    /// 自作 2D 流体コライダーの基底クラス。コライダーは数が少なくシーンで編集するため MonoBehaviour のまま。
    /// </summary>
    public abstract class Liquid2DCollider : MonoBehaviour
    {
        [SerializeField, LocalizationTooltip(
             "作用的粒子组标签。留空=作用于全部粒子；填写后仅阻挡 nameTag 匹配的粒子（其它粒子穿过）。",
             "Particle group tag to act on. Empty = affects all particles; set = only blocks particles whose nameTag matches (others pass through).",
             "作用する粒子グループのタグ。空=全粒子に作用、設定時は nameTag 一致の粒子のみ阻止（他は通過）。")]
        private string nameTag = string.Empty;

        protected Transform CachedTransform;

        // 缓存的力接收者（同物体或父级，OnEnable 解析）。 // Cached force receiver (same object or parent; resolved on enable). // 力レシーバーのキャッシュ。
        private ILiquid2DForceReceiver _forceReceiver;

        /// <summary>作用的粒子组标签（空=作用于全部）。 // Particle group tag to act on (empty = all). // 作用する粒子グループのタグ（空=全部）。</summary>
        public string NameTag => nameTag;

        /// <summary>
        /// 是否动态（参与双向耦合）：当同物体或父级挂有 <see cref="ILiquid2DForceReceiver"/>（如
        /// <see cref="Liquid2DRigidbodyBridge"/>）时为 true。
        /// Whether dynamic (two-way coupling): true when an <see cref="ILiquid2DForceReceiver"/> (e.g.
        /// <see cref="Liquid2DRigidbodyBridge"/>) exists on the same object or a parent.
        /// 動的か（双方向）：同オブジェクトまたは親に <see cref="ILiquid2DForceReceiver"/> がある場合 true。
        /// </summary>
        public bool IsDynamic => _forceReceiver != null;

        /// <summary>形状类型。 // Shape type. // 形状タイプ。</summary>
        public abstract Liquid2DColliderShape Shape { get; }

        /// <summary>
        /// 确保 <see cref="CachedTransform"/> 已初始化（编辑器/未 OnEnable 时安全调用，供 <see cref="Fill"/> / 面积计算使用）。
        /// Ensure <see cref="CachedTransform"/> is initialized (safe in editor / before OnEnable, for <see cref="Fill"/> / area).
        /// <see cref="CachedTransform"/> の初期化を保証（エディタ/ OnEnable 前でも安全）。
        /// </summary>
        public void EnsureInitialized()
        {
            if (!CachedTransform) CachedTransform = transform;
        }

        protected virtual void OnEnable()
        {
            CachedTransform = transform;
            _forceReceiver = GetComponentInParent<ILiquid2DForceReceiver>();
            Liquid2DColliderRegistry.Register(this);
        }

        protected virtual void OnDisable()
        {
            Liquid2DColliderRegistry.Unregister(this);
        }

        /// <summary>
        /// 把当前世界状态写入 blittable 描述；多边形/边链把世界顶点追加到 pointsAccum 并设置 pointStart/pointCount。
        /// Write the current world state into the blittable description; polygon/edge chain append world vertices to
        /// pointsAccum and set pointStart/pointCount.
        /// 現在のワールド状態を blittable 記述に書き込む。多角形/エッジは頂点を pointsAccum に追加。
        /// </summary>
        public abstract void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum);

        /// <summary>
        /// 将本碰撞体的所有子形状写入输出列表。默认实现调用 <see cref="Fill"/> 写入单个条目；包含多形状的碰撞体
        /// （如 <c>Liquid2DCustomCollider</c>）应重写此方法以写入多个 <see cref="Liquid2DColliderData"/> 条目。
        /// Write all sub-shapes of this collider into the output list. The default calls <see cref="Fill"/> once for a
        /// single entry; multi-shape colliders (e.g. <c>Liquid2DCustomCollider</c>) should override to append multiple
        /// <see cref="Liquid2DColliderData"/> entries.
        /// コライダーの全サブ形状を出力リストに書き込みます。デフォルトは <see cref="Fill"/> を 1 回呼ぶ。
        /// 多形状コライダー（例 <c>Liquid2DCustomCollider</c>）はこのメソッドをオーバーライドしてください。
        /// </summary>
        public virtual void FillAll(List<Liquid2DColliderData> dataOut, List<float2> pointsAccum)
        {
            var data = new Liquid2DColliderData();
            Fill(ref data, pointsAccum);
            dataOut.Add(data);
        }

        /// <summary>
        /// 碰撞体的力接收者（同物体或父级上的 <see cref="ILiquid2DForceReceiver"/>，OnEnable 缓存；为 null 时碰撞体为静态）。
        /// Force receiver of this collider (an <see cref="ILiquid2DForceReceiver"/> on the same object or a parent, cached on
        /// enable; the collider is static when null).
        /// コライダーの力レシーバー（同オブジェクトまたは親の実装、OnEnable でキャッシュ。null なら静的）。
        /// </summary>
        public virtual ILiquid2DForceReceiver ForceReceiver => _forceReceiver;

        protected float ZRotationRadians => CachedTransform.eulerAngles.z * Mathf.Deg2Rad;
        protected float2 WorldCenter => new float2(CachedTransform.position.x, CachedTransform.position.y);
    }
}

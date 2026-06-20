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
             "是否动态：动态碰撞体接收流体反作用冲量（双向耦合），可用于被水流推动的物体。",
             "Whether dynamic: dynamic colliders receive fluid reaction impulse (two-way coupling), for objects pushed by flow.",
             "動的かどうか：動的コライダーは流体の反作用力積を受け取り（双方向）、水流に押される物体に使えます。")]
        private bool isDynamic;

        protected Transform CachedTransform;

        /// <summary>是否动态（参与双向耦合）。 // Whether dynamic (two-way coupling). // 動的か。</summary>
        public bool IsDynamic => isDynamic;

        /// <summary>形状类型。 // Shape type. // 形状タイプ。</summary>
        public abstract Liquid2DColliderShape Shape { get; }

        protected virtual void OnEnable()
        {
            CachedTransform = transform;
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
        /// 动态碰撞体的力接收者（默认取同物体上的 <see cref="ILiquid2DForceReceiver"/>，可能为 null）。
        /// Force receiver for a dynamic collider (defaults to an <see cref="ILiquid2DForceReceiver"/> on the same object; may be null).
        /// 動的コライダーの力レシーバー（既定は同オブジェクトの実装、null 可）。
        /// </summary>
        public virtual ILiquid2DForceReceiver ForceReceiver => isDynamic ? GetComponent<ILiquid2DForceReceiver>() : null;

        protected float ZRotationRadians => CachedTransform.eulerAngles.z * Mathf.Deg2Rad;
        protected float2 WorldCenter => new float2(CachedTransform.position.x, CachedTransform.position.y);
    }
}

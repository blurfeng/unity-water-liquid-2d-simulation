using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体销毁区域。位于此区域内的流体粒子会被销毁（从模拟中移除）。取代旧的基于 Unity 物理触发器 + LayerMask 的实现，
    /// 适配纯数据 SPH 流体系统：启用时注册到 <see cref="Liquid2DDeadZoneRegistry"/>，由 <see cref="Liquid2DSimulation"/>
    /// 每帧在求解后回收落入区域的粒子（CPU/GPU 双模式）。形状复用碰撞体的 <see cref="Liquid2DColliderShape"/> 与判定数学。
    /// Fluid dead zone. Fluid particles inside this region are destroyed (removed from the simulation). Replaces the old
    /// Unity-physics-trigger + LayerMask implementation to fit the pure-data SPH system: on enable it registers into
    /// <see cref="Liquid2DDeadZoneRegistry"/>; <see cref="Liquid2DSimulation"/> recycles particles that fall inside each
    /// frame after solving (CPU/GPU). Shapes reuse the collider <see cref="Liquid2DColliderShape"/> and projection math.
    /// 流体破棄領域。この領域内の流体粒子は破棄されます。旧 Unity 物理トリガー + LayerMask 実装を置き換え、純データ
    /// SPH システムに適応。有効時に <see cref="Liquid2DDeadZoneRegistry"/> へ登録します。
    ///
    /// Bounds 模式（<see cref="BoundsMode"/>）反转判定：销毁形状【外】的粒子，使区域成为容器边界（类比
    /// <see cref="Liquid2DBounds"/> 之于碰撞体），常用于回收飞出play区域的粒子。
    /// Bounds mode (<see cref="BoundsMode"/>) inverts the test: destroys particles OUTSIDE the shape, turning the region
    /// into a containment boundary (analogous to <see cref="Liquid2DBounds"/> vs a collider) — handy for recycling escapees.
    /// Bounds モード（<see cref="BoundsMode"/>）は判定を反転し、形状の【外】の粒子を破棄します。
    /// </summary>
    [AddComponentMenu("Liquid 2D/Gameplay/Liquid 2D Dead Zone")]
    public class Liquid2DDeadZone : MonoBehaviour
    {
        /// <summary>
        /// 销毁区域支持的形状（实心形状子集）。 // Solid shapes supported by a dead zone. // 破棄領域がサポートする形状。
        /// </summary>
        public enum DeadZoneShape
        {
            /// <summary>有向矩形盒。 // Oriented box. // 有向ボックス。</summary>
            Box = 0,
            /// <summary>圆。 // Circle. // 円。</summary>
            Circle = 1,
            /// <summary>胶囊。 // Capsule. // カプセル。</summary>
            Capsule = 2,
            /// <summary>凸多边形。 // Convex polygon. // 凸多角形。</summary>
            Polygon = 3,
        }

        [SerializeField, LocalizationTooltip(
             "销毁区域的形状。",
             "Shape of the dead zone.",
             "破棄領域の形状。")]
        private DeadZoneShape shape = DeadZoneShape.Box;

        [SerializeField, LocalizationTooltip(
             "Bounds 模式：开启后销毁形状【外】的粒子（区域作为容器边界，逃逸的粒子被回收）；关闭则销毁形状【内】的粒子。",
             "Bounds mode: when on, destroys particles OUTSIDE the shape (the region acts as a containment boundary; escaping particles are recycled); when off, destroys particles INSIDE the shape.",
             "Bounds モード：オンで形状の【外】の粒子を破棄（領域を容器境界として扱い、逃げた粒子を回収）、オフで形状の【内】を破棄。")]
        private bool boundsMode;

        [SerializeField, LocalizationTooltip(
             "盒尺寸（全尺寸，受物体缩放影响）。仅 Box 形状使用。",
             "Box size (full size, scaled by transform). Used by Box shape only.",
             "ボックスサイズ（全サイズ、スケール影響）。Box 形状のみ使用。")]
        private Vector2 size = new Vector2(2f, 2f);

        [SerializeField, Min(0f), LocalizationTooltip(
             "半径（受物体缩放影响）。Circle / Capsule 形状使用。",
             "Radius (scaled by transform). Used by Circle / Capsule shapes.",
             "半径（スケール影響）。Circle / Capsule 形状で使用。")]
        private float radius = 1f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "胶囊线段全长（沿局部 X 轴）。仅 Capsule 形状使用。",
             "Capsule segment full length (along local X). Used by Capsule shape only.",
             "カプセル線分の全長（ローカル X 軸）。Capsule 形状のみ使用。")]
        private float length = 2f;

        [SerializeField, LocalizationTooltip(
             "局部顶点（按顺序，凸多边形）。仅 Polygon 形状使用。",
             "Local vertices (in order, convex polygon). Used by Polygon shape only.",
             "ローカル頂点（順序通り、凸多角形）。Polygon 形状のみ使用。")]
        private Vector2[] points;

        [SerializeField, LocalizationTooltip(
             "过滤的目标 nameTag。留空 = 销毁区域内全部流体粒子；填写 = 仅销毁该 nameTag 的流体粒子。",
             "Target nameTag filter. Empty = destroy all fluid particles in range; set = only destroy particles with this nameTag.",
             "絞り込み対象 nameTag。空 = 範囲内のすべての流体粒子を破棄、指定 = その nameTag の粒子のみ破棄。")]
        private string nameTag = string.Empty;

        private Transform _cachedTransform;

        /// <summary>过滤的目标 nameTag（空 = 全部）。 // Target nameTag filter (empty = all). // 絞り込み対象 nameTag。</summary>
        public string NameTag => nameTag;

        /// <summary>Bounds 模式：销毁形状外的粒子（容器边界）。 // Bounds mode: destroy particles outside the shape (containment). // Bounds モード：形状外を破棄。</summary>
        public bool BoundsMode => boundsMode;

        private float2 WorldCenter => new float2(_cachedTransform.position.x, _cachedTransform.position.y);
        private float ZRotationRadians => _cachedTransform.eulerAngles.z * Mathf.Deg2Rad;

        private void OnEnable()
        {
            _cachedTransform = transform;
            Liquid2DDeadZoneRegistry.Register(this);
        }

        private void OnDisable()
        {
            Liquid2DDeadZoneRegistry.Unregister(this);
        }

        /// <summary>
        /// 把当前世界状态写入 blittable 形状描述；多边形把世界顶点追加到 pointsAccum 并设置 pointStart/pointCount。
        /// 写法对齐对应的 <see cref="Liquid2DCollider"/> 子类（Box/Circle/Capsule/Polygon）。
        /// Write the current world state into the blittable shape description; polygon appends world vertices to
        /// pointsAccum and sets pointStart/pointCount. Mirrors the matching <see cref="Liquid2DCollider"/> subclasses.
        /// 現在のワールド状態を blittable 形状記述に書き込みます。
        /// </summary>
        public void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum)
        {
            Vector3 ls = _cachedTransform.lossyScale;
            float scale = Mathf.Abs(ls.x);

            switch (shape)
            {
                case DeadZoneShape.Circle:
                    data.Shape = Liquid2DColliderShape.Circle;
                    data.Center = WorldCenter;
                    data.Radius = radius * scale;
                    break;

                case DeadZoneShape.Capsule:
                    data.Shape = Liquid2DColliderShape.Capsule;
                    data.Center = WorldCenter;
                    data.Rotation = ZRotationRadians;
                    data.Size = new float2(0.5f * length * scale, 0f);
                    data.Radius = radius * scale;
                    break;

                case DeadZoneShape.Polygon:
                    data.Shape = Liquid2DColliderShape.Polygon;
                    data.Center = WorldCenter;
                    data.PointStart = pointsAccum.Count;
                    int n = points?.Length ?? 0;
                    if (points != null)
                    {
                        for (int i = 0; i < n; i++)
                        {
                            Vector3 w = _cachedTransform.TransformPoint(points[i]);
                            pointsAccum.Add(new float2(w.x, w.y));
                        }
                    }
                    data.PointCount = n;
                    break;

                default: // Box
                    data.Shape = Liquid2DColliderShape.Box;
                    data.Center = WorldCenter;
                    data.Rotation = ZRotationRadians;
                    data.Size = new float2(0.5f * size.x * Mathf.Abs(ls.x), 0.5f * size.y * Mathf.Abs(ls.y));
                    break;
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            // 销毁区域用醒目的红色线框；Bounds 模式（销毁区域外）改用橙色以区分。
            // Dead zones use a striking red wireframe; bounds mode (kill-outside) uses orange to distinguish.
            // 破棄領域は赤の枠線。Bounds モード（外側破棄）はオレンジで区別。
            Gizmos.color = boundsMode ? new Color(1f, 0.55f, 0.1f, 0.8f) : new Color(1f, 0.2f, 0.2f, 0.7f);

            switch (shape)
            {
                case DeadZoneShape.Circle:
                {
                    float scale = Mathf.Abs(transform.lossyScale.x);
                    Gizmos.DrawWireSphere(transform.position, radius * scale);
                    break;
                }
                case DeadZoneShape.Capsule:
                {
                    Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one * Mathf.Abs(transform.lossyScale.x));
                    float hl = 0.5f * length;
                    Gizmos.DrawWireSphere(new Vector3(-hl, 0f, 0f), radius);
                    Gizmos.DrawWireSphere(new Vector3(hl, 0f, 0f), radius);
                    Gizmos.DrawLine(new Vector3(-hl, radius, 0f), new Vector3(hl, radius, 0f));
                    Gizmos.DrawLine(new Vector3(-hl, -radius, 0f), new Vector3(hl, -radius, 0f));
                    Gizmos.matrix = Matrix4x4.identity;
                    break;
                }
                case DeadZoneShape.Polygon:
                {
                    if (points == null || points.Length < 2) break;
                    int n = points.Length;
                    for (int i = 0; i < n; i++)
                    {
                        Vector3 a = transform.TransformPoint(points[i]);
                        Vector3 b = transform.TransformPoint(points[(i + 1) % n]);
                        Gizmos.DrawLine(a, b);
                    }
                    break;
                }
                default: // Box
                {
                    Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
                    Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0f));
                    Gizmos.matrix = Matrix4x4.identity;
                    break;
                }
            }
        }
#endif
    }
}

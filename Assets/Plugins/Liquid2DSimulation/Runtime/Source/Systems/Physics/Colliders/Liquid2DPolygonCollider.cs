using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 多边形 / 边线段链流体碰撞体。closed=true 为凸多边形（实心，推粒子到外侧）；
    /// closed=false 为开放折线（薄壁双面阻挡，做复杂地形轮廓）。顶点为局部坐标，经物体 TRS 变换到世界。
    /// Polygon / edge-chain fluid collider. closed=true is a convex polygon (solid, pushes particles outward);
    /// closed=false is an open polyline (two-sided thin barrier for terrain outlines). Vertices are local and
    /// transformed to world by the object's TRS.
    /// 多角形 / エッジチェーン流体コライダー。closed=true は凸多角形（中実）、false は開いた折れ線（両面薄壁）。
    /// </summary>
    [AddComponentMenu("Liquid2D/Colliders/Liquid2D Polygon Collider")]
    public class Liquid2DPolygonCollider : Liquid2DCollider
    {
        [LocalizationTooltip(
             "闭合：作为凸多边形实心阻挡。非闭合：作为开放折线薄壁双面阻挡。",
             "Closed: solid convex polygon. Open: two-sided thin polyline barrier.",
             "閉じる：中実の凸多角形。開く：両面の薄い折れ線バリア。")]
        public bool closed = true;

        [LocalizationTooltip("局部顶点（按顺序）。", "Local vertices (in order).", "ローカル頂点（順序通り）。")]
        public Vector2[] points = { new Vector2(-0.5f, -0.5f), new Vector2(0.5f, -0.5f), new Vector2(0.5f, 0.5f), new Vector2(-0.5f, 0.5f) };

        public override Liquid2DColliderShape Shape => closed ? Liquid2DColliderShape.Polygon : Liquid2DColliderShape.EdgeChain;

        public override void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum)
        {
            data.shape = Shape;
            data.center = WorldCenter;
            data.pointStart = pointsAccum.Count;

            int n = points != null ? points.Length : 0;
            for (int i = 0; i < n; i++)
            {
                Vector3 w = CachedTransform.TransformPoint(points[i]);
                pointsAccum.Add(new float2(w.x, w.y));
            }
            data.pointCount = n;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (points == null || points.Length < 2) return;
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            int n = points.Length;
            int last = closed ? n : n - 1;
            for (int i = 0; i < last; i++)
            {
                Vector3 a = transform.TransformPoint(points[i]);
                Vector3 b = transform.TransformPoint(points[(i + 1) % n]);
                Gizmos.DrawLine(a, b);
            }
        }
#endif
    }
}

using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 基于 <see cref="EdgeCollider2D"/> 的流体边线碰撞体。在 Unity 编辑器中通过 EdgeCollider2D 组件的场景视图工具
    /// 可视化编辑边线路径；在非编辑器（打包）模式下，Awake 时自动移除 EdgeCollider2D 以避免干扰 Unity 内置物理。
    /// Fluid edge collider backed by <see cref="EdgeCollider2D"/>. The edge path is visually edited via EdgeCollider2D's
    /// scene-view gizmo in the Unity Editor; in non-editor (built) mode the EdgeCollider2D is removed during Awake to
    /// avoid interfering with Unity's built-in physics.
    /// <see cref="EdgeCollider2D"/> を使った流体エッジコライダー。エッジパスはエディター内で EC2D のシーンビューツールで編集。
    /// ビルド時は Awake で EdgeCollider2D を自動削除し、Unity 組み込み物理との干渉を防ぎます。
    /// </summary>
    [AddComponentMenu("Liquid2D/Colliders/Liquid2D Edge Collider")]
    [RequireComponent(typeof(EdgeCollider2D))]
    public class Liquid2DEdgeCollider : Liquid2DCollider
    {
        // 运行时缓存（从 EdgeCollider2D 复制）。 // Runtime cache (copied from EdgeCollider2D). // 実行時キャッシュ（EdgeCollider2D からコピー）。
        private Vector2[] _cachedPoints;
        private float _cachedEdgeRadius;

        public override Liquid2DColliderShape Shape => Liquid2DColliderShape.EdgeChain;

        private void Awake()
        {
            CachePointsFromEdge();

#if !UNITY_EDITOR
            // 打包后移除 EdgeCollider2D，避免其参与 Unity 物理模拟。
            // Remove EdgeCollider2D in builds to prevent it from participating in Unity physics simulation.
            // ビルド後は EdgeCollider2D を削除し、Unity の物理シミュレーションへの参加を防ぎます。
            var ec = GetComponent<EdgeCollider2D>();
            if (ec) Destroy(ec);
#endif
        }

        protected override void OnEnable()
        {
#if UNITY_EDITOR
            // 编辑器模式每次启用时重新读取，支持在 Play 模式下实时修改 EdgeCollider2D 顶点。
            // Re-cache in editor on each enable so EdgeCollider2D edits during Play are reflected immediately.
            // エディターでは毎回有効化時に再読み込みし、Play 中の EC2D 編集をリアルタイムに反映します。
            CachePointsFromEdge();
#endif
            base.OnEnable();
        }

        private void CachePointsFromEdge()
        {
            var ec = GetComponent<EdgeCollider2D>();
            if (!ec) return;
            _cachedPoints = ec.points;
            _cachedEdgeRadius = ec.edgeRadius;
        }

        public override void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum)
        {
            data.Shape = Liquid2DColliderShape.EdgeChain;
            data.Center = WorldCenter;
            data.PointStart = pointsAccum.Count;

#if UNITY_EDITOR
            // 编辑器中直接读，以响应顶点拖拽和 edgeRadius 修改。 // Read directly in editor to reflect vertex/radius edits live.
            var ec = GetComponent<EdgeCollider2D>();
            var pts = ec ? ec.points : _cachedPoints;
            var edgeRadius = ec ? ec.edgeRadius : _cachedEdgeRadius;
#else
            pts = _cachedPoints;
            edgeRadius = _cachedEdgeRadius;
#endif
            data.Radius = edgeRadius;

            int n = pts?.Length ?? 0;
            if (pts != null)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 w = CachedTransform.TransformPoint(pts[i]);
                    pointsAccum.Add(new float2(w.x, w.y));
                }
            }

            data.PointCount = n;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var ec = GetComponent<EdgeCollider2D>();
            Vector2[] pts = ec ? ec.points : _cachedPoints;
            if (pts == null || pts.Length < 2) return;
            Gizmos.color = new Color(1f, 0.7f, 0.2f, 0.6f);
            for (int i = 0; i < pts.Length - 1; i++)
            {
                Vector3 a = transform.TransformPoint(pts[i]);
                Vector3 b = transform.TransformPoint(pts[i + 1]);
                Gizmos.DrawLine(a, b);
            }
        }
#endif
    }
}

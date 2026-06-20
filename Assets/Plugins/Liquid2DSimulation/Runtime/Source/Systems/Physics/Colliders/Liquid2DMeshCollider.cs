using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 基于自定义 Mesh 的流体碰撞体。提取 Mesh 外轮廓边（仅属于单个三角形的边）并组装为有序环，
    /// 当存在多个环时取顶点数最多的环。closed=true 为实心多边形阻挡，false 为开放薄壁边链。
    /// Fluid collider based on a custom Mesh. Extracts the mesh's outer boundary edges (edges belonging to exactly
    /// one triangle) and assembles them into an ordered loop; when multiple loops exist, the longest is used.
    /// closed=true = solid polygon; false = open thin-wall edge chain.
    /// カスタム Mesh を使った流体コライダー。外輪郭エッジ（1三角形のみに属するエッジ）を抽出し有序ループに組み上げます。
    /// 複数ループ時は最長を使用。closed=true は中実多角形、false は開いた薄壁エッジチェーン。
    /// </summary>
    [AddComponentMenu("Liquid 2D/Colliders/Liquid 2D Mesh Collider")]
    public class Liquid2DMeshCollider : Liquid2DCollider
    {
        [SerializeField, LocalizationTooltip(
             "用于碰撞的 Mesh（XY 平面投影，取最长外轮廓环）。",
             "Mesh used for collision (XY-projected; longest outer boundary loop is used).",
             "衝突に使用する Mesh（XY 投影、最長の外輪郭ループを使用）。")]
        private Mesh mesh;

        [SerializeField, LocalizationTooltip(
             "闭合：作为实心多边形阻挡。非闭合：作为开放薄壁边链阻挡。",
             "Closed: solid polygon barrier. Open: two-sided thin edge-chain barrier.",
             "閉じる：中実多角形バリア。開く：両面薄壁エッジチェーン。")]
        private bool closed = true;

        private Vector2[] _cachedLocalPoints;

        public override Liquid2DColliderShape Shape => closed ? Liquid2DColliderShape.Polygon : Liquid2DColliderShape.EdgeChain;

        protected override void OnEnable()
        {
            RefreshBoundary();
            base.OnEnable();
        }

        /// <summary>
        /// 重新从 Mesh 提取外轮廓。更换 Mesh 引用后需手动调用。
        /// Re-extract boundary from the Mesh. Call manually after changing the Mesh reference.
        /// Mesh 変更後に手動で呼び出して外輪郭を再抽出します。
        /// </summary>
        public void RefreshBoundary()
        {
            _cachedLocalPoints = mesh ? ExtractLongestBoundaryLoop(mesh) : null;
            if (mesh && (_cachedLocalPoints == null || _cachedLocalPoints.Length < 2))
                Debug.LogWarning($"[Liquid2DMeshCollider] '{name}': failed to extract boundary loop from mesh '{mesh.name}'.", this);
        }

        public override void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum)
        {
            data.Shape = Shape;
            data.Center = WorldCenter;
            data.PointStart = pointsAccum.Count;

            int n = _cachedLocalPoints?.Length ?? 0;
            if (_cachedLocalPoints != null)
            {
                for (int i = 0; i < n; i++)
                {
                    Vector3 w = CachedTransform.TransformPoint(_cachedLocalPoints[i]);
                    pointsAccum.Add(new float2(w.x, w.y));
                }
            }

            data.PointCount = n;
        }

        // ---- 轮廓提取 ---- // ---- Boundary extraction ---- // ---- 輪郭抽出 ----

        private static Vector2[] ExtractLongestBoundaryLoop(Mesh m)
        {
            Vector3[] verts = m.vertices;
            int[] tris = m.triangles;
            if (tris.Length < 3) return null;

            // 统计各边出现次数（规范化 key = lo<<32|hi）。 // Count edge occurrences (canonical key = lo<<32|hi).
            var edgeCount = new Dictionary<long, int>();
            var edgeVerts = new Dictionary<long, (int a, int b)>();

            for (int i = 0; i < tris.Length; i += 3)
            {
                RecordEdge(edgeCount, edgeVerts, tris[i], tris[i + 1]);
                RecordEdge(edgeCount, edgeVerts, tris[i + 1], tris[i + 2]);
                RecordEdge(edgeCount, edgeVerts, tris[i + 2], tris[i]);
            }

            // 构建轮廓边邻接表（仅出现一次的边）。 // Build adjacency for boundary edges (count == 1).
            var adj = new Dictionary<int, List<int>>();
            foreach (var kv in edgeCount)
            {
                if (kv.Value != 1) continue;
                var (ea, eb) = edgeVerts[kv.Key];
                AddAdj(adj, ea, eb);
                AddAdj(adj, eb, ea);
            }

            if (adj.Count == 0) return null;

            // 按邻接表遍历所有环，取最长。 // Walk all loops using adjacency; keep the longest.
            var visited = new HashSet<int>();
            List<int> longestLoop = null;

            foreach (int startV in adj.Keys)
            {
                if (visited.Contains(startV)) continue;
                var loop = WalkLoop(adj, visited, startV);
                if (longestLoop == null || loop.Count > longestLoop.Count)
                    longestLoop = loop;
            }

            if (longestLoop == null || longestLoop.Count < 2) return null;

            var result = new Vector2[longestLoop.Count];
            for (int i = 0; i < longestLoop.Count; i++)
            {
                Vector3 v = verts[longestLoop[i]];
                result[i] = new Vector2(v.x, v.y);
            }
            return result;
        }

        private static void RecordEdge(Dictionary<long, int> count, Dictionary<long, (int, int)> verts, int a, int b)
        {
            int lo = a < b ? a : b, hi = a < b ? b : a;
            long key = ((long)lo << 32) | (uint)hi;
            count.TryGetValue(key, out int c);
            count[key] = c + 1;
            if (!verts.ContainsKey(key)) verts[key] = (lo, hi);
        }

        private static void AddAdj(Dictionary<int, List<int>> adj, int from, int to)
        {
            if (!adj.ContainsKey(from)) adj[from] = new List<int>();
            adj[from].Add(to);
        }

        private static List<int> WalkLoop(Dictionary<int, List<int>> adj, HashSet<int> visited, int start)
        {
            var loop = new List<int>();
            int current = start, prev = -1;
            while (!visited.Contains(current))
            {
                visited.Add(current);
                loop.Add(current);
                List<int> neighbors = adj[current];
                int next = -1;
                for (int i = 0; i < neighbors.Count; i++)
                {
                    int nb = neighbors[i];
                    if (nb != prev && !visited.Contains(nb)) { next = nb; break; }
                }
                if (next == -1) break;
                prev = current;
                current = next;
            }
            return loop;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (_cachedLocalPoints == null || _cachedLocalPoints.Length < 2) return;
            Gizmos.color = new Color(1f, 0.6f, 0.1f, 0.6f);
            int n = _cachedLocalPoints.Length;
            int last = closed ? n : n - 1;
            for (int i = 0; i < last; i++)
            {
                Vector3 a = transform.TransformPoint(_cachedLocalPoints[i]);
                Vector3 b = transform.TransformPoint(_cachedLocalPoints[(i + 1) % n]);
                Gizmos.DrawLine(a, b);
            }
        }
#endif
    }
}

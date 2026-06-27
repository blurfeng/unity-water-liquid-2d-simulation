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

        // 闭合实心模式的凸分解结果：每 3 个 int 为一个三角形（索引进 _cachedLocalPoints），由耳切三角剖分得到。
        // 每个三角形天然是凸的，FillAll 逐个作为 Polygon 发给求解器，杜绝凹轮廓被 ProjectPolygon（假设凸实心）推穿。
        // 在局部空间算一次即可：三角形拓扑在任意仿射变换下不变（平移/旋转/缩放后仍是覆盖该多边形的三角形）。
        // Convex decomposition for the closed/solid mode: every 3 ints form a triangle (indices into _cachedLocalPoints) from
        // ear-clipping. Each triangle is inherently convex; FillAll emits each as a Polygon so a concave outline can't be pushed
        // through by ProjectPolygon (which assumes a convex solid). Computed once in local space — the triangle topology is
        // invariant under any affine transform (it still tiles the polygon after translate/rotate/scale). // 凸分解（耳切、局所空間で一度）。
        private List<int> _triangles;

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

            // 闭合实心模式：把（可能凹的）轮廓环耳切三角剖分为多个凸三角形（FillAll 逐个作为 Polygon 发出）；开放（EdgeChain）模式无需分解。
            // Closed/solid mode: ear-clip the (possibly concave) outline into convex triangles (FillAll emits each as a Polygon); the open EdgeChain mode needs none.
            // 閉合実心モードのみ耳切で三角分解。
            _triangles = (closed && _cachedLocalPoints != null && _cachedLocalPoints.Length >= 3)
                ? TriangulateEarClip(_cachedLocalPoints)
                : null;
            if (closed && _cachedLocalPoints != null && _cachedLocalPoints.Length >= 3
                && (_triangles == null || _triangles.Count < (_cachedLocalPoints.Length - 2) * 3))
            {
                // 三角剖分不完整（多为自交/退化轮廓）：仍用已得三角形，但可能留缝。 // Incomplete triangulation (self-intersecting/degenerate outline): usable but may leave gaps. // 不完全（自交等）。
                Debug.LogWarning($"[Liquid2DMeshCollider] '{name}': mesh boundary triangulation incomplete (non-simple/degenerate outline?); solid coverage may have gaps.", this);
            }
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

        /// <summary>
        /// 闭合实心模式：把轮廓的凸分解（三角形）逐个作为 Polygon 发出（多发），避免凹轮廓被当凸实心推穿。
        /// 无三角剖分（开放 EdgeChain 模式 / 顶点不足 / 退化）时退回基类默认（单条目 <see cref="Fill"/>）。
        /// Closed/solid mode: emit each triangle of the outline's convex decomposition as a Polygon (multi-emit), so a concave
        /// outline isn't pushed through as a convex solid. Falls back to the base single-<see cref="Fill"/> entry when there is
        /// no triangulation (open EdgeChain mode / too few vertices / degenerate).
        /// 閉合実心モードは三角形を個別の Polygon として多発。三角分解が無ければ基底の単一 Fill にフォールバック。
        /// </summary>
        public override void FillAll(List<Liquid2DColliderData> dataOut, List<float2> pointsAccum)
        {
            // 同时检查实时的 closed：避免运行时切到开放模式后仍按缓存的三角剖分发 Polygon（与 Shape/Fill 实时读取一致）。
            // Also check the live `closed` flag so a runtime switch to open mode doesn't keep emitting Polygons from the cached triangulation (matches Shape/Fill reading it live).
            // 実時の closed も確認（実行時に開放へ切替えた場合の不整合を防ぐ）。
            if (!closed || _triangles == null || _triangles.Count < 3 || _cachedLocalPoints == null)
            {
                base.FillAll(dataOut, pointsAccum); // 开放/无分解：单条目（EdgeChain 或退化 Polygon）。 // open / no decomposition. // 開放/未分解。
                return;
            }

            EnsureInitialized();
            int triCount = _triangles.Count / 3;
            for (int t = 0; t < triCount; t++)
            {
                int i0 = _triangles[t * 3], i1 = _triangles[t * 3 + 1], i2 = _triangles[t * 3 + 2];
                var data = new Liquid2DColliderData
                {
                    Shape = Liquid2DColliderShape.Polygon,
                    Center = WorldCenter,
                    PointStart = pointsAccum.Count,
                    PointCount = 3,
                };
                AddWorldPoint(pointsAccum, _cachedLocalPoints[i0]);
                AddWorldPoint(pointsAccum, _cachedLocalPoints[i1]);
                AddWorldPoint(pointsAccum, _cachedLocalPoints[i2]);
                dataOut.Add(data); // 其余字段（Dynamic/BodyIndex/GroupId/ColliderMode/…）由注册表在 BuildBuffer 中按碰撞器统一回填。 // remaining fields filled per-collider by the registry. // 残りはレジストリが回填。
            }
        }

        private void AddWorldPoint(List<float2> acc, Vector2 local)
        {
            Vector3 w = CachedTransform.TransformPoint(local);
            acc.Add(new float2(w.x, w.y));
        }

        // ---- 凸分解（耳切三角剖分） ---- // ---- Convex decomposition (ear-clipping) ---- // ---- 凸分解（耳切） ----

        private const float _triEps = 1e-7f;

        /// <summary>
        /// 简单多边形耳切三角剖分 → 三角形索引三元组（索引进 pts）。三角形天然凸，是最稳健的凸分解。
        /// 兼容任意缠绕方向；遇自交/退化输入按迭代上限安全退出（返回已得三角形，调用方据完整度告警）。
        /// Ear-clipping triangulation of a simple polygon → triangle index triples (into pts). Triangles are inherently convex,
        /// the most robust convex decomposition. Handles either winding; on self-intersecting/degenerate input it exits safely
        /// by an iteration cap (returns the triangles produced; the caller warns on incompleteness).
        /// 単純多角形の耳切三角分解。任意の巻き方向に対応、自交/退化時は反復上限で安全終了。
        /// </summary>
        private static List<int> TriangulateEarClip(Vector2[] pts)
        {
            int n = pts.Length;
            var tris = new List<int>(n >= 3 ? (n - 2) * 3 : 0);
            if (n < 3) return tris;
            if (n == 3) { tris.Add(0); tris.Add(1); tris.Add(2); return tris; }

            var idx = new List<int>(n);
            for (int i = 0; i < n; i++) idx.Add(i);

            // 按有符号面积统一为 CCW 处理，使「凸顶点叉积 > 0」判定一致。 // Normalize to CCW via signed area so "convex vertex ⇔ cross > 0" holds. // 有符号面積で CCW に正規化。
            if (SignedArea(pts) < 0f) idx.Reverse();

            int iCur = 0;
            int safety = n * n * 2 + 32; // 迭代上限，防退化输入死循环。 // iteration cap against degenerate input. // 反復上限。
            while (idx.Count > 3 && safety-- > 0)
            {
                int m = idx.Count;
                int prev = (iCur - 1 + m) % m;
                int next = (iCur + 1) % m;
                int a = idx[prev], b = idx[iCur], c = idx[next];
                Vector2 pa = pts[a], pb = pts[b], pc = pts[c];

                // 凸顶点（CCW 下叉积 > 0）且三角形内不含其它顶点 → 是「耳」，剪掉。 // Convex vertex with no other vertex inside ⇒ ear; clip it. // 凸頂点かつ内点なし → 耳。
                bool isConvex = Cross2(pb - pa, pc - pb) > _triEps;
                if (isConvex && !AnyVertexInTriangle(pts, idx, prev, iCur, next, pa, pb, pc))
                {
                    tris.Add(a); tris.Add(b); tris.Add(c);
                    idx.RemoveAt(iCur);
                    if (iCur >= idx.Count) iCur = 0;
                }
                else
                {
                    iCur = (iCur + 1) % idx.Count;
                }
            }
            if (idx.Count == 3) { tris.Add(idx[0]); tris.Add(idx[1]); tris.Add(idx[2]); }
            return tris;
        }

        private static float Cross2(Vector2 u, Vector2 v) => u.x * v.y - u.y * v.x;

        private static float SignedArea(Vector2[] p)
        {
            float s = 0f;
            for (int i = 0; i < p.Length; i++)
            {
                Vector2 a = p[i], b = p[(i + 1) % p.Length];
                s += a.x * b.y - b.x * a.y;
            }
            return 0.5f * s;
        }

        // 候选耳三角 (a,b,c) 内是否含环上其它顶点（含边界算内，避免产生 sliver）。 // Whether any other ring vertex lies inside the candidate ear (boundary counts as inside, avoids slivers). // 他頂点が三角内か。
        private static bool AnyVertexInTriangle(Vector2[] pts, List<int> idx, int prev, int cur, int next, Vector2 a, Vector2 b, Vector2 c)
        {
            for (int k = 0; k < idx.Count; k++)
            {
                if (k == prev || k == cur || k == next) continue;
                if (PointInTriangle(pts[idx[k]], a, b, c)) return true;
            }
            return false;
        }

        private static bool PointInTriangle(Vector2 p, Vector2 a, Vector2 b, Vector2 c)
        {
            float d1 = Cross2(b - a, p - a);
            float d2 = Cross2(c - b, p - b);
            float d3 = Cross2(a - c, p - c);
            bool hasNeg = d1 < 0f || d2 < 0f || d3 < 0f;
            bool hasPos = d1 > 0f || d2 > 0f || d3 > 0f;
            return !(hasNeg && hasPos); // 三个叉积全同号（或为 0）→ 点在三角形内（或边界）。 // all same sign (or zero) ⇒ inside (or on boundary). // 全同号 → 内部。
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

            // 假设 Mesh 在 XY 平面：顶点投影到 (x,y)，z 被丢弃。带 Z 厚度或离面旋转的 Mesh 会得到几何错误的轮廓——请用平面 Mesh。
            // Assumes the mesh lies in the XY plane: vertices are projected to (x,y) and z is dropped. A mesh with Z extent or out-of-plane rotation yields a geometrically wrong outline — use a planar mesh.
            // Mesh は XY 平面前提：z は破棄。Z 厚みや面外回転は誤った輪郭になる。
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
                // 贪心取第一个未访问且非来源的邻居。假设流形边界（每个边界顶点度=2）；对度>2 的非流形顶点
                // （共享环、8 字形）此贪心可能走错环或提前终止，得到畸形轮廓——请用流形边界的平面 Mesh。
                // Greedily takes the first unvisited non-source neighbor. Assumes a manifold boundary (each boundary vertex has degree 2);
                // for degree>2 non-manifold vertices (shared loops, figure-eight) this greedy pick may cross loops or stop early, yielding a malformed outline — use a manifold-boundary planar mesh.
                // 多様体境界（境界頂点の次数=2）前提。次数>2 の非多様体頂点では誤ったループになり得る。
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

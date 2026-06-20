using Unity.Collections;
using static Unity.Mathematics.math;
using float2 = Unity.Mathematics.float2;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 碰撞体投影数学：把粒子（预测位置）推出碰撞体表面。所有方法 Burst 友好（无托管、无分配）。
    /// Collider projection math: push a particle (predicted position) out of a collider surface. All methods are
    /// Burst-friendly (no managed state, no allocation).
    /// コライダー投影の数学：粒子（予測位置）をコライダー表面の外へ押し出します。すべて Burst 対応（マネージド/割り当てなし）。
    /// </summary>
    public static class Liquid2DColliderMath
    {
        /// <summary>
        /// 计算把位置 p（半径 particleRadius）推出碰撞体所需的修正向量与表面外法线。
        /// Compute the correction vector and outward surface normal needed to push position p (radius particleRadius) out of the collider.
        /// 位置 p（半径 particleRadius）をコライダーの外へ押し出す補正ベクトルと外向き法線を計算します。
        /// </summary>
        /// <returns>是否发生穿透（true 时 correction/normal 有效）。 // whether penetrating. // 貫通したか。</returns>
        public static bool Project(in Liquid2DColliderData c, in NativeArray<float2> points,
            float2 p, float particleRadius, out float2 correction, out float2 normal)
        {
            switch (c.shape)
            {
                case Liquid2DColliderShape.Circle: return ProjectCircle(c, p, particleRadius, out correction, out normal);
                case Liquid2DColliderShape.Box: return ProjectBox(c, p, particleRadius, out correction, out normal);
                case Liquid2DColliderShape.Capsule: return ProjectCapsule(c, p, particleRadius, out correction, out normal);
                case Liquid2DColliderShape.Polygon: return ProjectPolygon(c, points, p, particleRadius, out correction, out normal);
                case Liquid2DColliderShape.EdgeChain: return ProjectEdgeChain(c, points, p, particleRadius, out correction, out normal);
                case Liquid2DColliderShape.BoundsBox: return ProjectBoundsBox(c, p, particleRadius, out correction, out normal);
                default: correction = float2.zero; normal = float2.zero; return false;
            }
        }

        private static bool ProjectCircle(in Liquid2DColliderData c, float2 p, float pr, out float2 correction, out float2 normal)
        {
            float2 d = p - c.center;
            float distSq = lengthsq(d);
            float minD = c.radius + pr;
            if (distSq >= minD * minD) { correction = float2.zero; normal = float2.zero; return false; }
            float dist = sqrt(distSq);
            normal = dist > 1e-6f ? d / dist : new float2(0f, 1f);
            correction = normal * (minD - dist);
            return true;
        }

        private static bool ProjectBox(in Liquid2DColliderData c, float2 p, float pr, out float2 correction, out float2 normal)
        {
            float cs = cos(c.rotation), sn = sin(c.rotation);
            float2 d = p - c.center;
            float2 local = new float2(d.x * cs + d.y * sn, -d.x * sn + d.y * cs); // R^T * d
            float2 half = c.size;
            float2 cl = clamp(local, -half, half);
            float2 diff = local - cl;
            float distSq = lengthsq(diff);

            float2 nLocal, corrLocal;
            if (distSq > 1e-12f)
            {
                if (distSq >= pr * pr) { correction = float2.zero; normal = float2.zero; return false; }
                float dist = sqrt(distSq);
                nLocal = diff / dist;
                corrLocal = nLocal * (pr - dist);
            }
            else
            {
                // 圆心在盒内部，沿最小穿透轴推出。 // Center inside the box; push out along the min-penetration axis. // 中心がボックス内部、最小貫通軸で押し出す。
                float dx = half.x - abs(local.x);
                float dy = half.y - abs(local.y);
                if (dx < dy)
                {
                    float sx = local.x >= 0f ? 1f : -1f;
                    nLocal = new float2(sx, 0f);
                    corrLocal = nLocal * (dx + pr);
                }
                else
                {
                    float sy = local.y >= 0f ? 1f : -1f;
                    nLocal = new float2(0f, sy);
                    corrLocal = nLocal * (dy + pr);
                }
            }

            correction = new float2(corrLocal.x * cs - corrLocal.y * sn, corrLocal.x * sn + corrLocal.y * cs);
            normal = new float2(nLocal.x * cs - nLocal.y * sn, nLocal.x * sn + nLocal.y * cs);
            return true;
        }

        private static bool ProjectCapsule(in Liquid2DColliderData c, float2 p, float pr, out float2 correction, out float2 normal)
        {
            float cs = cos(c.rotation), sn = sin(c.rotation);
            float2 d = p - c.center;
            float2 local = new float2(d.x * cs + d.y * sn, -d.x * sn + d.y * cs);
            float t = clamp(local.x, -c.size.x, c.size.x);
            float2 seg = new float2(t, 0f);
            float2 dl = local - seg;
            float distSq = lengthsq(dl);
            float minD = c.radius + pr;
            if (distSq >= minD * minD) { correction = float2.zero; normal = float2.zero; return false; }
            float dist = sqrt(distSq);
            float2 nLocal = dist > 1e-6f ? dl / dist : new float2(0f, 1f);
            float2 corrLocal = nLocal * (minD - dist);
            correction = new float2(corrLocal.x * cs - corrLocal.y * sn, corrLocal.x * sn + corrLocal.y * cs);
            normal = new float2(nLocal.x * cs - nLocal.y * sn, nLocal.x * sn + nLocal.y * cs);
            return true;
        }

        private static bool ProjectPolygon(in Liquid2DColliderData c, in NativeArray<float2> points,
            float2 p, float pr, out float2 correction, out float2 normal)
        {
            int n = c.pointCount;
            if (n < 3) { correction = float2.zero; normal = float2.zero; return false; }
            int s = c.pointStart;

            float minDistSq = float.MaxValue;
            float2 bestClosest = p;
            for (int i = 0; i < n; i++)
            {
                float2 a = points[s + i];
                float2 b = points[s + ((i + 1) % n)];
                float2 cp = ClosestPointOnSegment(a, b, p);
                float dsq = lengthsq(p - cp);
                if (dsq < minDistSq) { minDistSq = dsq; bestClosest = cp; }
            }

            bool inside = PointInPolygon(points, s, n, p);
            float dist = sqrt(minDistSq);
            if (!inside)
            {
                if (minDistSq >= pr * pr) { correction = float2.zero; normal = float2.zero; return false; }
                normal = dist > 1e-6f ? (p - bestClosest) / dist : new float2(0f, 1f);
                correction = normal * (pr - dist);
            }
            else
            {
                normal = dist > 1e-6f ? (bestClosest - p) / dist : new float2(0f, 1f);
                correction = normal * (dist + pr);
            }
            return true;
        }

        private static bool ProjectBoundsBox(in Liquid2DColliderData c, float2 p, float pr, out float2 correction, out float2 normal)
        {
            float cs = cos(c.rotation), sn = sin(c.rotation);
            float2 d = p - c.center;
            float2 local = new float2(d.x * cs + d.y * sn, -d.x * sn + d.y * cs);
            float2 effHalf = max(c.size - pr, 0f);
            if (abs(local.x) <= effHalf.x && abs(local.y) <= effHalf.y)
            {
                correction = float2.zero; normal = float2.zero; return false;
            }
            float2 target = clamp(local, -effHalf, effHalf);
            float2 corrLocal = target - local;
            float dist = sqrt(lengthsq(corrLocal));
            float2 nLocal = dist > 1e-6f ? corrLocal / dist : new float2(0f, 1f);
            correction = new float2(corrLocal.x * cs - corrLocal.y * sn, corrLocal.x * sn + corrLocal.y * cs);
            normal = new float2(nLocal.x * cs - nLocal.y * sn, nLocal.x * sn + nLocal.y * cs);
            return true;
        }

        private static bool ProjectEdgeChain(in Liquid2DColliderData c, in NativeArray<float2> points,
            float2 p, float pr, out float2 correction, out float2 normal)
        {
            int n = c.pointCount;
            if (n < 2) { correction = float2.zero; normal = float2.zero; return false; }
            int s = c.pointStart;

            float minDistSq = float.MaxValue;
            float2 bestClosest = p;
            for (int i = 0; i < n - 1; i++)
            {
                float2 a = points[s + i];
                float2 b = points[s + i + 1];
                float2 cp = ClosestPointOnSegment(a, b, p);
                float dsq = lengthsq(p - cp);
                if (dsq < minDistSq) { minDistSq = dsq; bestClosest = cp; }
            }

            // c.radius 为边线自身的扩展半径（EdgeCollider2D.edgeRadius），0 表示无扩展。
            // c.radius is the edge's own expansion radius (EdgeCollider2D.edgeRadius); 0 means no expansion.
            // c.radius はエッジ自身の拡張半径（EdgeCollider2D.edgeRadius）、0 は拡張なし。
            float minD = c.radius + pr;
            if (minDistSq >= minD * minD) { correction = float2.zero; normal = float2.zero; return false; }
            float dist = sqrt(minDistSq);
            normal = dist > 1e-6f ? (p - bestClosest) / dist : new float2(0f, 1f); // 两面：粒子所在侧。 // two-sided: the side the particle is on. // 両面：粒子のいる側。
            correction = normal * (minD - dist);
            return true;
        }

        private static float2 ClosestPointOnSegment(float2 a, float2 b, float2 p)
        {
            float2 ab = b - a;
            float denom = lengthsq(ab);
            if (denom < 1e-12f) return a;
            float t = clamp(dot(p - a, ab) / denom, 0f, 1f);
            return a + t * ab;
        }

        private static bool PointInPolygon(in NativeArray<float2> points, int start, int count, float2 p)
        {
            // 射线交叉数法（适用任意简单多边形）。 // Crossing-number test (works for any simple polygon). // 交差数法（任意の単純多角形に対応）。
            bool inside = false;
            for (int i = 0, j = count - 1; i < count; j = i++)
            {
                float2 vi = points[start + i];
                float2 vj = points[start + j];
                if (((vi.y > p.y) != (vj.y > p.y)) &&
                    (p.x < (vj.x - vi.x) * (p.y - vi.y) / (vj.y - vi.y) + vi.x))
                {
                    inside = !inside;
                }
            }
            return inside;
        }
    }
}

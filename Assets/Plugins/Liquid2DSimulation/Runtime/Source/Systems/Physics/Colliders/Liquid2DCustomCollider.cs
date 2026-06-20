using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 基于 Unity <see cref="CustomCollider2D"/> 的流体碰撞体。读取 CustomCollider2D 中所有子形状
    /// （Circle / Capsule / Polygon / Edges），将每个子形状转换为对应的流体碰撞形状并同帧注入求解器。
    /// 适合在运行时以代码动态设置复杂碰撞轮廓，或复用已有 CustomCollider2D 的几何描述。
    /// Fluid collider backed by Unity's <see cref="CustomCollider2D"/>. Reads all sub-shapes (Circle / Capsule /
    /// Polygon / Edges) from the CustomCollider2D, converts each to the corresponding fluid collision shape, and
    /// injects them all into the solver each frame. Useful for programmatically driven complex collision outlines
    /// or for reusing an existing CustomCollider2D's geometry.
    /// Unity の <see cref="CustomCollider2D"/> を使った流体コライダー。全サブ形状（Circle / Capsule / Polygon / Edges）を
    /// 読み取り、対応する流体衝突形状に変換して同フレームのソルバーに注入します。
    /// </summary>
    [AddComponentMenu("Liquid2D/Colliders/Liquid2D Custom Collider")]
    [RequireComponent(typeof(CustomCollider2D))]
    public class Liquid2DCustomCollider : Liquid2DCollider
    {
        // 每个子形状缓存其类型、半径及局部顶点，变换在 FillAll 中每帧应用。
        // Each sub-shape caches its type, radius, and local vertices; the transform is applied each frame in FillAll.
        // 各サブ形状はタイプ・半径・ローカル頂点をキャッシュ。変換は FillAll で毎フレーム適用。
        private CachedShape[] _cachedShapes;

        private struct CachedShape
        {
            public Liquid2DColliderShape Liquid2DShape;
            public float Radius;
            public Vector2[] LocalPoints; // Circle:1点(中心); Capsule:2点(端点); Polygon/EdgeChain:N点。
        }

        // Shape 属性由 FillAll 覆写，此处返回 Circle 作为占位，不影响实际行为。
        // Shape property is superseded by FillAll; Circle is returned as a placeholder with no behavioural impact.
        // FillAll がオーバーライドするため Shape は使われない。Circle をプレースホルダーとして返す。
        public override Liquid2DColliderShape Shape => Liquid2DColliderShape.Circle;

        // Fill 不被使用（FillAll 直接多条写入），实现为空操作。
        // Fill is unused here (FillAll writes multiple entries directly); implemented as no-op.
        // Fill は FillAll に置き換えられるため空実装。
        public override void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum) { }

        protected override void OnEnable()
        {
            RefreshShapes();
            base.OnEnable();
        }

        /// <summary>
        /// 重新从 <see cref="CustomCollider2D"/> 读取所有子形状并缓存。在运行时以代码修改 CustomCollider2D
        /// 的形状后，需手动调用此方法使流体碰撞同步更新。
        /// Re-reads all sub-shapes from <see cref="CustomCollider2D"/> and refreshes the cache. Call this manually
        /// after programmatically modifying the CustomCollider2D shapes at runtime.
        /// <see cref="CustomCollider2D"/> から全サブ形状を再読み込みしてキャッシュを更新します。実行時に形状を
        /// 変更した後はこのメソッドを手動で呼び出してください。
        /// </summary>
        public void RefreshShapes()
        {
            var cc = GetComponent<CustomCollider2D>();
            if (!cc) { _cachedShapes = null; return; }

            var group = new PhysicsShapeGroup2D();
            cc.GetCustomShapes(group);

            int count = group.shapeCount;
            if (count == 0) { _cachedShapes = null; return; }

            var list = new List<CachedShape>(count);
            for (int i = 0; i < count; i++)
            {
                PhysicsShape2D ps = group.GetShape(i);
                CachedShape cs;
                cs.Radius = ps.radius;
                cs.LocalPoints = null;

                switch (ps.shapeType)
                {
                    case PhysicsShapeType2D.Circle:
                        cs.Liquid2DShape = Liquid2DColliderShape.Circle;
                        cs.LocalPoints = new[] { group.GetShapeVertex(i, 0) };
                        break;

                    case PhysicsShapeType2D.Capsule:
                        cs.Liquid2DShape = Liquid2DColliderShape.Capsule;
                        cs.LocalPoints = new[] { group.GetShapeVertex(i, 0), group.GetShapeVertex(i, 1) };
                        break;

                    case PhysicsShapeType2D.Polygon:
                        cs.Liquid2DShape = Liquid2DColliderShape.Polygon;
                        cs.LocalPoints = new Vector2[ps.vertexCount];
                        for (int j = 0; j < ps.vertexCount; j++)
                            cs.LocalPoints[j] = group.GetShapeVertex(i, j);
                        break;

                    case PhysicsShapeType2D.Edges:
                        cs.Liquid2DShape = Liquid2DColliderShape.EdgeChain;
                        cs.LocalPoints = new Vector2[ps.vertexCount];
                        for (int j = 0; j < ps.vertexCount; j++)
                            cs.LocalPoints[j] = group.GetShapeVertex(i, j);
                        break;

                    default:
                        continue; // 跳过不支持的形状类型。 // Skip unsupported shape types. // 非対応形状はスキップ。
                }

                list.Add(cs);
            }

            _cachedShapes = list.Count > 0 ? list.ToArray() : null;
        }

        /// <summary>
        /// 将所有缓存的子形状转换为世界坐标并写入求解器输入列表。
        /// Convert all cached sub-shapes to world space and write them into the solver input list.
        /// キャッシュされた全サブ形状をワールド座標に変換してソルバー入力リストに書き込みます。
        /// </summary>
        public override void FillAll(List<Liquid2DColliderData> dataOut, List<float2> pointsAccum)
        {
            if (_cachedShapes == null) return;

            float scale = Mathf.Abs(CachedTransform.lossyScale.x); // 用于 Circle/Capsule 半径缩放。 // For Circle/Capsule radius scaling.

            for (int s = 0; s < _cachedShapes.Length; s++)
            {
                ref readonly CachedShape cs = ref _cachedShapes[s];
                var data = new Liquid2DColliderData { Shape = cs.Liquid2DShape };

                switch (cs.Liquid2DShape)
                {
                    case Liquid2DColliderShape.Circle:
                    {
                        Vector3 wc = CachedTransform.TransformPoint(cs.LocalPoints[0]);
                        data.Center = new float2(wc.x, wc.y);
                        data.Radius = cs.Radius * scale;
                        break;
                    }

                    case Liquid2DColliderShape.Capsule:
                    {
                        // 将两端点变换至世界坐标后推导胶囊中心、半长及旋转角。
                        // Derive capsule center, half-length, and rotation from the two world-space endpoints.
                        // 2端点をワールド変換し、カプセルの中心・半長・回転を算出。
                        Vector3 w0 = CachedTransform.TransformPoint(cs.LocalPoints[0]);
                        Vector3 w1 = CachedTransform.TransformPoint(cs.LocalPoints[1]);
                        float2 fw0 = new float2(w0.x, w0.y);
                        float2 fw1 = new float2(w1.x, w1.y);
                        float2 dir = fw1 - fw0;
                        data.Center = (fw0 + fw1) * 0.5f;
                        data.Size = new float2(math.length(dir) * 0.5f, 0f);
                        data.Rotation = math.atan2(dir.y, dir.x);
                        data.Radius = cs.Radius * scale;
                        break;
                    }

                    case Liquid2DColliderShape.Polygon:
                    case Liquid2DColliderShape.EdgeChain:
                    {
                        data.Center = WorldCenter; // Polygon/EdgeChain 未用 center，仅占位。 // center unused for polygon/edge.
                        data.PointStart = pointsAccum.Count;
                        int n = cs.LocalPoints?.Length ?? 0;
                        if (cs.LocalPoints != null)
                        {
                            for (int i = 0; i < n; i++)
                            {
                                Vector3 w = CachedTransform.TransformPoint(cs.LocalPoints[i]);
                                pointsAccum.Add(new float2(w.x, w.y));
                            }
                        }
                        data.PointCount = n;
                        break;
                    }
                }

                dataOut.Add(data);
            }
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            var cc = GetComponent<CustomCollider2D>();
            if (!cc) return;

            var group = new PhysicsShapeGroup2D();
            cc.GetCustomShapes(group);
            if (group.shapeCount == 0) return;

            Gizmos.color = new Color(0.6f, 0.4f, 1f, 0.6f);
            for (int i = 0; i < group.shapeCount; i++)
            {
                PhysicsShape2D ps = group.GetShape(i);
                switch (ps.shapeType)
                {
                    case PhysicsShapeType2D.Circle:
                    {
                        Vector3 wc = transform.TransformPoint(group.GetShapeVertex(i, 0));
                        float wr = ps.radius * Mathf.Abs(transform.lossyScale.x);
                        DrawCircle(wc, wr, 32);
                        break;
                    }
                    case PhysicsShapeType2D.Capsule:
                    {
                        Vector3 w0 = transform.TransformPoint(group.GetShapeVertex(i, 0));
                        Vector3 w1 = transform.TransformPoint(group.GetShapeVertex(i, 1));
                        float wr = ps.radius * Mathf.Abs(transform.lossyScale.x);
                        Gizmos.DrawLine(w0, w1);
                        DrawCircle(w0, wr, 16);
                        DrawCircle(w1, wr, 16);
                        break;
                    }
                    case PhysicsShapeType2D.Polygon:
                    {
                        int n = ps.vertexCount;
                        for (int j = 0; j < n; j++)
                        {
                            Vector3 a = transform.TransformPoint(group.GetShapeVertex(i, j));
                            Vector3 b = transform.TransformPoint(group.GetShapeVertex(i, (j + 1) % n));
                            Gizmos.DrawLine(a, b);
                        }
                        break;
                    }
                    case PhysicsShapeType2D.Edges:
                    {
                        int n = ps.vertexCount;
                        for (int j = 0; j < n - 1; j++)
                        {
                            Vector3 a = transform.TransformPoint(group.GetShapeVertex(i, j));
                            Vector3 b = transform.TransformPoint(group.GetShapeVertex(i, j + 1));
                            Gizmos.DrawLine(a, b);
                        }
                        break;
                    }
                }
            }
        }

        private static void DrawCircle(Vector3 center, float radius, int segments)
        {
            float step = 2f * Mathf.PI / segments;
            for (int k = 0; k < segments; k++)
            {
                float a0 = k * step, a1 = (k + 1) * step;
                Vector3 p0 = center + new Vector3(Mathf.Cos(a0), Mathf.Sin(a0)) * radius;
                Vector3 p1 = center + new Vector3(Mathf.Cos(a1), Mathf.Sin(a1)) * radius;
                Gizmos.DrawLine(p0, p1);
            }
        }
#endif
    }
}

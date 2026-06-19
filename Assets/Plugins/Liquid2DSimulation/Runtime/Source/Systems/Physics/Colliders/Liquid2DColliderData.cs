using Unity.Collections;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 碰撞体形状类型。
    /// Collider shape type.
    /// コライダー形状タイプ。
    /// </summary>
    public enum Liquid2DColliderShape : byte
    {
        /// <summary>圆。 // Circle. // 円。</summary>
        Circle = 0,
        /// <summary>有向矩形盒（可旋转 OBB）。 // Oriented box (rotatable OBB). // 有向ボックス（回転可能 OBB）。</summary>
        Box = 1,
        /// <summary>胶囊（沿局部 X 轴的线段 + 半径）。 // Capsule (segment along local X + radius). // カプセル（ローカルX軸の線分 + 半径）。</summary>
        Capsule = 2,
        /// <summary>凸多边形（实心，把粒子推到外侧）。 // Convex polygon (solid, pushes particles outward). // 凸多角形（中実、粒子を外へ押す）。</summary>
        Polygon = 3,
        /// <summary>边线段链（开放折线，薄壁双面阻挡）。 // Edge chain (open polyline, two-sided thin barrier). // エッジチェーン（開いた折れ線、両面の薄壁）。</summary>
        EdgeChain = 4,
    }

    /// <summary>
    /// 碰撞体的 blittable 描述，供 Burst Job 读取。多边形/边链的顶点存于共享 points 数组（世界坐标）。
    /// Blittable collider description for Burst jobs. Polygon/edge-chain vertices live in a shared points array (world space).
    /// Burst Job 用の blittable コライダー記述。多角形/エッジチェーンの頂点は共有 points 配列（ワールド座標）に格納。
    /// </summary>
    public struct Liquid2DColliderData
    {
        /// <summary>形状类型。 // Shape type. // 形状タイプ。</summary>
        public Liquid2DColliderShape shape;

        /// <summary>世界中心。 // World center. // ワールド中心。</summary>
        public float2 center;

        /// <summary>盒：半尺寸(x,y)；胶囊：x=半长。 // Box: half-size(x,y); Capsule: x=half-length. // ボックス：半サイズ；カプセル：x=半長。</summary>
        public float2 size;

        /// <summary>旋转（弧度）。 // Rotation (radians). // 回転（ラジアン）。</summary>
        public float rotation;

        /// <summary>圆/胶囊半径。 // Circle/capsule radius. // 円/カプセル半径。</summary>
        public float radius;

        /// <summary>多边形/边链顶点在共享数组的起始索引。 // Start index of polygon/edge vertices in the shared array. // 多角形/エッジ頂点の共有配列内開始インデックス。</summary>
        public int pointStart;

        /// <summary>多边形/边链顶点数。 // Polygon/edge vertex count. // 多角形/エッジ頂点数。</summary>
        public int pointCount;

        /// <summary>是否动态（接收流体反作用冲量，参与双向耦合）。 // Whether dynamic (receives fluid reaction impulse). // 動的か（流体の反作用力積を受ける）。</summary>
        public byte dynamic;

        /// <summary>动态体在冲量累积数组中的索引（dynamic==0 时为 -1）。 // Index into the impulse-accumulation array (-1 when not dynamic). // 力積累積配列内のインデックス。</summary>
        public int bodyIndex;
    }

    /// <summary>
    /// 扁平化后的碰撞体集合，按值传入求解 Job。
    /// Flattened collider set, passed by value into solve jobs.
    /// 平坦化されたコライダー集合。解法 Job に値渡しします。
    /// </summary>
    public struct Liquid2DColliderBuffer
    {
        [ReadOnly] public NativeArray<Liquid2DColliderData> colliders;
        [ReadOnly] public NativeArray<float2> points; // 多边形/边链共享顶点。 // shared polygon/edge vertices. // 共有頂点。

        public bool IsCreated => colliders.IsCreated;
        public int Count => colliders.IsCreated ? colliders.Length : 0;
    }
}

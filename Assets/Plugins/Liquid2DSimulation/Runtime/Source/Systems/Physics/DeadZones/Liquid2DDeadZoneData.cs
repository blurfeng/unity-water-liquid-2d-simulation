using Unity.Collections;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 销毁区域的 blittable 描述，供 Burst Job 读取。形状沿用 <see cref="Liquid2DColliderData"/>（复用
    /// <see cref="Liquid2DColliderMath.Project"/> 以 particleRadius=0 做"点是否在实心形状内"判定），并附带组过滤信息。
    /// Blittable dead-zone description for Burst jobs. The shape reuses <see cref="Liquid2DColliderData"/> (so
    /// <see cref="Liquid2DColliderMath.Project"/> with particleRadius=0 acts as a "point-inside-solid-shape" test) plus
    /// group-filter info.
    /// 破棄領域の blittable 記述（Burst Job 用）。形状は <see cref="Liquid2DColliderData"/> を再利用し、グループ絞り込み情報を付与。
    /// </summary>
    public struct Liquid2DDeadZoneData
    {
        /// <summary>形状描述（复用碰撞体形状；多边形顶点存于共享 points 数组）。 // Shape (reuses collider shape; polygon vertices in shared points). // 形状記述。</summary>
        public Liquid2DColliderData shape;

        /// <summary>过滤的目标组（nameTag 解析得到）。 // Target group to filter by (resolved from nameTag). // 絞り込み対象グループ。</summary>
        public int groupId;

        /// <summary>1=销毁区域内全部粒子（空 nameTag）；0=仅销毁 groupId 匹配的粒子。 // 1 = kill all particles in range (empty nameTag); 0 = only matching groupId. // 1=範囲内全粒子、0=groupId 一致のみ。</summary>
        public byte matchAll;

        /// <summary>
        /// Bounds 模式：1=销毁形状"外"的粒子（区域作为容器边界，逃逸者被回收）；0=默认，销毁形状"内"的粒子。
        /// Bounds mode: 1 = destroy particles "outside" the shape (the region acts as a containment boundary; escapees are
        /// recycled); 0 = default, destroy particles "inside" the shape.
        /// Bounds モード：1=形状の"外"の粒子を破棄（領域を容器境界として扱い、逃げた粒子を回収）、0=既定で形状"内"を破棄。
        /// </summary>
        public byte invert;
    }

    /// <summary>
    /// 扁平化后的销毁区域集合，按值传入求解 Job。
    /// Flattened dead-zone set, passed by value into solve jobs.
    /// 平坦化された破棄領域集合。解法 Job に値渡しします。
    /// </summary>
    public struct Liquid2DDeadZoneBuffer
    {
        [ReadOnly] public NativeArray<Liquid2DDeadZoneData> zones;
        [ReadOnly] public NativeArray<float2> points; // 多边形/边链共享顶点。 // shared polygon/edge vertices. // 共有頂点。

        public bool IsCreated => zones.IsCreated;
        public int Count => zones.IsCreated ? zones.Length : 0;
    }
}

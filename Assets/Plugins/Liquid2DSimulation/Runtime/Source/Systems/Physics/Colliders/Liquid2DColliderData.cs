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
        /// <summary>内边界盒（粒子不能逃出，推粒子到内侧）。 // Bounds box (particles cannot escape; pushes particles inward). // 境界ボックス（粒子が脱出不可、内側へ押し戻す）。</summary>
        BoundsBox = 5,
    }

    /// <summary>
    /// 碰撞体的 blittable 描述，供 Burst Job 读取。多边形/边链的顶点存于共享 points 数组（世界坐标）。
    /// Blittable collider description for Burst jobs. Polygon/edge-chain vertices live in a shared points array (world space).
    /// Burst Job 用の blittable コライダー記述。多角形/エッジチェーンの頂点は共有 points 配列（ワールド座標）に格納。
    /// </summary>
    public struct Liquid2DColliderData
    {
        /// <summary>形状类型。 // Shape type. // 形状タイプ。</summary>
        public Liquid2DColliderShape Shape;

        /// <summary>世界中心。 // World center. // ワールド中心。</summary>
        public float2 Center;

        /// <summary>盒：半尺寸(x,y)；胶囊：x=半长。 // Box: half-size(x,y); Capsule: x=half-length. // ボックス：半サイズ；カプセル：x=半長。</summary>
        public float2 Size;

        /// <summary>旋转（弧度）。 // Rotation (radians). // 回転（ラジアン）。</summary>
        public float Rotation;

        /// <summary>圆/胶囊半径。 // Circle/capsule radius. // 円/カプセル半径。</summary>
        public float Radius;

        /// <summary>多边形/边链顶点在共享数组的起始索引。 // Start index of polygon/edge vertices in the shared array. // 多角形/エッジ頂点の共有配列内開始インデックス。</summary>
        public int PointStart;

        /// <summary>多边形/边链顶点数。 // Polygon/edge vertex count. // 多角形/エッジ頂点数。</summary>
        public int PointCount;

        /// <summary>是否动态（接收流体反作用冲量，参与双向耦合）。 // Whether dynamic (receives fluid reaction impulse). // 動的か（流体の反作用力積を受ける）。</summary>
        public byte Dynamic;

        /// <summary>动态体在冲量累积数组中的索引（dynamic==0 时为 -1）。 // Index into the impulse-accumulation array (-1 when not dynamic). // 力積累積配列内のインデックス。</summary>
        public int BodyIndex;

        /// <summary>作用的目标粒子组（nameTag 解析得到）。 // Target particle group (resolved from nameTag). // 作用対象グループ。</summary>
        public int GroupId;

        /// <summary>1=作用于全部粒子（空 nameTag）；0=仅作用于 groupId 匹配的粒子。 // 1 = affects all particles (empty nameTag); 0 = only matching groupId. // 1=全粒子、0=groupId 一致のみ。</summary>
        public byte MatchAll;

        /// <summary>
        /// 碰撞交互模式（0=Push 推离，1=Submerge 淹没）。
        /// Collision interaction mode (0=Push, 1=Submerge).
        /// 衝突相互作用モード（0=Push 押し出し、1=Submerge 水没）。
        /// </summary>
        public byte ColliderMode;

        /// <summary>
        /// 淹没模式：位移耦合强度 k∈[0,1]。把覆盖粒子推向碰撞器速度的反方向（让水绕过物体回流、完成位置交换），随碰撞器速度从 0 渐入（静止不扰动流体），纯耗散保持稳定。
        /// Submerge: displacement-coupling strength k∈[0,1]. Pushes covered particles toward the OPPOSITE of the collider's velocity (fluid flows around the body — position exchange), ramped in by collider speed (a still collider doesn't disturb fluid); purely dissipative, stable.
        /// 水没：変位結合強度 k∈[0,1]。覆う粒子をコライダー速度の逆方向へ押し（物体を回り込み回流・位置交換）、速度で 0 から漸入（静止時は不擾乱）。純散逸で安定。
        /// </summary>
        public float SubmergeCoupling;

        /// <summary>
        /// 淹没模式：飞溅强度。碰撞器速度超过 <see cref="SubmergeSplashThreshold"/> 后，沿其速度反方向非线性注入额外速度产生喷溅（向下砸水→水往上溅）。仅高速瞬态生效。
        /// Submerge: splash strength. Above <see cref="SubmergeSplashThreshold"/>, injects extra velocity nonlinearly OPPOSITE to the collider's velocity to create a spray (slam down → spray up). Only the transient high-speed regime.
        /// 水没：水しぶき強度。<see cref="SubmergeSplashThreshold"/> 超過時、速度の逆方向へ非線形に追加速度を注入し飛沫を生成（下方衝突で上へ）。高速瞬間のみ。
        /// </summary>
        public float SubmergeSplashStrength;

        /// <summary>
        /// 淹没模式：飞溅起始速度阈值（世界单位/秒）。同时作为速度耦合渐入的参考速度（速度达此值时耦合到满）。
        /// Submerge: splash onset speed threshold (world units/s). Also the reference speed for ramping in the velocity coupling (coupling reaches full at this speed).
        /// 水没：飛沫開始の速度閾値（ワールド単位/秒）。速度結合の漸入参照速度でもある（この速度で結合が満に）。
        /// </summary>
        public float SubmergeSplashThreshold;

        /// <summary>
        /// 淹没模式：飞溅强度从 0 增至满的速度区间（世界单位/秒），自阈值起算。
        /// Submerge: speed range (world units/s) over which splash ramps from 0 to full, measured from the threshold.
        /// 水没：飛沫が 0 から満まで増える速度区間（ワールド単位/秒、閾値から）。
        /// </summary>
        public float SubmergeSplashRange;

        /// <summary>
        /// 淹没模式：施力流体密度阈值（相对静止密度的比例）。仅对 SPH 密度高于此值的粒子施加壳层位移/飞溅力，避免在空中弹飞孤立低密度水滴。0=不过滤。
        /// Submerge: fluid-density threshold (fraction of rest density). Only particles whose SPH density exceeds this get the shell displacement/splash force, so isolated low-density droplets aren't flung in the air. 0 = no filtering.
        /// 水没：施力する流体密度閾値（静止密度比）。SPH 密度がこれを超える粒子のみ殻層力を受ける。0=無効。
        /// </summary>
        public float SubmergeFluidDensityThreshold;

        /// <summary>
        /// 碰撞器线速度（世界空间），由 <see cref="Liquid2DCollider.ComputeVelocity"/> 每帧计算。淹没模式据此判断碰撞器是否在排开流体。
        /// Collider linear velocity (world space), computed each frame by <see cref="Liquid2DCollider.ComputeVelocity"/>. Submerge mode uses it to decide whether the collider is displacing fluid.
        /// コライダー線速度（ワールド空間）。<see cref="Liquid2DCollider.ComputeVelocity"/> が毎フレーム計算。水没モードで流体排除の判定に使用。
        /// </summary>
        public float2 Velocity;
    }

    /// <summary>
    /// 扁平化后的碰撞体集合，按值传入求解 Job。
    /// Flattened collider set, passed by value into solve jobs.
    /// 平坦化されたコライダー集合。解法 Job に値渡しします。
    /// </summary>
    public struct Liquid2DColliderBuffer
    {
        [ReadOnly] public NativeArray<Liquid2DColliderData> Colliders;
        [ReadOnly] public NativeArray<float2> Points; // 多边形/边链共享顶点。 // shared polygon/edge vertices. // 共有頂点。

        public bool IsCreated => Colliders.IsCreated;
        public int Count => Colliders.IsCreated ? Colliders.Length : 0;
    }
}

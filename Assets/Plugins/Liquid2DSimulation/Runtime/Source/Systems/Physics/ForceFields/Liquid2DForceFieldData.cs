using Unity.Collections;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 径向力的距离衰减模式。
    /// Radial-force distance profile mode.
    /// 径方向力の距離プロファイルモード。
    /// </summary>
    public enum Liquid2DForceFieldMode
    {
        /// <summary>按距离衰减（中心强、边缘弱，受 falloff 指数控制）。 // Distance falloff (strong at center, controlled by the falloff exponent). // 距離減衰。</summary>
        Falloff = 0,
        /// <summary>半径内恒定力（均匀，边缘硬截断；忽略 falloff）。 // Constant inside radius (uniform, hard cutoff; ignores falloff). // 半径内一定。</summary>
        Constant = 1,
    }

    /// <summary>
    /// 力场的 blittable 描述，供 Burst Job / GPU 读取。在求解的外力阶段对半径内的粒子施加径向（吸引/排斥）+ 切向（旋流）
    /// 加速度，可选削弱力场内重力，并按到中心的距离衰减粒子速度。
    /// Blittable force-field description for Burst jobs / GPU. During the external-forces stage it applies radial
    /// (attract/repel) + tangential (swirl) acceleration to particles inside the radius, optionally attenuates gravity
    /// inside the field, and damps particle velocity by distance-to-center.
    /// 力場の blittable 記述（Burst Job / GPU 用）。径方向（引力/斥力）+ 接線方向（旋流）加速度を与え、
    /// 任意で力場内の重力を減衰させ、中心距離で速度を減衰します。
    /// </summary>
    public struct Liquid2DForceFieldData
    {
        /// <summary>世界中心。 // World center. // ワールド中心。</summary>
        public float2 Center;

        /// <summary>作用半径（世界单位，半径外不施力）。 // Effect radius (world units; no force outside). // 作用半径。</summary>
        public float Radius;

        /// <summary>径向强度。&gt;0 吸引（指向中心），&lt;0 排斥（远离中心）。 // Radial strength. &gt;0 attract, &lt;0 repel. // 径方向強度。</summary>
        public float Strength;

        /// <summary>速度衰减系数（0=不衰减，1=参考默认；按到中心距离插值）。 // Velocity damping (0=none, 1=reference default; scaled by distance-to-center). // 速度減衰係数。</summary>
        public float VelocityDamping;

        /// <summary>切向（旋流）强度。&gt;0 逆时针，&lt;0 顺时针，0 纯径向。 // Tangential (swirl) strength. &gt;0 CCW, &lt;0 CW, 0 pure radial. // 接線（旋流）強度。</summary>
        public float SwirlStrength;

        /// <summary>力场内重力衰减（0=保留全重力，1=中心完全失重；按线性 centreT 插值，复刻参考喷泉）。 // Gravity attenuation inside field (0=full gravity, 1=weightless at center; scaled by linear centreT). // 力場内重力減衰。</summary>
        public float GravityAttenuation;

        /// <summary>径向/切向力幅值的距离衰减指数（1=线性 centreT，&gt;1 中心更尖锐，&lt;1 更平缓）。Constant 模式忽略。 // Falloff exponent on radial/swirl magnitude (1=linear; ignored in Constant mode). // 減衰指数。</summary>
        public float Falloff;

        /// <summary>径向力距离衰减模式。 // Radial-force distance mode. // 径方向力の距離モード。</summary>
        public Liquid2DForceFieldMode Mode;

        /// <summary>作用的目标粒子组（nameTag 解析得到）。 // Target particle group (resolved from nameTag). // 作用対象グループ。</summary>
        public int GroupId;

        /// <summary>1=作用于全部粒子（空 nameTag）；0=仅作用于 groupId 匹配的粒子。 // 1 = affects all particles (empty nameTag); 0 = only matching groupId. // 1=全粒子、0=groupId 一致のみ。</summary>
        public byte MatchAll;
    }

    /// <summary>
    /// 扁平化后的力场集合，按值传入求解 Job（仿 <see cref="Liquid2DColliderBuffer"/>）。
    /// Flattened force-field set, passed by value into solve jobs (mirrors <see cref="Liquid2DColliderBuffer"/>).
    /// 平坦化された力場集合。解法 Job に値渡しします。
    /// </summary>
    public struct Liquid2DForceFieldBuffer
    {
        [ReadOnly] public NativeArray<Liquid2DForceFieldData> Fields;

        public bool IsCreated => Fields.IsCreated;
        public int Count => Fields.IsCreated ? Fields.Length : 0;
    }
}

using Fs.Liquid2D.Localization;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体力场源基类。力场用于对范围内粒子施加吸引/排斥力（如吸引器、排斥器、爆炸、抽水口、磁铁，或鼠标交互）。
    /// 数量少、需在场景中编辑，故保留为 MonoBehaviour（仿 <see cref="Liquid2DCollider"/>）。启用时注册到
    /// <see cref="Liquid2DForceFieldRegistry"/>，由 <see cref="Liquid2DSimulation"/> 每帧扁平化为缓冲供求解器读取。
    /// 子类重写 <see cref="TryGetField"/> 提供本帧的力场参数；返回 false 表示本帧不施力（如鼠标未按下）。
    /// Base class for fluid force-field sources. Force fields apply attract/repel forces to particles in range (attractors,
    /// repellers, explosions, drains, magnets, or mouse interaction). They are few and authored in scenes, so they stay as
    /// MonoBehaviours (mirrors <see cref="Liquid2DCollider"/>). On enable they register into
    /// <see cref="Liquid2DForceFieldRegistry"/>; <see cref="Liquid2DSimulation"/> flattens them each frame for the solver.
    /// Subclasses override <see cref="TryGetField"/> to provide this frame's field; returning false skips it (e.g. mouse up).
    /// 流体力場ソースの基底クラス。範囲内の粒子に引力/斥力を与えます。数が少なくシーンで編集するため MonoBehaviour のまま。
    /// </summary>
    public abstract class Liquid2DForceFieldSource : MonoBehaviour
    {
        [SerializeField, Min(0f), LocalizationTooltip(
             "力场作用半径（世界单位）。半径外的粒子不受影响。",
             "Force-field effect radius (world units). Particles outside are unaffected.",
             "力場の作用半径（ワールド単位）。半径外の粒子は影響を受けません。")]
        private float radius = 2f;

        [SerializeField, LocalizationTooltip(
             "力场强度。大于 0 吸引（指向中心），小于 0 排斥（远离中心）。",
             "Force-field strength. Greater than 0 attracts (toward center), less than 0 repels (away from center).",
             "力場の強度。0より大きいと引力（中心へ）、0より小さいと斥力（中心から）。")]
        private float strength = 120f;

        [SerializeField, Range(0f, 1f), LocalizationTooltip(
             "速度衰减系数。0=不衰减，1=参考默认强度；越大越能快速抓住/制动范围内粒子（按到中心距离插值）。",
             "Velocity damping. 0 = none, 1 = reference default; higher grabs/brakes particles in range faster (scaled by distance-to-center).",
             "速度減衰係数。0=減衰なし、1=参考デフォルト。大きいほど範囲内の粒子を素早く掴む/制動します（中心距離で補間）。")]
        private float velocityDamping = 1f;

        [Header("Advanced")]
        [SerializeField, LocalizationTooltip(
             "切向（旋流）强度。大于 0 逆时针、小于 0 顺时针、0 为纯径向。用于主动制造可控的漩涡/涡流效果。",
             "Tangential (swirl) strength. Greater than 0 = counterclockwise, less than 0 = clockwise, 0 = pure radial. Use to intentionally create controllable vortex/whirlpool effects.",
             "接線（旋流）強度。0より大きいと反時計回り、0より小さいと時計回り、0で純径方向。渦/旋流効果を能動的に作ります。")]
        private float swirlStrength;

        [SerializeField, Range(0f, 1f), LocalizationTooltip(
             "力场内重力衰减。0=保留全部重力，1=中心处完全失重（按到中心距离插值）。调高可复刻参考的上涌喷泉效果。",
             "Gravity attenuation inside the field. 0 = keep full gravity, 1 = weightless at center (scaled by distance-to-center). Raise it to reproduce the reference's upwelling fountain.",
             "力場内の重力減衰。0=重力を全て保持、1=中心で完全に無重力（中心距離で補間）。上げると参考の噴き上げ噴水を再現します。")]
        private float gravityAttenuation = 0.8f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "力随距离的衰减指数。1=线性，大于 1 中心更尖锐集中，小于 1 更平缓。Constant 模式下忽略。",
             "Falloff exponent of force over distance. 1 = linear, greater than 1 = sharper toward center, less than 1 = flatter. Ignored in Constant mode.",
             "力の距離減衰指数。1=線形、1より大きいと中心へ鋭く、1より小さいと平坦。Constant モードでは無視。")]
        private float falloff = 1f;

        [SerializeField, LocalizationTooltip(
             "径向力距离模式。Falloff=按距离衰减（受指数控制）；Constant=半径内恒定力（均匀，边缘硬截断）。",
             "Radial-force distance mode. Falloff = decays over distance (controlled by the exponent); Constant = uniform force inside radius (hard cutoff at the edge).",
             "径方向力の距離モード。Falloff=距離で減衰（指数で制御）、Constant=半径内一定（均一、端で硬カット）。")]
        private Liquid2DForceFieldMode forceMode = Liquid2DForceFieldMode.Falloff;

        protected Transform CachedTransform;

        /// <summary>作用半径。 // Effect radius. // 作用半径。</summary>
        public float Radius { get => radius; set => radius = Mathf.Max(0f, value); }

        /// <summary>强度（&gt;0 吸引，&lt;0 排斥）。 // Strength (&gt;0 attract, &lt;0 repel). // 強度。</summary>
        public float Strength { get => strength; set => strength = value; }

        /// <summary>速度衰减系数。 // Velocity damping. // 速度減衰係数。</summary>
        public float VelocityDamping { get => velocityDamping; set => velocityDamping = Mathf.Clamp01(value); }

        /// <summary>切向（旋流）强度（&gt;0 逆时针，&lt;0 顺时针）。 // Tangential swirl strength (&gt;0 CCW, &lt;0 CW). // 旋流強度。</summary>
        public float SwirlStrength { get => swirlStrength; set => swirlStrength = value; }

        /// <summary>力场内重力衰减（0~1）。 // Gravity attenuation inside field (0~1). // 力場内重力減衰。</summary>
        public float GravityAttenuation { get => gravityAttenuation; set => gravityAttenuation = Mathf.Clamp01(value); }

        /// <summary>力随距离的衰减指数。 // Force falloff exponent. // 力の減衰指数。</summary>
        public float Falloff { get => falloff; set => falloff = Mathf.Max(0f, value); }

        /// <summary>径向力距离模式。 // Radial-force distance mode. // 径方向力の距離モード。</summary>
        public Liquid2DForceFieldMode ForceMode { get => forceMode; set => forceMode = value; }

        /// <summary>
        /// 用本组件当前的高级参数（旋流/重力衰减/衰减指数/模式）填充力场数据的对应字段。子类在 <see cref="TryGetField"/>
        /// 里设置好 center/radius/strength/velocityDamping 后调用，避免重复赋值。
        /// Fill the force-field data's advanced fields (swirl/gravity-attenuation/falloff/mode) from this component's current
        /// values. Subclasses call this after setting center/radius/strength/velocityDamping in <see cref="TryGetField"/>.
        /// 本コンポーネントの詳細パラメータでデータの該当フィールドを埋めます。
        /// </summary>
        protected void ApplyAdvanced(ref Liquid2DForceFieldData data)
        {
            data.swirlStrength = swirlStrength;
            data.gravityAttenuation = gravityAttenuation;
            data.falloff = falloff;
            data.mode = forceMode;
        }

        protected virtual void OnEnable()
        {
            CachedTransform = transform;
            Liquid2DForceFieldRegistry.Register(this);
        }

        protected virtual void OnDisable()
        {
            Liquid2DForceFieldRegistry.Unregister(this);
        }

        /// <summary>
        /// 提供本帧的力场参数。返回 false 表示本帧不施力（该源被跳过）。
        /// Provide this frame's force-field parameters. Returning false means no force this frame (the source is skipped).
        /// 本フレームの力場パラメータを提供します。false を返すと本フレームは施力しません。
        /// </summary>
        public abstract bool TryGetField(out Liquid2DForceFieldData data);
    }
}

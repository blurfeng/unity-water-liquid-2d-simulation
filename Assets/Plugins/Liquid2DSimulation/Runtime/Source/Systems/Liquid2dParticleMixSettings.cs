using System;
using UnityEngine;
using Fs.Liquid2D.Localization;
using UnityEngine.Serialization;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体粒子混合设置。用于定义不同颜色流体粒子颜色混合时的行为。
    /// Fluid particle mix settings. Used to define the behavior when different colored fluid particles mix colors.
    /// 流体パーティクルミックス設定。異なる色の流体パーティクルが色を混ぜるときの動作を定義するために使用されます。
    /// </summary>
    [Serializable]
    public class Liquid2dParticleMixSettings
    {
        [LocalizationTooltip(
             "启用颜色混合。如果启用，当不同颜色的流体粒子碰撞时，它们的颜色会混合在一起。只有相同 Liquid2DLayer 流体层的粒子才会混合颜色。",
             "Enable color mixing. If enabled, when different colored fluid particles collide, their colors will mix together. Only particles in the same Liquid2DLayer will mix colors.",
             "色の混合を有効にします。有効にすると、異なる色の流体パーティクルが衝突すると、その色が混ざり合います。同じLiquid2DLayerに属するパーティクルのみが色を混ぜます。")]
        public bool mixColors = false;
        
        [Range(0f, 1f)]
        [LocalizationTooltip(
            "颜色混合速度。0为不混合，1为瞬间混合。",
            "Color mix speed. 0 means no mixing, 1 means instant mixing.",
            "色の混合速度。0は混合しない、1は即座に混合します。")]
        public float mixColorsSpeed = 0.8f;
        
        [LocalizationTooltip(
            "是否根据粒子运动混合颜色。如果启用，粒子在运动时会更快地混合颜色。",
            "Whether to mix colors based on particle movement. If enabled, particles will mix colors faster when moving.",
            "粒子の動きに基づいて色を混ぜるかどうか。有効にすると、粒子は動いているときにより速く色を混ぜます。")]
        public bool mixColorsWithMovement = false;
        
        [LocalizationTooltip(
            "用于根据粒子速度调整颜色混合速度的最大速度。仅在启用根据粒子运动混合颜色时使用。",
            "Maximum speed used to adjust color mix speed based on particle velocity. Only used when mixing colors based on particle movement is enabled.",
            "粒子の速度に基づいて色の混合速度を調整するために使用される最大速度。粒子の動きに基づいて色を混ぜることが有効になっている場合にのみ使用されます。")]
        public float mixColorsWithMovementMaxSpeed = 100f;

        [LocalizationTooltip(
             "与接触的流体粒子混合颜色。只开启mixColors时，只在粒子碰撞时混合颜色。所有的粒子相对静止时不会混合颜色。开启此选项后，粒子在接触时也会混合颜色，即使它们没有碰撞。",
             "Mix colors with contacting fluid particles. When only mixColors is enabled, colors are mixed only when particles collide. Colors will not mix when all particles are relatively stationary. Enabling this option allows particles to mix colors when in contact, even if they are not colliding.",
             "接触している流体パーティクルと色を混ぜます。mixColorsのみが有効になっている場合、粒子が衝突したときにのみ色が混ざります。すべての粒子が比較的静止している場合、色は混ざりません。このオプションを有効にすると、粒子が接触しているときに衝突していなくても色を混ぜることができます。")]
        public bool mixWithContactParticles = true;
        
        [LocalizationTooltip(
             "与接触的流体粒子混合颜色的时间间隔（每个接触的粒子单独计时）。",
             "Time interval for mixing)] colors with contacting fluid particles (each contacting particle is timed separately).",
             "接触している流体パーティクルと色を混ぜるための時間間隔（各接触パーティクルは個別にタイミングされます）。")]
        public float mixWithContactParticlesInternal = 0.1f;

        [LocalizationTooltip(
             "与接触的流体粒子进行混色的确认检查间隔。",
             "Check interval for confirming color mixing with contacting fluid particles.",
             "接触している流体パーティクルとの色の混合を確認するためのチェック間隔。")]
        public float mixWithContactParticlesCheckInternal = 0.1f;
    }
}
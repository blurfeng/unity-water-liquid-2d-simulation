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
    public class Liquid2DParticleMixSettings
    {
        [LocalizationTooltip(
             "启用颜色混合。如果启用，当不同颜色的流体粒子碰撞时，它们的颜色会混合在一起。只有相同 Liquid2DLayer 流体层的粒子才会混合颜色。",
             "Enable color mixing. If enabled, when different colored fluid particles collide, their colors will mix together. Only particles in the same Liquid2DLayer will mix colors.",
             "色の混合を有効にします。有効にすると、異なる色の流体パーティクルが衝突すると、その色が混ざり合います。同じLiquid2DLayerに属するパーティクルのみが色を混ぜます。")]
        public bool mixColors = false;

        [LocalizationTooltip(
            "颜色混合半径倍率，基础为CircleCollider2D的半径。",
            "Color mix radius rate, based on the radius of CircleCollider2D.",
            "色の混合半径倍率、CircleCollider2Dの半径に基)]づきます。")]
        public float mixColorsRadiusRate = 2f;
        
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
             "静止时混合颜色。当仅启用mixColors时，只有运动的粒子才会混合颜色。启用此选项可在自身静止时也与接触的粒子混合颜色。",
             "Mix colors when stationary. When only mixColors is enabled, only moving particles will mix colors. Enabling this option allows mixing colors with contacting particles even when stationary.",
             "静止しているときに色を混ぜる。mixColorsのみが有効になっている場合、動いている粒子のみが色を混ぜます。このオプションを有効にすると、静止している場合でも接触している粒子と色を混ぜることができます。")]
        public bool mixColorsWhenStationary = true;
        
        [LocalizationTooltip(
             "与接触的流体粒子混合颜色的时间间隔（每个接触的粒子单独计时）。",
             "Time interval for mixing)] colors with contacting fluid particles (each contacting particle is timed separately).",
             "接触している流体パーティクルと色を混ぜるための時間間隔（各接触パーティクルは個別にタイミングされます）。")]
        public float mixColorsWithContactParticlesInternal = 0.1f;

        [LocalizationTooltip(
             "与接触的流体粒子进行混色的确认检查间隔。",
             "Check interval for confirming color mixing with contacting fluid particles.",
             "接触している流体パーティクルとの色の混合を確認するためのチェック間隔。")]
        public float mixColorsWithContactParticlesCheckInternal = 0.1f;
    }
}
namespace Fs.Liquid2D
{
    /// <summary>
    /// 粒子基础色来源模式（描述符层，逐粒子决定每个粒子写入流体纹理的颜色）。与整体覆盖色
    /// <see cref="ECoverColorMode"/> 正交：本模式决定每粒子色，覆盖色随后叠加在整片水体上。
    /// Per-particle base color source mode (descriptor level; decides each particle's color written into the fluid texture).
    /// Orthogonal to the whole-body <see cref="ECoverColorMode"/>: this mode decides the per-particle color, the cover color
    /// is layered over the whole fluid afterwards.
    /// 粒子基礎色のソースモード（記述子層、各粒子が流体テクスチャに書き込む色を決定）。全体カバー色
    /// <see cref="ECoverColorMode"/> とは直交します。
    /// </summary>
    public enum EParticleColorMode
    {
        /// <summary>
        /// 简单模式：使用单一的 <see cref="Liquid2DParticleRenderSettings.Color"/>（并叠加运行时混色）。默认行为。
        /// Simple: use the single <see cref="Liquid2DParticleRenderSettings.Color"/> (with runtime color mixing). The default behavior.
        /// シンプル：単一の <see cref="Liquid2DParticleRenderSettings.Color"/> を使用（実行時混色を加味）。既定動作。
        /// </summary>
        Simple = 0,

        /// <summary>
        /// 颜色映射：按每粒子标量（速度或空气混入量，见 <see cref="EGradientColorSource"/>）采样 Gradient。
        /// 此模式下不使用运行时混色（颜色每帧按物理量重算）。
        /// Color mapping: sample a Gradient by a per-particle scalar (speed or trapped-air, see <see cref="EGradientColorSource"/>).
        /// Runtime color mixing does not apply in this mode (color is recomputed each frame from a physics quantity).
        /// カラーマッピング：粒子ごとのスカラー（速度または混入空気量、<see cref="EGradientColorSource"/> 参照）で Gradient をサンプリング。
        /// このモードでは実行時混色は適用されません。
        /// </summary>
        Gradient = 1,
    }

    /// <summary>
    /// <see cref="EParticleColorMode.Gradient"/> 采样所用的每粒子标量来源。
    /// Per-particle scalar source used for <see cref="EParticleColorMode.Gradient"/> sampling.
    /// <see cref="EParticleColorMode.Gradient"/> のサンプリングに使う粒子ごとのスカラーソース。
    /// </summary>
    public enum EGradientColorSource
    {
        /// <summary>
        /// 速度大小：t = |velocity| / GradientSpeedMax（0..1）。快的粒子偏向 Gradient 右端。
        /// Speed magnitude: t = |velocity| / GradientSpeedMax (0..1). Faster particles map to the right of the Gradient.
        /// 速度の大きさ：t = |velocity| / GradientSpeedMax（0..1）。速い粒子ほど Gradient の右側へ。
        /// </summary>
        Speed = 0,

        /// <summary>
        /// 空气混入量（泡沫）：由 SPH 密度亏空推得。densityRatio = 密度/静止密度（水滴≈0.1、自由表面≈0.5、内部≈1），
        /// t = saturate((FoamStart - densityRatio) / (FoamStart - FoamEnd))。水体内部→0（无泡），表面/飞溅→1（起泡）。
        /// Trapped air (foam): derived from the SPH density deficit. densityRatio = density / rest density (droplet≈0.1, free
        /// surface≈0.5, interior≈1); t = saturate((FoamStart - densityRatio) / (FoamStart - FoamEnd)). Interior→0 (no foam),
        /// surface/spray→1 (foaming).
        /// 混入空気量（泡）：SPH 密度不足から算出。densityRatio = 密度/静止密度（水滴≈0.1、自由表面≈0.5、内部≈1）、
        /// t = saturate((FoamStart - densityRatio) / (FoamStart - FoamEnd))。内部→0（泡なし）、表面/飛沫→1（泡立ち）。
        /// </summary>
        Foam = 1,

        /// <summary>
        /// 泡沫 × 速度门控：以 Foam 为基础，再乘以速度因子，使静止的低密度水面不发泡、只有运动时（浪/飞溅）才起泡。
        /// t = saturate(foamT) × saturate(|velocity| / GradientSpeedMax)。同时使用 FoamStart/FoamEnd 与 GradientSpeedMax。
        /// 解决纯 Foam 模式下静止水面因密度低而恒发白的问题——真实泡沫在流体趋于静止时应逐渐消散。
        /// Foam × speed gate: Foam as the base, multiplied by a speed factor, so a still low-density surface does not foam and
        /// foam only appears with motion (waves/spray). t = saturate(foamT) × saturate(|velocity| / GradientSpeedMax). Uses both
        /// FoamStart/FoamEnd and GradientSpeedMax. Fixes pure Foam's always-white still surface — real foam dissipates as the
        /// fluid comes to rest.
        /// 泡 × 速度ゲート：Foam を基礎に速度係数を乗算。静止した低密度水面は泡立たず、運動時（波/飛沫）のみ泡立ちます。
        /// t = saturate(foamT) × saturate(|velocity| / GradientSpeedMax)。FoamStart/FoamEnd と GradientSpeedMax を併用。
        /// </summary>
        FoamWithSpeed = 2,
    }

    /// <summary>
    /// 整体覆盖色模式（Feature/Volume 层，作用于合成后的整片水体，叠加在每粒子色之上）。
    /// Whole-body cover color mode (feature/volume level; applied to the composited fluid body, layered over per-particle color).
    /// 全体カバー色モード（Feature/Volume 層、合成後の流体全体に適用、粒子色の上に重畳）。
    /// </summary>
    public enum ECoverColorMode
    {
        /// <summary>
        /// 不覆盖：保留每粒子色（含渐变/混色）。
        /// None: keep the per-particle color (including gradient/mixing).
        /// カバーなし：粒子ごとの色（渐变/混色を含む）を保持。
        /// </summary>
        None = 0,

        /// <summary>
        /// 覆盖：用 <see cref="Liquid2DRenderFeatureSettings.CoverColor"/> 覆盖整片水体色调（alpha 为强度）。
        /// Override: tint the whole fluid body with <see cref="Liquid2DRenderFeatureSettings.CoverColor"/> (alpha is intensity).
        /// 上書き：<see cref="Liquid2DRenderFeatureSettings.CoverColor"/> で流体全体を上書き（alpha が強度）。
        /// </summary>
        Override = 1,
    }
}

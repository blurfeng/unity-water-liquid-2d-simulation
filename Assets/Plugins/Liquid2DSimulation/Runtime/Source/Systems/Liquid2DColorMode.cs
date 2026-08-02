namespace Fs.Liquid2D
{
    /// <summary>
    /// 粒子基础色来源模式（描述符层，逐粒子决定每个粒子写入流体纹理的颜色）。
    /// Per-particle base color source mode (descriptor level; decides each particle's color written into the fluid texture).
    /// 粒子基礎色のソースモード（記述子層、各粒子が流体テクスチャに書き込む色を決定）。
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
        /// 密度：按 SPH 密度亏空采色（静态贴图，只看当前密度）。densityRatio = 密度/静止密度（水滴≈0.1、自由表面≈0.5、内部≈1），
        /// t = saturate((FoamStart - densityRatio) / (FoamStart - FoamEnd))。密度越低采越靠右，内部→0。
        /// 适合「表面恒有一层白」（如奶泡）。需要「低密度处发生冲击才产泡、静止后消退」的动态泡沫请用 <see cref="Impact"/>。
        /// Density: color by SPH density deficit (static, current density only). densityRatio = density / rest (droplet≈0.1, surface≈0.5,
        /// interior≈1); t = saturate((FoamStart - densityRatio) / (FoamStart - FoamEnd)). Lower density → further right, interior → 0.
        /// Good for an always-on surface tint (e.g. milk foam). For dynamic "foam only on impact in low-density regions, then fade" use <see cref="Impact"/>.
        /// 密度：SPH 密度不足で採色（静的、現在密度のみ）。表面の常時の白に。動的な泡は <see cref="Impact"/>。
        /// </summary>
        Density = 1,

        /// <summary>
        /// 密度门 × 冲击（先按密度筛选有效区域，再由「冲击=密度上升率」驱动颜色）：真实泡沫是流体在「表面附近的有效区域」被「快速压实」时裹挟空气产生的。
        /// 生成量 = 密度区域门 × 冲击：区域门 = 密度亏空(FoamStart/End；FoamStart 卡在内部/水底密度以下→排除高密度内部，只留表面附近)；
        /// 冲击 = saturate((Δ平滑密度/静止密度)×ImpactStrength)（密度正向上升=被压实）。F = max(F·decay, 生成量)，由 FoamPersistence 控制消退。
        /// 内部/水底（区域门0）不发白；平静表面（无冲击）不发白、已有泡渐隐；孤立飞滴（未被压实=冲击0）不发白；只有「有效区域内发生压实」才产泡，静止后消退。
        /// ⚠FoamStart 要卡在水底密度以下（水底往往≈1.0，则设 0.95~1.0）才能排除内部。ImpactRiseMin=冲击死区，扣除上升率下限以排除「变化很小」的轻微扰动伪冲击波。参数：FoamStart/End + ImpactStrength + ImpactRiseMin + FoamPersistence。
        /// Density × Impact (filter the valid region by density, then let impact = density rise rate drive the color). generation = densityGate × impact:
        /// densityGate = density deficit (FoamStart/End; FoamStart below the interior/bottom density to exclude it); impact = saturate((Δsmoothed-density/rest − ImpactRiseMin)×ImpactStrength).
        /// F = max(F·decay, generation), fade by FoamPersistence. Interior/bottom (gate 0), calm surface (no impact) and isolated droplets (not compressed) don't whiten.
        /// ⚠FoamStart must be below the bottom density (≈1.0 → 0.95~1.0). ImpactRiseMin = impact deadzone, subtracting a rise-rate floor to exclude weak 'tiny change' shockwave-like whitening. Params: FoamStart/End + ImpactStrength + ImpactRiseMin + FoamPersistence.
        /// 密度 × 衝撃：密度で有効領域を絞り、衝撃（密度上昇率）で色を駆動。⚠FoamStart は水底密度の下に。ImpactRiseMin=衝撃デッドゾーン（微弱な擾乱の偽白飛びを除外）。パラメータ：FoamStart/End + ImpactStrength + ImpactRiseMin + FoamPersistence。
        /// </summary>
        DensityWithImpact = 2,

        /// <summary>
        /// 密度门 × 速度（先按密度筛选有效区域，再由「速度」驱动颜色）：即旧的 FoamWithSpeed。低密度的有效区域里，运动越快越起泡；配合持久度可在运动停止后逐渐消退。
        /// 生成量 = 密度区域门 × 速度门控：区域门 = 密度亏空(FoamStart/End，同 DensityWithImpact)；速度门控 = saturate((平滑速度 − SpeedMin)/(SpeedMax − SpeedMin))。
        /// F = max(F·decay, 生成量)，由 FoamPersistence 控制消退。与 DensityWithImpact 的区别只在动态因子：这里用「速度」而不是「密度上升率」。
        /// 参数：FoamStart/End（密度区域门）+ SpeedMin/SpeedMax（速度门控）+ FoamPersistence。
        /// Density × Speed (filter the valid region by density, then let speed drive the color): the old FoamWithSpeed. In the low-density valid region, faster motion foams more; with persistence it fades after motion stops.
        /// generation = densityGate × speedGate: densityGate = density deficit (FoamStart/End, same as DensityWithImpact); speedGate = saturate((smoothedSpeed − SpeedMin)/(SpeedMax − SpeedMin)).
        /// F = max(F·decay, generation), fade by FoamPersistence. Differs from DensityWithImpact only in the dynamic factor (speed instead of density rise rate).
        /// Params: FoamStart/End + SpeedMin/SpeedMax + FoamPersistence.
        /// 密度 × 速度（旧 FoamWithSpeed）：低密度の有効領域で速いほど泡立ち、持続度で運動停止後に消える。動的因子が速度である点のみ異なる。パラメータ：FoamStart/End + SpeedMin/SpeedMax + FoamPersistence。
        /// </summary>
        DensityWithSpeed = 3,

        /// <summary>
        /// 纯冲击（只看密度变化率，无密度区域门）：生成量 = 冲击 = saturate((Δ平滑密度/静止密度 − ImpactRiseMin) × ImpactStrength)。
        /// 与 <see cref="DensityWithImpact"/> 的唯一区别是【不做密度区域门(FoamStart/End)筛选】——任意位置（含流体内部/水底）只要发生快速压实（密度快速上升）就发白，不再局限于表面附近。
        /// 适合「整体受冲击即整体发白」而非「只在表面起泡」的效果。静止区域密度稳定(上升率≈0)不发白；配合 ImpactRiseMin 死区滤除轻微扰动；F = max(F·decay, 生成量) 由 FoamPersistence 控制消退。
        /// 参数：ImpactStrength + ImpactRiseMin + FoamPersistence（不使用 FoamStart/End 与 SpeedMin/Max）。
        /// Pure impact (density rise rate only, no density region gate): generation = impact = saturate((Δsmoothed-density/rest − ImpactRiseMin) × ImpactStrength).
        /// The only difference from <see cref="DensityWithImpact"/> is that it does NOT apply the density region gate (FoamStart/End): any location (including fluid interior/bottom) whitens when rapidly compressed, not just near the surface.
        /// Good for "the whole body whitens on impact" rather than "foam only at the surface". Static regions (rise≈0) don't whiten; ImpactRiseMin filters weak disturbances; F = max(F·decay, generation), fade by FoamPersistence.
        /// Params: ImpactStrength + ImpactRiseMin + FoamPersistence (FoamStart/End and SpeedMin/Max unused).
        /// 純衝撃（密度変化率のみ、密度領域ゲート無し）：generation = saturate((Δ平滑密度/静止密度 − ImpactRiseMin)×ImpactStrength)。<see cref="DensityWithImpact"/> との違いは領域ゲート(FoamStart/End)を行わない点——任意の位置（内部/水底含む）で急激な圧縮があれば白くなる。パラメータ：ImpactStrength + ImpactRiseMin + FoamPersistence。
        /// </summary>
        Impact = 4,
    }
}

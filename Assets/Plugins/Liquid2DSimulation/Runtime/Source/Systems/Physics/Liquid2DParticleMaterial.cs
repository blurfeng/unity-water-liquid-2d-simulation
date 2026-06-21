using System;
using Fs.Liquid2D.Localization;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体材质参数。通过参数差异模拟水 / 熔岩 / 泡沫 / 沙子等不同介质。逐描述符（typeId）生效，
    /// 求解时与全局 <see cref="SolverParams"/> 合并（材质项覆盖/缩放全局项）。
    /// Fluid material parameters. Different presets simulate water / lava / foam / sand. Applied per descriptor (typeId)
    /// and merged with the global <see cref="SolverParams"/> at solve time (material values override/scale global ones).
    /// 流体マテリアルパラメータ。パラメータの違いで水 / 溶岩 / 泡 / 砂などを表現します。記述子（typeId）ごとに適用され、
    /// 解法時にグローバル <see cref="SolverParams"/> とマージされます（マテリアル値がグローバル値を上書き/スケール）。
    /// </summary>
    [Serializable]
    public class Liquid2DParticleMaterial
    {
        [Min(0.0001f), LocalizationTooltip(
             "粒子质量。影响受力与喷射初速（初速 = 冲量 / 质量）。熔岩/沙子偏大，泡沫偏小。",
             "Particle mass. Affects forces and ejection speed (speed = impulse / mass). Larger for lava/sand, smaller for foam.",
             "粒子の質量。受ける力と噴射初速（初速 = 力積 / 質量）に影響。溶岩/砂は大きめ、泡は小さめ。")]
        public float Mass = 1f;

        [Min(0.01f), LocalizationTooltip(
             "SPH 静止密度倍率（模拟调参，非物理质量密度）。缩放全局 TargetDensity，影响粒子堆积松紧与可压缩观感。此参数不参与浮力计算——浮力所用的流体质量密度请在下方 Density 字段单独设置。",
             "SPH rest density multiplier (simulation tuning, not physical mass density). Scales the global TargetDensity to control packing tightness and compressibility feel. This does NOT affect buoyancy — set fluid mass density for buoyancy in the Density field below.",
             "SPH 静止密度の倍率（シミュレーション調整用、物理的質量密度ではありません）。グローバル TargetDensity をスケールし、粒子の堆積具合と圧縮感を制御します。浮力には影響しません——浮力用の流体質量密度は下の Density フィールドで設定してください。")]
        public float TargetDensityScale = 1f;

        [Range(0f, 1f), LocalizationTooltip(
             "粘性（XSPH）。越大越粘稠、流动越缓（水低、熔岩高）。",
             "Viscosity (XSPH). Higher is thicker and flows slower (low for water, high for lava).",
             "粘性（XSPH）。大きいほど粘く流れが遅い（水は低、溶岩は高）。")]
        public float Viscosity = 0.01f;

        [Range(0f, 1f), LocalizationTooltip(
             "内聚力/表面张力。越大越易结团、成液滴（泡沫高，沙子为 0）。",
             "Cohesion/surface tension. Higher clumps into droplets more (high for foam, 0 for sand).",
             "内聚力/表面張力。大きいほど結団して液滴になりやすい（泡は高、砂は 0）。")]
        public float Cohesion = 0.1f;

        [Range(0f, 1f), LocalizationTooltip(
             "摩擦。与碰撞体接触时的切向阻尼，沙子高用于堆出休止角。",
             "Friction. Tangential damping on collider contact; high for sand to form a repose angle.",
             "摩擦。コライダー接触時の接線方向減衰。砂では高くして安息角を形成します。")]
        public float Friction;

        [Range(0f, 1f), LocalizationTooltip(
             "回弹。与碰撞体碰撞时的法向反弹系数。",
             "Restitution. Normal bounciness on collider collision.",
             "反発。コライダー衝突時の法線方向の反発係数。")]
        public float Restitution;

        [LocalizationTooltip(
             "重力缩放。1 为正常下落，0 为悬浮，负值上浮（泡沫可设 0 或负）。",
             "Gravity scale. 1 = normal fall, 0 = float, negative = rise (foam can be 0 or negative).",
             "重力スケール。1 は通常落下、0 は浮遊、負値は上昇（泡は 0 か負に）。")]
        public float GravityScale = 1f;

        [Min(0f), LocalizationTooltip(
             "流体质量密度（专用于阿基米德浮力比对）。物体密度（质量/体积）小于此值则漂浮、大于则下沉。水≈1，熔岩更大，泡沫更小。注意：此字段与上方 TargetDensityScale（SPH 堆积参数）相互独立——两者编码不同的物理属性，需分别调节。",
             "Fluid mass density (used exclusively for Archimedes buoyancy comparison). A body floats when its density (mass/volume) is below this value, sinks when above. Water≈1, lava higher, foam lower. Note: this is independent of TargetDensityScale (an SPH packing parameter) — they encode different physical properties and must be tuned separately.",
             "流体の質量密度（アルキメデス浮力の比較専用）。物体の密度（質量/体積）がこれより小さいと浮き、大きいと沈む。水≈1、溶岩は大、泡は小。注意：上の TargetDensityScale（SPH 堆積パラメータ）とは独立しています——異なる物理属性を表し、それぞれ個別に調整が必要です。")]
        public float Density = 1f;

        /// <summary>
        /// 转为 Burst Job 可用的 blittable 数据。
        /// Convert to blittable data usable in Burst jobs.
        /// Burst Job で使える blittable データに変換します。
        /// </summary>
        public Liquid2DMaterialData ToData() => new Liquid2DMaterialData
        {
            InvMass = Mass > 0f ? 1f / Mass : 0f,
            TargetDensityScale = Mathf.Max(0.01f, TargetDensityScale),
            Viscosity = Viscosity,
            Cohesion = Cohesion,
            Friction = Friction,
            Restitution = Restitution,
            GravityScale = GravityScale,
            Density = Density,
        };

        // —— 预设 // Presets // プリセット ——

        /// <summary>水：低粘、低张力、正常重力。 // Water: low viscosity/tension, normal gravity. // 水：低粘性/低張力、通常重力。</summary>
        public static Liquid2DParticleMaterial Water() => new Liquid2DParticleMaterial
            { Mass = 1f, TargetDensityScale = 1f, Viscosity = 0.01f, Cohesion = 0.08f, Friction = 0f, Restitution = 0f, GravityScale = 1f, Density = 1f };

        /// <summary>熔岩：高粘、中张力、高质量、缓动。 // Lava: high viscosity/mass, medium tension. // 溶岩：高粘性/高質量、中張力。</summary>
        public static Liquid2DParticleMaterial Lava() => new Liquid2DParticleMaterial
            { Mass = 3f, TargetDensityScale = 1.1f, Viscosity = 0.6f, Cohesion = 0.25f, Friction = 0.05f, Restitution = 0f, GravityScale = 1f, Density = 3f };

        /// <summary>泡沫：高张力、低质量、低/零重力，易结团上浮。 // Foam: high tension, low mass, low gravity. // 泡：高張力、低質量、低重力。</summary>
        public static Liquid2DParticleMaterial Foam() => new Liquid2DParticleMaterial
            { Mass = 0.3f, TargetDensityScale = 0.8f, Viscosity = 0.05f, Cohesion = 0.6f, Friction = 0f, Restitution = 0.1f, GravityScale = 0.1f, Density = 0.3f };

        /// <summary>沙子：高摩擦、低粘、零张力（颗粒近似，真实颗粒需后续摩擦约束扩展）。 // Sand: high friction, no tension (granular approximation). // 砂：高摩擦、無張力（近似）。</summary>
        public static Liquid2DParticleMaterial Sand() => new Liquid2DParticleMaterial
            { Mass = 1.5f, TargetDensityScale = 1f, Viscosity = 0.2f, Cohesion = 0f, Friction = 0.6f, Restitution = 0f, GravityScale = 1f, Density = 1.5f };
    }

    /// <summary>
    /// 材质参数的 blittable 形式，按 typeId 存入 NativeArray 供 Burst Job 读取。
    /// Blittable form of material parameters, stored in a NativeArray by typeId for Burst job reads.
    /// マテリアルパラメータの blittable 形式。typeId ごとに NativeArray に保存し Burst Job が読み取ります。
    /// </summary>
    public struct Liquid2DMaterialData
    {
        public float InvMass;
        public float TargetDensityScale;
        public float Viscosity;
        public float Cohesion;
        public float Friction;
        public float Restitution;
        public float GravityScale;
        public float Density; // 质量密度（阿基米德浮力用）。 // mass density (Archimedes buoyancy). // 質量密度（浮力用）。

        /// <summary>默认材质（等价于水的中性参数）。 // Default (water-like neutral). // デフォルト（水相当）。</summary>
        public static Liquid2DMaterialData Default => new Liquid2DMaterialData
            { InvMass = 1f, TargetDensityScale = 1f, Viscosity = 0.01f, Cohesion = 0.08f, Friction = 0f, Restitution = 0f, GravityScale = 1f, Density = 1f };
    }
}

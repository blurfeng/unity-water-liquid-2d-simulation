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
        public float mass = 1f;

        [Min(0.01f), LocalizationTooltip(
             "静止密度倍率。相对全局静止密度的缩放，影响堆积体积与可压缩观感。",
             "Rest density multiplier relative to the global rest density; affects packing volume and compressibility feel.",
             "静止密度の倍率。グローバル静止密度に対するスケールで、堆積体積と圧縮感に影響します。")]
        public float restDensityScale = 1f;

        [Range(0f, 1f), LocalizationTooltip(
             "粘性（XSPH）。越大越粘稠、流动越缓（水低、熔岩高）。",
             "Viscosity (XSPH). Higher is thicker and flows slower (low for water, high for lava).",
             "粘性（XSPH）。大きいほど粘く流れが遅い（水は低、溶岩は高）。")]
        public float viscosity = 0.01f;

        [Range(0f, 1f), LocalizationTooltip(
             "内聚力/表面张力。越大越易结团、成液滴（泡沫高，沙子为 0）。",
             "Cohesion/surface tension. Higher clumps into droplets more (high for foam, 0 for sand).",
             "内聚力/表面張力。大きいほど結団して液滴になりやすい（泡は高、砂は 0）。")]
        public float cohesion = 0.1f;

        [Range(0f, 1f), LocalizationTooltip(
             "摩擦。与碰撞体接触时的切向阻尼，沙子高用于堆出休止角。",
             "Friction. Tangential damping on collider contact; high for sand to form a repose angle.",
             "摩擦。コライダー接触時の接線方向減衰。砂では高くして安息角を形成します。")]
        public float friction = 0f;

        [Range(0f, 1f), LocalizationTooltip(
             "回弹。与碰撞体碰撞时的法向反弹系数。",
             "Restitution. Normal bounciness on collider collision.",
             "反発。コライダー衝突時の法線方向の反発係数。")]
        public float restitution = 0f;

        [LocalizationTooltip(
             "重力缩放。1 为正常下落，0 为悬浮，负值上浮（泡沫可设 0 或负）。",
             "Gravity scale. 1 = normal fall, 0 = float, negative = rise (foam can be 0 or negative).",
             "重力スケール。1 は通常落下、0 は浮遊、負値は上昇（泡は 0 か負に）。")]
        public float gravityScale = 1f;

        /// <summary>
        /// 转为 Burst Job 可用的 blittable 数据。
        /// Convert to blittable data usable in Burst jobs.
        /// Burst Job で使える blittable データに変換します。
        /// </summary>
        public Liquid2DMaterialData ToData() => new Liquid2DMaterialData
        {
            invMass = mass > 0f ? 1f / mass : 0f,
            restDensityScale = Mathf.Max(0.01f, restDensityScale),
            viscosity = viscosity,
            cohesion = cohesion,
            friction = friction,
            restitution = restitution,
            gravityScale = gravityScale,
        };

        // —— 预设 // Presets // プリセット ——

        /// <summary>水：低粘、低张力、正常重力。 // Water: low viscosity/tension, normal gravity. // 水：低粘性/低張力、通常重力。</summary>
        public static Liquid2DParticleMaterial Water() => new Liquid2DParticleMaterial
            { mass = 1f, restDensityScale = 1f, viscosity = 0.01f, cohesion = 0.08f, friction = 0f, restitution = 0f, gravityScale = 1f };

        /// <summary>熔岩：高粘、中张力、高质量、缓动。 // Lava: high viscosity/mass, medium tension. // 溶岩：高粘性/高質量、中張力。</summary>
        public static Liquid2DParticleMaterial Lava() => new Liquid2DParticleMaterial
            { mass = 3f, restDensityScale = 1.1f, viscosity = 0.6f, cohesion = 0.25f, friction = 0.05f, restitution = 0f, gravityScale = 1f };

        /// <summary>泡沫：高张力、低质量、低/零重力，易结团上浮。 // Foam: high tension, low mass, low gravity. // 泡：高張力、低質量、低重力。</summary>
        public static Liquid2DParticleMaterial Foam() => new Liquid2DParticleMaterial
            { mass = 0.3f, restDensityScale = 0.8f, viscosity = 0.05f, cohesion = 0.6f, friction = 0f, restitution = 0.1f, gravityScale = 0.1f };

        /// <summary>沙子：高摩擦、低粘、零张力（颗粒近似，真实颗粒需后续摩擦约束扩展）。 // Sand: high friction, no tension (granular approximation). // 砂：高摩擦、無張力（近似）。</summary>
        public static Liquid2DParticleMaterial Sand() => new Liquid2DParticleMaterial
            { mass = 1.5f, restDensityScale = 1f, viscosity = 0.2f, cohesion = 0f, friction = 0.6f, restitution = 0f, gravityScale = 1f };
    }

    /// <summary>
    /// 材质参数的 blittable 形式，按 typeId 存入 NativeArray 供 Burst Job 读取。
    /// Blittable form of material parameters, stored in a NativeArray by typeId for Burst job reads.
    /// マテリアルパラメータの blittable 形式。typeId ごとに NativeArray に保存し Burst Job が読み取ります。
    /// </summary>
    public struct Liquid2DMaterialData
    {
        public float invMass;
        public float restDensityScale;
        public float viscosity;
        public float cohesion;
        public float friction;
        public float restitution;
        public float gravityScale;

        /// <summary>默认材质（等价于水的中性参数）。 // Default (water-like neutral). // デフォルト（水相当）。</summary>
        public static Liquid2DMaterialData Default => new Liquid2DMaterialData
            { invMass = 1f, restDensityScale = 1f, viscosity = 0.01f, cohesion = 0.08f, friction = 0f, restitution = 0f, gravityScale = 1f };
    }
}

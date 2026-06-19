using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 模拟计算平台模式。CPU 为 Unity Job System + Burst；GPU 为 Compute Shader（数据常驻 GraphicsBuffer）。
    /// Simulation compute platform mode. CPU = Unity Job System + Burst; GPU = Compute Shader (data resident in GraphicsBuffer).
    /// シミュレーション計算プラットフォームモード。CPU は Unity Job System + Burst、GPU は Compute Shader（GraphicsBuffer 常駐）。
    /// </summary>
    public enum Liquid2DSimulationMode
    {
        /// <summary>CPU：Unity Job System + Burst。 // CPU: Unity Job System + Burst. // CPU：Unity Job System + Burst。</summary>
        Cpu = 0,
        /// <summary>GPU：Compute Shader（数据常驻 GPU）。 // GPU: Compute Shader (GPU-resident data). // GPU：Compute Shader（GPU 常駐）。</summary>
        Gpu = 1,
    }

    /// <summary>
    /// SPH 双密度求解全局参数（密度 + 近密度压力）。逐描述符的材质（<see cref="Liquid2DMaterialData"/>）在求解时与这些全局项合并：
    /// restDensityScale 缩放 targetDensity，material.viscosity 叠加 viscosityStrength，cohesion 调制近密度压力，
    /// friction/restitution 影响碰撞，gravityScale 缩放重力。
    /// Global SPH dual-density solver parameters (density + near-density pressure). Per-descriptor materials
    /// (<see cref="Liquid2DMaterialData"/>) merge with these at solve time: restDensityScale scales targetDensity,
    /// material.viscosity adds to viscosityStrength, cohesion modulates near-pressure, friction/restitution affect
    /// collisions, gravityScale scales gravity.
    /// SPH デュアル密度ソルバーのグローバルパラメータ（密度 + 近密度圧力）。記述子ごとのマテリアルは解法時にマージされます。
    /// </summary>
    public struct SolverParams
    {
        /// <summary>重力加速度（世界）。 // Gravity (world). // 重力（ワールド）。</summary>
        public float2 gravity;

        /// <summary>光滑核半径 h（约 2–4 × 粒子半径）。 // Smoothing radius h (~2–4 × particle radius). // 平滑核半径 h。</summary>
        public float h;

        /// <summary>目标静止密度 ρ0（压力 = (ρ - ρ0)·k）。 // Target rest density ρ0. // 目標静止密度 ρ0。</summary>
        public float targetDensity;

        /// <summary>压力系数 k（密度偏差转压力）。 // Pressure multiplier k. // 圧力係数 k。</summary>
        public float pressureMultiplier;

        /// <summary>近密度压力系数（增强刚性、防粒子重叠）。 // Near-pressure multiplier (stiffness/anti-overlap). // 近密度圧力係数。</summary>
        public float nearPressureMultiplier;

        /// <summary>粘性强度（材质 viscosity 叠加于此）。 // Viscosity strength (material viscosity adds to this). // 粘性強度。</summary>
        public float viscosityStrength;

        /// <summary>碰撞反弹能量保留系数（0–1，越小越吸能）。 // Collision bounce retention (0–1). // 衝突反発の保持係数。</summary>
        public float collisionDamping;

        /// <summary>预测位置系数（predicted = pos + v·predictionFactor，SebLague 取固定 1/120）。 // Prediction factor (fixed 1/120 in reference). // 予測係数。</summary>
        public float predictionFactor;

        /// <summary>子步进数（每个固定步细分）。 // Substeps per fixed step. // サブステップ数。</summary>
        public int substeps;

        /// <summary>
        /// 最大粒子速度限制（世界单位/秒，0 表示不限制）。防止压力爆炸或参数配置不当导致粒子速度失控。
        /// Maximum particle speed limit (world units/second; 0 = unlimited). Prevents runaway velocities from pressure
        /// explosions or misconfigured parameters.
        /// 最大粒子速度制限（ワールド単位/秒、0 は無制限）。圧力爆発やパラメータ設定ミスによる速度暴走を防ぐ。
        /// </summary>
        public float maxSpeed;

        /// <summary>
        /// 推荐默认值（参考 SebLague 2D Fluid-Sim 取值，最终以 Unity 内手感为准）。
        /// Recommended defaults (from SebLague's 2D Fluid-Sim; final tuning happens in Unity).
        /// 推奨デフォルト（SebLague の 2D Fluid-Sim 値。最終調整は Unity 内で）。
        /// </summary>
        public static SolverParams Default => new SolverParams
        {
            gravity = new float2(0f, -9.8f),
            h = 0.4f,
            targetDensity = 55f,
            pressureMultiplier = 25f,
            nearPressureMultiplier = 8f,
            viscosityStrength = 0.06f,
            collisionDamping = 0.4f,
            predictionFactor = 1f / 120f,
            substeps = 3,
            maxSpeed = 0f,
        };
    }
}

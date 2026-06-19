using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 模拟计算平台模式。本期实现 CPU；GPU 预留（同一套 SoA 数据，未来切 Compute Shader + GraphicsBuffer）。
    /// Simulation compute platform mode. CPU is implemented now; GPU is reserved (same SoA data, future Compute Shader + GraphicsBuffer).
    /// シミュレーション計算プラットフォームモード。今回は CPU を実装。GPU は予約（同一 SoA、将来 Compute Shader + GraphicsBuffer）。
    /// </summary>
    public enum Liquid2DSimulationMode
    {
        /// <summary>CPU：Unity Job System + Burst。 // CPU: Unity Job System + Burst. // CPU：Unity Job System + Burst。</summary>
        Cpu = 0,
        /// <summary>GPU：Compute Shader（预留，未实现）。 // GPU: Compute Shader (reserved, not implemented). // GPU：Compute Shader（予約、未実装）。</summary>
        Gpu = 1,
    }

    /// <summary>
    /// PBF 求解全局参数。逐描述符的材质（<see cref="Liquid2DMaterialData"/>）在求解时与这些全局项合并。
    /// Global PBF solver parameters. Per-descriptor materials (<see cref="Liquid2DMaterialData"/>) are merged with these at solve time.
    /// PBF ソルバーのグローバルパラメータ。記述子ごとのマテリアルは解法時にこれらとマージされます。
    /// </summary>
    public struct SolverParams
    {
        /// <summary>重力加速度（世界）。 // Gravity (world). // 重力（ワールド）。</summary>
        public float2 gravity;

        /// <summary>静止密度 ρ0。 // Rest density ρ0. // 静止密度 ρ0。</summary>
        public float restDensity;

        /// <summary>光滑核半径 h（约 2–4 × 粒子半径）。 // Smoothing radius h (~2–4 × particle radius). // 平滑核半径 h。</summary>
        public float h;

        /// <summary>每步约束迭代次数。 // Constraint iterations per step. // ステップごとの制約反復回数。</summary>
        public int constraintIterations;

        /// <summary>子步进数。 // Substeps. // サブステップ数。</summary>
        public int substeps;

        /// <summary>全局 XSPH 粘性基准（材质 viscosity 叠加于此）。 // Global XSPH viscosity baseline (material viscosity adds to this). // グローバル XSPH 粘性基準。</summary>
        public float viscosity;

        /// <summary>人工压力（表面张力）强度 k。 // Artificial pressure (surface tension) strength k. // 人工圧力（表面張力）強度 k。</summary>
        public float sCorrK;

        /// <summary>人工压力指数 n。 // Artificial pressure exponent n. // 人工圧力指数 n。</summary>
        public float sCorrN;

        /// <summary>人工压力参考距离比例 Δq = ratio × h。 // Reference distance ratio Δq = ratio × h. // 参照距離比 Δq = ratio × h。</summary>
        public float sCorrDqRatio;

        /// <summary>约束力混合松弛 ε（CFM）。 // Constraint-force-mixing relaxation ε (CFM). // 制約力混合緩和 ε（CFM）。</summary>
        public float relaxEps;

        /// <summary>
        /// 推荐默认值（Macklin 2013 常用取值，最终以 Unity 内手感为准）。
        /// Recommended defaults (common values from Macklin 2013; final tuning happens in Unity).
        /// 推奨デフォルト（Macklin 2013 の一般値。最終調整は Unity 内で）。
        /// </summary>
        public static SolverParams Default => new SolverParams
        {
            gravity = new float2(0f, -9.8f),
            restDensity = 80f,
            h = 0.4f,
            constraintIterations = 3,
            substeps = 2,
            viscosity = 0.01f,
            sCorrK = 0.0001f,
            sCorrN = 4f,
            sCorrDqRatio = 0.2f,
            relaxEps = 200f,
        };
    }
}

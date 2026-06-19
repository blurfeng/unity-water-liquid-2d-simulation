using Unity.Mathematics;
using UnityEngine;
using Fs.Liquid2D.Localization;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D 流体物理配置组件（可选）。配置自研 PBF 求解器的全局参数（<see cref="Liquid2DSimulation.Params"/>）、
    /// 计算模式、按 nameTag 的存活上限与物理固定时间步长。将其挂载到场景中任意常驻对象上即可。
    /// 2D fluid physics config component (optional). Configures the custom PBF solver's global parameters
    /// (<see cref="Liquid2DSimulation.Params"/>), compute mode, per-nameTag alive cap, and the physics fixed timestep.
    /// Attach it to any persistent object in the scene.
    /// 2D 流体物理設定コンポーネント（オプション）。自作 PBF ソルバーのグローバルパラメータ、計算モード、
    /// nameTag ごとの生存上限、物理固定タイムステップを設定します。シーン内の常駐オブジェクトにアタッチします。
    /// </summary>
    public class Liquid2DPhysicsConfig : MonoBehaviour
    {
        [SerializeField, LocalizationTooltip(
             "在 Awake 时自动应用以下设置。",
             "Automatically apply the settings below on Awake.",
             "Awake 時に以下の設定を自動適用します。")]
        private bool applyOnAwake = true;

        [Header("Solver")]
        [SerializeField, LocalizationTooltip(
             "计算平台模式。GPU 为预留项，当前回退到 CPU。",
             "Compute platform mode. GPU is reserved and currently falls back to CPU.",
             "計算プラットフォームモード。GPU は予約で現在は CPU にフォールバック。")]
        private Liquid2DSimulationMode mode = Liquid2DSimulationMode.Cpu;

        [SerializeField, LocalizationTooltip("重力加速度。", "Gravity acceleration.", "重力加速度。")]
        private Vector2 gravity = new Vector2(0f, -9.8f);

        [SerializeField, Min(0.01f), LocalizationTooltip(
             "静止密度 ρ0。越大流体越“紧实”，需配合粒子间距与核半径调整。",
             "Rest density ρ0. Higher packs tighter; tune with particle spacing and smoothing radius.",
             "静止密度 ρ0。大きいほど密。粒子間隔と核半径に合わせて調整。")]
        private float restDensity = 80f;

        [SerializeField, Min(0.01f), LocalizationTooltip(
             "光滑核半径 h（约 2–4 × 粒子半径）。决定邻居范围与网格 cell 大小。",
             "Smoothing radius h (~2–4 × particle radius). Sets neighbor range and grid cell size.",
             "平滑核半径 h（粒子半径の約 2–4 倍）。近傍範囲とグリッド cell サイズを決定。")]
        private float smoothingRadius = 0.4f;

        [SerializeField, Min(1), LocalizationTooltip(
             "约束迭代次数（越多越不可压缩、越稳定，开销越大）。",
             "Constraint iterations (more = more incompressible/stable, costlier).",
             "制約反復回数（多いほど非圧縮/安定だがコスト増）。")]
        private int constraintIterations = 3;

        [SerializeField, Min(1), LocalizationTooltip(
             "子步进数（每个固定步细分，提升快速运动稳定性）。",
             "Substeps (subdivide each fixed step; improves fast-motion stability).",
             "サブステップ数（固定ステップを細分し高速運動の安定性向上）。")]
        private int substeps = 2;

        [SerializeField, Range(0f, 1f), LocalizationTooltip(
             "全局 XSPH 粘性基准（材质 viscosity 叠加于此）。",
             "Global XSPH viscosity baseline (material viscosity adds to this).",
             "グローバル XSPH 粘性基準（マテリアル粘性が加算）。")]
        private float viscosity = 0.01f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "人工压力（表面张力）强度 k。增强表面成滴与防粒子聚集。",
             "Artificial pressure (surface tension) strength k. Enhances droplets and prevents clustering.",
             "人工圧力（表面張力）強度 k。液滴化とクラスタリング防止を強化。")]
        private float surfaceTension = 0.0001f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "约束力混合松弛 ε（CFM）。增大可稳定但更软。",
             "Constraint-force-mixing relaxation ε (CFM). Larger is more stable but softer.",
             "制約力混合緩和 ε（CFM）。大きいほど安定だが柔らかい。")]
        private float relaxEps = 200f;

        [Header("Limits")]
        [SerializeField, Min(0), LocalizationTooltip(
             "每个 nameTag 的最大存活粒子数（0 表示不限制）。超出回收最旧。",
             "Max alive particles per nameTag (0 = no limit). Oldest recycled when exceeded.",
             "nameTag ごとの最大生存粒子数（0 は無制限）。超過時は最古を回収。")]
        private int maxParticlesPerTag = 0;

        [SerializeField, LocalizationTooltip(
             "是否覆盖物理固定时间步长（fixedDeltaTime）。",
             "Whether to override the physics fixed timestep (fixedDeltaTime).",
             "物理の固定タイムステップ（fixedDeltaTime）を上書きするか。")]
        private bool overrideFixedTimestep = false;

        [SerializeField, Min(0.001f), LocalizationTooltip(
             "物理固定时间步长（秒）。引擎默认 0.02（50Hz）。",
             "Physics fixed timestep (seconds). Engine default is 0.02 (50Hz).",
             "物理の固定タイムステップ（秒）。エンジン既定は 0.02（50Hz）。")]
        private float fixedTimestep = 0.02f;

        private void Awake()
        {
            if (applyOnAwake) Apply();
        }

        /// <summary>
        /// 应用当前配置。
        /// Apply the current configuration.
        /// 現在の設定を適用します。
        /// </summary>
        public void Apply()
        {
            Liquid2DSimulation.Mode = mode;
            Liquid2DSimulation.MaxParticlesPerTag = maxParticlesPerTag;
            Liquid2DSimulation.Params = new SolverParams
            {
                gravity = new float2(gravity.x, gravity.y),
                restDensity = restDensity,
                h = smoothingRadius,
                constraintIterations = constraintIterations,
                substeps = substeps,
                viscosity = viscosity,
                sCorrK = surfaceTension,
                sCorrN = 4f,
                sCorrDqRatio = 0.2f,
                relaxEps = relaxEps,
            };

            if (overrideFixedTimestep && fixedTimestep > 0f)
                Time.fixedDeltaTime = fixedTimestep;
        }
    }
}

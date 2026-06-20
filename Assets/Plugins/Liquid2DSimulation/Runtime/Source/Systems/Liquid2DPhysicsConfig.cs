using Unity.Mathematics;
using UnityEngine;
using Fs.Liquid2D.Localization;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D 流体物理配置组件（可选）。配置 SPH 双密度求解器的全局参数（<see cref="Liquid2DSimulation.Params"/>）、
    /// 计算模式、按 nameTag 的存活上限与物理固定时间步长。将其挂载到场景中任意常驻对象上即可。
    /// 2D fluid physics config component (optional). Configures the SPH dual-density solver's global parameters
    /// (<see cref="Liquid2DSimulation.Params"/>), compute mode, per-nameTag alive cap, and the physics fixed timestep.
    /// Attach it to any persistent object in the scene.
    /// 2D 流体物理設定コンポーネント（オプション）。SPH デュアル密度ソルバーのグローバルパラメータ、計算モード、
    /// nameTag ごとの生存上限、物理固定タイムステップを設定します。シーン内の常駐オブジェクトにアタッチします。
    /// </summary>
    public class Liquid2DPhysicsConfig : MonoBehaviour
    {
        #region Description 使用说明 // 使用説明

        // ─────────────────────────────────────────────────────────────────────────────
        // 【GPU 模式下从 CPU 读取粒子数据】gpuReadbackToStore 使用流程
        //
        // 默认（gpuReadbackToStore = false）：GPU 模式下粒子数据常驻 GPU，CPU 侧 store 仅在扩容帧同步，
        //   其余帧是陈旧数据 —— 任何走 store 的 CPU 读取（GetPosition/GetVelocity/Gizmos）都不可靠。
        //   注意：Liquid2DParticleDisplay 与正式 Feature 渲染直读 GPU 缓冲，不经过 store，不受此开关影响、无需开启。
        //
        // 开启（gpuReadbackToStore = true）：每个 FixedUpdate 的 GPU Step 之后，SphGpuSolver.ReadbackToStore 会把
        //   position / velocity / color / lastMix 从 GPU 缓冲全量回读进 CPU store，当帧即为最新。
        //   ⚠ 这是同步 GPU→CPU 阻塞点，粒子越多越慢，除非有业务需求要使用 CPU 数据，否则正式使用务必关闭。
        //
        // 【Reading Particle Data from CPU in GPU Mode】gpuReadbackToStore Workflow
        //
        // Default (gpuReadbackToStore = false): In GPU mode, particle data permanently resides on the GPU.
        // The CPU-side store is only synchronized during expansion frames;
        // in all other frames, the data is stale —— any CPU reads via the store (GetPosition/GetVelocity/Gizmos) are unreliable.
        // Note: Liquid2DParticleDisplay and the official Feature rendering read directly from the GPU buffer
        // without going through the store, so they are unaffected by this switch and do not require it to be enabled.
        //
        // Enabled (gpuReadbackToStore = true): After the GPU Step of each FixedUpdate,
        // SphGpuSolver.ReadbackToStore will read back all position / velocity / color / lastMix data
        // from the GPU buffer into the CPU store, making it up-to-date within the same frame.
        // ⚠ This is a synchronous GPU→CPU bottleneck (blocking point). The more particles there are, the slower it becomes.
        // Unless there is a specific gameplay/business requirement to use CPU data, it MUST be turned off for production/official use.
        //
        // 【GPUモードにおけるCPUからのパーティクルデータ読み込み】gpuReadbackToStore の使用フロー
        //
        // デフォルト（gpuReadbackToStore = false）：GPUモードでは、パーティクルデータは常にGPU上に常駐します。
        //   CPU側の store は拡充フレーム（メモリ拡張時）のみ同期され、
        //   その他のフレームでは古いデータ（Stale Data）となります —— store を経由するすべてのCPU読み込み（GetPosition/GetVelocity/Gizmos）は信頼できません。
        //   注意：Liquid2DParticleDisplay および正式な Feature レンダリングは、store を経由せずGPUバッファから直接読み込むため、
        //   このスイッチの影響を受けず、有効にする必要はありません。
        //
        // 有効化（gpuReadbackToStore = true）：各 FixedUpdate の GPU Step の後、
        //   SphGpuSolver.ReadbackToStore が position / velocity / color / lastMix を
        //   GPUバッファから CPU store へ全量リードバック（回読）し、そのフレーム内で最新データとなります。
        //   ⚠ これはGPU→CPUの同期ブロッキングポイント（ボトルネック）であり、パーティクル数が多ければ多いほど処理が遅くなります。
        //   CPU側のデータを必要とする業務・仕様上の要求がない限り、正式リリース/本番環境では必ず無効（false）にしてください。
        // -----------------------------------------------------------------------------
        // 【Use Cases】
        // we can read particle data from CPU store in GPU mode only when gpuReadbackToStore = true. Two common use cases:
        //   1) single particle query (GetPosition/GetVelocity/IsAlive):
        //        var h = Liquid2DSimulation.Instance.Spawn(descriptor, pos, vel, ...);
        //        float2 p = Liquid2DSimulation.Instance.GetPosition(h);
        //        float2 v = Liquid2DSimulation.Instance.GetVelocity(h);
        //        bool alive = Liquid2DSimulation.Instance.IsAlive(h);
        //   2) batch particle query (debug gizmos, or any other CPU-side processing):
        //        if (Liquid2DSimulation.TryGetRenderData(out var store, out var active, out int count, out var descriptors))
        //        {
        //             for (int i = 0; i < count; i++)
        //             {
        //                  int slot = active[i];                 // 紧凑活动索引 → slot
        //                  float2 pos = store.positions[slot];   // 同理 velocities / colors / radii / typeId
        //             }
        //        }
        // ─────────────────────────────────────────────────────────────────────────────

        #endregion

        [SerializeField, LocalizationTooltip(
             "在 Awake 时自动应用以下设置。",
             "Automatically apply the settings below on Awake.",
             "Awake 時に以下の設定を自動適用します。")]
        private bool applyOnAwake = true;

        [Header("Solver")]
        [SerializeField, LocalizationTooltip(
             "计算平台模式。CPU 为 Job System + Burst；GPU 为 Compute Shader（数据常驻 GPU，适合高粒子数）。",
             "Compute platform mode. CPU = Job System + Burst; GPU = Compute Shader (GPU-resident data, for high particle counts).",
             "計算プラットフォームモード。CPU は Job System + Burst、GPU は Compute Shader（GPU 常駐、高粒子数向け）。")]
        private Liquid2DSimulationMode mode = Liquid2DSimulationMode.Cpu;

        [SerializeField, LocalizationTooltip(
             "⚠ 性能警告：GPU 模式下每帧把粒子数据从 GPU 全量回读到 CPU。仅在需要让依赖 CPU 数据的功能" +
             "（Liquid2DDebugGizmos 调试可视化、GetPosition/GetVelocity 查询）在 GPU 模式下工作时才开启。" +
             "这是同步 GPU→CPU 阻塞点，会严重降低性能（粒子越多越明显），正式使用务必关闭。Liquid2DParticleDisplay 与正式 Feature 渲染不受影响、无需此开关。",
             "⚠ Performance warning: in GPU mode, reads ALL particle data back from GPU to CPU every frame. Enable ONLY when you need " +
             "CPU-data-dependent features (Liquid2DDebugGizmos visualization, GetPosition/GetVelocity queries) to work under GPU mode. " +
             "This is a synchronous GPU→CPU stall that severely degrades performance (worse with more particles); keep it OFF for production. " +
             "Liquid2DParticleDisplay and the official Feature rendering are unaffected and do not need this.",
             "⚠ 性能警告：GPU モードで毎フレーム全粒子データを GPU から CPU へ回読します。CPU データ依存機能" +
             "（Liquid2DDebugGizmos、GetPosition/GetVelocity）を GPU モードで使う場合のみオンにしてください。" +
             "同期 GPU→CPU ストールで性能が大幅に低下します（粒子が多いほど顕著）。本番では必ずオフに。Liquid2DParticleDisplay と正式 Feature 描画は影響を受けません。")]
        private bool gpuReadbackToStore;

        [SerializeField, LocalizationTooltip("重力加速度。", "Gravity acceleration.", "重力加速度。")]
        private Vector2 gravity = new Vector2(0f, -9.8f);

        [SerializeField, Min(0.01f), LocalizationTooltip(
             "光滑核半径 h（约 2–4 × 粒子半径）。决定邻居范围与网格 cell 大小。",
             "Smoothing radius h (~2–4 × particle radius). Sets neighbor range and grid cell size.",
             "平滑核半径 h（粒子半径の約 2–4 倍）。近傍範囲とグリッド cell サイズを決定。")]
        private float smoothingRadius = 0.4f;

        [SerializeField, Min(0.01f), LocalizationTooltip(
             "目标静止密度 ρ0。压力按 (密度-ρ0)·压力系数 计算；越大流体越“紧实”。",
             "Target rest density ρ0. Pressure = (density - ρ0)·pressureMultiplier; higher packs tighter.",
             "目標静止密度 ρ0。圧力 = (密度-ρ0)·圧力係数。大きいほど密。")]
        private float targetDensity = 55f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "压力系数 k。密度偏差转换为压力的强度，越大越不可压缩、越“弹”。",
             "Pressure multiplier k. Converts density deviation to pressure; higher is more incompressible/springy.",
             "圧力係数 k。密度偏差を圧力に変換する強度。大きいほど非圧縮的で弾性的。")]
        private float pressureMultiplier = 125f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "近密度压力系数。增强近距离刚性，防止粒子相互重叠/穿插。",
             "Near-pressure multiplier. Adds short-range stiffness to prevent particle overlap.",
             "近密度圧力係数。近距離の剛性を加え、粒子の重なりを防止。")]
        private float nearPressureMultiplier = 8f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "全局粘性强度（材质 viscosity 叠加于此）。越大越粘稠、流动越缓。",
             "Global viscosity strength (material viscosity adds to this). Higher is thicker and slower.",
             "グローバル粘性強度（マテリアル粘性が加算）。大きいほど粘く遅い。")]
        private float viscosityStrength = 0.06f;

        [SerializeField, Range(0f, 1f), LocalizationTooltip(
             "碰撞反弹能量保留系数（0–1）。0 完全吸能、1 完全弹回。",
             "Collision bounce retention (0–1). 0 = fully absorb, 1 = fully bounce.",
             "衝突反発の保持係数（0–1）。0 は吸収、1 は完全反発。")]
        private float collisionDamping = 0.4f;

        [SerializeField, Min(1), LocalizationTooltip(
             "子步进数（每个固定步细分，提升快速运动与高压力稳定性）。",
             "Substeps (subdivide each fixed step; improves fast-motion and high-pressure stability).",
             "サブステップ数（固定ステップを細分し高速運動/高圧の安定性向上）。")]
        private int substeps = 3;

        [SerializeField, Min(0f), LocalizationTooltip(
             "最大粒子速度（世界单位/秒，0 表示不限制）。防止压力参数过大导致粒子速度失控乱飞，建议设为喷射力的 3–5 倍。",
             "Maximum particle speed (world units/s; 0 = unlimited). Prevents runaway velocities from high pressure settings; recommend 3–5× the ejection velocity.",
             "最大パーティクル速度（ワールド単位/秒、0 は無制限）。圧力過大による速度暴走を防ぎます。噴射速度の 3～5 倍を推奨。")]
        private float maxSpeed = 20f;

        [Header("Limits")]
        [SerializeField, Min(0), LocalizationTooltip(
             "每个 nameTag 的最大存活粒子数（0 表示不限制）。超出回收最旧。",
             "Max alive particles per nameTag (0 = no limit). Oldest recycled when exceeded.",
             "nameTag ごとの最大生存粒子数（0 は無制限）。超過時は最古を回収。")]
        private int maxParticlesPerTag;

        [SerializeField, LocalizationTooltip(
             "是否覆盖物理固定时间步长（fixedDeltaTime）。",
             "Whether to override the physics fixed timestep (fixedDeltaTime).",
             "物理の固定タイムステップ（fixedDeltaTime）を上書きするか。")]
        private bool overrideFixedTimestep;

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
            Liquid2DSimulation.GpuReadbackToStore = gpuReadbackToStore;
            Liquid2DSimulation.MaxParticlesPerTag = maxParticlesPerTag;
            Liquid2DSimulation.Params = new SolverParams
            {
                Gravity = new float2(gravity.x, gravity.y),
                H = smoothingRadius,
                TargetDensity = targetDensity,
                PressureMultiplier = pressureMultiplier,
                NearPressureMultiplier = nearPressureMultiplier,
                ViscosityStrength = viscosityStrength,
                CollisionDamping = collisionDamping,
                PredictionFactor = 1f / 120f,
                Substeps = substeps,
                MaxSpeed = maxSpeed,
            };

            if (overrideFixedTimestep && fixedTimestep > 0f)
                Time.fixedDeltaTime = fixedTimestep;
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            if (applyOnAwake && Application.isPlaying)
                Apply();
        }
#endif
    }
}
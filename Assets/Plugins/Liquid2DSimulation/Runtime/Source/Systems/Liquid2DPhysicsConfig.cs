using UnityEngine;
using Fs.Liquid2D.Localization;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D 流体物理配置组件（可选）。
    /// 由于项目级的 Physics 2D 设置不会随插件一起发布，此组件用于在运行时应用推荐的物理设置，
    /// 以提升千级流体粒子场景下的物理求解性能。将其挂载到场景中任意常驻对象上即可。
    /// 2D fluid physics config component (optional).
    /// Since project-level Physics 2D settings are not shipped with the plugin, this component applies
    /// recommended physics settings at runtime to improve solver performance in scenes with thousands of
    /// fluid particles. Attach it to any persistent object in the scene.
    /// 2D流体物理設定コンポーネント（オプション）。
    /// プロジェクトレベルの Physics 2D 設定はプラグインと一緒に配布されないため、このコンポーネントは
    /// ランタイムで推奨される物理設定を適用し、数千の流体粒子があるシーンでのソルバー性能を向上させます。
    /// シーン内の任意の常駐オブジェクトにアタッチしてください。
    /// </summary>
    public class Liquid2DPhysicsConfig : MonoBehaviour
    {
        [SerializeField, LocalizationTooltip(
             "在 Awake 时自动应用以下物理设置。",
             "Automatically apply the physics settings below on Awake.",
             "Awake時に以下の物理設定を自動的に適用します。")]
        private bool applyOnAwake = true;

        [SerializeField, LocalizationTooltip(
             "启用 2D 物理多线程求解器。大量动态刚体时可显著利用多核，降低主线程物理耗时。",
             "Enable the 2D physics multithreaded solver. With many dynamic rigidbodies this leverages multiple cores and reduces main-thread physics time.",
             "2D物理マルチスレッドソルバーを有効にします。多数の動的剛体がある場合、マルチコアを活用してメインスレッドの物理処理時間を削減できます。")]
        private bool enableMultithreading = true;

        [SerializeField, LocalizationTooltip(
             "是否覆盖物理求解迭代次数。流体粒子汤对求解精度不敏感，降低迭代次数可减少求解开销。注意：此设置全局影响所有 2D 物理。",
             "Whether to override physics solver iteration counts. A fluid particle soup is insensitive to solver precision, so lowering iterations reduces solver cost. Note: this affects ALL 2D physics globally.",
             "物理ソルバーの反復回数を上書きするかどうか。流体粒子のスープはソルバー精度に鈍感なため、反復回数を下げるとソルバーコストが削減されます。注意：この設定はすべての2D物理にグローバルに影響します。")]
        private bool overrideSolverIterations = true;

        [SerializeField, Min(1), LocalizationTooltip(
             "速度迭代次数（引擎默认 8）。建议流体场景降低到 4 左右。",
             "Velocity iteration count (engine default 8). Recommended ~4 for fluid scenes.",
             "速度反復回数（エンジンのデフォルトは8）。流体シーンでは4程度を推奨。")]
        private int velocityIterations = 4;

        [SerializeField, Min(1), LocalizationTooltip(
             "位置迭代次数（引擎默认 3）。建议流体场景降低到 2 左右。",
             "Position iteration count (engine default 3). Recommended ~2 for fluid scenes.",
             "位置反復回数（エンジンのデフォルトは3）。流体シーンでは2程度を推奨。")]
        private int positionIterations = 2;

        [SerializeField, Min(0), LocalizationTooltip(
             "每个 nameTag 的最大存活粒子数（0 表示不限制）。超出时回收该 nameTag 下最旧的粒子，用于确定性封顶物理最坏开销。",
             "Max alive particles per nameTag (0 = no limit). When exceeded, the oldest particle under that nameTag is recycled, to deterministically bound worst-case physics cost.",
             "nameTag ごとの最大生存粒子数（0は無制限）。超過時はその nameTag の最古の粒子を回収し、物理の最悪コストを確定的に制限します。")]
        private int maxParticlesPerTag = 0;

        [SerializeField, LocalizationTooltip(
             "是否覆盖物理固定时间步长（fixedDeltaTime）。Physics2D.Simulate 按固定步长调用，降低频率可近乎线性减少总物理耗时。",
             "Whether to override the physics fixed timestep (fixedDeltaTime). Physics2D.Simulate is called per fixed step, so lowering the frequency reduces total physics time almost linearly.",
             "物理の固定タイムステップ（fixedDeltaTime）を上書きするかどうか。Physics2D.Simulate は固定ステップ毎に呼ばれるため、頻度を下げると総物理処理時間がほぼ線形に減少します。")]
        private bool overrideFixedTimestep = true;

        [SerializeField, Min(0.001f), LocalizationTooltip(
             "物理固定时间步长（秒）。引擎默认 0.02（50Hz）。建议流体场景提高到 0.0333（30Hz）左右以降低 Physics2D.Simulate 总耗时；过大会降低物理平滑度与穿透稳定性。",
             "Physics fixed timestep (seconds). Engine default is 0.02 (50Hz). For fluid scenes, raising it to ~0.0333 (30Hz) lowers total Physics2D.Simulate time; too large reduces smoothness and tunneling stability.",
             "物理の固定タイムステップ（秒）。エンジンのデフォルトは0.02（50Hz）。流体シーンでは0.0333（30Hz）程度に上げると Physics2D.Simulate の総時間が下がります。大きすぎると滑らかさと貫通安定性が低下します。")]
        private float fixedTimestep = 0.0333f;

        private void Awake()
        {
            if (applyOnAwake)
            {
                Apply();
            }
        }

        /// <summary>
        /// 应用当前配置的物理设置。
        /// Apply the currently configured physics settings.
        /// 現在設定されている物理設定を適用します。
        /// </summary>
        public void Apply()
        {
            // 多线程求解器开关。 // Multithreaded solver toggle. // マルチスレッドソルバーの切り替え。
            var jobOptions = Physics2D.jobOptions;
            jobOptions.useMultithreading = enableMultithreading;
            Physics2D.jobOptions = jobOptions;

            // 求解迭代次数。 // Solver iteration counts. // ソルバー反復回数。
            if (overrideSolverIterations)
            {
                Physics2D.velocityIterations = velocityIterations;
                Physics2D.positionIterations = positionIterations;
            }

            // 每个 nameTag 的存活上限。 // Per-nameTag alive cap. // nameTag ごとの生存上限。
            Liquid2DParticleManager.MaxParticlesPerTag = maxParticlesPerTag;

            // 物理固定时间步长。降低物理频率以近乎线性减少 Physics2D.Simulate 总耗时。
            // Physics fixed timestep. Lower the physics frequency to reduce total Physics2D.Simulate time almost linearly.
            // 物理の固定タイムステップ。物理頻度を下げて Physics2D.Simulate の総時間をほぼ線形に削減します。
            if (overrideFixedTimestep && fixedTimestep > 0f)
            {
                Time.fixedDeltaTime = fixedTimestep;
            }
        }
    }
}

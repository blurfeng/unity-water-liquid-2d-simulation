using System;
using System.Collections.Generic;
using UnityEngine;
using Unity.Mathematics;
using Fs.Liquid2D.Localization;
using Fs.Liquid2D.Utility;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 区域粒子生成器。将一个或多个矩形区域按密度网格填充流体粒子，支持自动补充（Refill）模式：
    /// 持续监测各区域存活粒子数，当存活比例低于阈值时按设定流量逐步补充。
    /// Region particle spawner. Fills one or more rectangular regions with fluid particles arranged in a
    /// density grid. Supports auto-refill mode: continuously monitors alive particle count per region and
    /// refills at a configurable flow rate when the alive ratio drops below the threshold.
    /// 領域パーティクルスポナー。一つ以上の矩形領域を密度グリッドで流体粒子を充填します。
    /// 自動補充モードをサポート：各領域の生存粒子数を継続監視し、生存比率が閾値を下回った場合に
    /// 設定した流量で段階的に補充します。
    /// </summary>
    [AddComponentMenu("Liquid2D/Gameplay/Liquid2D Region Spawner")]
    public class Liquid2DRegionSpawner : MonoBehaviour
    {
        [SerializeField, LocalizationTooltip(
            "是否在启动时自动填充所有区域。",
            "Whether to automatically fill all regions on start.",
            "起動時にすべての領域を自動充填するかどうか。")]
        private bool spawnOnStart = true;

        [Header("Particle Settings")]
        [SerializeField, LocalizationTooltip(
            "默认流体粒子配置列表（区域未单独配置时使用）。",
            "Default fluid particle config list (used when a region does not override).",
            "デフォルト流体パーティクル設定リスト（領域で個別設定がない場合に使用）。")]
        private List<Liquid2DParticleConfig> particles = new List<Liquid2DParticleConfig>();

        [Header("Spawn Settings")]
        [SerializeField, Min(0.01f), LocalizationTooltip(
            "粒子密度（每平方世界单位的粒子数）。影响区域填满时的总粒子数。",
            "Particle density (particles per square world unit). Determines total count when a region is full.",
            "粒子密度（1平方ワールド単位あたりの粒子数）。領域が満たされたときの総数を決定します。")]
        private float spawnDensity = 2f;

        [SerializeField, LocalizationTooltip(
            "所有粒子的初始速度（世界空间）。",
            "Initial velocity applied to all spawned particles (world space).",
            "生成されたすべての粒子に適用される初期速度（ワールド空間）。")]
        private Vector2 initialVelocity;

        [SerializeField, Min(0f), LocalizationTooltip(
            "位置抖动强度。大于 0 时对每颗粒子施加随机位置偏移，避免规则网格感。",
            "Position jitter strength. When > 0, applies a random positional offset to each particle to reduce the look of a regular grid.",
            "位置ジッター強度。0より大きい場合、各粒子にランダムな位置オフセットを適用して規則的なグリッドの見た目を緩和します。")]
        private float jitterStr = 0.05f;

        [Header("Refill Settings")]
        [SerializeField, LocalizationTooltip(
            "是否开启自动补充模式。开启后每帧检测各区域存活粒子数，低于阈值时按流量逐步补充。",
            "Whether to enable auto-refill mode. When on, each frame monitors alive count per region and refills at the configured flow rate when below threshold.",
            "自動補充モードを有効にするかどうか。有効にすると毎フレーム各領域の生存粒子数を監視し、閾値を下回った場合に設定した流量で補充します。")]
        private bool refillEnabled;

        [SerializeField, Min(0.1f), LocalizationTooltip(
            "补充流量（每秒补充的粒子数）。仅在 Refill 模式开启时生效。",
            "Refill flow rate (particles per second). Only active when refill mode is enabled.",
            "補充流量（毎秒補充する粒子数）。補充モードが有効な場合のみ有効です。")]
        private float refillFlowRate = 20f;

        [SerializeField, Range(0f, 1f), LocalizationTooltip(
            "补充触发阈值（0~1）。当某区域存活粒子比例低于此值时触发补充。" +
            "1 = 有粒子死亡立即补充；0 = 从不补充。",
            "Refill trigger threshold (0~1). Triggers refill when a region's alive ratio falls below this value. " +
            "1 = refill immediately on any death; 0 = never refill.",
            "補充トリガー閾値（0～1）。ある領域の生存比率がこの値を下回ると補充します。" +
            "1=死亡があれば即補充、0=補充しない。")]
        private float refillThreshold = 1f;

        [Header("Regions")]
        [SerializeField, LocalizationTooltip(
            "生成区域列表。每个区域可独立配置粒子类型，位置为相对于本 Transform 的偏移。",
            "Spawn region list. Each region can independently configure particle types; position is an offset from this Transform.",
            "スポーン領域リスト。各領域で粒子タイプを個別設定でき、位置はこのTransformからのオフセットです。")]
        private List<Liquid2DSpawnRegion> spawnRegions = new List<Liquid2DSpawnRegion>();

#if UNITY_EDITOR
        [Space]
        [SerializeField, LocalizationTooltip(
            "在 Scene 视图中显示区域 Gizmos。",
            "Show region gizmos in the Scene view.",
            "Scene ビューに領域 Gizmos を表示する。")]
        private bool showGizmos = true;

        [SerializeField, LocalizationTooltip(
            "预计生成粒子总数（只读，由 Inspector 更新）。",
            "Estimated total particle count (read-only, updated by Inspector).",
            "推定総粒子数（読み取り専用、Inspectorで更新）。")]
        private int estimatedParticleCount;
#endif

        // 每个区域存活句柄列表（死亡句柄在补充时剪除）。
        // Per-region alive handle lists (dead handles pruned during refill tick).
        // 領域ごとの生存ハンドルリスト（補充タイミングで死亡ハンドルを剪定）。
        private List<Liquid2DParticleHandle>[] _regionHandles;

        // 每个区域满载时的目标粒子数（由 spawnDensity 和区域尺寸决定）。
        // Target particle count per region at full capacity (determined by spawnDensity and region size).
        // 満充填時の領域ごとの目標粒子数（spawnDensity と領域サイズで決定）。
        private int[] _regionTargetCount;

        // 补充模式计时器与间隔。 // Refill timer and interval. // 補充タイマーと間隔。
        private float _refillTimer;
        private float _refillInterval;

        // 每帧只剪除一次死亡句柄（避免同一帧多次 while 迭代时重复遍历）。
        // Prune dead handles at most once per frame to avoid repeated passes inside the while loop.
        // 同一フレームに複数回のwhileループで重複スキャンしないよう、死亡ハンドル剪定は1フレーム1回まで。
        private int _pruneFrame = -1;

        private static bool _isInitForJit;

        #region 公开方法 Public Methods 公開メソッド

        /// <summary>
        /// 立即用粒子填满所有区域（已有存活粒子的 slot 不重复生成）。
        /// Immediately fill all regions with particles (existing alive slots are not re-spawned).
        /// すべての領域を粒子で即座に充填します（既存の生存スロットは再生成されません）。
        /// </summary>
        public void Spawn()
        {
            var sim = Liquid2DSimulation.Instance;
            if (sim == null) return;
            EnsureHandleArrays();
            for (int i = 0; i < spawnRegions.Count; i++)
                FillRegion(i, sim);
        }

        /// <summary>
        /// 立即填满指定区域。
        /// Immediately fill the specified region.
        /// 指定した領域を即座に充填します。
        /// </summary>
        /// <param name="regionIndex">区域索引。 // Region index. // 領域インデックス。</param>
        public void Spawn(int regionIndex)
        {
            var sim = Liquid2DSimulation.Instance;
            if (sim == null) return;
            EnsureHandleArrays();
            FillRegion(regionIndex, sim);
        }

        /// <summary>
        /// 销毁所有已生成粒子。
        /// Despawn all spawned particles across all regions.
        /// すべての領域の生成済みパーティクルを破棄します。
        /// </summary>
        public void Clear()
        {
            var sim = Liquid2DSimulation.Instance;
            if (sim == null || _regionHandles == null) return;
            for (int i = 0; i < _regionHandles.Length; i++)
                DespawnRegion(i, sim);
        }

        /// <summary>
        /// 销毁指定区域的已生成粒子。
        /// Despawn all spawned particles in the specified region.
        /// 指定した領域の生成済みパーティクルをすべて破棄します。
        /// </summary>
        /// <param name="regionIndex">区域索引。 // Region index. // 領域インデックス。</param>
        public void Clear(int regionIndex)
        {
            var sim = Liquid2DSimulation.Instance;
            if (sim == null) return;
            EnsureHandleArrays();
            DespawnRegion(regionIndex, sim);
        }

        /// <summary>
        /// 设置补充流量并立即更新计时间隔。
        /// Set the refill flow rate and immediately update the timer interval.
        /// 補充流量を設定し、タイマー間隔を即座に更新します。
        /// </summary>
        /// <param name="rate">每秒粒子数（&gt;= 0.1）。 // Particles per second (>= 0.1). // 毎秒粒子数（≥0.1）。</param>
        public void SetRefillFlowRate(float rate)
        {
            refillFlowRate = Mathf.Max(0.1f, rate);
            _refillInterval = 1f / refillFlowRate;
        }

        #endregion

        #region Unity 生命周期 Lifecycle ライフサイクル

        private void Awake()
        {
            if (!_isInitForJit)
            {
                _isInitForJit = true;
                particles.RandomWeight();
            }
        }

        private void Start()
        {
            _refillInterval = refillFlowRate > 0f ? 1f / refillFlowRate : float.MaxValue;

            if (spawnOnStart)
                Spawn();
        }

        private void Update()
        {
            if (!refillEnabled || _regionHandles == null) return;
            var sim = Liquid2DSimulation.Instance;
            if (sim == null) return;

            _refillTimer += Time.deltaTime;
            if (_refillTimer < _refillInterval) return;

            int regionCount = Mathf.Min(spawnRegions.Count, _regionHandles.Length);

            // 每帧只剪除一次死亡句柄，避免 while 循环内重复扫描。
            // Prune dead handles once per frame; avoids redundant scans inside the while loop.
            // 毎フレーム1回だけ死亡ハンドルを剪定してwhileループ内の重複スキャンを避ける。
            if (_pruneFrame != Time.frameCount)
            {
                _pruneFrame = Time.frameCount;
                for (int i = 0; i < regionCount; i++)
                    PruneDeadHandles(i, sim);
            }

            while (_refillTimer >= _refillInterval)
            {
                _refillTimer -= _refillInterval;

                // 选取存活比最低的区域（低于阈值才补充）。
                // Pick the region with the lowest alive ratio (only if below threshold).
                // 生存比が最も低い領域を選択（閾値を下回る場合のみ補充）。
                int bestRegion = -1;
                float lowestRatio = refillThreshold;

                for (int i = 0; i < regionCount; i++)
                {
                    int target = _regionTargetCount[i];
                    if (target <= 0) continue;
                    float ratio = (float)_regionHandles[i].Count / target;
                    if (ratio < lowestRatio)
                    {
                        lowestRatio = ratio;
                        bestRegion = i;
                    }
                }

                if (bestRegion < 0)
                {
                    _refillTimer = 0f; // 无需补充，重置计时器。 // No refill needed; reset timer. // 補充不要、タイマーリセット。
                    break;
                }

                RefillOne(bestRegion, sim);
            }
        }

        #endregion

        #region 内部方法 Internal Methods 内部メソッド

        private void EnsureHandleArrays()
        {
            int count = spawnRegions.Count;
            if (_regionHandles != null && _regionHandles.Length == count) return;

            _regionHandles = new List<Liquid2DParticleHandle>[count];
            _regionTargetCount = new int[count];
            for (int i = 0; i < count; i++)
                _regionHandles[i] = new List<Liquid2DParticleHandle>();
        }

        private void FillRegion(int idx, Liquid2DSimulation sim)
        {
            if (idx < 0 || idx >= spawnRegions.Count) return;
            var region = spawnRegions[idx];

            var configs = ResolveConfigs(region);
            if (configs == null || configs.Count == 0) return;

            Vector2 worldCenter = (Vector2)transform.position + region.Position;
            Vector2Int grid = CalculateGridSize(region.Size, spawnDensity);
            _regionTargetCount[idx] = grid.x * grid.y;

            // 先剪除死亡句柄，避免重复统计。 // Prune dead handles before counting to avoid duplicate entries. // 重複カウントを避けるため先に死亡ハンドルを剪定。
            PruneDeadHandles(idx, sim);

            for (int y = 0; y < grid.y; y++)
            {
                for (int x = 0; x < grid.x; x++)
                {
                    float tx = grid.x > 1 ? x / (grid.x - 1f) : 0.5f;
                    float ty = grid.y > 1 ? y / (grid.y - 1f) : 0.5f;

                    float px = (tx - 0.5f) * region.Size.x + worldCenter.x;
                    float py = (ty - 0.5f) * region.Size.y + worldCenter.y;

                    if (jitterStr > 0f)
                    {
                        float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                        float jitter = jitterStr * (UnityEngine.Random.value - 0.5f);
                        px += Mathf.Cos(angle) * jitter;
                        py += Mathf.Sin(angle) * jitter;
                    }

                    var config = configs.RandomWeight();
                    if (config == null || config.Descriptor == null) continue;

                    float2 pos = new float2(px, py);
                    float2 vel = new float2(initialVelocity.x, initialVelocity.y);
                    var handle = sim.Spawn(config.Descriptor, pos, vel, 1f, config.Lifetime);
                    _regionHandles[idx].Add(handle);
                }
            }
        }

        private void RefillOne(int idx, Liquid2DSimulation sim)
        {
            if (idx < 0 || idx >= spawnRegions.Count) return;
            var region = spawnRegions[idx];

            var configs = ResolveConfigs(region);
            if (configs == null || configs.Count == 0) return;

            var config = configs.RandomWeight();
            if (config == null || config.Descriptor == null) return;

            // 在区域内随机选取位置（均匀随机，而非固定网格）。
            // Random uniform position within the region bounds (not a fixed grid slot).
            // 領域内のランダム均一位置（固定グリッドスロットではない）。
            Vector2 worldCenter = (Vector2)transform.position + region.Position;
            float px = worldCenter.x + UnityEngine.Random.Range(-region.Size.x * 0.5f, region.Size.x * 0.5f);
            float py = worldCenter.y + UnityEngine.Random.Range(-region.Size.y * 0.5f, region.Size.y * 0.5f);

            if (jitterStr > 0f)
            {
                float angle = UnityEngine.Random.value * Mathf.PI * 2f;
                float jitter = jitterStr * (UnityEngine.Random.value - 0.5f);
                px += Mathf.Cos(angle) * jitter;
                py += Mathf.Sin(angle) * jitter;
            }

            float2 pos = new float2(px, py);
            float2 vel = new float2(initialVelocity.x, initialVelocity.y);
            var handle = sim.Spawn(config.Descriptor, pos, vel, 1f, config.Lifetime);
            _regionHandles[idx].Add(handle);
        }

        private void DespawnRegion(int idx, Liquid2DSimulation sim)
        {
            if (_regionHandles == null || idx >= _regionHandles.Length) return;
            var handles = _regionHandles[idx];
            for (int i = 0; i < handles.Count; i++)
            {
                if (sim.IsAlive(handles[i]))
                    sim.Despawn(handles[i]);
            }
            handles.Clear();
        }

        private void PruneDeadHandles(int idx, Liquid2DSimulation sim)
        {
            if (_regionHandles == null || idx >= _regionHandles.Length) return;
            var handles = _regionHandles[idx];
            for (int i = handles.Count - 1; i >= 0; i--)
            {
                if (!sim.IsAlive(handles[i]))
                    handles.RemoveAt(i);
            }
        }

        private List<Liquid2DParticleConfig> ResolveConfigs(Liquid2DSpawnRegion region)
        {
            if (region.OverrideParticles && region.Particles is { Count: > 0 })
                return region.Particles;
            return particles;
        }

        private static Vector2Int CalculateGridSize(Vector2 size, float density)
        {
            if (size.x <= 0f || size.y <= 0f || density <= 0f) return Vector2Int.one;
            float area = size.x * size.y;
            int target = Mathf.Max(1, Mathf.CeilToInt(area * density));
            float lenSum = size.x + size.y;
            Vector2 t = size / lenSum;
            float m = Mathf.Sqrt(target / (t.x * t.y));
            int nx = Mathf.Max(1, Mathf.CeilToInt(t.x * m));
            int ny = Mathf.Max(1, Mathf.CeilToInt(t.y * m));
            return new Vector2Int(nx, ny);
        }

        #endregion

#if UNITY_EDITOR
        private void OnValidate()
        {
            estimatedParticleCount = 0;
            if (spawnRegions != null)
            {
                foreach (var r in spawnRegions)
                {
                    var gs = CalculateGridSize(r.Size, spawnDensity);
                    estimatedParticleCount += gs.x * gs.y;

                    if (!r.IsInit)
                    {
                        r.IsInit = true;
                        r.Size = new Vector2(2f, 2f);
                    }
                }
            }

            if (refillFlowRate > 0f)
                _refillInterval = 1f / refillFlowRate;
        }

        // 间距 / 直径（2r）的比值阈值。基准为「粒子圆是否恰好盖住空间」：间距≈直径即相切/略重叠=填满。
        // Spacing-to-diameter (2r) ratio thresholds. Anchored on "do particle circles just cover the area":
        // spacing ≈ diameter means circles touch/slightly overlap = filled.
        // 間隔/直径（2r）の比率閾値。「粒子円が空間をちょうど覆うか」を基準とする：間隔≈直径＝接触/微重なり＝充填。
        private const float FitRatioSparse = 1.5f; // ratio >= 1.5 → Sparse（间隙过大，难连成液面）
        private const float FitRatioOk     = 0.6f; // 0.6 <= ratio < 1.5 → OK（正合适）
        private const float FitRatioTight  = 0.4f; // 0.4 <= ratio < 0.6 → Tight（偏密，明显重叠）
                                                   // ratio < 0.4 → Overlap（过密，启动排斥飞开）

        // 返回一个区域的粒子适配信息：粒子数、加权平均半径、最小网格间距、适配状态。
        // Returns fit info for a region: particle count, weighted average radius, minimum grid spacing, fit status.
        // 領域の適合情報を返す：粒子数、加重平均半径、最小グリッド間隔、適合状態。
        private (int count, float avgRadius, float minSpacing, int status) GetRegionFit(Liquid2DSpawnRegion region)
        {
            // status: 0 = Unknown (no descriptor), 1 = OK, 2 = Tight, 3 = Overlap, 4 = Sparse
            var gs = CalculateGridSize(region.Size, spawnDensity);
            int count = gs.x * gs.y;

            float xSpacing = gs.x > 1 ? region.Size.x / (gs.x - 1f) : region.Size.x;
            float ySpacing = gs.y > 1 ? region.Size.y / (gs.y - 1f) : region.Size.y;
            float minSpacing = Mathf.Min(xSpacing, ySpacing);

            float totalWeight = 0f, weightedRadius = 0f;
            var configs = ResolveConfigs(region);
            if (configs != null)
            {
                foreach (var c in configs)
                {
                    if (!c?.Descriptor) continue;
                    float w = Mathf.Max(1f, c.Weight);
                    weightedRadius += c.Descriptor.Radius * w;
                    totalWeight += w;
                }
            }

            if (totalWeight <= 0f)
                return (count, 0f, minSpacing, 0);

            float avgRadius = weightedRadius / totalWeight;
            float ratio = minSpacing / (2f * avgRadius); // spacing / diameter
            int s = ratio >= FitRatioSparse ? 4  // 稀疏 // Sparse // 稀疏
                  : ratio >= FitRatioOk     ? 1  // 正合适 // OK // 適正
                  : ratio >= FitRatioTight  ? 2  // 偏密 // Tight // やや密
                  : 3;                           // 过密 // Overlap // 過密
            return (count, avgRadius, minSpacing, s);
        }

        private void OnDrawGizmos()
        {
            if (!showGizmos || spawnRegions == null) return;

            foreach (var region in spawnRegions)
            {
                if (region == null) continue;

                Vector3 worldCenter = new Vector3(
                    transform.position.x + region.Position.x,
                    transform.position.y + region.Position.y,
                    transform.position.z);

                var (count, avgRadius, minSpacing, status) = GetRegionFit(region);

                // 外框颜色：按适配状态染色；无描述符时保留原调试色。
                // Border color based on fit status; falls back to debug color when no descriptor.
                // 適合状態に基づく外枠カラー（記述子なしの場合はデバッグカラーを使用）。
                Color borderColor = status switch
                {
                    4 => new Color(0.3f, 0.6f, 1f), // Sparse 稀疏
                    3 => Color.red,                 // Overlap 过密
                    2 => Color.yellow,              // Tight 偏密
                    1 => Color.green,               // OK 正合适
                    _ => Color.aquamarine           // Unknown 无描述符
                };

                Gizmos.color = borderColor;
                Gizmos.DrawWireCube(worldCenter, new Vector3(region.Size.x, region.Size.y, 0.01f));

                // 中心参考圆：以平均粒子半径绘制，直观展示粒子尺寸与区域的比例关系。
                // Reference circle at center using average particle radius — gives an immediate sense of particle size vs region.
                // 中心参照円：平均粒子半径で描画し、粒子サイズと領域の比率を直観的に示す。
                if (avgRadius > 0f)
                {
                    UnityEditor.Handles.color = new Color(borderColor.r, borderColor.g, borderColor.b, 0.35f);
                    UnityEditor.Handles.DrawWireDisc(worldCenter, Vector3.forward, avgRadius);
                }

                // 顶部标签：粒子数 / 平均半径 / 最小间距 / 状态。
                // Top label: particle count / average radius / minimum spacing / status.
                // 上部ラベル：粒子数 / 平均半径 / 最小間隔 / 状態。
                string statusStr = status switch
                {
                    4 => "≈ Sparse",
                    3 => "⚠ Overlap",
                    2 => "~ Tight",
                    1 => "✓ OK",
                    _ => "",
                };
                string label = avgRadius > 0f
                    ? $"{count}p  r={avgRadius:F3}  Δ={minSpacing:F3}  {statusStr}"
                    : $"{count}p";

                UnityEditor.Handles.color = borderColor;
                UnityEditor.Handles.Label(
                    worldCenter + Vector3.up * (region.Size.y * 0.5f + 0.05f),
                    label);
            }
        }
#endif
    }

    /// <summary>
    /// 区域生成数据。定义一块矩形区域的位置、尺寸、调试颜色，以及可选的独立粒子配置。
    /// Spawn region data. Defines the position, size, debug color, and optional independent particle
    /// config for a single rectangular region.
    /// スポーン領域データ。矩形領域の位置、サイズ、デバッグカラー、およびオプションの独立したパーティクル設定を定義します。
    /// </summary>
    [Serializable]
    public class Liquid2DSpawnRegion
    {
#if UNITY_EDITOR
        public bool IsInit;
#endif
        
        [LocalizationTooltip(
            "区域中心位置（相对于生成器 Transform 的偏移，世界单位）。",
            "Region center position (offset from the spawner's Transform, in world units).",
            "領域中心位置（スポナーのTransformからのオフセット、ワールド単位）。")]
        public Vector2 Position;

        [LocalizationTooltip(
            "区域尺寸（世界单位，宽 × 高）。",
            "Region size (world units, width × height).",
            "領域サイズ（ワールド単位、幅×高さ）。")]
        public Vector2 Size;

        [LocalizationTooltip(
            "是否为本区域单独指定粒子配置（否则继承生成器全局配置）。",
            "Whether to use this region's own particle config (otherwise inherits the spawner's global config).",
            "この領域独自のパーティクル設定を使用するかどうか（使用しない場合はスポナーのグローバル設定を継承）。")]
        public bool OverrideParticles;

        [LocalizationTooltip(
            "本区域的粒子配置列表（仅在 overrideParticles 为 true 时生效）。",
            "Particle config list for this region (only active when overrideParticles is true).",
            "この領域のパーティクル設定リスト（overrideParticles が true の場合のみ有効）。")]
        public List<Liquid2DParticleConfig> Particles;
    }
}

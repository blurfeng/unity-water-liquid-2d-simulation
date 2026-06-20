using System;
using UnityEngine;
using System.Collections.Generic;
using Fs.Liquid2D.Utility;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 粒子生成器。持续喷射流体粒子到模拟器中，支持各种参数控制和随机化。
    /// Particle spawner. Continuously spawns fluid particles into the simulation, with various parameter controls and randomization.
    /// 粒子スポーナー。さまざまなパラメーター制御とランダム化を備えたシミュレーションへの流体粒子の継続的なスポーン。
    /// </summary>
    public class Liquid2DSpawner : MonoBehaviour
    {
        [SerializeField, LocalizationTooltip(
             "总开关：关闭后暂停所有粒子喷射，但不改变 IsSpawning 状态，重新开启后立即恢复。",
             "Master toggle: disables all particle spawning when off, without changing IsSpawning state; resumes immediately when re-enabled.",
             "マスタースイッチ：オフにすると IsSpawning 状態を変えずに噴射を一時停止し、オンに戻すと即座に再開します。")]
        private bool spawningEnabled = true;
        
        [Header("Common")]
        [SerializeField, LocalizationTooltip(
            "是否在启动时自动开始喷射", 
            "Whether to automatically start spraying on startup", 
            "起動時に自動的にスプレーを開始するかどうか")]
        private bool startOnAwake = true;
        
        [SerializeField, LocalizationTooltip(
             "初次启动时的延迟时间", 
             "Delay time for the first start", 
             "最初の起動時の遅延時間")]
        private float firstStartDelay = 1f;
        
        [SerializeField, LocalizationTooltip(
             "启动延迟时间（每次StartSpawn时都会应用）", 
             "Start delay time (applied every time StartSpawn is called)", 
             "開始遅延時間（StartSpawn呼び出し毎に適用）")]
        private float startDelay = 0.5f;
        
        [SerializeField, LocalizationTooltip(
             "持续时间（0表示无限持续）", 
             "Duration time (0 means infinite duration)", 
             "持続時間（0は無限持続を意味する）")]
        private float duration;

        [SerializeField, Min(0), LocalizationTooltip(
             "生成数量上限（0 表示无限制）。达到上限后自动停止喷射。",
             "Maximum spawn count (0 means unlimited). Spawning stops automatically when the limit is reached.",
             "生成数量の上限（0 は無制限）。上限に達すると自動的に噴射を停止します。")]
        private int maxSpawnCount;

#if UNITY_EDITOR
        [SerializeField, LocalizationTooltip(
             "已生成粒子总数（只读，运行时显示）", 
             "Total spawned particle count (read-only, displayed at runtime)", 
             "生成済みパーティクル総数（読み取り専用、実行時に表示）")]
        private int spawnedCount;
#endif
        
        [Header("Liquid Particle Settings")]
        [SerializeField, LocalizationTooltip(
             "流体粒子预制体（需挂载Liquid2DParticleRenderer）", 
             "Fluid particle prefab (requires Liquid2DParticleRenderer component)", 
             "流体パーティクルプレハブ（Liquid2DParticleRendererコンポーネントが必要）")]
        private List<Liquid2DParticleConfig> liquidParticles = new List<Liquid2DParticleConfig>();

        [SerializeField, Range(0.01f, 100f), LocalizationTooltip("喷嘴宽度。", "Nozzle width.", "ノズル幅。")]
        private float nozzleWidth = 1f;

        [SerializeField, Min(0f), LocalizationTooltip(
            "喷射深度抖动范围（沿喷射方向的随机偏移距离，世界单位）。大于 0 时，每颗粒子在喷射方向上额外随机偏移 [0, 此值]，" +
            "防止同帧多颗粒子叠在喷嘴同一位置而引起 SPH 压力爆炸。建议设置为光滑核半径（smoothingRadius）的 1–2 倍。",
            "Spawn depth jitter along the ejection direction (world units). When > 0, each particle is offset by a random " +
            "distance in [0, this value] along the ejection axis, preventing multiple same-frame particles from stacking " +
            "at the nozzle and triggering SPH pressure explosions. Recommended: 1–2× the smoothing radius.",
            "噴射深度ジッター（噴射方向のランダムオフセット距離、ワールド単位）。0 より大きい場合、各粒子が噴射方向に " +
            "[0, この値] のランダムオフセットを受け、同フレームの粒子がノズル同位置に積み重なり SPH 圧力爆発を起こすのを防ぐ。" +
            "平滑核半径（smoothingRadius）の 1～2 倍を推奨。")]
        private float spawnDepthJitter;
        
        [SerializeField, LocalizationTooltip(
            "流量。每秒喷射的粒子数量。", 
            "Flow rate. Number of particles sprayed per second.", 
            "流量。毎秒噴射されるパーティクル数。")]
        private float flowRate = 60f;

        [SerializeField, LocalizationTooltip("流量调整系数", "Flow rate adjustment factor", "流量調整係数")]
        private float flowRateFactor = 1f;
        
        [SerializeField, LocalizationTooltip(
             "尺寸随机范围（最小值，最大值）", 
             "Size random range (minimum, maximum)", 
             "サイズランダム範囲（最小値、最大値）")]
        private Vector2 sizeRandomRange = new Vector2(0.9f, 1.2f);

        [SerializeField, LocalizationTooltip("喷射力大小", "Ejection force magnitude", "噴射力の大きさ")]
        private float ejectForce = 40f;
        
        [SerializeField, LocalizationTooltip("喷射力调整系数", "Ejection force adjustment factor", "噴射力調整係数")]
        private float ejectForceFactor = 1f;
        
        [SerializeField, LocalizationTooltip(
             "喷射力随机范围（最小值，最大值）",
             "Ejection force random range (minimum, maximum)", 
             "噴射力ランダム範囲（最小値、最大値）")]
        private Vector2 ejectForceRandomRange = new Vector2(0.9f, 1.2f);
        
        [Header("Swing")]
        [SerializeField, LocalizationTooltip(
             "摆动角度范围（最大偏移，单位度）", 
             "Swing angle range (maximum offset, in degrees)", 
             "スイング角度範囲（最大オフセット、度単位）")]
        private float swingAngleRange;

        [SerializeField, LocalizationTooltip(
             "摆动速度（周期/秒）", 
             "Swing speed (cycles per second)", 
             "スイング速度（サイクル/秒）")]
        private float swingSpeed = 0.2f;

        public Transform TransformGet
        {
            get
            {
                if (!_transform) _transform = transform;
                return _transform;
            }
        }
        private Transform _transform;
        
        /// <summary>
        /// 流体粒子父节点。
        /// Parent transform for fluid particles.
        /// 流体パーティクルの親トランスフォーム。
        /// </summary>
        public Transform ParticleParentTs
        {
            get
            {
                if (!_particleParentTs)
                {
                    _particleParentTs = new GameObject($"{name}_LiquidParticles").transform;
                    _particleParentTs.position = TransformGet.position;
                }
                return _particleParentTs;
            }
        }
        private Transform _particleParentTs;
        
        private float _flowRateUse; // 实际流量。 // Actual flow rate. // 実際の流量。
        private float _flowRateInterval; // 流量间隔时间。 // Flow rate interval time. // 流量間隔時間。
        
        private float _ejectForceUse; // 实际喷射力。 // Actual ejection force. // 実際の噴射力。

        private bool _isDelayedStarted; // 是否已延迟启动。 // Whether delayed start has occurred. // 遅延スタートが発生したかどうか。
        private bool _isFirstStart = true; // 是否首次启动。 // Whether it is the first start. // 初回開始かどうか。
        private float _delayTimer; // 延迟计时器。 // Delay timer. // 遅延タイマー。
        private float _flowTimer; // 流量累积计时器。 // Flow rate accumulation timer. // 流量蓄積タイマー。
        private float _durationTimer; // 持续时间计时器。 // Duration timer. // 持続時間タイマー。
        private float _swingTime; // 摆动时间计时器。 // Swing time timer. // スイング時間タイマー。
        private bool _checkDelayStart = true; // 是否检查启动延迟。 // Whether to check start delay. // 開始遅延をチェックするかどうか。
        private bool _checkDuration = true; // 是否检查持续时间。 // Whether to check duration. // 持続時間をチェックするかどうか。
        private int _spawnedCount; // 已生成粒子总数。 // Total spawned particle count. // 生成済みパーティクル総数。

        public bool IsSpawning { get; private set; } // 是否正在喷射中。 // Whether it is currently spawning. // 現在噴射中かどうか。
        private static bool _isInitForJit; // 是否已为JIT预热。 // Whether JIT has been warmed up. // JITがウォームアップされているかどうか。

        #region 公开方法 Public Methods 

        /// <summary>
        /// 开始喷射流体粒子。
        /// Start spawning fluid particles.
        /// 流体粒子の噴射を開始。
        /// </summary>
        /// <param name="resetDelayStart">重置启动延迟计时器。假设设置了有效的启动延迟，那么在开始喷射前会有一段延迟时间。// reset start delay timer. Assuming a valid start delay is set, there will be a delay before spawning starts. // 開始遅延タイマーをリセット。 有効な開始遅延が設定されていると仮定すると、噴射が開始される前に遅延があります。</param>
        /// <param name="resetDuration">重置持续时间计时器。假设设置了有效的持续时间，那么在运行一段时间后会停止喷射。// reset duration timer. Assuming a valid duration is set, spawning will stop after running for a period of time. // 持続時間タイマーをリセット。 有効な持続時間が設定されていると仮定すると、一定期間実行した後に噴射が停止します。</param>
        /// <param name="resetSpawnCount">重置已生成粒子计数器。// Reset the spawned particle counter. // 生成済みパーティクルカウンターをリセット。</param>
        public void StartSpawn(bool resetDelayStart = true, bool resetDuration = true, bool resetSpawnCount = false)
        {
            if (IsSpawning) return;
            IsSpawning = true;
            
            // 重置计时器。 // Reset timers. // タイマーをリセット。
            if (resetDelayStart) ResetDelayStart(); 

            // 重置持续时间计时器。 // Reset duration timer. // 持続時間タイマーをリセット。
            if (resetDuration) ResetDuration();

            // 重置生成计数器。 // Reset spawn counter. // 生成カウンターをリセット。
            if (resetSpawnCount) ResetSpawnCount();
            
            // 标记非首次启动。 // Mark as not first start. // 初回開始ではないことをマーク。
            _isFirstStart = false;
        }
        
        /// <summary>
        /// 停止喷射流体粒子。
        /// Stop spawning fluid particles.
        /// 流体粒子の噴射を停止。
        /// </summary>
        public void StopSpawn()
        {
            if (!IsSpawning) return;
            IsSpawning = false;
            
            // 当调用停止生成时，不会再检查延迟和持续时间。
            _checkDelayStart = false;
            _checkDuration = false;
        }

        /// <summary>
        /// 设置流量。
        /// Set flow rate.
        /// 流量を設定します。
        /// </summary>
        /// <param name="aFlowRate"></param>
        public void SetFlowRate(float aFlowRate)
        {
            if (aFlowRate <= 0f)
            {
                flowRate = 0f;
                _flowRateUse = 0f;
                return;
            }

            flowRate = aFlowRate;
            _flowRateUse = flowRate * flowRateFactor;
            _flowRateInterval = 1f / _flowRateUse;
        }
        
        /// <summary>
        /// 设置流量调整系数。
        /// Set flow rate adjustment factor.
        /// 流量調整係数を設定します。
        /// </summary>
        /// <param name="aFlowRateFactor"></param>
        public void SetFlowRateFactor(float aFlowRateFactor)
        {
            if (aFlowRateFactor <= 0f)
            {
                this.flowRateFactor = 0f;
                _flowRateUse = 0f;
                return;
            }            
            
            flowRateFactor = aFlowRateFactor;
            _flowRateUse = flowRate * this.flowRateFactor;
            _flowRateInterval = 1f / _flowRateUse;
        }
        
        /// <summary>
        /// 设置喷射力。
        /// Set ejection force.
        /// 噴射力を設定します。
        /// </summary>
        /// <param name="aEjectForce"></param>
        public void SetEjectForce(float aEjectForce)
        {
            if (aEjectForce <= 0f)
            {
                ejectForce = 0f;
                _ejectForceUse = 0f;
                return;
            }

            ejectForce = aEjectForce;
            _ejectForceUse = ejectForce * ejectForceFactor;
        }
        
        /// <summary>
        /// 设置喷射力调整系数。
        /// Set ejection force adjustment factor.
        /// 噴射力調整係数を設定します。
        /// </summary>
        /// <param name="aEjectForceFactor"></param>
        public void SetEjectForceFactor(float aEjectForceFactor)
        {
            if (aEjectForceFactor <= 0f)
            {
                this.ejectForceFactor = 0f;
                _ejectForceUse = 0f;
                return;
            }
            
            ejectForceFactor = aEjectForceFactor;
            _ejectForceUse = ejectForce * this.ejectForceFactor;
        }

        /// <summary>
        /// 重置启动延迟计时器。
        /// </summary>
        public void ResetDelayStart()
        {
            _checkDelayStart = true;
            _isDelayedStarted = false;
            _delayTimer = 0f;
            _flowTimer = 0f;
        }

        /// <summary>
        /// 设置持续时间。
        /// </summary>
        /// <param name="aDuration"></param>
        /// <param name="resetDuration"></param>
        public void SetDuration(float aDuration, bool resetDuration = true)
        {
            duration = aDuration;
            
            if (resetDuration)
                ResetDuration();
        }

        /// <summary>
        /// 重置持续时间计时器。
        /// </summary>
        public void ResetDuration()
        {
            _checkDuration = true;
            _durationTimer = 0f;
        }

        /// <summary>
        /// 重置已生成粒子计数器。
        /// Reset the spawned particle counter.
        /// 生成済みパーティクルカウンターをリセット。
        /// </summary>
        public void ResetSpawnCount()
        {
            _spawnedCount = 0;
#if UNITY_EDITOR
            spawnedCount = _spawnedCount;
#endif
        }

        /// <summary>
        /// 设置生成数量上限（0 表示无限制）。
        /// Set the maximum spawn count (0 means unlimited).
        /// 生成数量の上限を設定します（0 は無制限）。
        /// </summary>
        /// <param name="aMaxSpawnCount"></param>
        /// <param name="resetCount">同时重置已生成计数器。// Also reset the spawned counter. // 生成済みカウンターも同時にリセット。</param>
        public void SetMaxSpawnCount(int aMaxSpawnCount, bool resetCount = true)
        {
            maxSpawnCount = Mathf.Max(0, aMaxSpawnCount);
            if (resetCount) ResetSpawnCount();
        }
        #endregion
        
        protected virtual void Awake()
        {
            // 预热JIT。一些复杂的类，特别是泛型类，在首次使用时会有较大的性能开销，可能会导致卡顿。
            // Warm up JIT. Some complex classes, especially generic classes, have a large performance overhead when used for the first time, which may cause stuttering.
            // JITをウォームアップします。特にジェネリッククラスなどの複雑なクラスは、初めて使用する際に大きなパフォーマンスオーバーヘッドが発生し、スタッタリングの原因となる可能性があります。
            if (!_isInitForJit)
            {
                _isInitForJit = true;
                liquidParticles.RandomWeight();
            }
        }

        protected virtual void Start()
        {
            // 初始化流量和喷射力。 // Initialize flow rate and ejection force. // 流量と噴射力を初期化します。
            SetFlowRate(flowRate);
            SetEjectForce(ejectForce);
            
            if (startOnAwake)
            {
                StartSpawn();
            }
        }
        
        protected virtual void Update()
        {
            // 总开关关闭时暂停，不影响内部状态。 // Master toggle off: pause without touching internal state. // マスタースイッチがオフのとき、内部状態を変えずに一時停止。
            if (!spawningEnabled) return;

            // 未在喷射中。 // Not spawning. // 噴射中ではない。
            if (!IsSpawning) return;
            
            // 延迟启动。 // Delayed start. // 遅延スタート。
            if (_checkDelayStart && !_isDelayedStarted)
            {
                float currentDelay = _isFirstStart ? firstStartDelay : startDelay;
                if (currentDelay > 0f)
                {
                    _delayTimer += Time.deltaTime;
                    if (_delayTimer >= currentDelay)
                    {
                        _isDelayedStarted = true;
                        _delayTimer = 0f;
                    }
                }
                else
                {
                    _isDelayedStarted = true;
                }
                return;
            }
            
            // 持续时间检查。 // Duration check. // 持続時間チェック。
            if (_checkDuration && duration > 0f)
            {
                _durationTimer += Time.deltaTime;
                if (_durationTimer >= duration)
                {
                    StopSpawn();
                    return;
                }
            }
            
            // 摆动。 // Swing. // スイング。
            if (swingAngleRange > 0f && swingSpeed > 0f)
            {
                _swingTime += Time.deltaTime;
            }
            
            // 喷射粒子。 // Spawn particles. // 粒子を噴射。
            if (_flowRateUse > 0f)
            {
                _flowTimer += Time.deltaTime;
                while (_flowTimer >= _flowRateInterval)
                {
                    _flowTimer -= _flowRateInterval;
                    SpawnOne();
                }
            }
        }
        
        /// <summary>
        /// 获取当前喷射角度，包含摆动偏移。
        /// Get current ejection angle, including swing offset.
        /// 現在の噴射角度を取得（スイング偏移を含む）。
        /// </summary>
        /// <returns></returns>
        private float GetCurrentEjectAngle()
        {
            if (swingAngleRange > 0f && swingSpeed > 0f)
            {
                float offset = Mathf.Sin(_swingTime * swingSpeed * Mathf.PI * 2f) * swingAngleRange;
                return offset;
            }
            return 0f;
        }
        
        /// <summary>
        /// 获取当前喷射方向，包含摆动偏移。
        /// Get current ejection direction, including swing offset.
        /// 現在の噴射方向を取得（スイング偏移を含む）。
        /// </summary>
        /// <returns></returns>
        private Vector2 GetCurrentEjectDirection()
        {
            Vector2 forward = -TransformGet.up; // 默认朝下
            Vector2 dir = Quaternion.Euler(0, 0, GetCurrentEjectAngle()) * forward;
            return dir;
        }

        /// <summary>
        /// 生成一个流体粒子（写入模拟器 SoA，无 GameObject）。 // Spawn one fluid particle (into the simulation SoA, no GameObject). // 1つの流体粒子を生成（SoA へ、GameObject なし）。
        /// </summary>
        /// <param name="onSpawned">生成回调，参数为粒子句柄。 // Spawn callback with the particle handle. // 生成コールバック（粒子ハンドル）。</param>
        public void SpawnOne(Action<Liquid2DParticleHandle> onSpawned = null)
        {
            // 随机选择一个流体粒子描述符。 // Randomly select a fluid particle descriptor. // ランダムに流体粒子記述子を選択。
            if (liquidParticles.Count == 0) return;
            Liquid2DParticleConfig config = liquidParticles.RandomWeight();
            if (config == null || config.Descriptor == null) return;

            var sim = Liquid2DSimulation.Instance;
            if (sim == null) return;

            // 获取当前喷射方向。 // Get current ejection direction. // 現在の噴射方向を取得。
            Vector2 dir = GetCurrentEjectDirection();

            // 随机获取生成位置。 // Randomly get spawn position. // ランダムに生成位置を取得。
            Vector2 normal = new Vector2(-dir.y, dir.x); // 计算法线 // Calculate normal // 法線を計算
            float offset = UnityEngine.Random.Range(-nozzleWidth * 0.5f, nozzleWidth * 0.5f);
            // 喷射方向深度抖动：防止同帧多颗粒子堆叠在喷嘴同一位置引发 SPH 压力爆炸。
            // Ejection-direction depth jitter: prevents multiple same-frame particles from stacking at the nozzle,
            // which would trigger SPH pressure explosions and scatter particles in random directions.
            // 噴射方向の深度ジッター：同フレームの粒子がノズル同位置に積み重なって SPH 圧力爆発を起こすのを防ぐ。
            float depthOffset = spawnDepthJitter > 0f ? UnityEngine.Random.Range(0f, spawnDepthJitter) : 0f;
            Vector3 spawnPos3 = TransformGet.position + (Vector3)(normal * offset) + (Vector3)(dir * depthOffset);
            float2 spawnPos = new float2(spawnPos3.x, spawnPos3.y);

            // 随机尺寸缩放。 // Random size scale. // ランダムサイズスケール。
            float sizeScale = 1f;
            if (sizeRandomRange.y > sizeRandomRange.x && sizeRandomRange.x > 0f)
                sizeScale = UnityEngine.Random.Range(sizeRandomRange.x, sizeRandomRange.y);

            // 喷射力 → 初速度（冲量/质量）。 // Ejection force → initial velocity (impulse/mass). // 噴射力 → 初速度（力積/質量）。
            float forceEject = _ejectForceUse;
            if (ejectForceRandomRange.y > ejectForceRandomRange.x && ejectForceRandomRange.x >= 0f)
                forceEject *= UnityEngine.Random.Range(ejectForceRandomRange.x, ejectForceRandomRange.y);

            float mass = config.Descriptor.material != null ? Mathf.Max(0.0001f, config.Descriptor.material.mass) : 1f;
            float2 velocity = new float2(dir.x, dir.y) * (forceEject / mass);

            var handle = sim.Spawn(config.Descriptor, spawnPos, velocity, sizeScale, config.Lifetime);
            onSpawned?.Invoke(handle);

            // 数量上限检查。 // Max spawn count check. // 生成数上限チェック。
            _spawnedCount++;
#if UNITY_EDITOR
            spawnedCount = _spawnedCount;
#endif
            if (maxSpawnCount > 0 && _spawnedCount >= maxSpawnCount)
            {
                StopSpawn();
            }
        }

        #if UNITY_EDITOR
        private Color _gizmosBodyColor = Color.yellow;
        
        private void OnValidate()
        {
            if (liquidParticles.Count > 0)
            {
                var d = liquidParticles[0].Descriptor;
                if (d && d.renderSettings != null)
                {
                    _gizmosBodyColor = d.renderSettings.color;
                }
            }
            
            // 流量参数即时生效：Inspector 修改后立即同步运行时计算值。
            // Immediately sync flow rate runtime values when Inspector values change.
            // Inspector 変更時に流量のランタイム計算値を即座に同期。
            SetFlowRate(flowRate);
            SetEjectForce(ejectForce);
            
            // 数量上限检查。 // Max spawn count check. // 生成数上限チェック。
            if (maxSpawnCount > 0 && _spawnedCount < maxSpawnCount)
            {
                StartSpawn();
            }
        }

        private void OnDrawGizmos()
        {
            // 屏幕像素目标（可按需调整）。 // Screen pixel targets (can be adjusted as needed). // 画面ピクセルターゲット（必要に応じて調整可能）。
            const float lineLengthPixels = 60f; // 主线长度。 // Main line length. // メインラインの長さ。
            const float arrowHeadPixels = 12f; // 箭头长度。 // Arrowhead length. // 矢印の長さ。
            const float cubeSizePixels = 20f; // 方块尺寸。 // Cube size. // キューブのサイズ。

            // 获取场景视图相机。 // Get scene view camera. // シーンビューカメラを取得します。
            Camera cam = UnityEditor.SceneView.lastActiveSceneView.camera;

            // 计算像素到世界单位的换算（以物体与相机的距离处为基准）。
            // Calculate pixel to world unit conversion (based on the distance between the object and the camera).
            // ピクセルからワールドユニットへの変換を計算します（オブジェクトとカメラの距離に基づいて）。
            float worldPerPixel = 0.01f; // 回退值。 // Fallback value. // フォールバック値。
            if (cam)
            {
                if (cam.orthographic)
                {
                    worldPerPixel = (cam.orthographicSize * 2f) / cam.pixelHeight;
                }
                else
                {
                    float distance = Mathf.Abs(Vector3.Dot(cam.transform.forward, TransformGet.position - cam.transform.position));
                    // 若距离过小（相机在物体内部），使用最小值避免 NaN/0。
                    // If the distance is too small (camera inside the object), use a minimum value to avoid NaN/0.
                    // 距離が小さすぎる場合（カメラがオブジェクト内にある場合）、NaN/0を避けるために最小値を使用します。
                    distance = Mathf.Max(distance, 0.0001f);
                    float frustumHeight = 2f * distance * Mathf.Tan(cam.fieldOfView * Mathf.Deg2Rad * 0.5f);
                    worldPerPixel = frustumHeight / cam.pixelHeight;
                }
            }

            // 将像素长度转换为世界单位。 // Convert pixel length to world units. // ピクセル長をワールドユニットに変換します。
            float lineLengthWorld = Mathf.Max(lineLengthPixels * worldPerPixel, 0.001f);
            float arrowHeadWorld = Mathf.Max(arrowHeadPixels * worldPerPixel, 0.001f);

            // 绘制主线与箭头（使用世界单位长度）。
            // Draw main line and arrowhead (using world unit length).
            // メインラインと矢印を描画します（ワールドユニットの長さを使用）。
            Gizmos.color = Color.cyan;
            Vector3 start = TransformGet.position;
            Vector2 dir2D = GetCurrentEjectDirection();
            Vector3 dir = dir2D.normalized;
            Vector3 end = start + dir * lineLengthWorld;
            Gizmos.DrawLine(start, end);

            Vector3 left = end + Quaternion.Euler(0, 0, 150f) * (dir * arrowHeadWorld);
            Vector3 right = end + Quaternion.Euler(0, 0, -150f) * (dir * arrowHeadWorld);
            Gizmos.DrawLine(end, left);
            Gizmos.DrawLine(end, right);

            // 绘制固定屏幕尺寸的方块（只应用位置和旋转）。
            // Draw fixed screen size cube (only apply position and rotation).
            // 固定画面サイズのキューブを描画します（位置と回転のみを適用します）。
            float cubeSizeWorld = Mathf.Max(cubeSizePixels * worldPerPixel, 0.001f);
            Gizmos.color = _gizmosBodyColor;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(TransformGet.position, TransformGet.rotation, Vector3.one);
            Gizmos.DrawCube(Vector3.zero, new Vector3(cubeSizeWorld, cubeSizeWorld, Mathf.Max(cubeSizeWorld * 0.1f, 0.01f)));
            Gizmos.matrix = oldMatrix;
            
            // 绘制喷嘴宽度线（真实世界单位，不随相机变化）
            Vector2 normal2D = new Vector2(-dir2D.y, dir2D.x).normalized;
            Vector3 normal = normal2D;
            Vector3 leftPos = start + normal * (nozzleWidth * 0.5f);
            Vector3 rightPos = start - normal * (nozzleWidth * 0.5f);

            Gizmos.color = Color.magenta;
            Gizmos.DrawLine(leftPos, rightPos); // 喷嘴宽度主线
            // 两端的短向外标记，便于视觉判断方向
            float endCapLen = Mathf.Min(0.1f, nozzleWidth * 0.1f);
            Gizmos.DrawLine(leftPos, leftPos + dir * endCapLen);
            Gizmos.DrawLine(rightPos, rightPos + dir * endCapLen);
        }
        #endif
    }
}
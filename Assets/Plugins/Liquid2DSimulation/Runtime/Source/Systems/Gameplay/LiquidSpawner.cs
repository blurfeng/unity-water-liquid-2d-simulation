using System;
using UnityEngine;
using System.Collections.Generic;
using Fs.Liquid2D.Utility;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    public class LiquidSpawner : MonoBehaviour
    {
        [Header("Common")]
        [LocalizationTooltip(
            "是否在启动时自动开始喷射", 
            "Whether to automatically start spraying on startup", 
            "起動時に自動的にスプレーを開始するかどうか")]
        public bool startOnAwake = true;
        
        [LocalizationTooltip(
            "初次启动时的延迟时间", 
            "Delay time for the first start", 
            "最初の起動時の遅延時間")]
        public float firstStartDelay = 1f;
        
        [LocalizationTooltip(
            "启动延迟时间（每次StartSpawn时都会应用）", 
            "Start delay time (applied every time StartSpawn is called)", 
            "開始遅延時間（StartSpawn呼び出し毎に適用）")]
        public float startDelay = 0.5f;
        
        [LocalizationTooltip(
            "持续时间（0表示无限持续）", 
            "Duration time (0 means infinite duration)", 
            "持続時間（0は無限持続を意味する）")]
        public float duration;
        
        [Header("Liquid Particle Settings")]
        [LocalizationTooltip(
            "流体粒子预制体（需挂载Liquid2DParticleRenderer）", 
            "Fluid particle prefab (requires Liquid2DParticleRenderer component)", 
            "流体パーティクルプレハブ（Liquid2DParticleRendererコンポーネントが必要）")]
        public List<Liquid2DParticleConfig> liquidParticles = new List<Liquid2DParticleConfig>();

        [Range(0.01f, 100f), LocalizationTooltip("喷嘴宽度。", "Nozzle width.", "ノズル幅。")]
        public float nozzleWidth = 1f;
        
        [SerializeField, LocalizationTooltip(
            "流量。每秒喷射的粒子数量。", 
            "Flow rate. Number of particles sprayed per second.", 
            "流量。毎秒噴射されるパーティクル数。")]
        private float flowRate = 60f;

        [SerializeField, LocalizationTooltip("流量调整系数", "Flow rate adjustment factor", "流量調整係数")]
        private float flowRateFactor = 1f;
        
        [LocalizationTooltip(
            "尺寸随机范围（最小值，最大值）", 
            "Size random range (minimum, maximum)", 
            "サイズランダム範囲（最小値、最大値）")]
        public Vector2 sizeRandomRange = new Vector2(0.9f, 1.2f);

        [SerializeField, LocalizationTooltip("喷射力大小", "Ejection force magnitude", "噴射力の大きさ")]
        private float ejectForce = 40f;
        
        [SerializeField, LocalizationTooltip("喷射力调整系数", "Ejection force adjustment factor", "噴射力調整係数")]
        private float ejectForceFactor = 1f;
        
        [LocalizationTooltip(
            "喷射力随机范围（最小值，最大值）",
            "Ejection force random range (minimum, maximum)", 
            "噴射力ランダム範囲（最小値、最大値）")]
        public Vector2 ejectForceRandomRange = new Vector2(0.9f, 1.2f);
        
        [Header("Swing")]
        [LocalizationTooltip(
            "摆动角度范围（最大偏移，单位度）", 
            "Swing angle range (maximum offset, in degrees)", 
            "スイング角度範囲（最大オフセット、度単位）")]
        public float swingAngleRange = 0f;

        [LocalizationTooltip(
            "摆动速度（周期/秒）", 
            "Swing speed (cycles per second)", 
            "スイング速度（サイクル/秒）")]
        public float swingSpeed = 0.2f;

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
        public void StartSpawn(bool resetDelayStart = true, bool resetDuration = true)
        {
            if (IsSpawning) return;
            IsSpawning = true;
            
            // 重置计时器。 // Reset timers. // タイマーをリセット。
            if (resetDelayStart) ResetDelayStart(); 

            // 重置持续时间计时器。 // Reset duration timer. // 持続時間タイマーをリセット。
            if (resetDuration) ResetDuration();
            
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
            if (config == null || config.descriptor == null) return;

            var sim = Liquid2DSimulation.Instance;
            if (sim == null) return;

            // 获取当前喷射方向。 // Get current ejection direction. // 現在の噴射方向を取得。
            Vector2 dir = GetCurrentEjectDirection();

            // 随机获取生成位置。 // Randomly get spawn position. // ランダムに生成位置を取得。
            Vector2 normal = new Vector2(-dir.y, dir.x); // 计算法线 // Calculate normal // 法線を計算
            float offset = UnityEngine.Random.Range(-nozzleWidth * 0.5f, nozzleWidth * 0.5f);
            Vector3 spawnPos3 = TransformGet.position + (Vector3)(normal * offset);
            float2 spawnPos = new float2(spawnPos3.x, spawnPos3.y);

            // 随机尺寸缩放。 // Random size scale. // ランダムサイズスケール。
            float sizeScale = 1f;
            if (sizeRandomRange.y > sizeRandomRange.x && sizeRandomRange.x > 0f)
                sizeScale = UnityEngine.Random.Range(sizeRandomRange.x, sizeRandomRange.y);

            // 喷射力 → 初速度（冲量/质量）。 // Ejection force → initial velocity (impulse/mass). // 噴射力 → 初速度（力積/質量）。
            float forceEject = _ejectForceUse;
            if (ejectForceRandomRange.y > ejectForceRandomRange.x && ejectForceRandomRange.x >= 0f)
                forceEject *= UnityEngine.Random.Range(ejectForceRandomRange.x, ejectForceRandomRange.y);

            float mass = config.descriptor.material != null ? Mathf.Max(0.0001f, config.descriptor.material.mass) : 1f;
            float2 velocity = new float2(dir.x, dir.y) * (forceEject / mass);

            var handle = sim.Spawn(config.descriptor, spawnPos, velocity, sizeScale, config.lifetime);
            onSpawned?.Invoke(handle);
        }

        #if UNITY_EDITOR
        private Color _gizmosBodyColor = Color.yellow;
        
        private void OnValidate()
        {
            if (liquidParticles.Count > 0)
            {
                var d = liquidParticles[0].descriptor;
                if (d && d.renderSettings != null)
                {
                    _gizmosBodyColor = d.renderSettings.color;
                }
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
                    worldPerPixel = frustumHeight / (float)cam.pixelHeight;
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
            Vector3 dir = (Vector3)dir2D.normalized;
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
            Vector3 normal = (Vector3)normal2D;
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
    
    /// <summary>
    /// 用于喷射的流体粒子设置。
    /// Liquid particle settings for spawning.
    /// スポーン用の流体パーティクル設定。
    /// </summary>
    [Serializable]
    public class Liquid2DParticleConfig : IRandomData
    {
        [LocalizationTooltip("流体粒子描述符（定义外观/混色/材质/尺寸）。",
             "Fluid particle descriptor (defines appearance/mixing/material/size).",
             "流体パーティクル記述子（外観/混色/マテリアル/サイズを定義）。")]
        public Liquid2DParticleDescriptor descriptor;

        [LocalizationTooltip("权重，决定被选中的概率。",
             "Weight, determines the probability of being selected.",
             "重み、選択される確率を決定します。")]
        public int weight = 1;

        [LocalizationTooltip("生命周期，单位秒。大于0则覆盖描述符默认寿命并在到时后自动销毁。",
             "Lifetime in seconds. If greater than 0, overrides the descriptor default and auto-destroys after expiry.",
             "ライフタイム（秒）。0より大きい場合、記述子の既定を上書きし期限後に自動破棄。")]
        public float lifetime = 0f;

        public int GetWeight()
        {
            return weight;
        }

#if UNITY_EDITOR
        public void OnValidate()
        {
            if (descriptor && !descriptor.IsValid())
            {
                Debug.LogWarning($"Liquid2DParticleDescriptor {descriptor.name} 缺少 Sprite/Material，渲染将被跳过。");
            }
        }
#endif
    }
}
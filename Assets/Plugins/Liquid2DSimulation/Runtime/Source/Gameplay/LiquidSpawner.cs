using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Fs.Liquid2D.Utility;
using Fs.Liquid2D.Localization;

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
        
        [LocalizationTooltip("启动时的延迟时间", "Delay time on startup", "起動時の遅延時間")]
        public float startDelay = 2f;
        
        [Header("Liquid Particle Settings")]
        [LocalizationTooltip(
            "流体粒子预制体（需挂载Liquid2DParticleRenderer）", 
            "Fluid particle prefab (requires Liquid2DParticleRenderer component)", 
            "流体パーティクルプレハブ（Liquid2DParticleRendererコンポーネントが必要）")]
        public List<LiquidParticle> liquidParticles = new List<LiquidParticle>();

        [Range(0.01f, 100f), LocalizationTooltip("喷嘴宽度。", "Nozzle width.", "ノズル幅。")]
        public float nozzleWidth = 1f;
        
        [LocalizationTooltip(
            "流量。每秒喷射的粒子数量。", 
            "Flow rate. Number of particles sprayed per second.", 
            "流量。毎秒噴射されるパーティクル数。")]
        public float flowRate = 60f;
        
        [LocalizationTooltip(
            "尺寸随机范围（最小值，最大值）", 
            "Size random range (minimum, maximum)", 
            "サイズランダム範囲（最小値、最大値）")]
        public Vector2 sizeRandomRange = new Vector2(0.9f, 1.2f);

        [LocalizationTooltip("喷射力大小", "Ejection force magnitude", "噴射力の大きさ")]
        public float ejectForce = 40f;
        
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

        [LocalizationTooltip("摆动速度（周期/秒）", "Swing speed (cycles per second)", "スイング速度（サイクル/秒）")]
        public float swingSpeed = 0.2f;

        public Transform TransformGet
        {
            get
            {
                if (_transform == null) _transform = transform;
                return _transform;
            }
        }
        private Transform _transform;

        private bool _isDelayedStarted = false;
        private float _timer = 0f;
        private float _swingTime;
        
        // 是否正在喷射中。 // Whether it is currently spawning. // 現在噴射中かどうか。
        private bool _isSpawning = false;
        private static bool _isInitForJit = false;
        
        private void Awake()
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

        private void Start()
        {
            if (startOnAwake)
            {
                StartSpawn();
            }
        }
        
        private void Update()
        {
            if (!_isSpawning) return;
            
            // 延迟启动。 // Delayed start. // 遅延スタート。
            if (!_isDelayedStarted)
            {
                if (startDelay > 0f)
                {
                    _timer += Time.deltaTime;
                    if (_timer >= startDelay)
                    {
                        _isDelayedStarted = true;
                        _timer = 0f;
                    }
                }
                else
                {
                    _isDelayedStarted = true;
                }
                return;
            }
            
            // 摆动。 // Swing. // スイング。
            if (swingAngleRange > 0f && swingSpeed > 0f)
            {
                _swingTime += Time.deltaTime;
            }
            
            // 喷射粒子。 // Spawn particles. // 粒子を噴射。
            if (flowRate > 0f)
            {
                _timer += Time.deltaTime;
                float intetval = 1f / flowRate;
                while (_timer >= intetval)
                {
                    _timer -= intetval;
                    SpawnOne();
                }
            }
        }

        /// <summary>
        /// 开始喷射流体粒子。
        /// Start spawning fluid particles.
        /// 流体粒子の噴射を開始。
        /// </summary>
        public void StartSpawn()
        {
            if (_isSpawning) return;
            _isSpawning = true;
        }
        
        /// <summary>
        /// 停止喷射流体粒子。
        /// Stop spawning fluid particles.
        /// 流体粒子の噴射を停止。
        /// </summary>
        public void StopSpawn()
        {
            if (!_isSpawning) return;
            _isSpawning = false;
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
        /// 生成一个流体粒子。 // Spawn one fluid particle. // 1つの流体粒子を生成。
        /// </summary>
        private void SpawnOne()
        {
            // 随机选择一个流体粒子预制体。 // Randomly select a fluid particle prefab. // ランダムに流体粒子のプレハブを選択。
            if (liquidParticles.Count == 0) return;
            LiquidParticle liquidParticle = liquidParticles.RandomWeight();
            
            // 获取当前喷射方向。 // Get current ejection direction. // 現在の噴射方向を取得。
            Vector2 dir = GetCurrentEjectDirection();
            Transform tsSpa = TransformGet;
            
            // 随机获取生成位置。 // Randomly get spawn position. // ランダムに生成位置を取得。
            Vector2 normal = new Vector2(-dir.y, dir.x); // 计算法线 // Calculate normal // 法線を計算
            float offset = UnityEngine.Random.Range(-nozzleWidth * 0.5f, nozzleWidth * 0.5f);
            Vector3 spawnPos = tsSpa.position + (Vector3)(normal * offset);
            
            // 生成预制体。 // Instantiate prefab. // プレハブをインスタンス化。
            Loader.Load(liquidParticle.liquidPrefab, (go) =>
            {
                Transform goTs = go.transform;
                goTs.position = spawnPos;
                // goTs.rotation = Quaternion.identity;
                go.transform.SetParent(tsSpa);
            
                // 检查是否挂载 Liquid2DParticleRenderer。 // Check if Liquid2DParticleRenderer is attached. // Liquid2DParticleRendererがアタッチされているか確認。
                var lRenderer = go.GetComponent<Liquid2DParticle>();
                if (lRenderer == null)
                {
                    Debug.LogWarning("预制体未挂载Liquid2DParticleRenderer！");
                    Destroy(go);
                    return;
                }
            
                // 随机设置尺寸。 // Randomly set size. // ランダムにサイズを設定。
                if (sizeRandomRange.y > sizeRandomRange.x && sizeRandomRange.x > 0f)
                {
                    float size = UnityEngine.Random.Range(sizeRandomRange.x, sizeRandomRange.y);
                    go.transform.localScale *= size;
                }
            
                var rb = go.GetComponent<Rigidbody2D>();
                if (rb != null)
                {
                    // 随机设置喷射力。 // Randomly set ejection force. // ランダムに噴射力を設定。
                    float force = ejectForce;
                    if (ejectForceRandomRange.y > ejectForceRandomRange.x && ejectForceRandomRange.x >= 0f)
                    {
                        force *= UnityEngine.Random.Range(ejectForceRandomRange.x, ejectForceRandomRange.y);
                    }
                    rb.AddForce(dir * force, ForceMode2D.Impulse);
                }
            
                lRenderer.SetLifetime(liquidParticle.lifetime);
            });
        }

        #if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Vector3 start = TransformGet.position;
            Vector2 dir = GetCurrentEjectDirection();
            Vector3 end = start + (Vector3)dir * 3.5f;

            Gizmos.DrawLine(start, end);

            Vector3 left = end + Quaternion.Euler(0, 0, 150) * (Vector3)(dir * 0.6f);
            Vector3 right = end + Quaternion.Euler(0, 0, -150) * (Vector3)(dir * 0.6f);
            Gizmos.DrawLine(end, left);
            Gizmos.DrawLine(end, right);
            
            Gizmos.color = Color.yellow;
            Matrix4x4 oldMatrix = Gizmos.matrix;
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawCube(Vector3.zero, new Vector3(2f, 2f, 2f));
            Gizmos.matrix = oldMatrix;
        }
        #endif
    }
    
    [Serializable]
    public class LiquidParticle : IRandomData
    {
        [LocalizationTooltip("流体粒子预制体（需挂载Liquid2DParticleRenderer）。",
             "Fluid particle prefab (requires Liquid2DParticleRenderer component).",
             "流体パーティクルプレハブ（Liquid2DParticleRendererコンポーネントが必要）。")]
        public GameObject liquidPrefab;
        
        [LocalizationTooltip("权重，决定被选中的概率。",
             "Weight, determines the probability of being selected.",
             "重み、選択される確率を決定します。")]
        public int weight = 1;
        
        [LocalizationTooltip("生命周期，单位秒。大于0则在时间到后自动销毁。",
             "Lifetime in seconds. If greater than 0, automatically destroys after time expires.",
             "ライフタイム（秒単位）。0より大きい場合、時間が経過すると自動的に破棄されます。")]
        public float lifetime = 0f;

        public int GetWeight()
        {
            return weight;
        }
        
#if UNITY_EDITOR
        public void OnValidate()
        {
            if (liquidPrefab != null && liquidPrefab.GetComponent<Liquid2DParticle>() == null)
            {
                Debug.LogWarning($"预制体 {liquidPrefab.name} 未挂载 Liquid2DParticleRenderer，已清空。");
                liquidPrefab = null;
            }
        }
#endif
    }
}
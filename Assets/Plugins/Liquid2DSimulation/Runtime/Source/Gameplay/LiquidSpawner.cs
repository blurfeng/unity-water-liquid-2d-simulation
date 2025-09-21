using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Fs.Liquid2D.Utility;

namespace Fs.Liquid2D
{
    public class LiquidSpawner : MonoBehaviour
    {
        [Header("Common")]
        [Tooltip("是否在启动时自动开始喷射")]
        public bool startOnAwake = true;
        
        [Tooltip("启动时的延迟时间")]
        public float startDelay = 2f;
        
        [Header("Liquid Particle Settings")]
        [Tooltip("流体粒子预制体（需挂载Liquid2DParticleRenderer）")]
        public List<LiquidParticle> liquidParticles = new List<LiquidParticle>();

        [Range(0.01f, 100f), Tooltip("喷嘴宽度。")]
        public float nozzleWidth = 1f;
        
        [Tooltip("流量。每秒喷射的粒子数量。")]
        public float flowRate = 60f;
        
        [Tooltip("尺寸随机范围（最小值，最大值）")]
        public Vector2 sizeRandomRange = new Vector2(0.9f, 1.2f);

        [Tooltip("喷射力大小")]
        public float ejectForce = 40f;
        
        [Tooltip("喷射力随机范围（最小值，最大值）")]
        public Vector2 ejectForceRandomRange = new Vector2(0.9f, 1.2f);
        
        [Header("Swing")]
        [Tooltip("摆动角度范围（最大偏移，单位度）")]
        public float swingAngleRange = 0f;

        [Tooltip("摆动速度（周期/秒）")]
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
        
        // 是否正在喷射中。
        private bool _isSpawning = false;
        
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
            
            // 延迟启动。
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
            
            // 摆动。
            if (swingAngleRange > 0f && swingSpeed > 0f)
            {
                _swingTime += Time.deltaTime;
            }
            
            // 喷射粒子。
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
        /// </summary>
        public void StartSpawn()
        {
            if (_isSpawning) return;
            _isSpawning = true;
        }
        
        /// <summary>
        /// 停止喷射流体粒子。
        /// </summary>
        public void StopSpawn()
        {
            if (!_isSpawning) return;
            _isSpawning = false;
        }
        
        /// <summary>
        /// 获取当前喷射角度，包含摆动偏移。
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
        /// </summary>
        /// <returns></returns>
        private Vector2 GetCurrentEjectDirection()
        {
            Vector2 forward = -TransformGet.up; // 默认朝下
            Vector2 dir = Quaternion.Euler(0, 0, GetCurrentEjectAngle()) * forward;
            return dir;
        }

        /// <summary>
        /// 生成一个流体粒子。
        /// </summary>
        private void SpawnOne()
        {
            // 随机选择一个流体粒子预制体。
            if (liquidParticles.Count == 0) return;
            LiquidParticle liquidParticle = liquidParticles.RandomWeight();
            
            // 获取当前喷射方向。
            Vector2 dir = GetCurrentEjectDirection();
            Transform ts = TransformGet;
            
            // 随机获取生成位置。
            Vector2 normal = new Vector2(-dir.y, dir.x); // 计算法线
            float offset = UnityEngine.Random.Range(-nozzleWidth * 0.5f, nozzleWidth * 0.5f);
            Vector3 spawnPos = ts.position + (Vector3)(normal * offset);
            
            // 生成预制体。
            var go = Instantiate(liquidParticle.liquidPrefab, spawnPos, Quaternion.identity);
            go.transform.SetParent(ts);
            
            // 检查是否挂载 Liquid2DParticleRenderer。
            var lRenderer = go.GetComponent<Liquid2DParticleRenderer>();
            if (lRenderer == null)
            {
                Debug.LogWarning("预制体未挂载Liquid2DParticleRenderer！");
                Destroy(go);
                return;
            }
            
            // 随机设置尺寸。
            if (sizeRandomRange.y > sizeRandomRange.x && sizeRandomRange.x > 0f)
            {
                float size = UnityEngine.Random.Range(sizeRandomRange.x, sizeRandomRange.y);
                go.transform.localScale *= size;
            }
            
            var rb = go.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                // 随机设置喷射力。
                float force = ejectForce;
                if (ejectForceRandomRange.y > ejectForceRandomRange.x && ejectForceRandomRange.x >= 0f)
                {
                    force *= UnityEngine.Random.Range(ejectForceRandomRange.x, ejectForceRandomRange.y);
                }
                rb.AddForce(dir * force, ForceMode2D.Impulse);
            }
            
            lRenderer.SetLifetime(liquidParticle.lifetime);
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
        [Tooltip("流体粒子预制体（需挂载Liquid2DParticleRenderer）。")]
        public GameObject liquidPrefab;
        
        [Tooltip("权重，决定被选中的概率。")]
        public int weight = 1;
        
        [Tooltip("生命周期，单位秒。大于0则在时间到后自动销毁。")]
        public float lifetime = 0f;

        public int GetWeight()
        {
            return weight;
        }
        
#if UNITY_EDITOR
        public void OnValidate()
        {
            if (liquidPrefab != null && liquidPrefab.GetComponent<Liquid2DParticleRenderer>() == null)
            {
                Debug.LogWarning($"预制体 {liquidPrefab.name} 未挂载 Liquid2DParticleRenderer，已清空。");
                liquidPrefab = null;
            }
        }
#endif
    }
}
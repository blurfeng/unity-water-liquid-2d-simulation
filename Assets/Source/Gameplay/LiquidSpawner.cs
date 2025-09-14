using System;
using UnityEngine;
using System.Collections;
using System.Collections.Generic;
using Fs.Utility;
using Unity.VisualScripting;
using UnityEngine.Serialization;

namespace Fs.Liquid2D
{
    public class LiquidSpawner : MonoBehaviour
    {
        [Header("Common")]
        [Tooltip("启动时的延迟时间")]
        public float startDelay = 2f;
        
        [Header("Liquid Particle Settings")]
        [Tooltip("流体粒子预制体（需挂载Liquid2DParticleRenderer）")]
        public List<LiquidParticle> liquidParticles = new List<LiquidParticle>();

        [Tooltip("每秒生成水量（粒子数量）")]
        public int spawnRate = 10;

        [Range(0, 360), Tooltip("喷射方向（角度，0为下，逆时针）")]
        public float ejectAngle = 0f;

        [Tooltip("喷射力大小")]
        public float ejectForce = 5f;
        
        [Header("摆动设置")]
        [Tooltip("摆动角度范围（最大偏移，单位度）")]
        public float swingAngleRange = 90f;

        [Tooltip("摆动速度（周期/秒）")]
        public float swingSpeed = 0.2f;

        private Coroutine _spawnRoutine;
        private float _swingTime;

        public void OnEnable()
        {
            if (_spawnRoutine == null)
                _spawnRoutine = StartCoroutine(DelayedStart());
        }

        public void OnDisable()
        {
            if (_spawnRoutine != null)
            {
                StopCoroutine(_spawnRoutine);
                _spawnRoutine = null;
            }
        }
        
        private void Update()
        {
            if (swingAngleRange > 0f && swingSpeed > 0f)
            {
                _swingTime += Time.deltaTime;
            }
        }
        
        private float GetCurrentEjectAngle()
        {
            if (swingAngleRange > 0f && swingSpeed > 0f)
            {
                float offset = Mathf.Sin(_swingTime * swingSpeed * Mathf.PI * 2f) * swingAngleRange;
                return ejectAngle + offset;
            }
            return ejectAngle;
        }
        
        private IEnumerator DelayedStart()
        {
            if (startDelay > 0f)
                yield return new WaitForSeconds(startDelay);

            _spawnRoutine = StartCoroutine(SpawnCoroutine());
        }

        private IEnumerator SpawnCoroutine()
        {
            while (true)
            {
                float interval = 1f / Mathf.Max(1, spawnRate);
                for (int i = 0; i < spawnRate; i++)
                {
                    SpawnOne();
                    yield return new WaitForSeconds(interval);
                }
            }
        }

        private void SpawnOne()
        {
            if (liquidParticles.Count == 0) return;

            LiquidParticle liquidParticle = liquidParticles.RandomWeight();
            
            float angle = GetCurrentEjectAngle();
            Vector2 dir = Quaternion.Euler(0, 0, angle) * Vector2.down;
            var go = Instantiate(liquidParticle.liquidPrefab, transform.position, Quaternion.identity);
            
            var lRenderer = go.GetComponent<Liquid2DParticleRenderer>();
            if (lRenderer == null)
            {
                Debug.LogWarning("预制体未挂载Liquid2DParticleRenderer！");
                Destroy(go);
                return;
            }

            var rb = go.GetComponent<Rigidbody2D>();
            if (rb != null)
            {
                rb.linearVelocity = dir * ejectForce;
            }
            
            lRenderer.SetLifetime(liquidParticle.lifetime);
        }

        #if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            Gizmos.color = Color.cyan;
            Vector3 start = transform.position;
            Vector2 dir = Quaternion.Euler(0, 0, GetCurrentEjectAngle()) * Vector2.down;
            Vector3 end = start + (Vector3)dir * 3.5f;

            Gizmos.DrawLine(start, end);

            Vector3 left = end + Quaternion.Euler(0, 0, 150) * (Vector3)(dir * 0.6f);
            Vector3 right = end + Quaternion.Euler(0, 0, -150) * (Vector3)(dir * 0.6f);
            Gizmos.DrawLine(end, left);
            Gizmos.DrawLine(end, right);
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
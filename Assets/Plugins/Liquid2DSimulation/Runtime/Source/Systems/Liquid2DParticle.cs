using System;
using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2d流体粒子渲染器。
    /// 挂载此组件到流体粒子对象上以管理流体粒子的渲染设置。
    /// [ExecuteAlways] 宏标确保在编辑模式下也能注册到 Liquid2DFeature 来保证可见性。
    /// 2D fluid particle renderer.
    /// Attach this component to fluid particle objects to manage fluid particle rendering settings.
    /// The [ExecuteAlways] attribute ensures registration to Liquid2DFeature even in edit mode for visibility.
    /// 2D流体粒子レンダラー。
    /// このコンポーネントを流体粒子オブジェクトにアタッチして、流体粒子のレンダリング設定を管理します。
    /// [ExecuteAlways]属性により、編集モードでもLiquid2DFeatureに登録されて可視性が保証されます。
    /// </summary>
    [ExecuteAlways]
    public class Liquid2DParticle : MonoBehaviour
    {
        [SerializeField, LocalizationTooltip(
             "粒子生命时间（秒），到时间后自动销毁。",
             "Particle lifetime (seconds), automatically destroy after time.",
             "粒子の寿命（秒）を設定し、時間後に自動破棄。" )]
        private float lifetime = 0f;
        
        [SerializeField, LocalizationTooltip(
             "流体粒子渲染器设置。",
             "Fluid particle renderer settings.",
             "流体パーティクルレンダラー設定。")]
        private Liquid2DParticleRenderSettings renderSettings = new Liquid2DParticleRenderSettings();
        
        /// <summary>
        /// 流体粒子渲染器。
        /// Fluid particle renderer.
        /// 流体粒子レンダラー。
        /// </summary>
        public Liquid2DParticleRenderSettings RenderSettings => renderSettings;
        
        private HashSet<Liquid2DParticle> _contactParticles = new();

        public Transform TransformGet
        {
            get
            {
                if (_transform == null) _transform = transform;
                return _transform;
            }
        }
        private Transform _transform;
        
        public Collider2D Collider2DGet
        {
            get
            {
                if (_collider2D == null) _collider2D = GetComponent<Collider2D>();
                return _collider2D;
            }
        }
        private Collider2D _collider2D;
        
        private void Start()
        {
            if (lifetime > 0f)
            {
                SetLifetime(lifetime);
            }
        }

        private void OnEnable()
        {
            if (!IsValid())
            {
                Debug.LogWarning("Liquid2dRenderer is not valid.");
                return;
            }
        
            // 注册到 Liquid2dFeature。 // Register to Liquid2dFeature. // Liquid2dFeatureに登録。
            Liquid2DFeature.RegisterLiquidParticle(this);
            
#if UNITY_EDITOR
            OnEnableEditor();
#endif
        }
    
        private void OnDisable()
        {
            // 从 Liquid2dFeature 注销。 // Unregister from Liquid2dFeature. // Liquid2dFeatureから登録解除。
            Liquid2DFeature.UnregisterLiquidParticle(this);
            
#if UNITY_EDITOR
            OnDisableEditor();
#endif
        }
        
        private void Update()
        {
            if (CanMixColor)
            {
                UpdateMix();
            }
        }

        private void OnCollisionEnter2D(Collision2D other)
        {
            var otherParticle = other.gameObject.GetComponent<Liquid2DParticle>();
            if (otherParticle != null && otherParticle != this)
                _contactParticles.Add(otherParticle);
            
            OnCollisionEnter2DMix(other);
        }

        private void OnCollisionExit2D(Collision2D other)
        {
            var otherParticle = other.gameObject.GetComponent<Liquid2DParticle>();
            if (otherParticle != null && otherParticle != this)
                _contactParticles.Remove(otherParticle);
        }

        /// <summary>
        /// 检查渲染器设置是否有效。
        /// Check if renderer settings are valid.
        /// レンダラー設定が有効かチェック。
        /// </summary>
        /// <returns></returns>
        private bool IsValid()
        {
            if (!renderSettings.IsValid())
                return false;

            return true;
        }

        /// <summary>
        /// 设置粒子生命时间（秒），到时间后自动销毁。
        /// 每次调用会重置计时。
        /// Set particle lifetime (seconds), automatically destroy after time.
        /// Each call resets the timer.
        /// 粒子の寿命（秒）を設定し、時間後に自動破棄。
        /// 各呼び出しでタイマーをリセット。
        /// </summary>
        /// <param name="setLifetime"></param>
        public void SetLifetime(float setLifetime)
        {
            if (!Application.isPlaying) return;
            if (setLifetime <= 0f) return;
            
            Destroy(gameObject, setLifetime);
        }

        #region Mix Color 混合颜色 // 色を混ぜる

        [SerializeField, LocalizationTooltip(
             "流体粒子混合设置。",
             "Fluid particle mix settings.",
             "流体パーティクルミックス設定。")]
        private Liquid2DParticleMixSettings mixSettings = new Liquid2DParticleMixSettings();
        
        private readonly Dictionary<Liquid2DParticle, float> _lastStaticMixTimes = new();
        
        public bool CanMixColor => mixSettings.mixColors && mixSettings.mixColorsSpeed > 0f;
        
        private float _lastContactCheckTime = 0f;

        private void UpdateMix()
        {
            if (!CanMixColor) return;
            MixWithContactParticles();
        }

        private void OnCollisionEnter2DMix(Collision2D other)
        {
            if (!CanMixColor) return;
            
            // 当流体粒子碰撞时，它们的颜色混合在一起。
            // When fluid particles collide, their colors mix together.
            // 流体パーティクルが衝突すると、その色が混ざり合います。
            var otherParticle = other.gameObject.GetComponent<Liquid2DParticle>();
            MixWithParticle(otherParticle);
        }
        
        /// <summary>
        /// 在静止状态下接触的粒子混合颜色。
        /// Mix colors of particles in contact while in static state.
        /// 静止状態で接触しているパーティクルの色を混ぜる。
        /// </summary>
        private void MixWithContactParticles()
        {
            if (!mixSettings.mixWithContactParticles) return;
            
            float now = Time.time;
            if (now - _lastContactCheckTime < mixSettings.mixWithContactParticlesCheckInternal)
                return;
            _lastContactCheckTime = now;

            foreach (var otherP in _contactParticles)
            {
                if (!_lastStaticMixTimes.TryGetValue(otherP, out float lastMix) ||
                    now - lastMix >= mixSettings.mixWithContactParticlesInternal)
                {
                    MixWithParticle(otherP);
                    _lastStaticMixTimes[otherP] = now;
                }
            }
        }
        
        /// <summary>
        /// 与接触的粒子混合颜色。
        /// Mix colors with contact particles.
        /// 接触しているパーティクルと色を混ぜる。
        /// </summary>
        /// <param name="otherParticle"></param>
        private void MixWithParticle(Liquid2DParticle otherParticle)
        {
            // 确保 otherParticle 存在且不是自己，并且启用了颜色混合。
            // Ensure otherParticle exists and is not itself, and color mixing is enabled.
            // otherParticleが存在し、自分自身ではなく、色の混合が有効になっていることを確認します。
            if (!otherParticle || otherParticle == this || !otherParticle.CanMixColor) return;
            
            Color thisColor = RenderSettings.color;
            Color otherColor = otherParticle.RenderSettings.color;
                    
            // 如果颜色相同则不混合。 // If colors are the same, do not mix. // 色が同じ場合は混ぜません。
            if (thisColor == otherColor)
                return;
            
            // 只有相同 Liquid2DLayer 流体层的粒子才会混合颜色。
            // Only particles in the same Liquid2DLayer will mix colors.
            // 同じLiquid2DLayerに属するパーティクルのみが色を混ぜます。
            if (otherParticle.RenderSettings.liquid2DLayerMask != RenderSettings.liquid2DLayerMask)
                return;
            
            // 计算混合速度。 // Calculate mix speed. // ミックス速度を計算します。
            float baseMixSpeed = (mixSettings.mixColorsSpeed + otherParticle.mixSettings.mixColorsSpeed) / 2f;
            float mixSpeed = baseMixSpeed;
            
            // 如果启用了根据粒子运动混合颜色，则根据粒子速度调整混合速度。
            // If mixing colors based on particle movement is enabled, adjust mix speed based on particle velocity.
            // 粒子の動きに基づいて色を混ぜることが有効になっている場合、粒子の速度に基づいてミックス速度を調整します。
            if (mixSpeed < 1f && mixSettings.mixColorsWithMovement && otherParticle.mixSettings.mixColorsWithMovement)
            {
                // 获取两个粒子的速度平方。 // Get the squared speeds of both particles. // 両方のパーティクルの速度の二乗を取得します。
                float speedASqr = GetComponent<Rigidbody2D>()?.linearVelocity.sqrMagnitude ?? 0f;
                float speedBSqr = otherParticle.GetComponent<Rigidbody2D>()?.linearVelocity.sqrMagnitude ?? 0f;
                float avgSpeedSqr = (speedASqr + speedBSqr) / 2f;

                // 归一化速度（假设最大速度为 maxSpeed，可根据实际情况调整）。
                // Normalize speed (assuming max speed is maxSpeed, can be adjusted based on actual situation).
                // 速度を正規化します（最大速度がmaxSpeedであると仮定し、実際の状況に応じて調整できます）。
                float maxSpeed = (mixSettings.mixColorsWithMovementMaxSpeed + otherParticle.mixSettings.mixColorsWithMovementMaxSpeed) / 2f;
                float maxSpeedSqr = maxSpeed * maxSpeed;
                float velocityFactor = Mathf.Clamp01(avgSpeedSqr / maxSpeedSqr);

                // 让混合速度随速度提升，最大不超过 1。
                // Let the mix speed increase with velocity, not exceeding 1.
                // ミックス速度を速度に応じて上げ、1を超えないようにします。
                mixSpeed = Mathf.Clamp01(baseMixSpeed + velocityFactor * (1f - baseMixSpeed));
            }

            // 计算平均色。 // Calculate average color. // 平均色を計算します。
            Color avgColor = new Color(
                (thisColor.r + otherColor.r) / 2f,
                (thisColor.g + otherColor.g) / 2f,
                (thisColor.b + otherColor.b) / 2f,
                (thisColor.a + otherColor.a) / 2f
            );

            // 判断颜色差异。 // Determine color difference. // 色の違いを判断します。
            float colorDiff = Vector4.Distance(
                new Vector4(thisColor.r, thisColor.g, thisColor.b, thisColor.a),
                new Vector4(otherColor.r, otherColor.g, otherColor.b, otherColor.a)
            );

            if (colorDiff < 0.01f) {
                RenderSettings.color = avgColor;
                otherParticle.RenderSettings.color = avgColor;
            } else {
                RenderSettings.color = Color.Lerp(thisColor, avgColor, mixSpeed);
                otherParticle.RenderSettings.color = Color.Lerp(otherColor, avgColor, mixSpeed);
            }
        }
        
        #endregion
        
#if UNITY_EDITOR
        private static readonly bool _debugEnable = false;

        private static int _liquidParticleCount = 0;
        private void OnEnableEditor()
        {
            if (_debugEnable)
            {
                _liquidParticleCount++;
                Debug.Log($"liquidParticleCount: {_liquidParticleCount}", this);
            }
        }

        private void OnDisableEditor()
        {
            if (_debugEnable)
            {
                _liquidParticleCount --;
                Debug.Log($"liquidParticleCount: {_liquidParticleCount}", this);
            }
        }
        
        void OnDrawGizmos()
        {
            #region 绘制可在 Scene 中选中的形状 // Draw shapes that can be selected in Scene // Sceneで選択可能な形状を描画

            var cld = Collider2DGet;
            if (cld == null) return;

            Gizmos.color = Color.clear;

            if (cld is CircleCollider2D circle)
            {
                float radius = circle.radius * circle.transform.lossyScale.x;
                Gizmos.DrawSphere(circle.transform.position + (Vector3)circle.offset, radius);
                // UnityEditor.Handles.Label(circle.transform.position, $"Circle r={circle.radius:F2}");
            }
            else if (cld is BoxCollider2D box)
            {
                var size = Vector3.Scale(box.size, box.transform.lossyScale);
                Gizmos.DrawCube(box.transform.position + (Vector3)box.offset, size);
            }
            else if (cld is PolygonCollider2D poly)
            {
                var pos = poly.transform.position;
                for (int i = 0; i < poly.pathCount; i++)
                {
                    var path = poly.GetPath(i);
                    for (int j = 0; j < path.Length; j++)
                    {
                        var a = pos + (Vector3)path[j];
                        var b = pos + (Vector3)path[(j + 1) % path.Length];
                        Gizmos.DrawLine(a, b);
                    }
                }
            }
            else if (cld is CapsuleCollider2D capsule)
            {
                var size = Vector3.Scale(capsule.size, capsule.transform.lossyScale);
                Gizmos.DrawCube(capsule.transform.position + (Vector3)capsule.offset, size);
            }

            #endregion
        }
#endif
    }
}

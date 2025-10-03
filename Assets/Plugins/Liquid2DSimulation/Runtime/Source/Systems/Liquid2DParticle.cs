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

        public Transform TransformGet => _transform;
        private Transform _transform;

        public CircleCollider2D CircleCollider2DGet => _circleCollider2D;
        private CircleCollider2D _circleCollider2D;
        
        public Rigidbody2D Rigidbody2DGet => _rigidbody2d;
        private Rigidbody2D _rigidbody2d;
        
        private static readonly Dictionary<Collider2D, Liquid2DParticle> _DicParticlesWithCollider = new Dictionary<Collider2D, Liquid2DParticle>();

        private void Awake()
        {
            _transform = transform;
            _circleCollider2D = GetComponent<CircleCollider2D>();
            _rigidbody2d = GetComponent<Rigidbody2D>();

            AwakeMixColor();
        }

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
            
            _DicParticlesWithCollider.Add(CircleCollider2DGet, this);
        
            // 注册到 Liquid2dFeature。 // Register to Liquid2dFeature. // Liquid2dFeatureに登録。
            Liquid2DFeature.RegisterLiquidParticle(this);
            
#if UNITY_EDITOR
            OnEnableEditor();
#endif
        }
    
        private void OnDisable()
        {
            _DicParticlesWithCollider.Remove(CircleCollider2DGet);
            
            // 从 Liquid2dFeature 注销。 // Unregister from Liquid2dFeature. // Liquid2dFeatureから登録解除。
            Liquid2DFeature.UnregisterLiquidParticle(this);
            
#if UNITY_EDITOR
            OnDisableEditor();
#endif
        }
        
        private void Update()
        {
            UpdateMixColor();
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
        
        public bool CanMixColor => mixSettings.mixColors && mixSettings.mixColorsSpeed > 0f;
        
        private float _lastContactCheckTime = 0f;
        
        private Collider2D[] _mixColorContacts;

        private ContactFilter2D _mixColorContactFilter;
        
        private void AwakeMixColor()
        {
            _mixColorContacts = new Collider2D[4];
            
            _mixColorContactFilter = new ContactFilter2D()
            {
                useTriggers = false,
                useLayerMask = true,
                layerMask = 1 << gameObject.layer,
                useDepth = false,
                useOutsideDepth = false,
            };
        }

        private void UpdateMixColor()
        {
            if (!CanMixColor) return;
            
            MixWithContactParticles();
        }
        
        /// <summary>
        /// 检查混合颜色间隔。
        /// Check mix color interval.
        /// 色の混合間隔をチェックします。
        /// </summary>
        /// <param name="force">是否强制通过检查，确认时间也将更新。 // Whether to force pass the check, the confirmation time will also be updated. // チェックを強制的に通過するかどうか、確認時間も更新されます。</param>
        /// <returns></returns>
        private bool CheckMixColorInterval(bool force = false)
        {
            float now = Time.time;
            if (now - _lastContactCheckTime < mixSettings.mixColorsWithContactParticlesInternal && !force)
                return false;
            _lastContactCheckTime = now;
            return true;
        }
        
        /// <summary>
        /// 和接触的粒子混合颜色。
        /// Mix colors with contact particles.
        /// 接触しているパーティクルと色を混ぜる。
        /// </summary>
        private void MixWithContactParticles()
        {
            // 如果粒子静止且未设置为在静止时混合颜色，则跳过混合。
            // If the particle is stationary and not set to mix colors when stationary, skip mixing.
            // 粒子が静止していて、静止時に色を混ぜるように設定されていない場合、ミックスをスキップします。
            if (Rigidbody2DGet.linearVelocity.sqrMagnitude < 0.00000001f && !mixSettings.mixColorsWhenStationary) return;
            
            // 检查混合颜色间隔。 // Check mix color interval. // 色の混合間隔をチェックします。
            if (!CheckMixColorInterval()) return;
            
            // 获取接触的粒子。 // Get contacting particles. // 接触しているパーティクルを取得します。
            Vector2 center = (Vector2)TransformGet.position + CircleCollider2DGet.offset;
            float radius = CircleCollider2DGet.radius * TransformGet.lossyScale.x * mixSettings.mixColorsRadiusRate;
            int contactNum = Physics2D.OverlapCircle(center, radius, _mixColorContactFilter, _mixColorContacts);
            if (contactNum == 0) return;
            
            // 和接触的粒子混合颜色。 // Mix colors with contacting particles. // 接触しているパーティクルと色を混ぜます。
            for (int i = 0; i < contactNum; i++)
            {
                var contact = _mixColorContacts[i];
                if (!contact || !contact.gameObject.activeSelf) continue;
                if (contact.gameObject == gameObject) continue;
                if (!_DicParticlesWithCollider.TryGetValue(contact, out Liquid2DParticle otherP)) continue;
                if (!otherP) continue;
                
                // 偶现的获取到距离过远的粒子，跳过。
                // Occasionally get particles that are too far away, skip.
                // 時々、遠すぎるパーティクルが取得されることがあるため、スキップします。
                if (Vector2.Distance(transform.position, contact.transform.position) > radius * 2f)
                    continue;

                // Debug.Log($"Self:{name} OtherP: {otherP.name} Radius:{radius} Distance: {Vector2.Distance(transform.position, contact.transform.position)}");
                // Debug.DrawLine(transform.position, contact.transform.position, Color.red, 0f, false);
                // if (Vector2.Distance(transform.position, contact.transform.position) > radius * 2f)
                // {
                //     var otherCollider = contact as CircleCollider2D;
                //     float otherRadius = otherCollider ? otherCollider.radius * otherCollider.transform.lossyScale.x : -1f;
                //     Debug.Log(
                //         $"Self:{name} OtherP: {otherP?.name} " +
                //         $"Radius:{radius} Distance:{Vector2.Distance(transform.position, contact.transform.position)} " +
                //         $"ContactNull:{contact == null} ContactGO:{contact?.gameObject} " +
                //         $"OtherRadius:{otherRadius} OtherPos:{contact.transform.position}"
                //     );
                // }

                MixWithParticle(otherP);
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
                float speedASqr = Rigidbody2DGet.linearVelocity.sqrMagnitude;
                float speedBSqr = otherParticle.Rigidbody2DGet.linearVelocity.sqrMagnitude;
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

            // 如果颜色差异很小，则直接设置为平均色，否则按混合速度插值。
            // If color difference is very small, set to average color directly, otherwise interpolate according to mix speed.
            // 色の違いが非常に小さい場合は平均色に直接設定し、そうでない場合はミックス速度に応じて補間します。
            if (colorDiff < 0.01f) {
                SetMixColor(avgColor);
                otherParticle.SetMixColor(avgColor);
            } 
            // 如果混合速度接近 1，则直接设置为平均色，否则按混合速度插值。
            // If mix speed is close to 1, set to average color directly, otherwise interpolate according to mix speed.
            // ミックス速度が1に近い場合は平均色に直接設定し、そうでない場合はミックス速度に応じて補間します。
            else
            {
                SetMixColor(mixSpeed >= 1 ? avgColor : Color.Lerp(thisColor, avgColor, mixSpeed));
                otherParticle.SetMixColor(mixSpeed >= 1 ? avgColor : Color.Lerp(otherColor, avgColor, mixSpeed));
            }
        }

        /// <summary>
        /// 设置混合颜色。
        /// Set mix color.
        /// 色を混ぜる。
        /// </summary>
        /// <param name="color"></param>
        /// <param name="resetInterval">是否重置混合颜色间隔计时。 // Whether to reset the mix color interval timer. // 色の混合間隔タイマーをリセットするかどうか。</param>
        private void SetMixColor(Color color, bool resetInterval = true)
        {
            RenderSettings.color = color;

            // 重置混合颜色间隔计时。如果为被检测到的粒子设置颜色时重置间隔，那么次粒子之后将不会进行主动的混色检测，可以提高性能。
            // Reset mix color interval timer. If the interval is reset when setting the color for detected particles, then the particle will not actively check for color mixing afterwards, which can improve performance.
            // 色の混合間隔タイマーをリセットします。検出されたパーティクルの色を設定するときに間隔がリセットされる場合、その後、パーティクルは色の混合を積極的にチェックしなくなり、パフォーマンスが向上します。
            if (resetInterval)
            {
                CheckMixColorInterval(true);
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

            if (CircleCollider2DGet)
            {
                Gizmos.color = Color.clear;
                float radius = CircleCollider2DGet.radius * CircleCollider2DGet.transform.lossyScale.x;
                Gizmos.DrawSphere(CircleCollider2DGet.transform.position + (Vector3)CircleCollider2DGet.offset, radius);
                // UnityEditor.Handles.Label(circle.transform.position, $"Circle r={circle.radius:F2}");
            }
            
            #endregion
        }
#endif
    }
}

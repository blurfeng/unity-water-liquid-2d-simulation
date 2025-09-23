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
        [SerializeField, Tooltip("流体粒子渲染器设置。")]
        private Liquid2dParticleSettings settings = new Liquid2dParticleSettings();
    
        [SerializeField, Tooltip("粒子生命时间（秒），到时间后自动销毁。")]
        private float lifetime = 0f;
        
        /// <summary>
        /// 流体粒子渲染器。
        /// Fluid particle renderer.
        /// 流体粒子レンダラー。
        /// </summary>
        public Liquid2dParticleSettings Settings => settings;

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
        }
    
        private void OnDisable()
        {
            // 从 Liquid2dFeature 注销。 // Unregister from Liquid2dFeature. // Liquid2dFeatureから登録解除。
            Liquid2DFeature.UnregisterLiquidParticle(this);
        }
    
        /// <summary>
        /// 检查渲染器设置是否有效。
        /// Check if renderer settings are valid.
        /// レンダラー設定が有効かチェック。
        /// </summary>
        /// <returns></returns>
        private bool IsValid()
        {
            if (!settings.IsValid())
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
        
#if UNITY_EDITOR
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

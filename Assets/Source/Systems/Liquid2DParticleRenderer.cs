using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2d流体粒子渲染器。
    /// 挂载此组件到流体粒子对象上以管理流体粒子的渲染设置。
    /// [ExecuteAlways] 宏标确保在编辑模式下也能注册到 Liquid2DFeature 来保证可见性。
    /// </summary>
    [ExecuteAlways] 
    public class Liquid2DParticleRenderer : MonoBehaviour
    {
        [SerializeField, Tooltip("流体粒子渲染器设置。")]
        private Liquid2dParticleRendererSettings settings = new Liquid2dParticleRendererSettings();
    
        [SerializeField, Tooltip("粒子生命时间（秒），到时间后自动销毁。")]
        private float lifetime = 0f;
        
        /// <summary>
        /// 流体粒子渲染器。
        /// </summary>
        public Liquid2dParticleRendererSettings Settings => settings;

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
        
            // 注册到 Liquid2dFeature。
            Liquid2DFeature.RegisterLiquidParticle(this);
        }
    
        private void OnDisable()
        {
            // 从 Liquid2dFeature 注销。
            Liquid2DFeature.UnregisterLiquidParticle(this);
        }
    
        /// <summary>
        /// 检查渲染器设置是否有效。
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
            #region 绘制可在 Scene 中选中的形状

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

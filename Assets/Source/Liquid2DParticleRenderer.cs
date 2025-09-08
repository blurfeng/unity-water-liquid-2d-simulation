using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2d流体粒子渲染器。
    /// 挂载此组件到流体粒子对象上以管理流体粒子的渲染设置。
    /// </summary>
    public class Liquid2DParticleRenderer : MonoBehaviour
    {
        [SerializeField, Tooltip("流体粒子渲染器设置。")]
        private Liquid2dParticleRendererSettings settings = new Liquid2dParticleRendererSettings();
    
        /// <summary>
        /// 流体粒子渲染器。
        /// </summary>
        public Liquid2dParticleRendererSettings Settings => settings;

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
    }
}

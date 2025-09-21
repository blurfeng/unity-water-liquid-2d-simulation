using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D 流体效果渲染特性。
    /// 我们使用 2D 球体模拟每个流体粒子，然后使用自定义 Shader 对球体进行渲染处理，模拟出流体效果。
    /// </summary>
    public class Liquid2DFeature : ScriptableRendererFeature
    {
        [SerializeField, Tooltip("用于模糊流体粒子的 Shader。")]
        private Shader shaderBlur;
        
        [SerializeField, Tooltip("用于渲染流体效果的 Shader。")]
        private Shader shaderEffect;
        
        [SerializeField, Tooltip("流体效果设置。")] 
        private Liquid2DRenderFeatureSettings renderFeatureSettings;
        
        private Liquid2dPass _liquid2dPass;
        private Material _materialBlur;
        private Material _materialEffect;
        
        private void Awake()
        {
            CheckSettings();
        }
        
        private void OnValidate()
        {
            CheckSettings();
        }
        
        private void CheckSettings()
        {
            if (shaderBlur == null)
            {
                shaderBlur = Shader.Find("Custom/URP/2D/Liquid2DBlur");
            }
            if (shaderEffect == null)
            {
                shaderEffect = Shader.Find("Custom/URP/2D/Liquid2DEffect");
            }
            if (renderFeatureSettings == null)
            {
                renderFeatureSettings = new Liquid2DRenderFeatureSettings();
            }
        }

        public override void Create()
        {
            // 检查 Shader 是否可用。
            if (!shaderBlur || !shaderBlur.isSupported ||
                !shaderEffect || !shaderEffect.isSupported)
            {
                Debug.LogWarning(
                    $"Liquid2dFeature: Missing or unsupported shader for {GetType().Name}. Liquid2dPass feature will not execute.");
                return;
            }
            
            // 使用 shader 创建材质，并创建 Pass。
            _materialBlur = new Material(shaderBlur);
            _materialEffect = new Material(shaderEffect);
            _materialBlur.renderQueue = 3000;
            _materialEffect.renderQueue = 3000;
            _liquid2dPass = new Liquid2dPass(_materialBlur, _materialEffect, renderFeatureSettings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // // 不在预览相机中渲染。
            // if (renderingData.cameraData.cameraType != CameraType.Game)
            //     return;
            
            // 确认 Pass 和材质是否可用。
            if (_liquid2dPass == null || _materialBlur == null || _materialEffect == null)
            {
                // Debug.LogWarning($"Liquid2dFeature: Missing Liquid2d Pass. {GetType().Name} render pass will not execute.");
                return;
            }
            
            renderer.EnqueuePass(_liquid2dPass);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _liquid2dPass?.Dispose();
            }
        }

        #region Liquid Particle 流体粒子管理
        
        // 所有的流体粒子注册到这里，按设置分组。用于之后的渲染。

        private static readonly Dictionary<Liquid2dParticleRendererSettings, List<Liquid2DParticleRenderer>>
            _particlesDic = new Dictionary<Liquid2dParticleRendererSettings, List<Liquid2DParticleRenderer>>();
        
        /// <summary>
        /// 所有注册的流体粒子，按设置分组。用于批量渲染。
        /// </summary>
        internal static Dictionary<Liquid2dParticleRendererSettings, List<Liquid2DParticleRenderer>> ParticlesDic => _particlesDic;

        /// <summary>
        /// 注册流体粒子。
        /// 为了 Game Instancing 批量渲染，我们需要将相同设置的粒子进行分组。
        /// </summary>
        /// <param name="particle"></param>
        public static void RegisterLiquidParticle(Liquid2DParticleRenderer particle)
        {
            if (particle == null || particle.Settings == null) return;

            if (!_particlesDic.TryGetValue(particle.Settings, out var list))
            {
                list = new List<Liquid2DParticleRenderer>();
                _particlesDic[particle.Settings] = list;
            }

            if (!list.Contains(particle))
            {
                list.Add(particle);
            }
        }

        /// <summary>
        /// 注销流体粒子。
        /// </summary>
        /// <param name="particle"></param>
        public static void UnregisterLiquidParticle(Liquid2DParticleRenderer particle)
        {
            if (particle == null || particle.Settings == null) return;

            if (_particlesDic.TryGetValue(particle.Settings, out var list))
            {
                if (list.Contains(particle))
                {
                    list.Remove(particle);
                }

                if (list.Count == 0)
                {
                    _particlesDic.Remove(particle.Settings);
                }
            }
        }

        #endregion
    }
}
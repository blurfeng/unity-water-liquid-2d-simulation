using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using UnityEngine;
using UnityEngine.Rendering.Universal;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D 流体效果渲染特性。
    /// 我们使用 2D 球体模拟每个流体粒子，然后使用自定义 Shader 对球体进行渲染处理，模拟出流体效果。
    /// 2D fluid effect rendering feature.
    /// We use 2D spheres to simulate each fluid particle, then use custom Shader to render the spheres and simulate fluid effects.
    /// 2D流体エフェクトレンダリング機能。
    /// 2D球体を使用して各流体粒子をシミュレーションし、カスタムシェーダーで球体をレンダリング処理して流体効果をシミュレーションします。
    /// </summary>
    public class Liquid2DFeature : ScriptableRendererFeature
    {
        [SerializeField, LocalizationTooltip(
             "用于模糊流体粒子的 Shader。",
             "Shader used for blurring fluid particles.",
             "流体パーティクルをブラーするために使用されるシェーダー。")]
        private Shader shaderBlur;
        
        [SerializeField, LocalizationTooltip(
             "用于渲染流体效果的 Shader。",
             "Shader used for rendering fluid effects.",
             "流体エフェクトをレンダリングするために使用されるシェーダー。")]
        private Shader shaderEffect;
        
        [SerializeField, LocalizationTooltip(
             "流体效果设置。",
             "Fluid effect settings.",
             "流体エフェクト設定。")] 
        private Liquid2DRenderFeatureSettings renderFeatureSettings;
        
        private Liquid2DPass _liquid2DPass;
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
            CheckSettings();
            
            // 检查 Shader 是否可用。 // Check if Shader is available. // シェーダーが利用可能かチェック。
            if (!shaderBlur || !shaderBlur.isSupported ||
                !shaderEffect || !shaderEffect.isSupported)
            {
                Debug.LogWarning(
                    $"Liquid2dFeature: Missing or unsupported shader for {GetType().Name}. Liquid2dPass feature will not execute.");
                return;
            }
            
            // 使用 shader 创建材质，并创建 Pass。 // Create materials using shader and create Pass. // シェーダーを使用してマテリアルを作成し、Passを作成。
            _materialBlur = new Material(shaderBlur);
            _materialEffect = new Material(shaderEffect);
            _materialBlur.renderQueue = 3000;
            _materialEffect.renderQueue = 3000;
            _liquid2DPass = new Liquid2DPass(_materialBlur, _materialEffect, renderFeatureSettings);
        }

        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            // 不在预览相机中渲染。 // Don't render in preview camera. // プレビューカメラではレンダリングしない。
            // if (renderingData.cameraData.cameraType != CameraType.Game)
            //     return;
            
            // 确认 Pass 和材质是否可用。 // Confirm if Pass and materials are available. // Passとマテリアルが利用可能かを確認。
            if (_liquid2DPass == null || _materialBlur == null || _materialEffect == null)
            {
                // Debug.LogWarning($"Liquid2dFeature: Missing Liquid2d Pass. {GetType().Name} render pass will not execute.");
                return;
            }
            
            renderer.EnqueuePass(_liquid2DPass);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                _liquid2DPass?.Dispose();
            }
        }

        #region Liquid Particle
        
        // 所有的流体粒子注册到这里，按设置分组。用于之后的渲染。
        // All fluid particles are registered here, grouped by settings. Used for later rendering.
        // すべての流体粒子がここに登録され、設定によってグループ化されます。後のレンダリングに使用。

        private static readonly Dictionary<Liquid2DParticleRenderSettings, List<Liquid2DParticle>>
            _particlesDic = new Dictionary<Liquid2DParticleRenderSettings, List<Liquid2DParticle>>();
        
        /// <summary>
        /// 所有注册的流体粒子，按设置分组。用于批量渲染。
        /// All registered fluid particles, grouped by settings. Used for batch rendering.
        /// 登録されたすべての流体粒子を設定別にグループ化。バッチレンダリングに使用。
        /// </summary>
        internal static Dictionary<Liquid2DParticleRenderSettings, List<Liquid2DParticle>> ParticlesDic => _particlesDic;

        /// <summary>
        /// 注册流体粒子。
        /// 为了 Game Instancing 批量渲染，我们需要将相同设置的粒子进行分组。
        /// Register fluid particle.
        /// For GPU Instancing batch rendering, we need to group particles with the same settings.
        /// 流体粒子を登録。
        /// GPU Instancingバッチレンダリングのために、同じ設定の粒子をグループ化する必要があります。
        /// </summary>
        /// <param name="particle"></param>
        public static void RegisterLiquidParticle(Liquid2DParticle particle)
        {
            if (particle == null || particle.RenderSettings == null) return;

            if (!_particlesDic.TryGetValue(particle.RenderSettings, out var list))
            {
                list = new List<Liquid2DParticle>();
                _particlesDic[particle.RenderSettings] = list;
            }

            if (!list.Contains(particle))
            {
                list.Add(particle);
            }
        }

        /// <summary>
        /// 注销流体粒子。
        /// Unregister fluid particle.
        /// 流体粒子の登録を解除。
        /// </summary>
        /// <param name="particle"></param>
        public static void UnregisterLiquidParticle(Liquid2DParticle particle)
        {
            if (particle == null || particle.RenderSettings == null) return;

            if (_particlesDic.TryGetValue(particle.RenderSettings, out var list))
            {
                if (list.Contains(particle))
                {
                    list.Remove(particle);
                }

                if (list.Count == 0)
                {
                    _particlesDic.Remove(particle.RenderSettings);
                }
            }
        }

        #endregion
    }
}
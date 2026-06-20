using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using System.Reflection;

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
            if (!shaderBlur)
            {
                shaderBlur = Shader.Find("Custom/URP/2D/Liquid2DBlur");
            }
            if (!shaderEffect)
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

#if UNITY_EDITOR
        /// <summary>
        /// 检查指定 Camera 使用的 ScriptableRenderer 上，是否存在 renderFeatureSettings.nameTag 与 nameTag 相同的 Liquid2DFeature。
        /// Check if the ScriptableRenderer used by the specified Camera has a Liquid2DFeature with renderFeatureSettings.nameTag same as nameTag.
        /// 指定されたカメラが使用するScriptableRendererに、renderFeatureSettings.nameTagがnameTagと同じLiquid2DFeatureが存在するかどうかを確認します。
        /// </summary>
        /// <param name="nameTag"></param>
        /// <param name="camera"></param>
        /// <returns></returns>
        public static bool HasFeatureWithNameTag(string nameTag, Camera camera)
        {
            if (string.IsNullOrEmpty(nameTag) || camera == null) return false;

            var additional = camera.GetUniversalAdditionalCameraData();
            if (!additional) return false;

            var renderer = additional.scriptableRenderer;
            if (renderer == null) return false;

            // 通过反射尝试获取 renderer 的 rendererFeatures（支持不同 URP 版本的命名）。
            object featuresObj =
                renderer.GetType().GetProperty("rendererFeatures", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(renderer)
                ?? renderer.GetType().GetField("m_RendererFeatures", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(renderer)
                ?? renderer.GetType().GetField("rendererFeatures", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                    ?.GetValue(renderer);

            if (featuresObj is IEnumerable<ScriptableRendererFeature> features)
            {
                foreach (var f in features)
                {
                    if (f is Liquid2DFeature feature)
                    {
                        var settings = feature.renderFeatureSettings;
                        if (settings != null && string.Equals(settings.NameTag, nameTag, System.StringComparison.Ordinal))
                            return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 检查主相机使用的 ScriptableRenderer 上，是否存在 renderFeatureSettings.nameTag 与 nameTag 相同的 Liquid2DFeature。
        /// Check if the ScriptableRenderer used by the main Camera has a Liquid2DFeature with renderFeatureSettings.nameTag same as nameTag.
        /// 主カメラが使用するScriptableRendererに、renderFeatureSettings.nameTagがnameTagと同じLiquid2DFeatureが存在するかどうかを確認します。
        /// </summary>
        /// <param name="nameTag"></param>
        /// <returns></returns>
        public static bool HasFeatureWithNameTag(string nameTag)
        {
            return HasFeatureWithNameTag(nameTag, Camera.main);
        }
#endif
    }
}
using System;
using Fs.Liquid2D.Volumes;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Fs.Liquid2D
{
    public class Liquid2dPass : ScriptableRenderPass
    {
        /// <summary>
        /// Shader 属性 ID。
        /// </summary>
        private static class ShaderIds
        {
            internal static readonly int MainTexId = Shader.PropertyToID("_MainTex");
            internal static readonly int SecondTex = Shader.PropertyToID("_SecondTex");
            internal static readonly int ColorId = Shader.PropertyToID("_Color");
            internal static readonly int ColorIntensityId = Shader.PropertyToID("_ColorIntensity");
            internal static readonly int BlurOffsetId = Shader.PropertyToID("_BlurOffset");
            internal static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
            internal static readonly int ObstructionTex = Shader.PropertyToID("_ObstructionTex");
            internal static readonly int OpacityValue = Shader.PropertyToID("_OpacityValue");
            internal static readonly int CoverColorId = Shader.PropertyToID("_CoverColor");
            internal static readonly int EdgeStart = Shader.PropertyToID("_EdgeStart");
            internal static readonly int EdgeEnd = Shader.PropertyToID("_EdgeEnd");
            internal static readonly int EdgeMixStart = Shader.PropertyToID("_EdgeMixStart");
            internal static readonly int EdgeColor = Shader.PropertyToID("_EdgeColor");
            internal static readonly int BackgroundTex = Shader.PropertyToID("_BackgroundTex");
            
            // 扰动相关。
            internal static readonly int Magnitude = Shader.PropertyToID("_Magnitude");
            internal static readonly int Frequency = Shader.PropertyToID("_Frequency");
            internal static readonly int Amplitude = Shader.PropertyToID("_Amplitude");
            internal static readonly int DistortSpeed = Shader.PropertyToID("_DistortSpeed");
            internal static readonly int DistortTimeFactors = Shader.PropertyToID("_DistortTimeFactors");
            internal static readonly int NoiseCoordOffset = Shader.PropertyToID("_NoiseCoordOffset");
            
            // 像素化相关。
            internal static readonly int PixelSize = Shader.PropertyToID("_PixelSize");
        }
        
        private static readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");

        private const string ShaderPathBlurCombineTwo = "Custom/URP/2D/CombineTwo";
        private static Material _materialBlurCombineTwo;
        /// <summary>
        /// 合并两张纹理的材质。用于将两张模糊纹理进行叠加。
        /// </summary>
        private static Material MaterialBlurCombineTwo
        {
            get
            {
                if (_materialBlurCombineTwo == null)
                {
                    _materialBlurCombineTwo = CoreUtils.CreateEngineMaterial(ShaderPathBlurCombineTwo);
                }
                return _materialBlurCombineTwo;
            }
        }
        
        private Material _materialBlur; // 流体模糊材质。
        private Material _materialEffect; // 流体效果材质。
        private bool IsValidMat => _materialBlur != null && _materialEffect != null;
        
        private readonly Mesh _quadMesh; // 用于绘制流体粒子的四边形网格。
        
        // 默认设置。用于在没有 Volume 或 Volume 未启用时使用。
        private readonly Liquid2DRenderFeatureSettings _settingsDefault;
        private readonly Liquid2DRenderFeatureSettings _settings;
        
        public Liquid2dPass(Material materialBlur, Material materialEffect, Liquid2DRenderFeatureSettings settings)
        {
            _materialBlur = materialBlur;
            _materialEffect = materialEffect;

            _settingsDefault = settings;
            _settings = settings.Clone();

            _quadMesh = GenerateQuadMesh();
            
            SetObstructionFilteringSettings();
            
            // 设置 Pass 执行时机。
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }
        
        public void Dispose()
        {
            CoreUtils.Destroy(_materialBlur);
            _materialBlur = null;
            CoreUtils.Destroy(_materialBlurCombineTwo);
            _materialBlurCombineTwo = null;
            CoreUtils.Destroy(_materialEffect);
            _materialEffect = null;
            CoreUtils.Destroy(_materialClone);
            _materialClone = null;
        }

        private class PassData
        {
            public UniversalCameraData cameraData;
            // public UniversalResourceData resourceData;
            // 当前设置。数据来源为 Volume 或默认设置。
            public Liquid2DRenderFeatureSettings settings;
            
            public TextureHandle sourceTh;
            
            public Material grabAsBgMaterial;
            public Color grabAsBgEdgeColor;
            public float grabAsBgEdgeColorIntensity;
            
            public Material cloneMaterial;
            public TextureHandle cloneSourceTh;
            
            // 流体粒子绘制 Pass 相关。
            public Matrix4x4[] matricesCache = new Matrix4x4[512];
            public Vector4[] colorArrayCache = new Vector4[512];
            public Mesh quadMesh;
            
            // 模糊 Pass 相关。
            public Material materialBlur;
            public TextureHandle blurSource;
            public int blurIteration;
            
            // 流体阻挡 Pass 相关。
            public RendererListHandle obstructionRendererListHandle;
            public TextureHandle obstructionTh;
            
            // 水体效果 Pass 相关。
            public Material materialEffect;
            public TextureHandle blurFinalTh;
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // ---- 更新设置 ---- //
            UpdateSettings();
            
            // ---- 获取基础数据 ---- //
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            var sourceTextureHandle = resourceData.activeColorTexture;
            
            // ---- 创建主纹理描述符 ---- //
            // 主纹理描述。之后其他纹理描述都基于这个进行修改。
            TextureDesc mainDesc = renderGraph.GetTextureDesc(sourceTextureHandle);
            mainDesc.clearBuffer = false; // 不清理纹理，保留原有内容。
            mainDesc.msaaSamples = MSAASamples.None; // 不使用多重采样。
            mainDesc.depthBufferBits = 0; // 不需要深度缓冲。
            mainDesc.useMipMap = false; // 关闭 mipmap 以节省内存和提升性能。开启后在 Effect 中的扰动采样会有问题。
            mainDesc.autoGenerateMips = false; // 关闭自动生成 mipmap。
            mainDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat; // 使用半精度格式以支持 HDR 颜色。

            #region 获取当前相机纹理

            // 获取当前相机的渲染目标纹理作为底图，将alpha处理成0。这样在之后的 Blur 混合后流体边缘会融入背景颜色。
            // 使用单色纹理背景色会影响 Blur 混合，最终渲染回主纹理时如果背景色和场景色差异过大，边缘会有明显的色差。
            TextureDesc grabAsBgDesc = mainDesc;
            // 抓取当前相机渲染目标纹理作为背景图。之后在 Effect 中作为背景图使用。
            grabAsBgDesc.name = GetName("grabAsBgSourceTh");
            TextureHandle grabAsBgSourceTh = renderGraph.CreateTexture(grabAsBgDesc);
            // 抓取当前相机渲染目标纹理作为流体粒子绘制的背景图。这里 alpha 处理成 0。在之后的 Blur 混合后流体边缘会融入背景颜色。
            grabAsBgDesc.name = GetName("liquidParticleTh");
            TextureHandle liquidParticleTh = renderGraph.CreateTexture(grabAsBgDesc);
            PassGrabAsBg(renderGraph, sourceTextureHandle, grabAsBgSourceTh, liquidParticleTh, GetName("Grab As Bg"));
            
            #endregion
            
            #region 绘制流体粒子
            
            // 判断当前相机是否为场景视图相机。
            bool isSceneView = cameraData.cameraType == CameraType.SceneView;

            // ---- 添加绘制到 Pass ---- //
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("Particles"), out var passData))
            {
                // 通道数据设置。
                // passData.resourceData = resourceData;
                passData.cameraData = cameraData;
                passData.settings = _settings;
                
                passData.quadMesh = _quadMesh; // 用于绘制流体粒子的四边形网格。

                builder.SetRenderAttachment(
                    // 设置渲染目标纹理句柄和声明使用纹理句柄。
                    isSceneView ? sourceTextureHandle : liquidParticleTh, 
                    0, AccessFlags.Write);

                // 设置绘制方法。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassParticle(data, context));
            }

            // 编辑场景相机不进行后续模糊和效果处理。直接返回粒子，方便编辑操作。
            if (isSceneView)
                return;
            
            // TEST: 直接将流体粒子TH拷贝回当前源TH。
            // PassClone(renderGraph, liquidParticleTh, sourceTextureHandle);
            // return;
            #endregion
            
            #region 模糊处理
            
            // 最终模糊纹理句柄，给到之后的处理步骤。
            TextureHandle blurThFinal;
            
            // 多次迭代模糊。
            if (_settings.blur.iterations > 0)
            {
                // ----- 创建纹理句柄 ----- //
                // 模糊纹理描述。
                TextureDesc blurDesc = mainDesc;
                // 这里使用当前相机尺寸四分之一的尺寸来提升性能。// 注意。缩放尺寸也会影响模糊的效果。
                blurDesc.width = cameraData.cameraTargetDescriptor.width / (int)_settings.blur.scaleFactor;
                blurDesc.height = cameraData.cameraTargetDescriptor.height / (int)_settings.blur.scaleFactor;
                
                blurDesc.name = GetName("Blur Left");
                TextureHandle blurThLeft = renderGraph.CreateTexture(blurDesc);
                blurDesc.name = GetName("Blur Right");
                TextureHandle blurThRight = renderGraph.CreateTexture(blurDesc);
                blurDesc.name = GetName("Blur Core");
                TextureHandle blurThCore = renderGraph.CreateTexture(blurDesc);
                
                // 复制流体粒子纹理到第一个模糊纹理和源颜色纹理。因为尺寸不同不能直接使用 liquidParticleTh。
                renderGraph.AddBlitPass(
                    liquidParticleTh, blurThLeft, 
                    Vector2.one, Vector2.zero, passName: GetName("Particles to Blur"));
                
                // ---- 添加绘制到 Pass ---- //
                // 计算核心保持使用哪次迭代的模糊纹理。
                int coreKeepIteration = Mathf.Clamp((int)(_settings.blur.iterations *  (1 - _settings.blur.coreKeepIntensity)), 1, _settings.blur.iterations - 1);
                bool coreKeep = coreKeepIteration < _settings.blur.iterations;
                
                TextureHandle blurThMain = blurThLeft; // 模糊迭代的最终纹理句柄。
                for (var i = 0; i < _settings.blur.iterations; ++i)
                {
                    // 交替使用两个模糊纹理句柄进行模糊。
                    PassBlur(
                        renderGraph, 
                        i % 2 == 0 ? blurThLeft : blurThRight, i % 2 == 0 ? blurThRight : blurThLeft, 
                        i, GetName($"Blur: {i}"));
                    
                    // 选择某次迭代的模糊图作为核心保持图。
                    if (coreKeep && i == coreKeepIteration)
                    {
                        renderGraph.AddBlitPass(
                            blurThRight, blurThCore, 
                            Vector2.one, Vector2.zero, passName: GetName("Blur: Get Blur Core"));
                    }
                    
                    // 记录最后一张模糊图作为最终模糊图。
                    if (i == _settings.blur.iterations - 1)
                    {
                        blurThMain = i % 2 == 0 ? blurThRight : blurThLeft;
                    }
                }

                // ---- 核心保持图叠加 ---- //
                if (coreKeep)
                {
                    // 将前期模糊的一张图作为核心保持图，和最后一张模糊图进行叠加，得到最终的模糊图。
                    // 这样在模糊迭代多且强度大时，也能保持粒子核心的形状，防止孤立粒子被过度模糊透明度过低而被裁剪掉。
                    // 更接近 SDF 的效果。
                    
                    blurDesc.name = GetName("Blur Combine Core");
                    TextureHandle blurThCombineCore = renderGraph.CreateTexture(blurDesc);
                    using (var builder = renderGraph.AddRasterRenderPass<PassData>("Blur: Combine Core ", out var passData))
                    {
                        // 设置渲染目标纹理句柄和声明使用纹理句柄。
                        builder.SetRenderAttachment(blurThCombineCore, 0, AccessFlags.Write);
                        builder.UseTexture(blurThMain, AccessFlags.Read);
                        builder.UseTexture(blurThCore, AccessFlags.Read);
                
                        // 设置绘制方法。
                        builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                        {
                            var cmd = context.cmd;
                        
                            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                            mpb.SetTexture(ShaderIds.MainTexId, blurThMain);
                            mpb.SetTexture(ShaderIds.SecondTex, blurThCore);
                            cmd.DrawProcedural(
                                Matrix4x4.identity, MaterialBlurCombineTwo, 0, 
                                MeshTopology.Triangles, 3, 1, mpb);
                        });
                    }
                
                    // ---- 叠加后模糊 ---- //
                    // 这一步使得核心保持图和最后的模糊图更好地融合在一起。
                    blurDesc.name = GetName("Blur: Final");
                    blurThFinal = renderGraph.CreateTexture(blurDesc);
                    PassBlur(renderGraph, blurThCombineCore, blurThFinal, 0, GetName($"Blur: Final"));
                }
                else
                {
                    // 不进行核心保持时，直接使用最后一张模糊图作为最终模糊图。
                    blurThFinal = blurThMain;
                }
            }
            else
            {
                // 不进行模糊时，直接使用流体粒子纹理作为最终模糊纹理。
                blurThFinal = liquidParticleTh;
            }
            
            // TEST: 直接将最后一个模糊 RT 拷贝回当前源纹理句柄。
            // PassClone(renderGraph, blurThFinal, sourceTextureHandle);
            // return;
            #endregion

            #region 流体阻挡
            
            // 创建阻挡纹理。用于之后的水体效果处理Shader。一般是挡板、管道、地形、容器等。

            // ---- 创建流体阻挡纹理 ---- //
            TextureDesc liquidObstructionDesc = mainDesc;
            liquidObstructionDesc.name = GetName("liquid 2d Obstruction");
            TextureHandle liquidObstructionTh = renderGraph.CreateTexture(liquidObstructionDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("liquid 2d Obstruction"), out PassData passData))
            {
                // 设置渲染目标纹理为流体阻挡纹理。
                builder.SetRenderAttachment(liquidObstructionTh, 0, AccessFlags.Write);
                
                // 获取所有 Liquid Obstruction Layer Mask层的 Renderer 列表。
                var drawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
                var param = new RendererListParams(renderingData.cullResults, drawSettings, _obstructionFilteringSettings);
                passData.obstructionRendererListHandle = renderGraph.CreateRendererList(param);
                builder.UseRendererList(passData.obstructionRendererListHandle);
                
                builder.SetRenderFunc(
                    (PassData data, RasterGraphContext context) => 
                    {
                        // context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1, 0);
                        context.cmd.DrawRendererList(data.obstructionRendererListHandle);
                    }
                );
            }
            
            // TEST: 直接将最后一个 RT 拷贝回当前源纹理句柄。
            // PassClone(renderGraph, liquidObstructionTh, sourceTextureHandle);
            // renderGraph.AddBlitPass(
            //     liquidObstructionTh, sourceTextureHandle, 
            //     Vector2.one, Vector2.zero, passName: GetName("obstructionTh to sourceTextureHandle"));
            // return;

            #endregion
            
            #region 水体效果处理

            // ---- 添加绘制到 Pass ---- //
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("liquid 2d Effect"), out var passData))
            {
                // 通道数据设置。
                // passData.resourceData = resourceData;
                passData.cameraData = cameraData;
                passData.settings = _settings;
                
                passData.materialEffect = _materialEffect;
                passData.sourceTh = grabAsBgSourceTh; // 当前相机纹理作为背景图。
                passData.blurFinalTh = blurThFinal; // 最终模糊纹理。
                passData.obstructionTh = liquidObstructionTh; // 流体阻挡纹理。
            
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(sourceTextureHandle, 0, AccessFlags.Write);
                builder.UseTexture(passData.sourceTh, AccessFlags.Read);
                builder.UseTexture(passData.blurFinalTh, AccessFlags.Read);
                builder.UseTexture(passData.obstructionTh, AccessFlags.Read);
                
                // 设置绘制方法。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassEffect(data, context));
            }
            #endregion
        }
        
        /// <summary>
        /// 流体粒子绘制 Pass。将所有注册的流体粒子绘制到流体绘制。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        private static void ExecutePassParticle(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;
            Camera cam = data.cameraData.camera;
            var planes = GeometryUtility.CalculateFrustumPlanes(cam);
             
            // 绘制所有流体粒子到 流体绘制RT。这里使用 GPU Instancing 来批量绘制。
            ELiquid2DLayer targetLayerMask = data.settings.liquid2DLayerMask;
            foreach (var kvp in Liquid2DFeature.ParticlesDic)
            {
                var settings = kvp.Key;
                var list = kvp.Value;
                if (list.Count == 0 || settings.sprite == null) continue;
             
                // 扩容渲染数据缓存数组。
                EnsureCacheSize(data, list.Count);
                
                // 填充流体粒子绘制用数据。
                int count = 0; // 实际渲染的粒子数量。
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
                    
                    // 跳过无效或禁用的粒子。
                    if (item == null || !item.isActiveAndEnabled) continue;
                    
                    // 使用层遮罩过滤粒子。只渲染需要的粒子。
                    if ((item.Settings.liquid2DLayerMask & targetLayerMask) == 0) continue;
                    
                    var ts = item.TransformGet;
                    
                    // 计算粒子的包围盒，不在相机视锥体内的不渲染。
                    var bounds = new Bounds(ts.position, ts.localScale);
                    if (!GeometryUtility.TestPlanesAABB(planes, bounds)) continue; 
                    
                    // 填充矩阵和颜色数据。
                    data.matricesCache[count] = Matrix4x4.TRS(ts.position, ts.rotation, ts.localScale);
                    data.colorArrayCache[count] = item.Settings.color;
                    count++;
                }
             
                // GUP Instancing 一次批量渲染。
                if (count > 0)
                {
                    var mpb = new MaterialPropertyBlock();
                    mpb.SetTexture(ShaderIds.MainTexId, settings.sprite.texture);
                    mpb.SetVectorArray(ShaderIds.ColorId, data.colorArrayCache);
             
                    cmd.DrawMeshInstanced(data.quadMesh, 0, settings.material, 0, data.matricesCache, count, mpb);
                }
            }
        }

        private void PassBlur(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, int iteration, string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out PassData passData))
            {
                // 通道数据设置。
                // passData.resourceData = resourceData;
                // passData.cameraData = cameraData;
                passData.settings = _settings;
                    
                passData.materialBlur = _materialBlur;
                passData.blurSource = source;
                passData.blurIteration = iteration;
                    
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.UseTexture(source, AccessFlags.Read);
            
                // 设置绘制方法。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassBlur(data, context));
            }
        }
        
        /// <summary>
        /// 模糊 Pass。对输入的流体纹理进行模糊处理，并输出到下一个模糊纹理。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        private static void ExecutePassBlur(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            // 模糊偏移强度递增，并乘以缩放比例。
            float offset = 
                (0.5f + data.blurIteration * data.settings.blur.blurSpread) * 3f / (int)data.settings.blur.scaleFactor;

            // 设置模糊材质属性块，传入当前模糊材质和偏移强度。
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetTexture(ShaderIds.MainTexId,data.blurSource); // 传入当前模糊纹理。
            mpb.SetFloat(ShaderIds.BlurOffsetId, offset); // 设置模糊偏移强度。
            // 是否忽略背景色。
            if (data.settings.blur.ignoreBgColor)
                data.materialBlur.EnableKeyword("_IGNORE_BG_COLOR");
            else
                data.materialBlur.DisableKeyword("_IGNORE_BG_COLOR");

            // 绘制一个全屏三角形，使用模糊材质，并传入属性块。
            cmd.DrawProcedural(
                Matrix4x4.identity, data.materialBlur, 0, 
                MeshTopology.Triangles, 3, 1, mpb);
        }

        /// <summary>
        /// 水体效果 Pass。将模糊后的流体纹理处理并绘制到当前相机的渲染目标上。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        private static void ExecutePassEffect(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            // 设置外描边材质属性块，传入绘制RT。
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetTexture(ShaderIds.MainTexId, data.blurFinalTh); // 流体纹理。
            mpb.SetTexture(ShaderIds.ObstructionTex, data.obstructionTh); // 流体阻挡纹理。
            mpb.SetFloat(ShaderIds.Cutoff, data.settings.cutoff); // 裁剪阈值。
            mpb.SetTexture(ShaderIds.BackgroundTex, data.sourceTh); // 背景纹理。用于扰动采样。

            // 透明度调整参数。
            if (data.settings.opacityMode == EOpacityMode.Multiply)
            {
                data.materialEffect.EnableKeyword("_OPACITY_MULTIPLY");
                data.materialEffect.DisableKeyword("_OPACITY_REPLACE");
            }
            else if (data.settings.opacityMode == EOpacityMode.Replace)
            {
                data.materialEffect.EnableKeyword("_OPACITY_REPLACE");
                data.materialEffect.DisableKeyword("_OPACITY_MULTIPLY");
            }
            else // Default
            {
                data.materialEffect.DisableKeyword("_OPACITY_MULTIPLY");
                data.materialEffect.DisableKeyword("_OPACITY_REPLACE");
            }
            mpb.SetFloat(ShaderIds.OpacityValue, data.settings.opacityValue); // 透明度值。
            
            // 覆盖颜色。透明度值是强度。
            mpb.SetColor(ShaderIds.CoverColorId, data.settings.coverColor);

            if (data.settings.edge.enable)
            {
                data.materialEffect.EnableKeyword("_EDGE_ENABLE");
                
                float cutoff = data.settings.cutoff;
                float edgeRange = data.settings.edge.edgeRange;
                float edgeIntensity = data.settings.edge.edgeIntensity;
                
                // 计算边缘参数。
                float edgeStart = cutoff;
                float edgeEnd = cutoff + edgeRange * (1 - cutoff);
                float edgeMixStart = Mathf.Lerp(edgeStart, edgeEnd, edgeIntensity * 0.999f);
            
                mpb.SetFloat(ShaderIds.EdgeStart, edgeStart); // 边缘开始位置。
                mpb.SetFloat(ShaderIds.EdgeEnd, edgeEnd); // 边缘结束位置。
                mpb.SetFloat(ShaderIds.EdgeMixStart, edgeMixStart); // 边缘混合开始位置。用于 smoothstep 计算。
                mpb.SetColor(ShaderIds.EdgeColor, data.settings.edge.edgeColor); // 边缘颜色。

                switch (data.settings.edge.blendType)
                {
                    case Liquid2DRenderFeatureSettings.Edge.EdgeBlendType.BlendSrcAlphaOneMinusSrcAlpha:
                        data.materialEffect.EnableKeyword("_EDGE_BLEND_SA_OMSA");
                        data.materialEffect.DisableKeyword("_EDGE_BLEND_LERP");
                        break;
                    case Liquid2DRenderFeatureSettings.Edge.EdgeBlendType.Lerp:
                        data.materialEffect.EnableKeyword("_EDGE_BLEND_LERP");
                        data.materialEffect.DisableKeyword("_EDGE_BLEND_SA_OMSA");
                        break;
                }
            }
            else
            {
                data.materialEffect.DisableKeyword("_EDGE_ENABLE");
            }

            // 像素风格化。
            if (data.settings.pixel.enable)
            {
                data.materialEffect.EnableKeyword("_PIXEL_ENABLE");
                // 计算像素化尺寸。
                float aspect = (float)Screen.width / Screen.height; // 屏幕宽高比。
                int pixelWidthCount = Screen.width / data.settings.pixel.pixelSize; // 水平像素块数量。
                Vector2 pixelSize = new Vector2(pixelWidthCount, pixelWidthCount / aspect); // 计算垂直像素块数量。
                mpb.SetVector(ShaderIds.PixelSize, pixelSize); // 传入像素化尺寸。

                // 是否使背景像素化。在水体透明时背景色也会像素化。
                if (data.settings.pixel.pixelBg)
                    data.materialEffect.EnableKeyword("_PIXEL_BG");
                else
                    data.materialEffect.DisableKeyword("_PIXEL_BG");
            }
            else
            {
                data.materialEffect.DisableKeyword("_PIXEL_ENABLE");
            }

            // 水体扰动纹理和强度。
            bool distortEnable = data.settings.distort.enable
                                 // 完全不透明时不进行扰动。
                                 && !(data.settings.opacityMode == EOpacityMode.Replace && data.settings.opacityValue >= 1f); 
            if (distortEnable)
            {
                data.materialEffect.EnableKeyword("_DISTORT_ENABLE");
                
                mpb.SetFloat(ShaderIds.Magnitude, data.settings.distort.magnitude);
                mpb.SetFloat(ShaderIds.Frequency, data.settings.distort.frequency);
                mpb.SetFloat(ShaderIds.Amplitude, data.settings.distort.amplitude);
                mpb.SetVector(ShaderIds.DistortSpeed, data.settings.distort.distortSpeed);
                mpb.SetVector(ShaderIds.DistortTimeFactors, data.settings.distort.distortTimeFactors);
                mpb.SetFloat(ShaderIds.NoiseCoordOffset, data.settings.distort.noiseCoordOffset);
            }
            else
            {
                data.materialEffect.DisableKeyword("_DISTORT_ENABLE");
            }
            
            // 绘制一个全屏三角形，使用外描边材质，并传入属性块。
            cmd.DrawProcedural(Matrix4x4.identity, data.materialEffect, 0, MeshTopology.Triangles, 3, 1,
                mpb);
        }
        
        #region Grab as Bg Pass

        private const string ShaderPathGrabAsBg = "Custom/URP/2D/GrabAsBg";
        private static Material _materialGrabAsBg;
        private static Material MaterialGrabAsBg
        {
            get
            {
                if (_materialGrabAsBg == null)
                {
                    _materialGrabAsBg = CoreUtils.CreateEngineMaterial(ShaderPathGrabAsBg);
                }
                return _materialGrabAsBg;
            }
        }

        /// <summary>
        /// 抓取源纹理作为背景 Pass。将源纹理拷贝到目标纹理，并将 alpha 设为 0。
        /// </summary>
        /// <param name="renderGraph"></param>
        /// <param name="source"></param>
        /// <param name="renderAttachment1"></param>
        /// <param name="renderAttachment2"></param>
        /// <param name="passNameSet"></param>
        private void PassGrabAsBg(
            RenderGraph renderGraph, TextureHandle source, TextureHandle renderAttachment1, TextureHandle renderAttachment2, string passNameSet  = "Grab As Bg")
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passNameSet, out var passData))
            {
                passData.grabAsBgMaterial = MaterialGrabAsBg;
                passData.sourceTh = source;
                passData.grabAsBgEdgeColor = _settings.blur.blurBgColor;
                passData.grabAsBgEdgeColorIntensity = _settings.blur.blurBgColorIntensity;
            
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(renderAttachment1, 0, AccessFlags.Write);
                builder.SetRenderAttachment(renderAttachment2, 1, AccessFlags.Write);
                builder.UseTexture(source, AccessFlags.Read);
            
                // 设置绘制方法。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    var material = data.grabAsBgMaterial;
                    
                    // material.SetColor(ShaderIds.ColorId, data.grabAsBgEdgeColor);
                    // material.SetFloat(ShaderIds.ColorIntensityId, data.grabAsBgEdgeColorIntensity);
                    // Blitter.BlitTexture(cmd, sourceTh, Vector2.one, material, 0);
                    
                    MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                    mpb.SetTexture(ShaderIds.MainTexId, data.sourceTh);
                    mpb.SetColor(ShaderIds.ColorId, data.grabAsBgEdgeColor);
                    mpb.SetFloat(ShaderIds.ColorIntensityId, data.grabAsBgEdgeColorIntensity);
                    // 绘制一个全屏三角形，使用外描边材质，并传入属性块。
                    cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1,
                        mpb);
                });
            }
        }

        #endregion

        #region Layer

        private FilteringSettings _obstructionFilteringSettings;
        
        /// <summary>
        /// 设置阻挡层过滤设置。
        /// </summary>
        private void SetObstructionFilteringSettings()
        {
            _obstructionFilteringSettings = 
                new FilteringSettings(
                    RenderQueueRange.all,
                    ~0,
                    _settings.obstructionRenderingLayerMask);
        }

        #endregion
        
        #region Volume
        // 通过 Volume 设置，你可以在运行时动态修改流体效果的参数。 
        
        /// <summary>
        /// 获取自身 NameTag 的 Volume 数据。
        /// </summary>
        private Liquid2DVolumeData VolumeData
        {
            get
            {
                if (_volumeData == null)
                {
                    Liquid2DVolume volumeComponent = VolumeManager.instance.stack.GetComponent<Liquid2DVolume>();
                    if (volumeComponent != null)
                    {
                        volumeComponent.GetData(_settings.nameTag, out _volumeData);
                    }
                }

                return _volumeData;
            }
        }
        private Liquid2DVolumeData _volumeData;
         
        /// <summary>
        /// 更新设置。
        /// 支持通过 Volume 系统重载当前 Renderer Feature 的配置。
        /// 如果没有 Volume 或 Volume 未启用，则使用默认设置。
        /// </summary>
        private void UpdateSettings()
        {
            if (!IsValidMat)
                return;
             
            // 获取 Volume。
            Liquid2DVolume volumeComponent = VolumeManager.instance.stack.GetComponent<Liquid2DVolume>();
            // 判断 Volume 是否启用且当前组件是否启用且数据有效。
            bool isActive =
                volumeComponent != null
                && volumeComponent.isActive.value
                && volumeComponent.liquid2DVolumeDataList.overrideState
                && VolumeData != null && VolumeData.isActive;
             
            // ---- 重载设置。 ---- //
            // 2D流体层遮罩。只会渲染设定的流体层的粒子。
            _settings.liquid2DLayerMask = isActive ? VolumeData.liquid2DLayerMask : _settingsDefault.liquid2DLayerMask;
            
            // 阻挡层遮罩。
            _settings.obstructionRenderingLayerMask = isActive ? VolumeData.obstructionRenderingLayerMask : _settingsDefault.obstructionRenderingLayerMask;
            if (_obstructionFilteringSettings.layerMask != _settings.obstructionRenderingLayerMask)
            {
                SetObstructionFilteringSettings();
            }
            
            _settings.cutoff = isActive ? VolumeData.cutoff : _settingsDefault.cutoff;
            
            _settings.opacityMode = isActive ? VolumeData.opacityMode : _settingsDefault.opacityMode;
            _settings.opacityValue = isActive ? VolumeData.opacityValue : _settingsDefault.opacityValue;
            _settings.coverColor = isActive ? VolumeData.coverColor : _settingsDefault.coverColor;
            
            // ---- 边缘设置 ---- //
            _settings.edge.enable = isActive ? VolumeData.edge.enable : _settingsDefault.edge.enable;
            _settings.edge.edgeRange = isActive ? VolumeData.edge.edgeRange : _settingsDefault.edge.edgeRange;
            _settings.edge.edgeIntensity = isActive ? VolumeData.edge.edgeIntensity : _settingsDefault.edge.edgeIntensity;
            _settings.edge.edgeColor = isActive ? VolumeData.edge.edgeColor : _settingsDefault.edge.edgeColor;
            _settings.edge.blendType = isActive ? VolumeData.edge.blendType : _settingsDefault.edge.blendType;

            // ---- 模糊设置 ---- //
            _settings.blur.iterations = isActive ? VolumeData.blur.iterations : _settingsDefault.blur.iterations;
            _settings.blur.blurSpread = isActive ? VolumeData.blur.blurSpread : _settingsDefault.blur.blurSpread;
            _settings.blur.coreKeepIntensity = isActive ? VolumeData.blur.coreKeepIntensity : _settingsDefault.blur.coreKeepIntensity;
            _settings.blur.scaleFactor = isActive ? VolumeData.blur.scaleFactor : _settingsDefault.blur.scaleFactor;
            _settings.blur.ignoreBgColor = isActive ? VolumeData.blur.ignoreBgColor : _settingsDefault.blur.ignoreBgColor;
            // 模糊背景色和强度。实际上在 Blur 前作为底图进行混合。默认的底图是当前相机的场景纹理（alpha为0）。
            _settings.blur.blurBgColor = isActive ? VolumeData.blur.blurBgColor : _settingsDefault.blur.blurBgColor;
            _settings.blur.blurBgColorIntensity = isActive ? VolumeData.blur.blurBgColorIntensity : _settingsDefault.blur.blurBgColorIntensity;
            
            // ---- 水体扰动设置 ---- //
            _settings.distort.enable = isActive ? VolumeData.distort.enable : _settingsDefault.distort.enable;
            _settings.distort.frequency = isActive ? VolumeData.distort.frequency : _settingsDefault.distort.frequency;
            _settings.distort.amplitude = isActive ? VolumeData.distort.amplitude : _settingsDefault.distort.amplitude;
            _settings.distort.distortSpeed = isActive ? VolumeData.distort.distortSpeed : _settingsDefault.distort.distortSpeed;
            _settings.distort.distortTimeFactors = isActive ? VolumeData.distort.distortTimeFactors : _settingsDefault.distort.distortTimeFactors;
            _settings.distort.noiseCoordOffset = isActive ? VolumeData.distort.noiseCoordOffset : _settingsDefault.distort.noiseCoordOffset;
            
            // ---- 像素化 ---- //
            _settings.pixel.enable = isActive ? VolumeData.pixel.enable : _settingsDefault.pixel.enable;
            _settings.pixel.pixelSize = isActive ? VolumeData.pixel.pixelSize : _settingsDefault.pixel.pixelSize;
            _settings.pixel.pixelBg = isActive ? VolumeData.pixel.pixelBg : _settingsDefault.pixel.pixelBg;
        }

        #endregion

        #region Tools

        #region Clone Pass

        private const string ShaderPathClone = "Custom/URP/2D/Clone";
        private static Material _materialClone;
        
        /// <summary>
        /// 克隆材质。直接将源纹理拷贝到目标纹理。
        /// </summary>
        private static Material MaterialClone
        {
            get
            {
                if (_materialClone == null)
                {
                    _materialClone = CoreUtils.CreateEngineMaterial(ShaderPathClone);
                }
                return _materialClone;
            }
        }

        /// <summary>
        /// 复制 Pass。将源纹理拷贝到目标纹理。
        /// Blend SrcAlpha OneMinusSrcAlpha
        /// </summary>
        /// <param name="renderGraph"></param>
        /// <param name="source"></param>
        /// <param name="renderAttachment"></param>
        /// <param name="passNameSet"></param>
        private void PassClone(RenderGraph renderGraph, TextureHandle source, TextureHandle renderAttachment, string passNameSet  = "liquid 2d Clone")
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passNameSet, out var passData))
            {
                passData.cloneMaterial = MaterialClone;
                passData.cloneSourceTh = source;
            
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(renderAttachment, 0, AccessFlags.Write);
                builder.UseTexture(source, AccessFlags.Read);
            
                // 设置绘制方法。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    var material = data.cloneMaterial;
                    var sourceTh = data.cloneSourceTh;
                    
                    Blitter.BlitTexture(cmd, sourceTh, Vector2.one, material, 0);
                });
            }
        }

        #endregion
        
        /// <summary>
        /// 确保渲染用缓存数组大小足够。
        /// 缓存数组用于 GPU Instancing 批量渲染。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="size"></param>
        private static void EnsureCacheSize(PassData data, int size)
        {
            if (data.matricesCache == null || data.matricesCache.Length < size)
                data.matricesCache = new Matrix4x4[size];
            if (data.colorArrayCache == null || data.colorArrayCache.Length < size)
                data.colorArrayCache = new Vector4[size];
        }
    
        /// <summary>
        /// 生成一个简单的四边形网格。
        /// 用于全屏渲染。
        /// </summary>
        /// <returns></returns>
        private Mesh GenerateQuadMesh()
        {
            var mesh = new Mesh
            {
                vertices = new Vector3[]
                {
                    new Vector3(-0.5f, -0.5f, 0),
                    new Vector3(0.5f, -0.5f, 0),
                    new Vector3(0.5f, 0.5f, 0),
                    new Vector3(-0.5f, 0.5f, 0)
                },
                uv = new Vector2[]
                {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(1, 1),
                    new Vector2(0, 1)
                },
                triangles = new int[] { 0, 1, 2, 2, 3, 0 }
            };
            return mesh;
        }

        private string GetName(string name)
        {
            return $"[Liquid 2D] [{_settings.nameTag}] {name}";
        }

        #endregion
    }
}
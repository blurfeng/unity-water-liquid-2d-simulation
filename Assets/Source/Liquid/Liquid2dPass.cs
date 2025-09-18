using System.Collections.Generic;
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
            internal static readonly int OpacityRate = Shader.PropertyToID("_OpacityRate");
        }
        
        private static readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
        
        private const string ShaderPathBlurCombineTwo = "Custom/URP/2D/CombineTwo";
        private static Material _materialBlurCombineTwo;
        
        /// <summary>
        /// 克隆材质。直接将源纹理拷贝到目标纹理。
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
        
        // 流体模糊材质。
        private Material _materialBlur;
        // 流体效果材质。
        private Material _materialEffect;
        // 材质是否有效。
        private bool IsValidMat => _materialBlur != null && _materialEffect != null;
        
        // 默认设置。用于在没有 Volume 或 Volume 未启用时使用。
        private readonly Liquid2DRenderFeatureSettings _settingsDefault;
        private readonly Liquid2DRenderFeatureSettings _settings;
        private readonly Mesh _quadMesh;
        
        private FilteringSettings _obstructionFilteringSettings;
        
        public Liquid2dPass(Material materialBlur, Material materialEffect, Liquid2DRenderFeatureSettings settings)
        {
            _materialBlur = materialBlur;
            _materialEffect = materialEffect;

            _settingsDefault = settings;
            _settings = settings.Clone();

            _quadMesh = GenerateQuadMesh();
            _obstructionFilteringSettings = new FilteringSettings(RenderQueueRange.all, settings.obstructionLayerMask);
            
            // 设置 Pass 执行时机。
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }
        
        public void Dispose()
        {
            CoreUtils.Destroy(_materialBlur);
            _materialBlur = null;
            CoreUtils.Destroy(_materialEffect);
            _materialEffect = null;
            CoreUtils.Destroy(_materialClone);
            _materialClone = null;
            CoreUtils.Destroy(_materialBlurCombineTwo);
            _materialBlurCombineTwo = null;
        }

        private class PassData
        {
            public UniversalCameraData cameraData;
            // public UniversalResourceData resourceData;
            // 当前设置。数据来源为 Volume 或默认设置。
            public Liquid2DRenderFeatureSettings settings;
            
            public Material grabAsBgMaterial;
            public TextureHandle grabAsBgSourceTh;
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
            public TextureHandle blurDestination;
            public int blurIteration;
            
            // 流体遮挡 Pass 相关。
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
            
            // ---- 创建纹理描述符 ---- //
            TextureDesc mainDesc = renderGraph.GetTextureDesc(sourceTextureHandle);
            mainDesc.name = GetName("liquid 2d Particles");
            mainDesc.clearBuffer = false;
            mainDesc.msaaSamples = MSAASamples.None;
            mainDesc.depthBufferBits = 0;
            mainDesc.useMipMap = _materialBlur;
            mainDesc.autoGenerateMips = false;
            mainDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat; // 使用半精度格式以支持 HDR 颜色。

            #region 获取当前相机源纹理

            // 获取当前相机的渲染目标纹理作为底图，将alpha处理成0。这样在之后的 Blur 混合后流体边缘会融入背景颜色。
            // 使用单色纹理背景色会影响 Blur 混合，最终渲染回主纹理时如果背景色和场景色差异过大，边缘会有明显的色差。
            TextureHandle liquidParticleTh = renderGraph.CreateTexture(mainDesc);
            PassGrabAsBg(renderGraph, sourceTextureHandle, liquidParticleTh, GetName("Grab As Bg"));
            
            #endregion
            
            #region 流体粒子绘制
            
            // ----- 创建纹理句柄 ----- //
            // 流体绘制材质句柄。
            bool isSceneView = cameraData.cameraType == CameraType.SceneView;

            // ---- 添加绘制到 Pass ---- //
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("Particles"), out var passData))
            {
                // 通道数据设置。
                // passData.resourceData = resourceData;
                passData.cameraData = cameraData;
                passData.settings = _settings;
                
                passData.quadMesh = _quadMesh;

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
            
            #endregion
            
            // TEST: 直接将流体粒子TH拷贝回当前源TH。
            // ClonePass(renderGraph, liquidParticleTh, sourceTextureHandle);
            // return;
            
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
                blurDesc.name = GetName("Blur First");
                TextureHandle blurThCore = renderGraph.CreateTexture(blurDesc);
                
                // 复制流体粒子纹理到第一个模糊纹理和源颜色纹理。
                renderGraph.AddBlitPass(
                    liquidParticleTh, blurThLeft, 
                    Vector2.one, Vector2.zero, passName: GetName("Particles to Blur"));
                
                // ---- 添加绘制到 Pass ---- //
                // 计算核心保持使用哪次迭代的模糊纹理。
                int coreKeepIteration = Mathf.Clamp((int)(_settings.blur.iterations *  (1 - _settings.blur.coreKeepIntensity)), 1, _settings.blur.iterations - 1);
                bool coreKeep = coreKeepIteration < _settings.blur.iterations;
                
                TextureHandle blurThTarget = blurThLeft; // 渲染目标纹理句柄。
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
                        blurThTarget = i % 2 == 0 ? blurThRight : blurThLeft;
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
                    var blurThSource = blurThTarget; // 最后一张模糊图。
                    using (var builder = renderGraph.AddRasterRenderPass<PassData>("Blur: Combine Core ", out var passData))
                    {
                        // 设置渲染目标纹理句柄和声明使用纹理句柄。
                        builder.SetRenderAttachment(blurThCombineCore, 0, AccessFlags.Write);
                        builder.UseTexture(blurThSource, AccessFlags.Read);
                        builder.UseTexture(blurThCore, AccessFlags.Read);
                
                        // 设置绘制方法。
                        builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                        {
                            var cmd = context.cmd;
                        
                            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
                            mpb.SetTexture(ShaderIds.MainTexId, blurThSource);
                            mpb.SetTexture(ShaderIds.SecondTex, blurThCore);
                            cmd.DrawProcedural(
                                Matrix4x4.identity, MaterialBlurCombineTwo, 0, 
                                MeshTopology.Triangles, 3, 1, mpb);
                        });
                    }
                
                    // ---- 叠加后的图再做一次模糊 ---- //
                    // 这一步使得核心保持图和最后的模糊图更好的融合在一起。
                    blurDesc.name = GetName("Blur Final");
                    blurThFinal = renderGraph.CreateTexture(blurDesc);
                    PassBlur(renderGraph, blurThCombineCore, blurThFinal, 0, GetName($"Blur Final"));
                }
                else
                {
                    // 不进行核心保持时，直接使用最后一张模糊图作为最终模糊图。
                    blurThFinal = blurThTarget;
                }
            }
            else
            {
                // 不进行模糊时，直接使用流体粒子纹理作为最终模糊纹理。
                blurThFinal = liquidParticleTh;
            }
            
            #endregion

            // TEST: 直接将最后一个模糊 RT 拷贝回当前源纹理句柄。
            // ClonePass(renderGraph, blurThFinal, sourceTextureHandle);
            // return;

            #region 流体阻挡
            
            // 创建阻挡纹理。用于之后的水体效果处理Shader。一般是挡板、管道、地形、容器等。

            // ---- 创建流体遮挡纹理 ---- //
            TextureDesc liquidObstructionDesc = mainDesc;
            liquidObstructionDesc.name = GetName("liquid 2d Obstruction");
            TextureHandle liquidObstructionTh = renderGraph.CreateTexture(liquidObstructionDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("liquid 2d Obstruction"), out PassData passData))
            {
                // 设置渲染目标纹理为流体遮挡纹理。
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

            #endregion
            
            // TEST: 直接将最后一个 RT 拷贝回当前源纹理句柄。
            // ClonePass(renderGraph, obstructionTh, sourceTextureHandle);
            // renderGraph.AddBlitPass(
            //     obstructionTh, sourceTextureHandle, 
            //     Vector2.one, Vector2.zero, passName: GetName("obstructionTh to sourceTextureHandle"));
            // return;
            
            #region 水体效果处理

            // ---- 添加绘制到 Pass ---- //
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("liquid 2d Effect"), out var passData))
            {
                // 通道数据设置。
                // passData.resourceData = resourceData;
                // passData.cameraData = cameraData;
                passData.quadMesh = _quadMesh;
                passData.settings = _settings;
                
                passData.blurFinalTh = blurThFinal;
                passData.materialEffect = _materialEffect;
                passData.obstructionTh = liquidObstructionTh;
            
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(sourceTextureHandle, 0, AccessFlags.Write);
                builder.UseTexture(blurThFinal, AccessFlags.Read);
                builder.UseTexture(liquidObstructionTh, AccessFlags.Read);
            
                // 设置绘制方法。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassEffect(data, context));
            }
            #endregion

            // TEST: 直接将流体粒子TH拷贝回当前源TH。
            // ClonePass(renderGraph, effectTh, sourceTextureHandle);
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
                
                // 填充数据。
                int count = 0; // 实际渲染的粒子数量。
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];
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

        #region 模糊 Pass

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
                passData.blurDestination = destination;
                passData.blurIteration = iteration;
                    
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                // 用于记录每个模糊片元的原有颜色。
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
            float offset = (0.5f + data.blurIteration * data.settings.blur.blurSpread) * 3f / (int)data.settings.blur.scaleFactor;

            // 设置模糊材质属性块，传入当前模糊材质和偏移强度。
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            // 传入当前模糊纹理。
            mpb.SetTexture(ShaderIds.MainTexId,data.blurSource);
            // 设置模糊偏移强度。
            mpb.SetFloat(ShaderIds.BlurOffsetId, offset);

            // 绘制一个全屏三角形，使用模糊材质，并传入属性块。
            cmd.DrawProcedural(
                Matrix4x4.identity, data.materialBlur, 0, 
                MeshTopology.Triangles, 3, 1, mpb);
        }

        #endregion

        /// <summary>
        /// 水体效果 Pass。将模糊后的流体纹理处理并绘制到当前相机的渲染目标上。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        private static void ExecutePassEffect(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            // 设置绘制目标为当前相机的渲染目标。
            // 设置外描边材质属性块，传入绘制RT。
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetTexture(ShaderIds.MainTexId, data.blurFinalTh);
            mpb.SetTexture(ShaderIds.ObstructionTex, data.obstructionTh);
            mpb.SetFloat(ShaderIds.Cutoff, data.settings.cutoff);
            mpb.SetFloat(ShaderIds.OpacityRate, data.settings.opacityRate);
            // 绘制一个全屏三角形，使用外描边材质，并传入属性块。
            cmd.DrawProcedural(Matrix4x4.identity, data.materialEffect, 0, MeshTopology.Triangles, 3, 1,
                mpb);
        }
        
        #region Volume
        // 通过 Volume 设置，你可以在运行时动态修改流体效果的参数。 
        
        private Liquid2DVolumeData VolumeData
        {
            get
            {
                if (_volumeData == null)
                {
                    // 获取 Volume。
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
        /// 每次执行时都会调用来支持运行时动态修改配置，确保设置是最新的。
        /// </summary>
        private void UpdateSettings()
        {
            if (!IsValidMat)
                return;
             
            // 获取 Volume。
            Liquid2DVolume volumeComponent = VolumeManager.instance.stack.GetComponent<Liquid2DVolume>();
            bool isActive =
                volumeComponent != null
                && volumeComponent.isActive.value
                && volumeComponent.liquid2DVolumeDataList.overrideState
                && VolumeData != null && VolumeData.isActive;
             
            // ---- 重载设置。 ---- //
            _settings.liquid2DLayerMask = isActive ? VolumeData.liquid2DLayerMask : _settingsDefault.liquid2DLayerMask;
            
            _settings.blur.iterations = isActive ? VolumeData.blur.iterations : _settingsDefault.blur.iterations;
            _settings.blur.blurSpread = isActive ? VolumeData.blur.blurSpread : _settingsDefault.blur.blurSpread;
            _settings.blur.coreKeepIntensity = isActive ? VolumeData.blur.coreKeepIntensity : _settingsDefault.blur.coreKeepIntensity;
            _settings.blur.scaleFactor = isActive ? VolumeData.blur.scaleFactor : _settingsDefault.blur.scaleFactor;
            
            _settings.cutoff = isActive ? VolumeData.cutoff : _settingsDefault.cutoff;
            _settings.opacityRate = isActive ? VolumeData.opacityRate : _settingsDefault.opacityRate;
            
            // 阻挡层遮罩。
            _settings.obstructionLayerMask = isActive ? VolumeData.obstructionLayerMask : _settingsDefault.obstructionLayerMask;
            if (_obstructionFilteringSettings.layerMask != _settings.obstructionLayerMask)
            {
                _obstructionFilteringSettings = new FilteringSettings(RenderQueueRange.all, _settings.obstructionLayerMask);
            }
            
            // 边缘色和强度。实际上在 Blur 前作为底图进行混合。默认的底图是当前相机的场景纹理（alpha为0）。
            _settings.blur.blurEdgeColor = isActive ? VolumeData.blur.blurEdgeColor : _settingsDefault.blur.blurEdgeColor;
            _settings.blur.blurEdgeColorIntensity = isActive ? VolumeData.blur.blurEdgeColorIntensity : _settingsDefault.blur.blurEdgeColorIntensity;
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
        /// <param name="renderAttachment"></param>
        /// <param name="passNameSet"></param>
        private void PassGrabAsBg(
            RenderGraph renderGraph, TextureHandle source, TextureHandle renderAttachment, string passNameSet  = "Grab As Bg")
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passNameSet, out var passData))
            {
                passData.grabAsBgMaterial = MaterialGrabAsBg;
                passData.grabAsBgSourceTh = source;
                passData.grabAsBgEdgeColor = _settings.blur.blurEdgeColor;
                passData.grabAsBgEdgeColorIntensity = _settings.blur.blurEdgeColorIntensity;
            
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(renderAttachment, 0, AccessFlags.Write);
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
                    mpb.SetTexture(ShaderIds.MainTexId, data.grabAsBgSourceTh);
                    mpb.SetColor(ShaderIds.ColorId, data.grabAsBgEdgeColor);
                    mpb.SetFloat(ShaderIds.ColorIntensityId, data.grabAsBgEdgeColorIntensity);
                    // 绘制一个全屏三角形，使用外描边材质，并传入属性块。
                    cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1,
                        mpb);
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
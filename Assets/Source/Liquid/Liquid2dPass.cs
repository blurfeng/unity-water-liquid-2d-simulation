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
            internal static readonly int ColorId = Shader.PropertyToID("_Color");
            internal static readonly int BlurOffsetId = Shader.PropertyToID("_BlurOffset");
            internal static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
            internal static readonly int OcclusionTex = Shader.PropertyToID("_OcclusionTex");
        }
        
        private static readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");
        
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
        
        private readonly List<TextureHandle> _blurTHs = new List<TextureHandle>();
        
        private FilteringSettings _liquidOcclusionFilteringSettings;
        
        public Liquid2dPass(Material materialBlur, Material materialEffect, Liquid2DRenderFeatureSettings settings)
        {
            _materialBlur = materialBlur;
            _materialEffect = materialEffect;

            _settingsDefault = settings;
            _settings = settings.Clone();

            _quadMesh = GenerateQuadMesh();
            _liquidOcclusionFilteringSettings = new FilteringSettings(RenderQueueRange.all, settings.liquidOcclusionLayerMask);
            
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
        }

        private class PassData
        {
            public UniversalCameraData cameraData;
            // public UniversalResourceData resourceData;
            // 当前设置。数据来源为 Volume 或默认设置。
            public Liquid2DRenderFeatureSettings settings;
            
            public Material cloneMaterial;
            public TextureHandle cloneSourceTh;
            
            // 流体粒子绘制 Pass 相关。
            public Matrix4x4[] matricesCache = new Matrix4x4[512];
            public Vector4[] colorArrayCache = new Vector4[512];
            public Mesh quadMesh;
            
            // 模糊 Pass 相关。
            public Material materialBlur;
            public List<TextureHandle> blurThs = new List<TextureHandle>();
            public int blurIndex;
            
            // 流体遮挡 Pass 相关。
            public RendererListHandle liquidOcclusionRendererListHandle;
            public TextureHandle liquidOcclusionTh;
            
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
            
            #region 流体粒子绘制

            // ---- 创建纹理描述符 ---- //
            // 流体描述符。
            TextureDesc mainDesc = renderGraph.GetTextureDesc(sourceTextureHandle);
            mainDesc.name = GetName("liquid 2d Particles");
            mainDesc.clearBuffer = false;
            mainDesc.msaaSamples = MSAASamples.None;
            mainDesc.depthBufferBits = 0;
            mainDesc.useMipMap = _materialBlur;
            mainDesc.autoGenerateMips = false;
            mainDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat; // 使用半精度格式以支持 HDR 颜色。

            // ----- 创建纹理句柄 ----- //
            // 流体绘制材质句柄。
            TextureHandle liquidParticleTh = renderGraph.CreateTexture(mainDesc);
            bool isSceneView = cameraData.cameraType == CameraType.SceneView;

            // ---- 添加绘制到 Pass ---- //
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("liquid 2d Particles"), out var passData))
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

            if (isSceneView)
            {
                return;
            }
            
            #endregion
            
            // TEST: 直接将流体粒子TH拷贝回当前源TH。
            // ClonePass(liquidParticleTh, _blurTHs[_blurTHs.Count - 1], sourceTextureHandle);
            // return;
            
            #region 模糊处理
            
            // ----- 创建纹理句柄 ----- //
            // 模糊纹理描述。
            TextureDesc blurDesc = mainDesc;
            // 这里使用当前相机尺寸四分之一的尺寸来提升性能。// 注意。缩放尺寸也会影响模糊的效果。
            blurDesc.width = cameraData.cameraTargetDescriptor.width / (int)_settings.scaleFactor;
            blurDesc.height = cameraData.cameraTargetDescriptor.height / (int)_settings.scaleFactor;
            
            // 这里目前只使用两个模糊纹理句柄，交替使用。但任先保留 List 以便后续扩展或渲染机制调整。
            _blurTHs.Clear();
            for (int i = 0; i < 2; ++i)
            {
                blurDesc.name = GetName(i == 0 ? "liquid 2d Blur Left" : "liquid 2d Blur Right");
                var blurRT = renderGraph.CreateTexture(blurDesc);
                _blurTHs.Add(blurRT);
            }
            
            // 复制流体粒子纹理到第一个模糊纹理和源颜色纹理。
            renderGraph.AddBlitPass(
                liquidParticleTh, _blurTHs[0], 
                Vector2.one, Vector2.zero, passName: GetName("Liquid 2d particles to Blur"));
            
            // ---- 添加绘制到 Pass ---- //
            // 进行多次迭代模糊。
            int blurCount = 0;
            for (var i = 0; i < _settings.iterations; ++i)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName($"liquid 2d Blur {i}"), out var passData))
                {
                    // 通道数据设置。
                    // passData.resourceData = resourceData;
                    // passData.cameraData = cameraData;
                    passData.settings = _settings;
                    
                    passData.materialBlur = _materialBlur;
                    passData.blurThs = _blurTHs;
                    passData.blurIndex = blurCount = i;
                    
                    // 设置渲染目标纹理句柄和声明使用纹理句柄。
                    builder.SetRenderAttachment(i % 2 == 0 ? _blurTHs[1] : _blurTHs[0], 0, AccessFlags.Write);
                    // 用于记录每个模糊片元的原有颜色。
                    builder.UseTexture(i % 2 == 0 ? _blurTHs[0] : _blurTHs[1], AccessFlags.Read);
            
                    // 设置绘制方法。
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassBlur(data, context));
                }
            }
            
            #endregion

            // TEST: 直接将最后一个模糊 RT 拷贝回当前源纹理句柄。
            // ClonePass(renderGraph, blurCount % 2 == 0 ? _blurTHs[1] : _blurTHs[0], sourceTextureHandle);
            // return;

            #region 流体阻挡

            // ---- 创建流体遮挡纹理 ---- //
            TextureDesc liquidOcclusionDesc = mainDesc;
            liquidOcclusionDesc.name = GetName("liquid 2d Occlusion");
            TextureHandle liquidOcclusionTh = renderGraph.CreateTexture(liquidOcclusionDesc);

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("liquid 2d Occlusion"), out PassData passData))
            {
                // 设置渲染目标纹理为流体遮挡纹理。
                builder.SetRenderAttachment(liquidOcclusionTh, 0, AccessFlags.Write);
                
                // 获取所有 Liquid Occlusion Layer Mask层的 Renderer 列表。
                var drawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
                var param = new RendererListParams(renderingData.cullResults, drawSettings, _liquidOcclusionFilteringSettings);
                passData.liquidOcclusionRendererListHandle = renderGraph.CreateRendererList(param);
                builder.UseRendererList(passData.liquidOcclusionRendererListHandle);
                
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        // context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1, 0);
                        context.cmd.DrawRendererList(data.liquidOcclusionRendererListHandle);
                    }
                );
            }

            #endregion
            
            // TEST: 直接将最后一个 RT 拷贝回当前源纹理句柄。
            // ClonePass(renderGraph, liquidOcclusionTh, sourceTextureHandle);
            // renderGraph.AddBlitPass(
            //     liquidOcclusionTh, sourceTextureHandle, 
            //     Vector2.one, Vector2.zero, passName: GetName("liquidOcclusionTh to sourceTextureHandle"));
            // return;
            
            #region 水体效果处理

            // ---- 添加绘制到 Pass ---- //
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("liquid 2d Effect"), out var passData))
            {
                var finalTh = blurCount % 2 == 0 ? _blurTHs[1] : _blurTHs[0];
                
                // 通道数据设置。
                // passData.resourceData = resourceData;
                // passData.cameraData = cameraData;
                passData.quadMesh = _quadMesh;
                passData.settings = _settings;
                
                passData.blurFinalTh = finalTh;
                passData.materialEffect = _materialEffect;
                passData.liquidOcclusionTh = liquidOcclusionTh;
            
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(sourceTextureHandle, 0, AccessFlags.Write);
                builder.UseTexture(finalTh, AccessFlags.Read);
                builder.UseTexture(liquidOcclusionTh, AccessFlags.Read);
            
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

            bool isSceneView = data.cameraData.cameraType == CameraType.SceneView;
            if (!isSceneView)
            {
                // 在绘制 Scene View 时直接绘制到当前相机的渲染目标上用于编辑操作就结束了，所以不清理RT。
                // 否则会导致当前相机的渲染目标的数据都被清理掉。
                
                // 清理流体绘制 RT。背景颜色会影响流体模糊时的混合颜色。
                Color col = data.settings.backgroundColor;
                cmd.ClearRenderTarget(true, true, 
                    new Color(col.r, col.g, col.b, 0));
            }
             
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
                    // 使用层遮罩过滤粒子。只渲染需要的粒子。
                    if ((item.Settings.liquid2DLayerMask & targetLayerMask) == 0) continue;
                    var ts = item.TransformGet;
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

        /// <summary>
        /// 模糊 Pass。对输入的流体纹理进行模糊处理，并输出到下一个模糊纹理。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        private static void ExecutePassBlur(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            // 模糊偏移强度递增，并乘以缩放比例。
            float offset = (0.5f + data.blurIndex * data.settings.blurSpread) * 3f / (int)data.settings.scaleFactor;

            // 设置模糊材质属性块，传入当前模糊材质和偏移强度。
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetTexture(ShaderIds.MainTexId, data.blurIndex % 2 == 0 ? data.blurThs[0] : data.blurThs[1]);
            mpb.SetFloat(ShaderIds.BlurOffsetId, offset);
            if (data.settings.blurSamplingMode == EBlurSamplingMode.Four)
            {
                // 4次采样。
                data.materialBlur.DisableKeyword("EIGHT_SAMPLINGS");
            }
            else
            {
                // 8次采样。
                data.materialBlur.EnableKeyword("EIGHT_SAMPLINGS");
            }

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
            
            // 设置绘制目标为当前相机的渲染目标。
            // 设置外描边材质属性块，传入绘制RT。
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetTexture(ShaderIds.MainTexId, data.blurFinalTh);
            mpb.SetTexture(ShaderIds.OcclusionTex, data.liquidOcclusionTh);
            mpb.SetFloat(ShaderIds.Cutoff, data.settings.cutoff);
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
            _settings.iterations = isActive ? VolumeData.iterations : _settingsDefault.iterations;
            _settings.blurSpread = isActive ? VolumeData.blurSpread : _settingsDefault.blurSpread;
            _settings.liquid2DLayerMask = isActive ? VolumeData.liquid2DLayerMask : _settingsDefault.liquid2DLayerMask;
            _settings.scaleFactor = isActive ? VolumeData.scaleFactor : _settingsDefault.scaleFactor;
            _settings.cutoff = isActive ? VolumeData.cutoff : _settingsDefault.cutoff;
            _settings.liquidOcclusionLayerMask = isActive ? VolumeData.liquidOcclusionLayerMask : _settingsDefault.liquidOcclusionLayerMask;
            // 更新遮罩过滤设置。
            if (_liquidOcclusionFilteringSettings.layerMask != _settings.liquidOcclusionLayerMask)
            {
                _liquidOcclusionFilteringSettings = new FilteringSettings(RenderQueueRange.all, _settings.liquidOcclusionLayerMask);
            }
            _settings.backgroundColor = isActive ? VolumeData.backgroundColor : _settingsDefault.backgroundColor;
            _settings.blurSamplingMode = isActive ? VolumeData.blurSamplingMode : _settingsDefault.blurSamplingMode;
        }

        #endregion

        #region Tools
        
        private const string ShaderPath = "Custom/URP/2D/Clone";
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
                    _materialClone = CoreUtils.CreateEngineMaterial(ShaderPath);
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
        private static void ClonePass(
            RenderGraph renderGraph, 
            TextureHandle source, TextureHandle renderAttachment)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("liquid 2d Clone", out var passData))
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
            return $"[{_settings.nameTag}] {name}";
        }

        #endregion
    }
}
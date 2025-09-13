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
        private const string ShaderPath = "Custom/URP/2D/Clone";
        private static Material _materialClone;
        /// <summary>
        /// 克隆材质。直接将源纹理拷贝到目标纹理。保留透明度信息。
        /// 用于测试。
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
        /// Shader 属性 ID。
        /// </summary>
        private static class ShaderIDs
        {
            internal static readonly int MainTexId = Shader.PropertyToID("_MainTex");
            internal static readonly int ColorId = Shader.PropertyToID("_Color");
            internal static readonly int BlurOffsetId = Shader.PropertyToID("_BlurOffset");
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
        
        private readonly List<TextureHandle> _blurTHs = new List<TextureHandle>();
        
        public Liquid2dPass(Material materialBlur, Material materialEffect, Liquid2DRenderFeatureSettings settings)
        {
            _materialBlur = materialBlur;
            _materialEffect = materialEffect;

            _settingsDefault = settings;
            _settings = settings.Clone();

            _quadMesh = GenerateQuadMesh();
            
            // 设置 Pass 执行时机。
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
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
            // public UniversalCameraData cameraData;
            // public UniversalResourceData resourceData;
            // 当前设置。数据来源为 Volume 或默认设置。
            public Liquid2DRenderFeatureSettings settings;
            
            public Material cloneMaterial;
            public TextureHandle cloneSourceTh;
            
            public Matrix4x4[] matricesCache = new Matrix4x4[512];
            public Vector4[] colorArrayCache = new Vector4[512];
            public Mesh quadMesh;
            
            public Material materialBlur;
            public List<TextureHandle> blurThs = new List<TextureHandle>();
            public int blurCount;
            
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
            var sourceTextureHandle = resourceData.activeColorTexture;

            #region 流体粒子绘制

            // ---- 创建纹理描述符 ---- //
            // 流体描述符。
            TextureDesc mainDesc = renderGraph.GetTextureDesc(sourceTextureHandle);
            mainDesc.name = "liquid 2d Texture";
            mainDesc.clearBuffer = false;
            mainDesc.msaaSamples = MSAASamples.None;
            mainDesc.depthBufferBits = 0;
            mainDesc.useMipMap = _materialBlur;
            mainDesc.autoGenerateMips = false;
            mainDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat; // 使用半精度格式以支持 HDR 颜色。

            // ----- 创建纹理句柄 ----- //
            // 流体绘制材质句柄。
            TextureHandle liquidParticleTh = renderGraph.CreateTexture(mainDesc);

            // ---- 添加绘制到 Pass ---- //
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("liquid 2d Particles", out var passData))
            {
                // 通道数据设置。
                // passData.resourceData = resourceData;
                // passData.cameraData = cameraData;
                passData.settings = _settings;
                
                passData.quadMesh = _quadMesh;

                builder.SetRenderAttachment(
                    // 设置渲染目标纹理句柄和声明使用纹理句柄。
                    cameraData.cameraType == CameraType.SceneView ? sourceTextureHandle : liquidParticleTh, 
                    0, AccessFlags.Write);

                // 设置绘制方法。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassParticle(data, context));
            }

            if (cameraData.cameraType == CameraType.SceneView)
            {
                return;
            }
            #endregion
            
            // TEST: 直接将流体粒子TH拷贝回当前源TH。
            // ClonePass(liquidParticleTh, _blurTHs[_blurTHs.Count - 1], sourceTextureHandle);
            // return;
            
            #region 模糊处理
            
            // ---- 创建纹理描述符 ---- //
            TextureDesc blueDesc = mainDesc;
            // 这里使用当前相机尺寸四分之一的尺寸来提升性能。// 注意。缩放尺寸也会影响模糊的效果。
            blueDesc.width = cameraData.cameraTargetDescriptor.width / 4;
            blueDesc.height = cameraData.cameraTargetDescriptor.height / 4;
            
            // ----- 创建纹理句柄 ----- //
            _blurTHs.Clear();
            for (int i = 0; i < _settings.iterations + 1; ++i)
            {
                var blurRT = renderGraph.CreateTexture(blueDesc);
                _blurTHs.Add(blurRT);
            }
            
            // 先将流体纹理句柄 Blit 到第一个模糊纹理句柄。
            renderGraph.AddBlitPass(liquidParticleTh, _blurTHs[0], Vector2.one, Vector2.zero, passName: "Liquid 2d to Blur TH 0");
            
            // ---- 添加绘制到 Pass ---- //
            // 进行多次迭代模糊。
            for (var i = 0; i < _settings.iterations; ++i)
            {
                bool isLast = i == _settings.iterations - 1;
                using (var builder = renderGraph.AddRasterRenderPass<PassData>($"liquid 2d Blur {i}", out var passData))
                {
                    // 通道数据设置。
                    // passData.resourceData = resourceData;
                    // passData.cameraData = cameraData;
                    passData.settings = _settings;
                    
                    passData.materialBlur = _materialBlur;
                    passData.blurThs = _blurTHs;
                    passData.blurCount = i;
                    
                    // 设置渲染目标纹理句柄和声明使用纹理句柄。
                    builder.SetRenderAttachment(_blurTHs[i + 1], 0, AccessFlags.Write);
                    builder.UseTexture(_blurTHs[i], AccessFlags.Read);  
            
                    // 设置绘制方法。
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassBlur(data, context));
                }
            }
            
            #endregion

            // TEST: 直接将最后一个模糊 RT 拷贝回当前源纹理句柄。
            // ClonePass(renderGraph, _blurTHs[_blurTHs.Count - 1], sourceTextureHandle);
            // return;
            
            #region 水体效果处理
            
            // TextureHandle effectTh = renderGraph.CreateTexture(mainDesc);
            
            // ---- 添加绘制到 Pass ---- //
            using (var builder = renderGraph.AddRasterRenderPass<PassData>("liquid 2d Effect", out var passData))
            {
                var finalTh = _blurTHs[^1];
                
                // 通道数据设置。
                // passData.resourceData = resourceData;
                // passData.cameraData = cameraData;
                passData.quadMesh = _quadMesh;
                passData.settings = _settings;
                
                passData.blurFinalTh = finalTh;
                passData.materialEffect = _materialEffect;
            
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(sourceTextureHandle, 0, AccessFlags.Write);
                builder.UseTexture(finalTh, AccessFlags.Read);  
            
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
                    mpb.SetTexture(ShaderIDs.MainTexId, settings.sprite.texture);
                    mpb.SetVectorArray(ShaderIDs.ColorId, data.colorArrayCache);
             
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
            
            // 模糊偏移强度递增。
            float offset = 0.5f + data.blurCount * data.settings.blurSpread;

            // 设置模糊材质属性块，传入当前模糊 RT 和偏移强度。
            MaterialPropertyBlock mpb = new MaterialPropertyBlock();
            mpb.SetTexture(ShaderIDs.MainTexId, data.blurThs[data.blurCount]);
            mpb.SetFloat(ShaderIDs.BlurOffsetId, offset);

            // 绘制一个全屏三角形，使用模糊材质，并传入属性块。
            cmd.DrawProcedural(Matrix4x4.identity, data.materialBlur, 0, MeshTopology.Triangles, 3, 1,
                mpb);
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
            mpb.SetTexture(ShaderIDs.MainTexId, data.blurFinalTh);
            // 绘制一个全屏三角形，使用外描边材质，并传入属性块。
            cmd.DrawProcedural(Matrix4x4.identity, data.materialEffect, 0, MeshTopology.Triangles, 3, 1,
                mpb);
        }

        /// <summary>
        /// 复制 Pass。将源纹理拷贝到目标纹理。测试用。
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
                
                // builder.SetRenderAttachmentDepth(resourceData.activeDepthTexture, AccessFlags.Write);
                // builder.AllowPassCulling(false);
                // builder.AllowGlobalStateModification(true);
            
                // 设置绘制方法。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    var material = data.cloneMaterial;
                    var sourceTh = data.cloneSourceTh;
                    // Blitterの便利関数を使ってBlit実行
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
            var mesh = new Mesh();
            mesh.vertices = new Vector3[]
            {
                new Vector3(-0.5f, -0.5f, 0),
                new Vector3(0.5f, -0.5f, 0),
                new Vector3(0.5f, 0.5f, 0),
                new Vector3(-0.5f, 0.5f, 0)
            };
            mesh.uv = new Vector2[]
            {
                new Vector2(0, 0),
                new Vector2(1, 0),
                new Vector2(1, 1),
                new Vector2(0, 1)
            };
            mesh.triangles = new int[] { 0, 1, 2, 2, 3, 0 };
            return mesh;
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
            _settings.iterations = isActive && VolumeData.isActive
                ? VolumeData.iterations
                : _settingsDefault.iterations;
             
            _settings.blurSpread = isActive && VolumeData.isActive
                ? VolumeData.blurSpread
                : _settingsDefault.blurSpread;
             
            _settings.liquid2DLayerMask = isActive && VolumeData.isActive
                ? VolumeData.liquid2DLayerMask
                : _settingsDefault.liquid2DLayerMask;
        }

        #endregion
    }
}
using System.Collections.Generic;
using Fs.Liquid2D.Volumes;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D 流体效果渲染特性。
    /// 我们使用 2D 球体模拟每个流体粒子，然后使用自定义 Shader 对球体进行渲染处理，模拟出流体效果。
    /// </summary>
    public class Liquid2DFeature : ScriptableRendererFeature
    {
        class Liquid2dPass : ScriptableRenderPass
        {
            private static readonly List<ShaderTagId> _shaderTagIds = new List<ShaderTagId>
            {
                new ShaderTagId("SRPDefaultUnlit"),
                new ShaderTagId("UniversalForward"),
                new ShaderTagId("UniversalForwardOnly"),
            };

            /// <summary>
            /// Shader 属性 ID。
            /// </summary>
            private static class ShaderIDs
            {
                internal static readonly int MainTexId = Shader.PropertyToID("_MainTex");
                internal static readonly int ColorId = Shader.PropertyToID("_Color");
                internal static readonly int BlurOffsetId = Shader.PropertyToID("_BlurOffset");
            }

            private readonly Liquid2DFeatureSettings _defaultFeatureSettings;

            // 当前设置。数据来源为 Volume 或默认设置。
            private readonly Liquid2DFeatureSettings _featureSettings;

            private readonly Material _materialBlur;
            private readonly Material _materialEffect;
            private bool IsMaterialValid => _materialBlur != null && _materialEffect != null;


            // 模糊 RT 描述符缓存。
            private readonly MaterialPropertyBlock _propertyBlock;
            private RTHandle _liquidDrawRT;
            private readonly List<RTHandle> _blurRTs = new List<RTHandle>();
            private static Mesh _quadMesh;
            
            private Matrix4x4[] _matricesCache = new Matrix4x4[512];
            private Vector4[] _colorArrayCache = new Vector4[512];

            public Liquid2dPass(Material materialBlur, Material materialEffect, Liquid2DFeatureSettings featureSettings)
            {
                // Configures where the render pass should be injected.
                renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;

                _materialBlur = materialBlur;
                _materialEffect = materialEffect;
                _defaultFeatureSettings = featureSettings;
                _featureSettings = featureSettings.Clone();

                _propertyBlock = new MaterialPropertyBlock();
                _quadMesh = GenerateQuadMesh();
            }

            public void Dispose()
            {
                _liquidDrawRT?.Release();
                _liquidDrawRT = null;

                if (_blurRTs.Count > 0)
                {
                    foreach (var rt in _blurRTs)
                    {
                        rt?.Release();
                    }

                    _blurRTs.Clear();
                }
            }

            // This method is called before executing the render pass.
            // It can be used to configure render targets and their clear state. Also to create temporary render target textures.
            // When empty this render pass will render to the active camera render target.
            // You should never call CommandBuffer.SetRenderTarget. Instead call <c>ConfigureTarget</c> and <c>ConfigureClear</c>.
            // The render pipeline will ensure target setup and clearing happens in a performant manner.
            public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
            {
                // 重置渲染目标。这会将之前的更改全部清除掉。
                ResetTarget();
                
                // 分配 流体绘制 RT。
                RenderingUtils.ReAllocateIfNeeded(
                    ref _liquidDrawRT, 
                    CreateDescriptorForRT(renderingData.cameraData.cameraTargetDescriptor), 
                    name: "Liquid Draw RT");
            }

            // Here you can implement the rendering logic.
            // Use <c>ScriptableRenderContext</c> to issue drawing commands or execute command buffers
            // https://docs.unity3d.com/ScriptReference/Rendering.ScriptableRenderContext.html
            // You don't have to call ScriptableRenderContext.submit, the render pipeline will call it at specific points in the pipeline.
            public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
            {
                // 更新设置。
                UpdateSettings();

                // 创建命令缓冲区。
                CommandBuffer cmd = CommandBufferPool.Get("Liquid2d");

                // ---- 绘制流体基础 RT ---- //
                // 设置绘制目标为流体绘制RT。并在渲染前清空RT。
                cmd.SetRenderTarget(_liquidDrawRT);
                cmd.ClearRenderTarget(true, true, Color.clear);

                // 绘制所有流体粒子到 流体绘制RT。这里使用 GPU Instancing 来批量绘制。
                ELiquid2DLayer targetLayerMask = _featureSettings.liquid2DLayerMask;

                foreach (var kvp in _particlesDic)
                {
                    var settings = kvp.Key;
                    var list = kvp.Value;
                    if (list.Count == 0 || settings.sprite == null) continue;

                    // 扩容渲染数据缓存数组。
                    EnsureCacheSize(list.Count);

                    // 填充数据。
                    int count = 0; // 实际渲染的粒子数量。
                    for (int i = 0; i < list.Count; i++)
                    {
                        var item = list[i];
                        // 使用层遮罩过滤粒子。只渲染需要的粒子。
                        if ((item.Settings.liquid2DLayerMask & targetLayerMask) == 0) continue;
                        var ts = item.TransformGet;
                        _matricesCache[count] = Matrix4x4.TRS(ts.position, ts.rotation, ts.localScale);
                        _colorArrayCache[count] = item.Settings.color;
                        count++;
                    }

                    // GUP Instancing 一次批量渲染。
                    if (count > 0)
                    {
                        var mpb = new MaterialPropertyBlock();
                        mpb.SetVectorArray(ShaderIDs.ColorId, _colorArrayCache);
                        mpb.SetTexture(ShaderIDs.MainTexId, settings.sprite.texture);

                        cmd.DrawMeshInstanced(_quadMesh, 0, settings.material, 0, _matricesCache, count, mpb);
                    }
                }

                // ---- 对流体粒子进行模糊 ----//

                // 分配模糊 RT。// 需要的模糊 RT 数量比当前多，进行分配。
                if (_featureSettings.iterations + 1 > _blurRTs.Count)
                {
                    // 使用当前相机的渲染目标描述符来配置 RT。
                    RenderTextureDescriptor descBlur = CreateDescriptorForRT(renderingData.cameraData.cameraTargetDescriptor);

#if UNITY_EDITOR
                    // 在编辑器模式下，使用全分辨率。
                    // 因为当前有Scene和Game两个视图可能导致获取的 renderingData.cameraData.cameraTargetDescriptor.width 数据不正确。
                    if (Camera.main)
                    {
                        descBlur.width = Camera.main.pixelWidth / 4;
                        descBlur.height = Camera.main.pixelHeight / 4;
                    }
#else
                    // 这里使用当前相机尺寸四分之一的尺寸来提升性能。// 注意。缩放尺寸也会影响模糊的效果。
                    descBlur.width = renderingData.cameraData.cameraTargetDescriptor.width / 4;
                    descBlur.height = renderingData.cameraData.cameraTargetDescriptor.height / 4;
#endif
                    for (var i = _blurRTs.Count; i < _featureSettings.iterations + 1; ++i)
                    {
                        RTHandle blurRT = null;
                        RenderingUtils.ReAllocateIfNeeded(ref blurRT, descBlur, name: $"Blur RT {i}");
                        _blurRTs.Add(blurRT);
                    }
                }

                // 先将 流体绘制RT 拷贝到第一个模糊 RT。
                cmd.Blit(_liquidDrawRT, _blurRTs[0]);

                // 进行多次迭代模糊。
                for (var i = 0; i < _featureSettings.iterations; ++i)
                {
                    // 设置绘制目标为下一个模糊 RT，并先清空它。
                    cmd.SetRenderTarget(_blurRTs[i + 1]);
                    cmd.ClearRenderTarget(true, true, Color.clear);

                    // 模糊偏移强度递增。
                    float offset = 0.5f + i * _featureSettings.blurSpread;

                    // 设置模糊材质属性块，传入当前模糊 RT 和偏移强度。
                    MaterialPropertyBlock propertyBlockBlur = new MaterialPropertyBlock();
                    propertyBlockBlur.SetTexture(ShaderIDs.MainTexId, _blurRTs[i]);
                    propertyBlockBlur.SetFloat(ShaderIDs.BlurOffsetId, offset);

                    // 绘制一个全屏三角形，使用模糊材质，并传入属性块。
                    cmd.DrawProcedural(Matrix4x4.identity, _materialBlur, 0, MeshTopology.Triangles, 3, 1,
                        propertyBlockBlur);
                }

                // 最终将最后一个模糊 RT 拷贝回 正常尺寸的绘制RT。
                cmd.Blit(_blurRTs[_featureSettings.iterations - 1], _liquidDrawRT);

                // ---- 对模糊后的流体 RT 进行遮罩 ---- //
                // 设置绘制目标为当前相机的渲染目标。
                cmd.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
                // 设置外描边材质属性块，传入绘制RT。
                _propertyBlock.SetTexture(ShaderIDs.MainTexId, _liquidDrawRT);
                // 绘制一个全屏三角形，使用外描边材质，并传入属性块。
                cmd.DrawProcedural(Matrix4x4.identity, _materialEffect, 0, MeshTopology.Triangles, 3, 1,
                    _propertyBlock);

                // ---- 执行绘制 ---- //
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }

            // Cleanup any allocated resources that were created during the execution of this render pass.
            public override void OnCameraCleanup(CommandBuffer cmd)
            {
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
            
            /// <summary>
            /// 确保渲染用缓存数组大小足够。
            /// 缓存数组用于 GPU Instancing 批量渲染。
            /// </summary>
            /// <param name="size"></param>
            private void EnsureCacheSize(int size)
            {
                if (_matricesCache == null || _matricesCache.Length < size)
                    _matricesCache = new Matrix4x4[size];
                if (_colorArrayCache == null || _colorArrayCache.Length < size)
                    _colorArrayCache = new Vector4[size];
            }

            /// <summary>
            /// 创建用于流体渲染的 RT 描述符。
            /// </summary>
            /// <param name="descriptor"></param>
            /// <returns></returns>
            private RenderTextureDescriptor CreateDescriptorForRT(RenderTextureDescriptor descriptor)
            {
                // 使用当前相机的渲染目标描述符来配置RT。
                RenderTextureDescriptor desc = descriptor;
                // 不需要太高的抗锯齿。
                desc.msaaSamples = 1;
                // 不需要深度缓冲。
                desc.depthBufferBits = 0;
                // 支持HDR的颜色格式。
                desc.colorFormat = RenderTextureFormat.ARGBHalf;
                desc.useMipMap = false;
                desc.autoGenerateMips = false;

                return desc;
            }

            #region Volume
            
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
                            volumeComponent.GetData(_featureSettings.nameTag, out _volumeData);
                        }
                    }

                    return _volumeData;
                }
            }
            private Liquid2DVolumeData _volumeData = null;
            
            /// <summary>
            /// 更新设置。
            /// 每次执行时都会调用来支持运行时动态修改配置，确保设置是最新的。
            /// </summary>
            private void UpdateSettings()
            {
                if (!IsMaterialValid) return;
                
                // 获取 Volume。
                Liquid2DVolume volumeComponent = VolumeManager.instance.stack.GetComponent<Liquid2DVolume>();
                bool isActive =
                    volumeComponent != null
                    && volumeComponent.isActive.value
                    && volumeComponent.liquid2DVolumeDataList.overrideState
                    && VolumeData != null && VolumeData.isActive;
                
                // ---- 重载设置。 ---- //
                _featureSettings.iterations = isActive && VolumeData.isActive
                    ? VolumeData.iterations
                    : _defaultFeatureSettings.iterations;
                
                _featureSettings.blurSpread = isActive && VolumeData.isActive
                    ? VolumeData.blurSpread
                    : _defaultFeatureSettings.blurSpread;
                
                _featureSettings.liquid2DLayerMask = isActive && VolumeData.isActive
                    ? VolumeData.liquid2DLayerMask
                    : _defaultFeatureSettings.liquid2DLayerMask;
            }

            #endregion
        }

        [SerializeField, Tooltip("用于模糊流体粒子的 Shader。")]
        private Shader shaderBlur;

        [SerializeField, Tooltip("用于渲染流体效果的 Shader。")]
        private Shader shaderEffect;

        [SerializeField, Tooltip("流体效果设置。")] private Liquid2DFeatureSettings featureSettings;

        private Liquid2dPass _liquid2dPass;
        private Material _materialBlur;
        private Material _materialEffect;

        /// <inheritdoc/>
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
            _liquid2dPass = new Liquid2dPass(_materialBlur, _materialEffect, featureSettings);
        }

        // Here you can inject one or multiple render passes in the renderer.
        // This method is called when setting up the renderer once per-camera.
        public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
        {
            if (_materialBlur == null || _materialEffect == null || _liquid2dPass == null)
            {
                // Debug.LogWarning($"Liquid2dFeature: Missing Liquid2d Pass. {GetType().Name} render pass will not execute.");
                return;
            }

            // 将 Pass 注入渲染器队列。
            renderer.EnqueuePass(_liquid2dPass);
        }

        /// <summary>
        /// 释放资源。
        /// </summary>
        /// <param name="disposing"></param>
        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            // 释放 Pass 相关资源。
            _liquid2dPass?.Dispose();
        }

        #region Liquid Particle 水粒子

        // 所有注册的流体粒子，按设置分组。用于批量渲染。
        private static readonly Dictionary<Liquid2dParticleRendererSettings, List<Liquid2DParticleRenderer>>
            _particlesDic = new Dictionary<Liquid2dParticleRendererSettings, List<Liquid2DParticleRenderer>>();

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
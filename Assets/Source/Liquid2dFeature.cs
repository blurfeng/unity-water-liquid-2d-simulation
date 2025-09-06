using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using Volumes;

/// <summary>
/// 2D 流体效果渲染特性。
/// 我们使用 2D 球体模拟每个流体粒子，然后使用自定义 Shader 对球体进行渲染处理，模拟出流体效果。
/// </summary>
public class Liquid2dFeature : ScriptableRendererFeature
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
            internal static readonly int BlurOffsetId = Shader.PropertyToID("_BlurOffset");
        }
        
        private readonly Liquid2dSettings _defaultSettings;
        private Liquid2dSettings _settings;
        private readonly Material _materialBlur;
        private readonly Material _materialMask;
        private bool IsMaterialValid => _materialBlur != null && _materialMask != null;
        private readonly MaterialPropertyBlock _propertyBlock;
        private FilteringSettings _filteringSettings;
        private RTHandle _maskRT;

        private RenderTextureDescriptor _descBlur;
        private readonly List<RTHandle> _blurRTs = new List<RTHandle>();

        public Liquid2dPass(Material materialBlur, Material materialMask, Liquid2dSettings settings)
        {
            // Configures where the render pass should be injected.
            renderPassEvent = RenderPassEvent.BeforeRenderingPostProcessing;
            
            _materialBlur = materialBlur;
            _materialMask = materialMask;
            _defaultSettings = settings;
            _settings = settings.Clone();
            
            _propertyBlock = new MaterialPropertyBlock();
            
            // 只渲染指定 Rendering Layer 的物体。
            _filteringSettings = 
                new FilteringSettings(
                    RenderQueueRange.all, 
                    renderingLayerMask: (uint)_defaultSettings.renderingLayerMask);
        }
        
        public void Dispose()
        {
            _maskRT?.Release();
            _maskRT = null;
            
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
            
            // 使用当前相机的渲染目标描述符来配置RT。
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            // 不需要太高的抗锯齿。
            desc.msaaSamples = 1;
            // 不需要深度缓冲。
            desc.depthBufferBits = 0;
            // 选择有Alpha通道的颜色格式，后续处理需要。
            desc.colorFormat = RenderTextureFormat.ARGB32;
            
            // 分配 Mask RT。
            RenderingUtils.ReAllocateIfNeeded(ref _maskRT, desc, name:"_MaskRT");
            
            _descBlur = renderingData.cameraData.cameraTargetDescriptor;
            _descBlur.msaaSamples = 1;
            _descBlur.depthBufferBits = 0;
            _descBlur.colorFormat = RenderTextureFormat.ARGB32;
            _descBlur.width = Mathf.Max(512, Screen.width / 1);
            _descBlur.height = Mathf.Max(512,  Screen.height / 1);
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
            
            // ---- 遮罩 RT ---- //
            // 设置绘制目标为_outlineMaskRT。并在渲染前清空RT。
            cmd.SetRenderTarget(_maskRT);
            cmd.ClearRenderTarget(true, true, Color.clear);
            
            // 设置绘制属性并创建渲染列表，然后绘制。
            var drawingSettings = CreateDrawingSettings(_shaderTagIds, ref  renderingData, SortingCriteria.None);
            var rendererListParams = new RendererListParams(renderingData.cullResults, drawingSettings, _filteringSettings);
            var list = context.CreateRendererList(ref rendererListParams);
            // 绘制一批已有的渲染器。这里的来源是 context（场景中的 Mesh Renderer），但我们指定了过滤，只绘制我们想要的物体。
            cmd.DrawRendererList(list);
            
            // ---- 对流体粒子进行模糊 ----//
            
            // 分配模糊 RT。
            for (var i = 0; i < _settings.iterations + 1; ++i)
            {
                if (i >= _blurRTs.Count)
                {
                    RTHandle blurRt = null;
                    RenderingUtils.ReAllocateIfNeeded(ref blurRt, _descBlur, name:$"blurRt{i}");
                    _blurRTs.Add(blurRt);
                }
                
                //cmd.SetRenderTarget(_blurRTs[i]);
                //cmd.ClearRenderTarget(true, true, Color.clear);
            }
            
            // 先将 Mask RT 拷贝到第一个模糊 RT。
            cmd.Blit(_maskRT, _blurRTs[0]);
            
            // 进行多次迭代模糊。
            for (var i = 0; i < _settings.iterations; ++i)
            {
                cmd.SetRenderTarget(_blurRTs[i + 1]);
                cmd.ClearRenderTarget(true, true, Color.clear);
                float offset = 0.5f + i * _settings.blurSpread;
                MaterialPropertyBlock propertyBlockBlur = new MaterialPropertyBlock();
                propertyBlockBlur.SetTexture(ShaderIDs.MainTexId, _blurRTs[i]);
                propertyBlockBlur.SetFloat(ShaderIDs.BlurOffsetId, offset);
                cmd.DrawProcedural(Matrix4x4.identity, _materialBlur, 0, MeshTopology.Triangles, 3, 1, propertyBlockBlur);
            }
            
            // 最终将最后一个模糊 RT 拷贝回 Mask RT。
            cmd.Blit(_blurRTs[_blurRTs.Count - 1], _maskRT);
            
            // ---- 对模糊后的流体 RT 进行遮罩 ---- //
            // 设置绘制目标为当前相机的渲染目标。
            cmd.SetRenderTarget(renderingData.cameraData.renderer.cameraColorTargetHandle);
            // 设置外描边材质属性块，传入 Mask RT。
            _propertyBlock.SetTexture("_MainTex", _maskRT);
            // 绘制一个全屏三角形，使用外描边材质，并传入属性块。
            cmd.DrawProcedural(Matrix4x4.identity, _materialMask, 0, MeshTopology.Triangles, 3, 1, _propertyBlock);

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
        /// 更新设置。
        /// 每次执行时都会调用来支持运行时动态修改配置，确保设置是最新的。
        /// </summary>
        private void UpdateSettings()
        {
            if (!IsMaterialValid) return;

            // 获取 Volume。
            var volumeComponent = VolumeManager.instance.stack.GetComponent<Liquid2d>();
            bool isActive = volumeComponent != null && volumeComponent.isActive.value;
            
            // ---- 根据 Volume 设置和默认值更新配置 ---- //
            _settings.renderingLayerMask = isActive && volumeComponent.renderingLayerMask.overrideState ?
                (ERenderingLayerMask)volumeComponent.renderingLayerMask.value : _defaultSettings.renderingLayerMask;
            uint renderingLayerMask = (uint)_settings.renderingLayerMask;
            // 更新过滤设置，最终应用于渲染。
            if (renderingLayerMask != _filteringSettings.renderingLayerMask)
            {
                _filteringSettings.renderingLayerMask = renderingLayerMask;
            }
            
            _settings.iterations = isActive && volumeComponent.iterations.overrideState ?
                volumeComponent.iterations.value : _defaultSettings.iterations;
            
            _settings.blurSpread = isActive && volumeComponent.blurSpread.overrideState ?
                volumeComponent.blurSpread.value : _defaultSettings.blurSpread;
            
            // ---- 更新材质 ---- //
            //_material.SetColor(_outlineColorId, outlineColor);
            //_material.SetFloat(_outlineWidthId, outlineWidth);
        }
    }

    [SerializeField] private Shader shaderBlur;
    [SerializeField] private Shader shaderMask;
    [SerializeField] private Liquid2dSettings settings;
    
    private Liquid2dPass _liquid2dPass;
    private Material _materialBlur;
    private Material _materialMask;
    
    /// <inheritdoc/>
    public override void Create()
    {
        // 检查 Shader 是否可用。
        if (!shaderBlur || !shaderBlur.isSupported || !shaderMask || !shaderMask.isSupported)
        {
            Debug.LogWarning($"Liquid2dFeature: Missing or unsupported shader for {GetType().Name}. Liquid2dPass feature will not execute.");
            return;
        }
        
        // 使用 shader 创建材质，并创建 Pass。
        _materialBlur = new  Material(shaderBlur);
        _materialMask = new Material(shaderMask);
        _liquid2dPass = new Liquid2dPass(_materialBlur, _materialMask, settings);
    }

    // Here you can inject one or multiple render passes in the renderer.
    // This method is called when setting up the renderer once per-camera.
    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_materialBlur == null || _materialMask == null || _liquid2dPass == null)
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
        _liquid2dPass.Dispose();
    }
}



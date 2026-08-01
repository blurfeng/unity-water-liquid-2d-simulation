// 单源：Unity 2022.3 / URP 14 命令式渲染路径（无 Render Graph）。仅在非 U6（URP17 以下）编译；U6 走 Liquid2DPass.cs 的 Render Graph 版本。
#if !UNITY_6000_0_OR_NEWER
using System.Collections.Generic;
using Fs.Liquid2D.Volumes;
using Unity.Mathematics;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D 流体渲染 Pass（Unity 2022.3 / URP 14 移植版）。
    /// 从 Unity 6 的 Render Graph 版本（RecordRenderGraph）改写为 URP14 Compatibility Mode 的命令式 Execute。
    /// 流程与 Unity 6 一致：GrabAsBg（双目标 MRT）→ 从仿真绘制粒子（CPU DrawMeshInstanced）→ 迭代模糊+核心保持 →
    /// 障碍物/遮挡物渲染层纹理 → Liquid2DEffect 合成 → 调试显示 Overlay。
    /// Ported from the Unity 6 Render Graph pass; rewritten imperatively for URP 14 (no Render Graph).
    /// </summary>
    public class Liquid2DPass : ScriptableRenderPass
    {
        /// <summary>
        /// Shader 属性 ID。 // Shader property IDs. // シェーダープロパティID。
        /// </summary>
        private static class ShaderIds
        {
            internal static readonly int MainTexId = Shader.PropertyToID("_MainTex");
            internal static readonly int SecondTex = Shader.PropertyToID("_SecondTex");
            internal static readonly int ColorId = Shader.PropertyToID("_Color");
            internal static readonly int ColorIntensityId = Shader.PropertyToID("_ColorIntensity");
            internal static readonly int BlurOffsetId = Shader.PropertyToID("_BlurOffset");
            internal static readonly int Cutoff = Shader.PropertyToID("_Cutoff");
            internal static readonly int ObstructorTex = Shader.PropertyToID("_ObstructorTex");
            internal static readonly int OccluderTex = Shader.PropertyToID("_OccluderTex");
            internal static readonly int OpacityValue = Shader.PropertyToID("_OpacityValue");
            internal static readonly int EdgeEnd = Shader.PropertyToID("_EdgeEnd");
            internal static readonly int EdgeMixStart = Shader.PropertyToID("_EdgeMixStart");
            internal static readonly int EdgeColor = Shader.PropertyToID("_EdgeColor");
            internal static readonly int BackgroundTex = Shader.PropertyToID("_BackgroundTex");

            // 扰动相关。 // Distortion related. // 歪み関連。
            internal static readonly int Magnitude = Shader.PropertyToID("_Magnitude");
            internal static readonly int Frequency = Shader.PropertyToID("_Frequency");
            internal static readonly int Amplitude = Shader.PropertyToID("_Amplitude");
            internal static readonly int DistortSpeed = Shader.PropertyToID("_DistortSpeed");
            internal static readonly int DistortTimeFactors = Shader.PropertyToID("_DistortTimeFactors");
            internal static readonly int NoiseCoordOffset = Shader.PropertyToID("_NoiseCoordOffset");

            // 像素化相关。 // Pixelation related. // ピクセル化関連。
            internal static readonly int PixelSize = Shader.PropertyToID("_PixelSize");

            // GPU 常驻粒子绘制相关（当前 CPU 优先，GPU 路径保留待验证）。 // GPU-resident particle draw (deferred).
            internal static readonly int PositionsBuf = Shader.PropertyToID("_Positions");
            internal static readonly int ColorsBuf = Shader.PropertyToID("_Colors");
            internal static readonly int RadiiBuf = Shader.PropertyToID("_Radii");
            internal static readonly int TypeIdsBuf = Shader.PropertyToID("_TypeIds");
            internal static readonly int ActiveIdxBuf = Shader.PropertyToID("_ActiveIndices");
            internal static readonly int TargetType = Shader.PropertyToID("_TargetType");
            internal static readonly int RenderScale = Shader.PropertyToID("_RenderScale");
        }

        private static readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");

        // ── 材质（惰性创建）Materials (lazy) ──────────────────────────────────
        private Material _materialBlur;   // 流体模糊材质（由 Feature 传入）。
        private Material _materialEffect;  // 流体效果材质（由 Feature 传入）。
        private bool IsValidMat => _materialBlur && _materialEffect;

        private const string ShaderPathBlurCombineTwo = "Custom/URP/2D/CombineTwo";
        private Material _materialBlurCombineTwo;
        private Material MaterialBlurCombineTwo
        {
            get
            {
                if (!_materialBlurCombineTwo)
                    _materialBlurCombineTwo = CoreUtils.CreateEngineMaterial(ShaderPathBlurCombineTwo);
                return _materialBlurCombineTwo;
            }
        }

        private const string ShaderPathGrabAsBg = "Custom/URP/2D/GrabAsBg";
        private Material _materialGrabAsBg;
        private Material MaterialGrabAsBg
        {
            get
            {
                if (!_materialGrabAsBg)
                    _materialGrabAsBg = CoreUtils.CreateEngineMaterial(ShaderPathGrabAsBg);
                return _materialGrabAsBg;
            }
        }

        private const string ShaderPathParticleGpu = "Custom/URP/2D/Liquid2DParticleGpu";
        private Material _materialParticleGpu;
        private Material MaterialParticleGpu
        {
            get
            {
                if (!_materialParticleGpu) _materialParticleGpu = CoreUtils.CreateEngineMaterial(ShaderPathParticleGpu);
                return _materialParticleGpu;
            }
        }

        private const string ShaderPathClone = "Custom/URP/2D/Clone";
        private Material _materialClone;

        // ── 绘制缓存 Draw caches ──────────────────────────────────────────────
        private readonly Mesh _quadMesh;
        private const int MaxInstancesPerBatch = 1023;
        private readonly Matrix4x4[] _matricesCache = new Matrix4x4[MaxInstancesPerBatch];
        private readonly Vector4[] _colorArrayCache = new Vector4[MaxInstancesPerBatch];
        private readonly Plane[] _frustumPlanes = new Plane[6];

        private readonly MaterialPropertyBlock _mpbParticle = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbBlur = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbEffect = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbGrabAsBg = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbCombineCore = new MaterialPropertyBlock();

        private readonly Liquid2DRenderFeatureSettings _settingsDefault;
        private readonly Liquid2DRenderFeatureSettings _settings;

        // ── 持久 RTHandle（URP14 无 RenderGraph 纹理池，改由本 Pass 持有并按需重分配）──
        private RTHandle _grabAsBgSourceRT;   // 背景拷贝（保留 alpha）。
        private RTHandle _liquidParticleRT;   // 粒子绘制目标（alpha→0 的背景底）。
        private RTHandle _blurLeftRT;         // ping。
        private RTHandle _blurRightRT;        // pong。
        private RTHandle _blurCoreRT;         // 核心保持快照。
        private RTHandle _blurCombineCoreRT;  // 合并输出。
        private RTHandle _blurFinalRT;        // 合并后最终模糊。
        private RTHandle _obstructorRT;       // 障碍物渲染层纹理。
        private RTHandle _occluderRT;         // 遮挡物渲染层纹理。
        private RTHandle _cameraColorRT;      // 相机颜色目标（renderer 持有，勿释放）。

        // MRT 目标数组，复用避免每帧分配。 // Reused MRT target array.
        private readonly RenderTargetIdentifier[] _grabMrt = new RenderTargetIdentifier[2];

        private FilteringSettings _obstructorFilteringSettings;
        private FilteringSettings _occluderFilteringSettings;

        public Liquid2DPass(Material materialBlur, Material materialEffect, Liquid2DRenderFeatureSettings settings)
        {
            _materialBlur = materialBlur;
            _materialEffect = materialEffect;

            _settingsDefault = settings;
            _settings = settings.Clone();

            _quadMesh = GenerateQuadMesh();

            SetObstructorFilteringSettings();
            SetOccluderFilteringSettings();

            // 设置 Pass 执行时机。 // Set the execution timing of the Pass.
            renderPassEvent = RenderPassEvent.AfterRenderingTransparents;
        }

        public void Dispose()
        {
            CoreUtils.Destroy(_materialBlur); _materialBlur = null;
            CoreUtils.Destroy(_materialBlurCombineTwo); _materialBlurCombineTwo = null;
            CoreUtils.Destroy(_materialEffect); _materialEffect = null;
            CoreUtils.Destroy(_materialParticleGpu); _materialParticleGpu = null;
            CoreUtils.Destroy(_materialClone); _materialClone = null;
            CoreUtils.Destroy(_materialGrabAsBg); _materialGrabAsBg = null;
            CoreUtils.Destroy(_quadMesh);

            // 释放 RTHandle（_cameraColorRT 由 renderer 持有，勿释放）。 // Release RTHandles (not the renderer-owned camera color).
            _grabAsBgSourceRT?.Release(); _grabAsBgSourceRT = null;
            _liquidParticleRT?.Release(); _liquidParticleRT = null;
            _blurLeftRT?.Release(); _blurLeftRT = null;
            _blurRightRT?.Release(); _blurRightRT = null;
            _blurCoreRT?.Release(); _blurCoreRT = null;
            _blurCombineCoreRT?.Release(); _blurCombineCoreRT = null;
            _blurFinalRT?.Release(); _blurFinalRT = null;
            _obstructorRT?.Release(); _obstructorRT = null;
            _occluderRT?.Release(); _occluderRT = null;
        }

        // ── OnCameraSetup：合并设置 + 分配 RT + 缓存相机颜色目标 ──────────────
        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // 先合并设置，使下面的 blur 描述符使用当前帧的 ScaleFactor。 // Merge settings first so blur desc uses current ScaleFactor.
            UpdateSettings();

            // URP14：相机颜色目标只能在 OnCameraSetup/Execute 中读取，不能在构造/AddRenderPasses 中读。
            _cameraColorRT = renderingData.cameraData.renderer.cameraColorTargetHandle;

            if (!IsValidMat) return;

            // 主纹理描述符（全分辨率 HDR）。 // Main descriptor (full-res HDR).
            var camDesc = renderingData.cameraData.cameraTargetDescriptor;
            RenderTextureDescriptor mainDesc = camDesc;
            mainDesc.msaaSamples = 1;
            mainDesc.depthBufferBits = 0;
            mainDesc.useMipMap = false;
            mainDesc.autoGenerateMips = false;
            mainDesc.graphicsFormat = GraphicsFormat.R16G16B16A16_SFloat; // HDR，色彩混合与扭曲采样需要。

            RenderingUtils.ReAllocateIfNeeded(ref _grabAsBgSourceRT, mainDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("GrabAsBgSource"));
            RenderingUtils.ReAllocateIfNeeded(ref _liquidParticleRT, mainDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("LiquidParticle"));
            RenderingUtils.ReAllocateIfNeeded(ref _obstructorRT, mainDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("Obstructor"));
            RenderingUtils.ReAllocateIfNeeded(ref _occluderRT, mainDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("Occluder"));

            // 模糊描述符（按 ScaleFactor 降采样）。 // Blur descriptor (downscaled by ScaleFactor).
            int sf = (int)_settings.Blur.ScaleFactor;
            RenderTextureDescriptor blurDesc = mainDesc;
            blurDesc.width = Mathf.Max(1, camDesc.width / sf);
            blurDesc.height = Mathf.Max(1, camDesc.height / sf);
            RenderingUtils.ReAllocateIfNeeded(ref _blurLeftRT, blurDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("BlurLeft"));
            RenderingUtils.ReAllocateIfNeeded(ref _blurRightRT, blurDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("BlurRight"));
            RenderingUtils.ReAllocateIfNeeded(ref _blurCoreRT, blurDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("BlurCore"));
            RenderingUtils.ReAllocateIfNeeded(ref _blurCombineCoreRT, blurDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("BlurCombineCore"));
            RenderingUtils.ReAllocateIfNeeded(ref _blurFinalRT, blurDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("BlurFinal"));
        }

        // ── Execute：整条链的命令式编排 ───────────────────────────────────────
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!IsValidMat) return;

            // 若本 Feature（按 nameTag）本帧没有可渲染粒子，则跳过整条链。 // Early-out when nothing to render.
            if (Liquid2DSimulation.GetRenderableAliveCount(_settings.NameTag) == 0) return;

            bool isSceneView = renderingData.cameraData.cameraType == CameraType.SceneView;

            CommandBuffer cmd = CommandBufferPool.Get("Liquid2D");
            try
            {
                // 1. GrabAsBg：一次全屏三角形，双目标 MRT 写背景拷贝 + alpha=0 的粒子底。
                ExecuteGrabAsBg(cmd);

                // 2. 从仿真绘制粒子（CPU DrawMeshInstanced）。Scene 视图直接画到相机颜色。
                cmd.SetRenderTarget(isSceneView ? _cameraColorRT : _liquidParticleRT);
                ExecuteParticles(cmd, ref renderingData);

                // 3. Scene 视图：只画粒子到相机颜色即返回（不做模糊/效果，便于编辑）。
                if (isSceneView) return; // finally 会把已入队的粒子绘制刷入相机颜色。

                // 4. 迭代模糊 + 核心保持。
                RTHandle blurFinal = ExecuteBlur(cmd);

                // 5. 障碍物渲染层纹理。
                ExecuteLayerRenderers(context, cmd, ref renderingData, _obstructorRT, ref _obstructorFilteringSettings);

                // 6. 遮挡物渲染层纹理（仅当渲染层遮罩非 0）。
                bool isHaveOccluder = _occluderFilteringSettings.renderingLayerMask != 0;
                if (isHaveOccluder)
                    ExecuteLayerRenderers(context, cmd, ref renderingData, _occluderRT, ref _occluderFilteringSettings);

                // 7. Liquid2DEffect 合成到相机颜色。
                ExecuteEffect(cmd, ref renderingData, blurFinal, isHaveOccluder);

                // 8. 调试显示 Overlay（在 Effect 之后叠加，Editor+Build 一致）。
                var displays = Liquid2DDebugParticleDisplay.Instances;
                if (displays.Count > 0)
                {
                    cmd.SetRenderTarget(_cameraColorRT);
                    for (int i = 0; i < displays.Count; i++)
                        displays[i].ExecuteDraw(cmd);
                }
            }
            finally
            {
                context.ExecuteCommandBuffer(cmd);
                cmd.Clear();
                CommandBufferPool.Release(cmd);
            }
        }

        // ── GrabAsBg（双目标 MRT）────────────────────────────────────────────
        private void ExecuteGrabAsBg(CommandBuffer cmd)
        {
            _grabMrt[0] = _grabAsBgSourceRT; // SV_Target0：背景拷贝（保留 alpha）。
            _grabMrt[1] = _liquidParticleRT; // SV_Target1：alpha 置 0 的粒子底。
            cmd.SetRenderTarget(_grabMrt, _grabAsBgSourceRT); // 无深度，用 color0 作深度参数占位。
            // 着色器全屏覆盖，无需清屏。 // Shader overwrites every pixel; no clear.

            _mpbGrabAsBg.Clear();
            _mpbGrabAsBg.SetTexture(ShaderIds.MainTexId, _cameraColorRT);
            _mpbGrabAsBg.SetColor(ShaderIds.ColorId, _settings.Blur.BlurBgColor);
            _mpbGrabAsBg.SetFloat(ShaderIds.ColorIntensityId, _settings.Blur.BlurBgColorIntensity);
            cmd.DrawProcedural(Matrix4x4.identity, MaterialGrabAsBg, 0, MeshTopology.Triangles, 3, 1, _mpbGrabAsBg);
        }

        // ── 从仿真绘制粒子（CPU 路径）───────────────────────────────────────
        private void ExecuteParticles(CommandBuffer cmd, ref RenderingData renderingData)
        {
            // GPU 常驻路径：Mode==Gpu 且能取到常驻 GPU 缓冲时，直接 DrawProcedural 读缓冲，绕过 CPU 逐粒子矩阵与回读。
            if (Liquid2DSimulation.Mode == Liquid2DSimulationMode.Gpu
                && Liquid2DSimulation.TryGetRenderBuffers(out var gpuPositions, out var gpuColors, out var gpuRadii,
                    out var gpuTypeIds, out var gpuActive, out _, out _, out int gpuCount, out var gpuDescriptors))
            {
                ExecuteParticlesGpu(cmd, gpuPositions, gpuColors, gpuRadii, gpuTypeIds, gpuActive, gpuCount, gpuDescriptors);
                return;
            }

            // CPU 路径：从仿真 SoA 逐粒子构建矩阵，GPU Instancing 批量绘制。
            Camera cam = renderingData.cameraData.camera;
            GeometryUtility.CalculateFrustumPlanes(cam, _frustumPlanes);

            string nameTag = _settings.NameTag;
            if (!Liquid2DSimulation.TryGetRenderData(out var store, out NativeArray<int> active, out int activeCount,
                    out IReadOnlyList<Liquid2DParticleDescriptor> descriptors))
                return;

            var positions = store.positions;
            var radiiArr = store.radii;
            var colorArr = store.colors;
            var typeArr = store.typeId;

            // 是否需在上传前把 store 的手调 sRGB 色转 linear（对齐 CPU 绘制路径与渐变 LUT 的上传边界）。循环外缓存，避免每粒子查询色彩空间。
            // Whether to convert the store's authored sRGB colors to linear before upload (aligning with the CPU draw path and the gradient LUT's upload boundary). // 上传前に sRGB→linear が要るか。
            bool toLinear = Liquid2DColorSpace.IsLinear;

            for (int t = 0; t < descriptors.Count; t++)
            {
                var d = descriptors[t];
                if (!d || !d.IsValid()) continue;
                var settings = d.RenderSettings;

                // nameTag 门控：空 tag 的粒子被所有 Feature 绘制；有 tag 的仅由同名 Feature 绘制。
                if (!string.IsNullOrEmpty(settings.NameTag) && !settings.NameTag.Equals(nameTag)) continue;

                _mpbParticle.Clear();
                _mpbParticle.SetTexture(ShaderIds.MainTexId, settings.Sprite.texture);

                int count = 0;
                for (int k = 0; k < activeCount; k++)
                {
                    int slot = active[k];
                    if (typeArr[slot] != t) continue; // 按描述符分组。

                    float2 p = positions[slot];
                    float diameter = radiiArr[slot] * 2f * d.RenderScale; // 可视尺寸与物理半径解耦。
                    var center = new Vector3(p.x, p.y, 0f);

                    var bounds = new Bounds(center, new Vector3(diameter, diameter, diameter));
                    if (!GeometryUtility.TestPlanesAABB(_frustumPlanes, bounds)) continue; // 视锥剔除。

                    Matrix4x4 m = Matrix4x4.identity; // 只做缩放+平移（不用 TRS）。
                    m.m00 = diameter; m.m11 = diameter;
                    m.m03 = center.x; m.m13 = center.y; m.m23 = center.z;
                    _matricesCache[count] = m;
                    // store 存的是手调 sRGB 值；线性项目下按 SetColor 的口径转 linear 再上传。 // The store holds authored sRGB values; convert to linear per SetColor in linear projects. // store は sRGB 値。線形項目では linear へ。
                    _colorArrayCache[count] = Liquid2DColorSpace.ToGpuUpload(colorArr[slot], toLinear);
                    count++;

                    if (count == MaxInstancesPerBatch)
                    {
                        _mpbParticle.SetVectorArray(ShaderIds.ColorId, _colorArrayCache);
                        cmd.DrawMeshInstanced(_quadMesh, 0, settings.Material, 0, _matricesCache, count, _mpbParticle);
                        count = 0;
                    }
                }

                if (count > 0)
                {
                    _mpbParticle.SetVectorArray(ShaderIds.ColorId, _colorArrayCache);
                    cmd.DrawMeshInstanced(_quadMesh, 0, settings.Material, 0, _matricesCache, count, _mpbParticle);
                }
            }
        }

        // ── GPU 常驻路径的粒子绘制（DrawProcedural 直读 ComputeBuffer）───────
        private void ExecuteParticlesGpu(CommandBuffer cmd, ComputeBuffer positions, ComputeBuffer colors,
            ComputeBuffer radii, ComputeBuffer typeIds, ComputeBuffer active, int count,
            IReadOnlyList<Liquid2DParticleDescriptor> descriptors)
        {
            if (descriptors == null || count <= 0) return;
            var gpuMat = MaterialParticleGpu;
            if (!gpuMat) return;

            string nameTag = _settings.NameTag;
            for (int t = 0; t < descriptors.Count; t++)
            {
                var d = descriptors[t];
                if (!d || !d.IsValid()) continue;
                var settings = d.RenderSettings;
                if (!string.IsNullOrEmpty(settings.NameTag) && !settings.NameTag.Equals(nameTag)) continue;

                _mpbParticle.Clear();
                _mpbParticle.SetBuffer(ShaderIds.PositionsBuf, positions);
                _mpbParticle.SetBuffer(ShaderIds.ColorsBuf, colors);
                _mpbParticle.SetBuffer(ShaderIds.RadiiBuf, radii);
                _mpbParticle.SetBuffer(ShaderIds.TypeIdsBuf, typeIds);
                _mpbParticle.SetBuffer(ShaderIds.ActiveIdxBuf, active);
                _mpbParticle.SetTexture(ShaderIds.MainTexId, settings.Sprite.texture);
                _mpbParticle.SetInteger(ShaderIds.TargetType, t);
                _mpbParticle.SetFloat(ShaderIds.RenderScale, d.RenderScale);

                // 6 顶点/实例（两三角拼四边形），实例数 = 活动粒子数；Shader 内按 typeId 剔除非本类。
                cmd.DrawProcedural(Matrix4x4.identity, gpuMat, 0, MeshTopology.Triangles, 6, count, _mpbParticle);
            }
        }

        // ── 迭代模糊 + 核心保持，返回最终模糊 RT ─────────────────────────────
        private RTHandle ExecuteBlur(CommandBuffer cmd)
        {
            int iterations = _settings.Blur.Iterations;
            if (iterations <= 0)
                return _liquidParticleRT; // 不模糊：Effect 直接采样粒子纹理。

            // 种子：把全分辨率粒子纹理拷进（降采样的）第一张模糊 RT。
            Blitter.BlitCameraTexture(cmd, _liquidParticleRT, _blurLeftRT);

            int coreKeepIteration = Mathf.Clamp(
                (int)(iterations * (1 - _settings.Blur.CoreKeepIntensity)), 1, iterations - 1);
            bool coreKeep = coreKeepIteration < iterations;

            for (int i = 0; i < iterations; i++)
            {
                RTHandle src = (i % 2 == 0) ? _blurLeftRT : _blurRightRT;
                RTHandle dst = (i % 2 == 0) ? _blurRightRT : _blurLeftRT;
                ExecuteBlurPass(cmd, src, dst, i);

                if (coreKeep && i == coreKeepIteration)
                {
                    // 关键：快照必须读第 i 次迭代实际写入的 RT（偶→right，奇→left）。
                    RTHandle iterResult = (i % 2 == 0) ? _blurRightRT : _blurLeftRT;
                    Blitter.BlitCameraTexture(cmd, iterResult, _blurCoreRT);
                }
            }

            RTHandle blurMain = ((iterations - 1) % 2 == 0) ? _blurRightRT : _blurLeftRT;

            if (!coreKeep) return blurMain;

            // 合并 blurMain + blurCore → combineCore，再做一次模糊融合 → blurFinal。
            _mpbCombineCore.Clear();
            _mpbCombineCore.SetTexture(ShaderIds.MainTexId, blurMain);
            _mpbCombineCore.SetTexture(ShaderIds.SecondTex, _blurCoreRT);
            cmd.SetRenderTarget(_blurCombineCoreRT);
            cmd.DrawProcedural(Matrix4x4.identity, MaterialBlurCombineTwo, 0, MeshTopology.Triangles, 3, 1, _mpbCombineCore);

            ExecuteBlurPass(cmd, _blurCombineCoreRT, _blurFinalRT, 0);
            return _blurFinalRT;
        }

        private void ExecuteBlurPass(CommandBuffer cmd, RTHandle src, RTHandle dst, int iteration)
        {
            float offset = (0.5f + iteration * _settings.Blur.BlurSpread) * 3f / (int)_settings.Blur.ScaleFactor;
            _mpbBlur.Clear();
            _mpbBlur.SetTexture(ShaderIds.MainTexId, src);
            _mpbBlur.SetFloat(ShaderIds.BlurOffsetId, offset);
            SetKeyword(_materialBlur, "_IGNORE_BG_COLOR", _settings.Blur.IgnoreBgColor);
            cmd.SetRenderTarget(dst);
            cmd.DrawProcedural(Matrix4x4.identity, _materialBlur, 0, MeshTopology.Triangles, 3, 1, _mpbBlur);
        }

        // ── 障碍物/遮挡物渲染层纹理：DrawRenderers（须先刷 cmd）────────────────
        private void ExecuteLayerRenderers(ScriptableRenderContext context, CommandBuffer cmd,
            ref RenderingData renderingData, RTHandle target, ref FilteringSettings filtering)
        {
            // 绑定并清屏（持久 RTHandle 会保留上一帧内容，未清则残留成移动的黑块）。
            CoreUtils.SetRenderTarget(cmd, target, ClearFlag.Color, Color.clear);
            context.ExecuteCommandBuffer(cmd); // 刷出 SetRenderTarget/Clear，使 DrawRenderers 落到该目标。
            cmd.Clear();

            var sortFlags = renderingData.cameraData.defaultOpaqueSortFlags;
            // CreateDrawingSettings 是 ScriptableRenderPass 继承的 protected 方法（URP14 保证可访问）。
            var drawSettings = CreateDrawingSettings(_shaderTagId, ref renderingData, sortFlags);
            context.DrawRenderers(renderingData.cullResults, ref drawSettings, ref filtering);
        }

        // ── Liquid2DEffect 合成 ───────────────────────────────────────────────
        private void ExecuteEffect(CommandBuffer cmd, ref RenderingData renderingData, RTHandle blurFinal, bool isHaveOccluder)
        {
            cmd.SetRenderTarget(_cameraColorRT); // 合成到相机颜色（不清屏，叠加在实景上）。

            _mpbEffect.Clear();
            _mpbEffect.SetTexture(ShaderIds.MainTexId, blurFinal);
            _mpbEffect.SetTexture(ShaderIds.ObstructorTex, _obstructorRT);
            _mpbEffect.SetFloat(ShaderIds.Cutoff, _settings.Cutoff);
            _mpbEffect.SetTexture(ShaderIds.BackgroundTex, _grabAsBgSourceRT);

            // 遮挡物关键字。 // Occluder keyword.
            SetKeyword(_materialEffect, "_OCCLUDER_ENABLE", isHaveOccluder);
            if (isHaveOccluder) _mpbEffect.SetTexture(ShaderIds.OccluderTex, _occluderRT);

            // 透明度。 // Opacity.
            SetKeyword(_materialEffect, "_OPACITY_MULTIPLY", _settings.OpacityMode == EOpacityMode.Multiply);
            SetKeyword(_materialEffect, "_OPACITY_REPLACE", _settings.OpacityMode == EOpacityMode.Replace);
            _mpbEffect.SetFloat(ShaderIds.OpacityValue, _settings.OpacityValue);

            // 边缘。 // Edge.
            SetKeyword(_materialEffect, "_EDGE_ENABLE", _settings.Edge.Enable);
            if (_settings.Edge.Enable)
            {
                float cutoff = _settings.Cutoff;
                float edgeRange = _settings.Edge.EdgeRange;
                float edgeIntensity = _settings.Edge.EdgeIntensity;
                float edgeEnd = cutoff + edgeRange * (1 - cutoff);
                float edgeMixStart = Mathf.Lerp(cutoff, edgeEnd, edgeIntensity * 0.999f);
                _mpbEffect.SetFloat(ShaderIds.EdgeEnd, edgeEnd);
                _mpbEffect.SetFloat(ShaderIds.EdgeMixStart, edgeMixStart);
                _mpbEffect.SetColor(ShaderIds.EdgeColor, _settings.Edge.EdgeColor);
                bool lerpBlend = _settings.Edge.BlendType == Liquid2DRenderFeatureSettings.EdgeSettings.EdgeBlendType.Lerp;
                SetKeyword(_materialEffect, "_EDGE_BLEND_SA_OMSA", !lerpBlend);
                SetKeyword(_materialEffect, "_EDGE_BLEND_LERP", lerpBlend);
            }

            // 像素化。 // Pixelation.
            SetKeyword(_materialEffect, "_PIXEL_ENABLE", _settings.Pixel.Enable);
            SetKeyword(_materialEffect, "_PIXEL_BG", _settings.Pixel.Enable && _settings.Pixel.PixelBg); // 无条件解析，避免关键字残留。
            if (_settings.Pixel.Enable)
            {
                var targetDesc = renderingData.cameraData.cameraTargetDescriptor;
                float aspect = (float)targetDesc.width / targetDesc.height;
                int pixelWidthCount = targetDesc.width / _settings.Pixel.PixelSize;
                Vector2 pixelSize = new Vector2(pixelWidthCount, pixelWidthCount / aspect);
                _mpbEffect.SetVector(ShaderIds.PixelSize, pixelSize);
            }

            // 扭曲（不透明 Replace 时禁用）。 // Distortion (disabled when opaque-replace).
            bool distortEnable = _settings.Distort.Enable
                && !(_settings.OpacityMode == EOpacityMode.Replace && _settings.OpacityValue >= 1f);
            SetKeyword(_materialEffect, "_DISTORT_ENABLE", distortEnable);
            if (distortEnable)
            {
                _mpbEffect.SetFloat(ShaderIds.Magnitude, _settings.Distort.Magnitude);
                _mpbEffect.SetFloat(ShaderIds.Frequency, _settings.Distort.Frequency);
                _mpbEffect.SetFloat(ShaderIds.Amplitude, _settings.Distort.Amplitude);
                _mpbEffect.SetVector(ShaderIds.DistortSpeed, _settings.Distort.DistortSpeed);
                _mpbEffect.SetVector(ShaderIds.DistortTimeFactors, _settings.Distort.DistortTimeFactors);
                _mpbEffect.SetFloat(ShaderIds.NoiseCoordOffset, _settings.Distort.NoiseCoordOffset);
            }

            cmd.DrawProcedural(Matrix4x4.identity, _materialEffect, 0, MeshTopology.Triangles, 3, 1, _mpbEffect);
        }

        // ── Volume 合并 ───────────────────────────────────────────────────────
        private void UpdateSettings()
        {
            if (!IsValidMat) return;

            Liquid2DVolume volumeComponent = VolumeManager.instance.stack.GetComponent<Liquid2DVolume>();
            Liquid2DVolumeData volumeData = null;
            bool isActive = volumeComponent
                && volumeComponent.IsActive.value
                && volumeComponent.Liquid2DVolumeDataList.overrideState
                && volumeComponent.GetData(_settings.NameTag, out volumeData)
                && volumeData != null && volumeData.IsActive;

            _settings.CopyFrom(isActive ? volumeData : _settingsDefault);

            if (_obstructorFilteringSettings.renderingLayerMask != _settings.ObstructorRenderingLayerMask)
                SetObstructorFilteringSettings();
            if (_occluderFilteringSettings.renderingLayerMask != _settings.OccluderRenderingLayerMask)
                SetOccluderFilteringSettings();
        }

        private void SetObstructorFilteringSettings()
        {
            _obstructorFilteringSettings = new FilteringSettings(RenderQueueRange.all, ~0, _settings.ObstructorRenderingLayerMask);
        }

        private void SetOccluderFilteringSettings()
        {
            _occluderFilteringSettings = new FilteringSettings(RenderQueueRange.all, ~0, _settings.OccluderRenderingLayerMask);
        }

        // ── 工具 ─────────────────────────────────────────────────────────────
        private static void SetKeyword(Material material, string keyword, bool enable)
        {
            if (material.IsKeywordEnabled(keyword) == enable) return;
            if (enable) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        private Mesh GenerateQuadMesh()
        {
            var mesh = new Mesh
            {
                vertices = new[]
                {
                    new Vector3(-0.5f, -0.5f, 0),
                    new Vector3(0.5f, -0.5f, 0),
                    new Vector3(0.5f, 0.5f, 0),
                    new Vector3(-0.5f, 0.5f, 0)
                },
                uv = new[]
                {
                    new Vector2(0, 0),
                    new Vector2(1, 0),
                    new Vector2(1, 1),
                    new Vector2(0, 1)
                },
                triangles = new[] { 0, 1, 2, 2, 3, 0 }
            };
            return mesh;
        }

        // RT 命名缓存（按 nameTag）。 // RT name cache (by nameTag).
        private readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>();
        private string _nameCacheTag;

        private string GetName(string name)
        {
            if (_nameCacheTag != _settings.NameTag)
            {
                _nameCache.Clear();
                _nameCacheTag = _settings.NameTag;
            }
            if (!_nameCache.TryGetValue(name, out var full))
            {
                full = $"[Liquid 2D] [{_settings.NameTag}] {name}";
                _nameCache[name] = full;
            }
            return full;
        }
    }
}
#endif

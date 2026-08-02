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
            internal static readonly int OpacityTex = Shader.PropertyToID("_OpacityTex"); // 独立透明度场源纹理(RG)：模糊/合成的第二源。 // Independent opacity field source (RG): 2nd source for blur/composite. // 独立透明度場ソース(RG)。
            internal static readonly int ColorId = Shader.PropertyToID("_Color");
            internal static readonly int Opacity = Shader.PropertyToID("_Opacity"); // CPU 路径逐实例最终透明度倍率 O。 // CPU path per-instance opacity multiplier O. // CPU 逐実例 O。
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

            // GPU 渐变颜色映射相关。 // GPU gradient color-mapping related. // GPU 渐変カラーマッピング関連。
            internal static readonly int RenderScalarsBuf = Shader.PropertyToID("_RenderScalars"); // 平滑标量 float3 (x=密度, y=速度, z=Impact 冲击泡沫累加器 F)。 // smoothed scalars float3 (x=density, y=speed, z=Impact accumulator F). // 平滑スカラー。
            internal static readonly int GradientLut = Shader.PropertyToID("_GradientLut");
            internal static readonly int OpacityLut = Shader.PropertyToID("_OpacityLut"); // GPU 路径透明度 LUT。 // GPU path opacity LUT. // GPU 透明度 LUT。
            internal static readonly int UseGradient = Shader.PropertyToID("_UseGradient");
            internal static readonly int GradientSource = Shader.PropertyToID("_GradientSource");
            internal static readonly int GradientSpeedMin = Shader.PropertyToID("_SpeedMin");
            internal static readonly int GradientSpeedMax = Shader.PropertyToID("_GradientSpeedMax");
            internal static readonly int RestDensity = Shader.PropertyToID("_RestDensity");
            internal static readonly int FoamStart = Shader.PropertyToID("_FoamStart");
            internal static readonly int FoamEnd = Shader.PropertyToID("_FoamEnd");
        }

        private static readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");

        // 独立透明度场关键字（粒子/模糊/合成三处 shader 共用）。开启时绑定第二渲染目标(RG)：R=Σ覆盖×O, G=Σ覆盖。
        // Independent opacity field keyword (shared by the particle/blur/composite shaders). When on, a second RG target is bound.
        // 独立透明度場キーワード（粒子/ブラー/合成の3シェーダーで共有）。
        private const string OpacityFieldKeyword = "_OPACITY_FIELD";

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
        // CPU 路径逐实例最终透明度倍率 O 缓存（仅独立透明度场启用时填充）。 // CPU path per-instance opacity multiplier O cache (filled only when the opacity field is active). // CPU 逐実例 O キャッシュ。
        private readonly float[] _opacityArrayCache = new float[MaxInstancesPerBatch];
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

        // ── 独立透明度场 RTHandle（RG，仅有非恒1透明度曲线的 Gradient 粒子时按需分配）──
        // Independent opacity field RTHandles (RG; allocated on demand only when a Gradient particle has a non-constant-1 opacity curve).
        private RTHandle _liquidOpacityRT;    // 粒子加性累加目标(RG)：R=Σ覆盖×O, G=Σ覆盖，每帧清零。
        private RTHandle _blurLeftORT;        // 透明度场模糊 ping(RG)。
        private RTHandle _blurRightORT;       // 透明度场模糊 pong(RG)。
        private RTHandle _blurFinalORT;       // 透明度场最终模糊(RG)。

        // 主/模糊纹理描述符缓存（OnCameraSetup 记录，供 Execute 按需分配 RG 透明度场 RT）。 // Cached main/blur descriptors (recorded in OnCameraSetup) for on-demand RG opacity RT allocation. // 記述子キャッシュ。
        private RenderTextureDescriptor _mainDesc;
        private RenderTextureDescriptor _blurDesc;

        // MRT 目标数组，复用避免每帧分配。 // Reused MRT target arrays.
        private readonly RenderTargetIdentifier[] _grabMrt = new RenderTargetIdentifier[2];
        private readonly RenderTargetIdentifier[] _particleMrt = new RenderTargetIdentifier[2]; // 粒子绘制 MRT[颜色, 透明度场(RG)]。 // particle MRT[color, opacity field]. // 粒子 MRT。
        private readonly RenderTargetIdentifier[] _blurMrt = new RenderTargetIdentifier[2];     // 模糊 MRT[颜色, 透明度场(RG)]。 // blur MRT[color, opacity field]. // ブラー MRT。

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

            // 独立透明度场 RT。 // Independent opacity field RTs. // 独立透明度場 RT。
            _liquidOpacityRT?.Release(); _liquidOpacityRT = null;
            _blurLeftORT?.Release(); _blurLeftORT = null;
            _blurRightORT?.Release(); _blurRightORT = null;
            _blurFinalORT?.Release(); _blurFinalORT = null;
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

            // 记录描述符，供 Execute 判定需要时按需分配 RG 透明度场 RT（避免非渐变透明度场景常驻多余显存）。
            // Record descriptors so Execute can allocate the RG opacity-field RTs on demand (avoids keeping extra VRAM when the opacity field is unused).
            // 記述子を記録し、Execute で必要時のみ RG 透明度場 RT を確保。
            _mainDesc = mainDesc;
            _blurDesc = blurDesc;
        }

        // ── Execute：整条链的命令式编排 ───────────────────────────────────────
        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (!IsValidMat) return;

            // 若本 Feature（按 nameTag）本帧没有可渲染粒子，则跳过整条链。 // Early-out when nothing to render.
            if (Liquid2DSimulation.GetRenderableAliveCount(_settings.NameTag) == 0) return;

            // 预热渐变 LUT（主线程创建纹理），并返回本 Feature 本帧是否有含非恒1透明度曲线的 Gradient 粒子（决定是否启用独立透明度场链路）。
            // Warm up gradient LUTs (creates textures on the main thread) and return whether any Gradient particle this feature draws
            // has a non-constant-1 opacity curve (drives the independent-opacity-field path). // 渐変 LUT を予熱、独立透明度場の要否を返す。
            bool opacityFieldActive = PrepareGradientLuts();

            bool isSceneView = renderingData.cameraData.cameraType == CameraType.SceneView;

            // 独立透明度场：仅在有非恒1透明度曲线的 Gradient 粒子且非场景视图时启用（场景视图直接绘制到相机、无后续模糊/合成，不需要）。
            // Enabled only when a Gradient particle has a non-constant-1 opacity curve and this is not the scene view. // 独立透明度場の有効判定。
            bool useOpacityField = opacityFieldActive && !isSceneView;
            if (useOpacityField) EnsureOpacityRTs();

            CommandBuffer cmd = CommandBufferPool.Get("Liquid2D");
            try
            {
                // 1. GrabAsBg：一次全屏三角形，双目标 MRT 写背景拷贝 + alpha=0 的粒子底。
                ExecuteGrabAsBg(cmd);

                // 2. 从仿真绘制粒子（CPU DrawMeshInstanced）。Scene 视图直接画到相机颜色。
                if (useOpacityField)
                {
                    // 独立透明度场(RG)加性累加，须每帧清零；随后绑定 MRT[粒子色, 透明度场]。粒子色目标 _liquidParticleRT 已由 GrabAsBg 写入 alpha=0 底，不清。
                    // The opacity field (RG) accumulates additively and must be cleared each frame; then bind MRT[color, opacity]. The color target keeps its GrabAsBg base and is not cleared.
                    // 透明度場(RG)は加算累加のため毎フレーム清零、その後 MRT を束縛。色目標は清零しない。
                    CoreUtils.SetRenderTarget(cmd, _liquidOpacityRT, ClearFlag.Color, Color.clear);
                    _particleMrt[0] = _liquidParticleRT;
                    _particleMrt[1] = _liquidOpacityRT;
                    cmd.SetRenderTarget(_particleMrt, _liquidParticleRT);
                }
                else
                {
                    cmd.SetRenderTarget(isSceneView ? _cameraColorRT : _liquidParticleRT);
                }
                ExecuteParticles(cmd, ref renderingData, useOpacityField);

                // 3. Scene 视图：只画粒子到相机颜色即返回（不做模糊/效果，便于编辑）。
                if (isSceneView) return; // finally 会把已入队的粒子绘制刷入相机颜色。

                // 4. 迭代模糊 + 核心保持（独立透明度场并行走完同一条模糊链）。
                RTHandle blurFinal = ExecuteBlur(cmd, useOpacityField, out RTHandle blurFinalO);

                // 5. 障碍物渲染层纹理。
                ExecuteLayerRenderers(context, cmd, ref renderingData, _obstructorRT, ref _obstructorFilteringSettings);

                // 6. 遮挡物渲染层纹理（仅当渲染层遮罩非 0）。
                bool isHaveOccluder = _occluderFilteringSettings.renderingLayerMask != 0;
                if (isHaveOccluder)
                    ExecuteLayerRenderers(context, cmd, ref renderingData, _occluderRT, ref _occluderFilteringSettings);

                // 7. Liquid2DEffect 合成到相机颜色。
                ExecuteEffect(cmd, ref renderingData, blurFinal, isHaveOccluder, useOpacityField, blurFinalO);

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

        // ── 预热渐变 LUT + 判定是否需要独立透明度场 ──────────────────────────
        /// <summary>
        /// 预热本 Feature 会绘制的 Gradient 模式描述符的渐变 LUT（主线程创建纹理）。仅烘焙尚未烘焙/已失效者，无粒子或非 Gradient 时零开销。
        /// 返回值：本 Feature 本帧是否有含非恒1透明度曲线的 Gradient 粒子（决定是否启用独立透明度场 MRT 链路）。
        /// Warm up gradient LUTs for the Gradient-mode descriptors this feature will draw (creates textures on the main thread).
        /// Returns whether this feature draws any Gradient particle with a non-constant-1 opacity curve (drives the opacity-field path).
        /// 本 Feature が描画する Gradient 記述子の LUT を予熱し、独立透明度場が要るかを返す。
        /// </summary>
        private bool PrepareGradientLuts()
        {
            if (!Liquid2DSimulation.TryGetRenderData(out _, out _, out _, out var descriptors) || descriptors == null)
                return false;
            string nameTag = _settings.NameTag;
            bool opacityActive = false;
            for (int t = 0; t < descriptors.Count; t++)
            {
                var d = descriptors[t];
                if (!d || !d.IsValid()) continue;
                var rs = d.RenderSettings;
                if (rs.ColorMode != EParticleColorMode.Gradient) continue;
                // 与绘制时一致的 nameTag 门控。 // Same nameTag gate as at draw time. // 描画時と同じ nameTag ゲート。
                if (!string.IsNullOrEmpty(rs.NameTag) && !rs.NameTag.Equals(nameTag)) continue;
                rs.EnsureGradientLut();
                // 本 Feature 会绘制的某个 Gradient 描述符含非恒1的透明度曲线 → 需要独立透明度场。
                // A Gradient descriptor this feature will draw has a non-constant-1 opacity curve → needs the opacity field.
                if (rs.GradientOpacityActive) opacityActive = true;
            }
            return opacityActive;
        }

        // ── 按需分配独立透明度场 RG 纹理（沿用主/模糊描述符尺寸，格式改为 RG 半精度）─────
        private void EnsureOpacityRTs()
        {
            // 全分辨率透明度场累加目标(RG)：R=Σ覆盖×O, G=Σ覆盖。 // Full-res opacity accumulation target (RG). // 全解像度 RG。
            RenderTextureDescriptor opDesc = _mainDesc;
            opDesc.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref _liquidOpacityRT, opDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("LiquidOpacity"));

            // 降采样透明度场模糊对(RG，ping-pong)与最终模糊图。 // Downscaled opacity blur pair (RG) + final. // 下採样 RG。
            RenderTextureDescriptor opBlurDesc = _blurDesc;
            opBlurDesc.graphicsFormat = GraphicsFormat.R16G16_SFloat;
            RenderingUtils.ReAllocateIfNeeded(ref _blurLeftORT, opBlurDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("BlurLeftO"));
            RenderingUtils.ReAllocateIfNeeded(ref _blurRightORT, opBlurDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("BlurRightO"));
            RenderingUtils.ReAllocateIfNeeded(ref _blurFinalORT, opBlurDesc, FilterMode.Bilinear, TextureWrapMode.Clamp, name: GetName("BlurFinalO"));
        }

        // ── 从仿真绘制粒子（CPU 路径）───────────────────────────────────────
        private void ExecuteParticles(CommandBuffer cmd, ref RenderingData renderingData, bool useOpacityField)
        {
            // GPU 常驻路径：Mode==Gpu 且能取到常驻 GPU 缓冲时，直接 DrawProcedural 读缓冲，绕过 CPU 逐粒子矩阵与回读。
            // 渲染读平滑后的标量 float3 (x=密度, y=速度, z=Impact 冲击泡沫累加器 F)，供渐变采样；原始 velocity 丢弃。
            if (Liquid2DSimulation.Mode == Liquid2DSimulationMode.Gpu
                && Liquid2DSimulation.TryGetRenderBuffers(out var gpuPositions, out var gpuColors, out var gpuRadii,
                    out var gpuTypeIds, out var gpuActive, out _, out var gpuScalars, out int gpuCount, out var gpuDescriptors))
            {
                ExecuteParticlesGpu(cmd, gpuPositions, gpuColors, gpuRadii, gpuTypeIds, gpuActive, gpuScalars, gpuCount, gpuDescriptors, useOpacityField);
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
            var speedArr = store.renderSpeeds; // 渐变 Speed 用（平滑速度）。 // for gradient Speed (smoothed speed). // Speed 用（平滑）。
            var densArr = store.densities;     // 渐变 Foam 用（平滑密度，纯密度亏空静态贴图）。 // for gradient Foam (smoothed density). // Foam 用（平滑）。
            var foamArr = store.renderFoam;    // 渐变 Impact 用（冲击泡沫累加器 F，求解器每帧算好）。 // for gradient Impact (impact-foam accumulator F). // Impact 用。

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

                // 独立透明度场：切换本描述符材质的 _OPACITY_FIELD，须与 Pass 绑定的渲染目标数一致（否则 shader 输出与附件不匹配）。
                // Toggle _OPACITY_FIELD on this descriptor's material; must match the render-target count the Pass bound. // 材質の _OPACITY_FIELD を切替。
                if (settings.Material) SetKeyword(settings.Material, OpacityFieldKeyword, useOpacityField);

                // 渐变颜色映射参数（逐描述符预算，循环内每粒子按 t 采 CPU LUT 得色）。 // Gradient params (per descriptor; each particle samples the CPU LUT by t). // 渐変パラメータ。
                bool gradient = settings.ColorMode == EParticleColorMode.Gradient;
                var gradientSource = settings.GradientSource;
                float speedMin = settings.GradientSpeedMin;
                // 速度重映射：t = (speed − Min) / (Max − Min)，裁剪 0..1。低于 Min 视作 0（缓慢移动不出色/泡）。 // Speed remap; below Min → 0. // 速度リマップ。
                float speedRangeInv = 1f / Mathf.Max(1e-4f, settings.GradientSpeedMax - settings.GradientSpeedMin);
                // 该类粒子静止密度 = 全局目标密度 × 材质密度倍率（与求解器 rho0 一致），用于把 SPH 密度归一化为密度比。 // rest density = global target × material scale. // 静止密度。
                float restDensity = Mathf.Max(1e-4f, Liquid2DSimulation.Params.TargetDensity *
                    (d.Material != null ? Mathf.Max(0.01f, d.Material.TargetDensityScale) : 1f));
                float foamStart = settings.GradientFoamStart;
                float foamRangeInv = 1f / Mathf.Max(1e-4f, settings.GradientFoamStart - settings.GradientFoamEnd);
                // 渐变 CPU LUT：PrepareGradientLuts 已预热，这里每描述符取一次数组直接按索引查，避免每粒子调用 EnsureGradientLut（含 Unity 对象判空）的开销。
                // Gradient CPU LUTs: warmed above; fetch the arrays once per descriptor and index directly. // 記述子ごとに一度取得。
                Color[] cpuGradientLut = gradient ? settings.GetGradientLutCpu() : null;
                float[] cpuOpacityLut = gradient && useOpacityField ? settings.GetOpacityLutCpu() : null;

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
                    // 颜色：Gradient 模式按每粒子标量采 CPU LUT（绕过运行时混色）；否则用 store 的每粒子色。
                    // Color: Gradient mode samples the CPU LUT by a per-particle scalar; otherwise the store's per-particle color. // 色：Gradient は LUT サンプル、否則は store 色。
                    if (gradient)
                    {
                        float tt;
                        if (gradientSource == EGradientColorSource.Speed)
                        {
                            tt = math.saturate((speedArr[slot] - speedMin) * speedRangeInv); // 速度重映射。 // remapped speed. // 速度リマップ。
                        }
                        else if (gradientSource == EGradientColorSource.Density)
                        {
                            // Density（=1）：纯密度亏空静态贴图。 // Density: pure density-deficit static map. // 純密度不足。
                            float ratio = densArr[slot] / restDensity;
                            tt = math.saturate((foamStart - ratio) * foamRangeInv);
                        }
                        else
                        {
                            // DensityWithImpact（=2）/ DensityWithSpeed（=3）/ Impact（=4）：求解器算好的动态泡沫累加器 F。 // solver's dynamic-foam accumulator F. // 動的泡累加器 F。
                            tt = foamArr[slot];
                        }
                        // 每粒子只算一次 LUT 索引，颜色与透明度共用（GradientLutIndex 内部已 saturate(t)）。 // One LUT index per particle, shared by color and opacity. // 索引を1回だけ算出。
                        int lutIdx = Liquid2DParticleRenderSettings.GradientLutIndex(tt);
                        Color gc = cpuGradientLut[lutIdx];
                        _colorArrayCache[count] = new Vector4(gc.r, gc.g, gc.b, gc.a);
                        // 透明度：按同一索引取 CPU 透明度 LUT。 // Opacity: index the CPU opacity LUT with the same index. // 透明度：同じ索引で CPU 透明度 LUT。
                        if (useOpacityField) _opacityArrayCache[count] = cpuOpacityLut[lutIdx];
                    }
                    else
                    {
                        // store 存的是手调 sRGB 值；线性项目下按 SetColor 的口径转 linear 再上传（渐变分支的 LUT 已烘焙为上传值，不走这里）。
                        // The store holds authored sRGB values; convert to linear per SetColor in linear projects (the gradient branch's LUT is already baked to upload values). // store は sRGB 値。
                        _colorArrayCache[count] = Liquid2DColorSpace.ToGpuUpload(colorArr[slot], toLinear);
                        // 非渐变粒子透明度倍率恒 1（不改变最终透明度）。 // Non-gradient particles: opacity multiplier is a constant 1. // 非渐変は O=1。
                        if (useOpacityField) _opacityArrayCache[count] = 1f;
                    }
                    count++;

                    if (count == MaxInstancesPerBatch)
                    {
                        _mpbParticle.SetVectorArray(ShaderIds.ColorId, _colorArrayCache);
                        if (useOpacityField) _mpbParticle.SetFloatArray(ShaderIds.Opacity, _opacityArrayCache);
                        cmd.DrawMeshInstanced(_quadMesh, 0, settings.Material, 0, _matricesCache, count, _mpbParticle);
                        count = 0;
                    }
                }

                if (count > 0)
                {
                    _mpbParticle.SetVectorArray(ShaderIds.ColorId, _colorArrayCache);
                    if (useOpacityField) _mpbParticle.SetFloatArray(ShaderIds.Opacity, _opacityArrayCache);
                    cmd.DrawMeshInstanced(_quadMesh, 0, settings.Material, 0, _matricesCache, count, _mpbParticle);
                }
            }
        }

        // ── GPU 常驻路径的粒子绘制（DrawProcedural 直读 ComputeBuffer）───────
        private void ExecuteParticlesGpu(CommandBuffer cmd, ComputeBuffer positions, ComputeBuffer colors,
            ComputeBuffer radii, ComputeBuffer typeIds, ComputeBuffer active, ComputeBuffer renderScalars, int count,
            IReadOnlyList<Liquid2DParticleDescriptor> descriptors, bool useOpacityField)
        {
            if (descriptors == null || count <= 0) return;
            var gpuMat = MaterialParticleGpu;
            if (!gpuMat) return;

            // 独立透明度场：GPU 路径为共享材质，关键字整批统一切换（须与 Pass 绑定的渲染目标数一致）。 // Shared GPU material: toggle the keyword once for the whole batch. // 共有材質のためバッチ一括切替。
            SetKeyword(gpuMat, OpacityFieldKeyword, useOpacityField);

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

                // 渐变颜色映射：绑定速度/密度缓冲与 LUT，shader 内按每粒子标量采样（绕过 store 颜色）。标量缓冲恒绑定（GPU 模式下必有效），
                // _UseGradient=0 时 shader 不会真正采样。 // Gradient mapping: bind the scalar buffer + LUT; the shader samples by a per-particle scalar. // 渐変マッピング。
                bool gradient = settings.ColorMode == EParticleColorMode.Gradient && renderScalars != null;
                _mpbParticle.SetInteger(ShaderIds.UseGradient, gradient ? 1 : 0);
                if (renderScalars != null) _mpbParticle.SetBuffer(ShaderIds.RenderScalarsBuf, renderScalars);
                if (gradient)
                {
                    var lut = settings.GetGradientLut();
                    _mpbParticle.SetTexture(ShaderIds.GradientLut, lut ? lut : Texture2D.blackTexture);
                    _mpbParticle.SetInteger(ShaderIds.GradientSource, (int)settings.GradientSource);
                    _mpbParticle.SetFloat(ShaderIds.GradientSpeedMin, settings.GradientSpeedMin);
                    _mpbParticle.SetFloat(ShaderIds.GradientSpeedMax, settings.GradientSpeedMax);
                    // 该类粒子静止密度 = 全局目标密度 × 材质密度倍率（与 compute 的 rho0 一致）。 // rest density = global target × material scale. // 静止密度。
                    float restDensity = Mathf.Max(1e-4f, Liquid2DSimulation.Params.TargetDensity *
                        (d.Material != null ? Mathf.Max(0.01f, d.Material.TargetDensityScale) : 1f));
                    _mpbParticle.SetFloat(ShaderIds.RestDensity, restDensity);
                    _mpbParticle.SetFloat(ShaderIds.FoamStart, settings.GradientFoamStart);
                    _mpbParticle.SetFloat(ShaderIds.FoamEnd, settings.GradientFoamEnd);
                    // 独立透明度场：绑定透明度 LUT（shader 内按同一 t 采样得 O）。 // Opacity field: bind the opacity LUT. // 透明度 LUT を束縛。
                    if (useOpacityField)
                    {
                        var olut = settings.GetOpacityLut();
                        _mpbParticle.SetTexture(ShaderIds.OpacityLut, olut ? olut : Texture2D.blackTexture);
                    }
                }
                else
                {
                    // 非渐变仍绑定占位 LUT，避免采样器未绑定告警（_UseGradient=0 时不会真正采样）。 // Bind a placeholder LUT to avoid unbound-sampler warnings. // 占位 LUT。
                    _mpbParticle.SetTexture(ShaderIds.GradientLut, Texture2D.blackTexture);
                    if (useOpacityField) _mpbParticle.SetTexture(ShaderIds.OpacityLut, Texture2D.blackTexture);
                }

                // 6 顶点/实例（两三角拼四边形），实例数 = 活动粒子数；Shader 内按 typeId 剔除非本类。
                cmd.DrawProcedural(Matrix4x4.identity, gpuMat, 0, MeshTopology.Triangles, 6, count, _mpbParticle);
            }
        }

        // ── 迭代模糊 + 核心保持，返回最终模糊 RT ─────────────────────────────
        // useOpacityField 时透明度场(RG)与颜色图并行走完同一条模糊链（真 MRT），blurFinalO 输出最终透明度图供合成 O=R/G。
        // When useOpacityField, the opacity field (RG) is blurred alongside the color texture (true MRT); blurFinalO is the final opacity image.
        private RTHandle ExecuteBlur(CommandBuffer cmd, bool useOpacityField, out RTHandle blurFinalO)
        {
            blurFinalO = default;
            int iterations = _settings.Blur.Iterations;
            if (iterations <= 0)
            {
                // 不模糊：Effect 直接采样粒子纹理与原始透明度场。 // No blur: Effect samples the particle texture and raw opacity field directly. // ブラーなし。
                if (useOpacityField) blurFinalO = _liquidOpacityRT;
                return _liquidParticleRT;
            }

            // 种子：把全分辨率粒子纹理拷进（降采样的）第一张模糊 RT。透明度场同样下采样拷贝到第一张透明度模糊图(RG)。
            Blitter.BlitCameraTexture(cmd, _liquidParticleRT, _blurLeftRT);
            if (useOpacityField)
                Blitter.BlitCameraTexture(cmd, _liquidOpacityRT, _blurLeftORT);

            int coreKeepIteration = Mathf.Clamp(
                (int)(iterations * (1 - _settings.Blur.CoreKeepIntensity)), 1, iterations - 1);
            bool coreKeep = coreKeepIteration < iterations;

            for (int i = 0; i < iterations; i++)
            {
                RTHandle src = (i % 2 == 0) ? _blurLeftRT : _blurRightRT;
                RTHandle dst = (i % 2 == 0) ? _blurRightRT : _blurLeftRT;
                // 透明度场(RG)与颜色图共用同一批模糊偏移一起模糊。 // Opacity field (RG) blurred with the same offsets. // 透明度場は同時ブラー。
                RTHandle srcO = (i % 2 == 0) ? _blurLeftORT : _blurRightORT;
                RTHandle dstO = (i % 2 == 0) ? _blurRightORT : _blurLeftORT;
                ExecuteBlurPass(cmd, src, dst, i, useOpacityField, srcO, dstO);

                if (coreKeep && i == coreKeepIteration)
                {
                    // 关键：快照必须读第 i 次迭代实际写入的 RT（偶→right，奇→left）。
                    RTHandle iterResult = (i % 2 == 0) ? _blurRightRT : _blurLeftRT;
                    Blitter.BlitCameraTexture(cmd, iterResult, _blurCoreRT);
                }
            }

            RTHandle blurMain = ((iterations - 1) % 2 == 0) ? _blurRightRT : _blurLeftRT;
            RTHandle blurMainO = ((iterations - 1) % 2 == 0) ? _blurRightORT : _blurLeftORT;

            if (!coreKeep)
            {
                if (useOpacityField) blurFinalO = blurMainO;
                return blurMain;
            }

            // 合并 blurMain + blurCore → combineCore，再做一次模糊融合 → blurFinal。
            _mpbCombineCore.Clear();
            _mpbCombineCore.SetTexture(ShaderIds.MainTexId, blurMain);
            _mpbCombineCore.SetTexture(ShaderIds.SecondTex, _blurCoreRT);
            cmd.SetRenderTarget(_blurCombineCoreRT);
            cmd.DrawProcedural(Matrix4x4.identity, MaterialBlurCombineTwo, 0, MeshTopology.Triangles, 3, 1, _mpbCombineCore);

            // 透明度场不参与 CombineTwo（那是覆盖度/颜色的核心保持锐化，会破坏 R/G 比值）；仅取迭代末的透明度图，与颜色的最终模糊共用同一 PassBlur。
            // Opacity skips CombineTwo (a coverage/color core-keep sharpen that would break the R/G ratio); it shares the final PassBlur.
            ExecuteBlurPass(cmd, _blurCombineCoreRT, _blurFinalRT, 0, useOpacityField, blurMainO, _blurFinalORT);
            if (useOpacityField) blurFinalO = _blurFinalORT;
            return _blurFinalRT;
        }

        private void ExecuteBlurPass(CommandBuffer cmd, RTHandle src, RTHandle dst, int iteration,
            bool opacityField = false, RTHandle opacitySrc = null, RTHandle opacityDst = null)
        {
            float offset = (0.5f + iteration * _settings.Blur.BlurSpread) * 3f / (int)_settings.Blur.ScaleFactor;
            _mpbBlur.Clear();
            _mpbBlur.SetTexture(ShaderIds.MainTexId, src);
            _mpbBlur.SetFloat(ShaderIds.BlurOffsetId, offset);
            SetKeyword(_materialBlur, "_IGNORE_BG_COLOR", _settings.Blur.IgnoreBgColor);
            // 独立透明度场：开启第二渲染目标输出并绑定 RG 源，与颜色共用同一批模糊偏移。关键字须与 SetRenderTarget 的目标数一致。
            // Opacity field: enable the 2nd output and bind the RG source, sharing the same offsets; keyword must match the target count.
            SetKeyword(_materialBlur, OpacityFieldKeyword, opacityField);
            if (opacityField)
            {
                _mpbBlur.SetTexture(ShaderIds.OpacityTex, opacitySrc);
                _blurMrt[0] = dst;
                _blurMrt[1] = opacityDst;
                cmd.SetRenderTarget(_blurMrt, dst); // 模糊 Blend One Zero 全屏覆盖，无需清屏。 // Blur overwrites (Blend One Zero); no clear. // 上書きのため清零不要。
            }
            else
            {
                cmd.SetRenderTarget(dst);
            }
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
        private void ExecuteEffect(CommandBuffer cmd, ref RenderingData renderingData, RTHandle blurFinal, bool isHaveOccluder,
            bool useOpacityField, RTHandle blurFinalO)
        {
            cmd.SetRenderTarget(_cameraColorRT); // 合成到相机颜色（不清屏，叠加在实景上）。

            _mpbEffect.Clear();
            _mpbEffect.SetTexture(ShaderIds.MainTexId, blurFinal);
            _mpbEffect.SetTexture(ShaderIds.ObstructorTex, _obstructorRT);
            _mpbEffect.SetFloat(ShaderIds.Cutoff, _settings.Cutoff);
            _mpbEffect.SetTexture(ShaderIds.BackgroundTex, _grabAsBgSourceRT);

            // 独立透明度场：切换关键字并绑定最终透明度图(RG)，shader 内 O=R/G 并入最终 alpha。 // Opacity field: toggle keyword and bind the final RG texture; shader O=R/G into final alpha. // 独立透明度場：キーワード切替 + RG 束縛。
            SetKeyword(_materialEffect, OpacityFieldKeyword, useOpacityField);
            if (useOpacityField) _mpbEffect.SetTexture(ShaderIds.OpacityTex, blurFinalO);

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

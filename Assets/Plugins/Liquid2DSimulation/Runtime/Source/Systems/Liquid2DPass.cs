using System.Collections.Generic;
using Fs.Liquid2D.Volumes;
using UnityEngine;
using Unity.Mathematics;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.RenderGraphModule.Util;
using UnityEngine.Rendering.Universal;

namespace Fs.Liquid2D
{
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
            internal static readonly int CoverColorId = Shader.PropertyToID("_CoverColor");
            internal static readonly int EdgeStart = Shader.PropertyToID("_EdgeStart");
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

            // GPU 常驻粒子绘制相关。 // GPU-resident particle draw related. // GPU 常駐粒子描画関連。
            internal static readonly int PositionsBuf = Shader.PropertyToID("_Positions");
            internal static readonly int ColorsBuf = Shader.PropertyToID("_Colors");
            internal static readonly int RadiiBuf = Shader.PropertyToID("_Radii");
            internal static readonly int TypeIdsBuf = Shader.PropertyToID("_TypeIds");
            internal static readonly int ActiveIdxBuf = Shader.PropertyToID("_ActiveIndices");
            internal static readonly int TargetType = Shader.PropertyToID("_TargetType");
            internal static readonly int RenderScale = Shader.PropertyToID("_RenderScale");
        }
        
        private static readonly ShaderTagId _shaderTagId = new ShaderTagId("UniversalForward");

        private const string ShaderPathBlurCombineTwo = "Custom/URP/2D/CombineTwo";
        private Material _materialBlurCombineTwo;
        /// <summary>
        /// 合并两张纹理的材质。用于将两张模糊纹理进行叠加。
        /// Material for combining two textures. Used to overlay two blurred textures.
        /// 2つのテクスチャを合成するマテリアル。2つのブラーテクスチャのオーバーレイに使用。
        /// </summary>
        private Material MaterialBlurCombineTwo
        {
            get
            {
                if (!_materialBlurCombineTwo)
                {
                    _materialBlurCombineTwo = CoreUtils.CreateEngineMaterial(ShaderPathBlurCombineTwo);
                }
                return _materialBlurCombineTwo;
            }
        }
        
        private Material _materialBlur; // 流体模糊材质。 // Fluid blur material. // 流体ブラー材。
        private Material _materialEffect; // 流体效果材质。 // Fluid effect material. // 流体エフェクトマテリアル。

        private const string ShaderPathParticleGpu = "Custom/URP/2D/Liquid2DParticleGpu";
        private Material _materialParticleGpu; // GPU 常驻粒子程序化绘制材质（共享）。 // Shared material for GPU-resident procedural particle draw. // GPU 常駐粒子描画材（共有）。
        /// <summary>GPU 常驻粒子绘制材质（懒创建）。 // GPU-resident particle draw material (lazy). // GPU 常駐粒子描画材（遅延）。</summary>
        private Material MaterialParticleGpu
        {
            get
            {
                if (!_materialParticleGpu) _materialParticleGpu = CoreUtils.CreateEngineMaterial(ShaderPathParticleGpu);
                return _materialParticleGpu;
            }
        }
        private bool IsValidMat => _materialBlur && _materialEffect;
        
        private readonly Mesh _quadMesh; // 用于绘制流体粒子的四边形网格。 // Quad mesh for drawing fluid particles. // 流体粒子を描画するためのクアッドメッシュ。

        // GPU Instancing 单次绘制上限。DrawMeshInstanced 一次最多绘制 1023 个实例，超出需分批。
        // GPU Instancing per-draw limit. DrawMeshInstanced draws at most 1023 instances at once; batches are required beyond this.
        // GPUインスタンシングの1回あたりの上限。DrawMeshInstancedは一度に最大1023インスタンスまで描画でき、超過分はバッチが必要です。
        private const int MaxInstancesPerBatch = 1023;

        // 流体粒子绘制缓存。固定为单次批量上限大小，按批填充并绘制，避免每帧分配。
        // Fluid particle drawing caches. Fixed to the per-batch limit size, filled and drawn per batch to avoid per-frame allocation.
        // 流体粒子描画キャッシュ。バッチ上限サイズに固定し、バッチごとに充填・描画して毎フレームの割り当てを回避します。
        private readonly Matrix4x4[] _matricesCache = new Matrix4x4[MaxInstancesPerBatch];
        private readonly Vector4[] _colorArrayCache = new Vector4[MaxInstancesPerBatch];
        // 视锥平面缓存。复用以避免每帧分配。 // Frustum planes cache. Reused to avoid per-frame allocation. // 視錐台平面キャッシュ。毎フレームの割り当てを避けるため再利用。
        private readonly Plane[] _frustumPlanes = new Plane[6];

        // 各 Pass 复用的 MaterialPropertyBlock，避免每帧/每 Pass 重新分配。
        // (CommandBuffer 在调用绘制时会拷贝 MPB 内容，因此跨顺序执行的 Pass 复用同一实例是安全的。)
        // Reusable MaterialPropertyBlock per pass, to avoid per-frame/per-pass allocation.
        // (CommandBuffer copies MPB contents on draw, so reusing one instance across sequentially-executed passes is safe.)
        // 各Passで再利用するMaterialPropertyBlock。毎フレーム/毎Passの再割り当てを回避します。
        // (CommandBufferは描画時にMPB内容をコピーするため、順次実行されるPass間で同一インスタンスを再利用しても安全です。)
        private readonly MaterialPropertyBlock _mpbParticle = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbBlur = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbEffect = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbGrabAsBg = new MaterialPropertyBlock();
        private readonly MaterialPropertyBlock _mpbCombineCore = new MaterialPropertyBlock();

        // 默认设置。用于在没有 Volume 或 Volume 未启用时使用。
        // Default settings. Used when there is no Volume or Volume is not enabled.
        // デフォルト設定。ボリュームがない場合やボリュームが有効になっていない場合に使用されます。
        private readonly Liquid2DRenderFeatureSettings _settingsDefault;
        private readonly Liquid2DRenderFeatureSettings _settings;
        
        public Liquid2DPass(Material materialBlur, Material materialEffect, Liquid2DRenderFeatureSettings settings)
        {
            _materialBlur = materialBlur;
            _materialEffect = materialEffect;

            _settingsDefault = settings;
            _settings = settings.Clone();

            _quadMesh = GenerateQuadMesh();
            
            SetObstructorFilteringSettings();
            SetOccluderFilteringSettings();
            
            // 设置 Pass 执行时机。 // Set the execution timing of the Pass. // パスの実行タイミングを設定します。
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
            CoreUtils.Destroy(_materialParticleGpu);
            _materialParticleGpu = null;
            CoreUtils.Destroy(_materialClone);
            _materialClone = null;
            CoreUtils.Destroy(_materialGrabAsBg);
            _materialGrabAsBg = null;
        }

        private class PassData
        {
            public UniversalCameraData CameraData;
            public Liquid2DRenderFeatureSettings Settings;
            
            public TextureHandle SourceTh;
            
            public Material GrabAsBgMaterial;
            public Color GrabAsBgEdgeColor;
            public float GrabAsBgEdgeColorIntensity;
            
            public Material CloneMaterial;
            public TextureHandle CloneSourceTh;

            // 复用的 MaterialPropertyBlock 引用（指向 Pass 实例上的复用实例）。
            // Reference to a reusable MaterialPropertyBlock (points to the reusable instance on the Pass).
            // 再利用するMaterialPropertyBlockへの参照（Pass上の再利用インスタンスを指す）。
            public MaterialPropertyBlock Mpb;

            // 流体粒子绘制 Pass 相关。 // Fluid particle drawing Pass related. // 流体粒子描画Pass関連。
            // 引用 Pass 实例上的复用缓存，不在此分配。 // Reference reusable caches on the Pass instance; not allocated here. // Pass上の再利用キャッシュを参照し、ここでは割り当てません。
            public Matrix4x4[] MatricesCache;
            public Vector4[] ColorArrayCache;
            public Plane[] FrustumPlanes;
            public Mesh QuadMesh;

            // GPU 常驻粒子绘制（DrawProcedural 直读 GPU 缓冲）。 // GPU-resident particle draw (DrawProcedural). // GPU 常駐粒子描画。
            public bool GPUMode;
            public Material GPUMaterial;
            public ComputeBuffer GPUPositions;
            public ComputeBuffer GPUColors;
            public ComputeBuffer GPURadii;
            public ComputeBuffer GPUTypeIds;
            public ComputeBuffer GPUActive;
            public int GPUCount;
            public IReadOnlyList<Liquid2DParticleDescriptor> GPUDescriptors;

            // 模糊 Pass 相关。 // Blur Pass related. // ブラーPass関連。
            public Material MaterialBlur;
            public TextureHandle BlurSource;
            public int BlurIteration;

            // 核心保持叠加 Pass 相关。 // Core-keep combine Pass related. // コア保持合成Pass関連。
            public Material CombineMaterial;
            public TextureHandle CombineMainTh;
            public TextureHandle CombineCoreTh;
            
            // 流体阻挡 Pass 相关。 // Fluid Obstructor Pass related. // 流体オブストラクターパス関連。
            public RendererListHandle ObstructorRendererListHandle;
            public TextureHandle ObstructorTh;
            
            // 流体遮挡 Pass 相关。 // Fluid occluder Pass related. // 流体オクルーダーパス関連。
            public bool IsHaveOccluder;
            public RendererListHandle OccluderRendererListHandle;
            public TextureHandle OccluderTh;
            
            // 水体效果 Pass 相关。 // Water effect Pass related. // 水体エフェクトPass関連。
            public Material MaterialEffect;
            public TextureHandle BlurFinalTh;

#if UNITY_EDITOR
            // Display Overlay Pass 相关（Editor only）。 // Display Overlay Pass related (Editor only). // Display Overlay Pass 関連（Editor 専用）。
            public Liquid2DDebugParticleDisplay[] Displays;
#endif
        }

        public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
        {
            // ---- 更新设置 // Update settings // 設定を更新する ---- //
            UpdateSettings();
            
            // ---- 获取基础数据 // Get basic data // 基本データを取得する ---- //
            UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
            UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
            UniversalRenderingData renderingData = frameData.Get<UniversalRenderingData>();
            UniversalLightData lightData = frameData.Get<UniversalLightData>();
            var sourceTextureHandle = resourceData.activeColorTexture;
            
            // ---- 创建主纹理描述符 // Create main texture descriptor // メインテクスチャ記述子を作成する ---- //
            // 主纹理描述。之后其他纹理描述都基于这个进行修改。
            // Main texture description. Other texture descriptions are based on this for modification.
            // メインテクスチャの説明。その他のテクスチャの説明はこれに基づいて変更されます。
            TextureDesc mainDesc = renderGraph.GetTextureDesc(sourceTextureHandle);
            // 不清理纹理，保留原有内容。 // Do not clear the texture, keep the original content. // テクスチャをクリアせず、元の内容を保持します。
            mainDesc.clearBuffer = false;
            mainDesc.msaaSamples = MSAASamples.None; // 不使用多重采样。 // Do not use multi-sampling. // マルチサンプリングを使用しません。
            mainDesc.depthBufferBits = 0; // 不需要深度缓冲。 // No depth buffer needed. // 深度バッファは不要です。
            // 关闭 mipmap 以节省内存和提升性能。开启后在 Effect 中的扰动采样会有问题。
            // Disable mipmap to save memory and improve performance. Enabling it will cause problems with distortion sampling in Effect.
            // メモリを節約し、パフォーマンスを向上させるためにミップマップを無効にします。有効にすると、Effectの歪みサンプリングに問題が発生します。
            mainDesc.useMipMap = false;
            mainDesc.autoGenerateMips = false; // 关闭自动生成 mipmap。 // Disable automatic mipmap generation. // 自動ミップマップ生成を無効にします。
            // 使用半精度格式以支持 HDR 颜色。 // Use half-precision format to support HDR colors. // HDRカラーをサポートするために半精度フォーマットを使用します。
            mainDesc.colorFormat = GraphicsFormat.R16G16B16A16_SFloat;

            #region 获取当前相机纹理 // Get current camera texture // 現在のカメラテクスチャを取得する

            // 获取当前相机的渲染目标纹理作为底图，将alpha处理成0。这样在之后的 Blur 混合后流体边缘会融入背景颜色。
            // 使用单色纹理背景色会影响 Blur 混合，最终渲染回主纹理时如果背景色和场景色差异过大，边缘会有明显的色差。
            // Get the current camera's render target texture as the background and set alpha to 0. This way, after Blur blending, the fluid edges will blend into the background color.
            // Using a solid color texture background will affect Blur blending. When finally rendering back to the main texture, if the background color differs too much from the scene color, there will be obvious color differences at the edges.
            // 現在のカメラのレンダーターゲットテクスチャを背景として取得し、アルファを0に設定します。これにより、Blur ブレンド後に流体の端が背景色に溶け込みます。
            // 単色テクスチャの背景色はBlur ブレンドに影響します。最終的にメインテクスチャにレンダリングし直すとき、背景色とシーンの色の差が大きすぎると、端に明らかな色差が生じます。
            TextureDesc grabAsBgDesc = mainDesc;
            // 抓取当前相机渲染目标纹理作为背景图。之后在 Effect 中作为背景图使用。
            // Grab the current camera's render target texture as the background image. Later used as background in Effect.
            // 現在のカメラのレンダーターゲットテクスチャを背景画像として取得します。後でEffectで背景として使用されます。
            grabAsBgDesc.name = GetName("grabAsBgSourceTh");
            TextureHandle grabAsBgSourceTh = renderGraph.CreateTexture(grabAsBgDesc);
            // 抓取当前相机渲染目标纹理作为流体粒子绘制的背景图。这里 alpha 处理成 0。在之后的 Blur 混合后流体边缘会融入背景颜色。
            // Grab the current camera's render target texture as the background for fluid particle rendering. Here alpha is set to 0. After Blur blending, fluid edges will blend into the background color.
            // 現在のカメラのレンダーターゲットテクスチャを流体粒子レンダリングの背景として取得します。ここでアルファは0に設定されます。Blur ブレンド後、流体の端が背景色に溶け込みます。
            grabAsBgDesc.name = GetName("liquidParticleTh");
            TextureHandle liquidParticleTh = renderGraph.CreateTexture(grabAsBgDesc);
            PassGrabAsBg(renderGraph, sourceTextureHandle, grabAsBgSourceTh, liquidParticleTh, GetName("Grab As Bg"));
            
            #endregion
            
            #region 绘制流体粒子 // Draw fluid particles // 流体粒子を描画する
            
            // 判断当前相机是否为场景视图相机。 // Check if the current camera is a scene view camera. // 現在のカメラがシーンビューカメラかどうかを判断します。
            bool isSceneView = cameraData.cameraType == CameraType.SceneView;

            // ---- 添加绘制到 Pass // Add drawing to Pass // パスに描画を追加 ---- //
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("Particles"), out var passData))
            {
                // 通道数据设置。 // Pass data setup.  // パスデータ設定。
                passData.CameraData = cameraData;
                passData.Settings = _settings;

                passData.QuadMesh = _quadMesh; // 用于绘制流体粒子的四边形网格。
                passData.MatricesCache = _matricesCache;
                passData.ColorArrayCache = _colorArrayCache;
                passData.FrustumPlanes = _frustumPlanes;
                passData.Mpb = _mpbParticle;

                // GPU 常驻模式：取 GPU 缓冲直读绘制（DrawProcedural），否则走 CPU 路径。
                // GPU resident mode: read GPU buffers and draw via DrawProcedural; otherwise the CPU path.
                // GPU 常駐モード：GPU バッファを直読して DrawProcedural、それ以外は CPU パス。
                bool hasGpuBuffers = Liquid2DSimulation.TryGetRenderBuffers(out var gpuPos, out var gpuCol, out var gpuRad,
                    out var gpuType, out var gpuActive, out _, out int gpuCount, out var gpuDesc);
                passData.GPUMode = Liquid2DSimulation.Mode == Liquid2DSimulationMode.Gpu && hasGpuBuffers;
                if (passData.GPUMode)
                {
                    passData.GPUMaterial = MaterialParticleGpu;
                    passData.GPUPositions = gpuPos;
                    passData.GPUColors = gpuCol;
                    passData.GPURadii = gpuRad;
                    passData.GPUTypeIds = gpuType;
                    passData.GPUActive = gpuActive;
                    passData.GPUCount = gpuCount;
                    passData.GPUDescriptors = gpuDesc;
                }

                builder.SetRenderAttachment(
                    // 设置渲染目标纹理句柄和声明使用纹理句柄。
                    // Set render target texture handle and declare usage texture handle.
                    // レンダーターゲットテクスチャハンドルを設定し、使用テクスチャハンドルを宣言します。
                    isSceneView ? sourceTextureHandle : liquidParticleTh, 
                    0, AccessFlags.Write);

                // 设置绘制方法。 // Set drawing method. // 描画メソッドを設定します。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassParticle(data, context));
            }

            // 编辑场景相机不进行后续模糊和效果处理。直接返回粒子，方便编辑操作。
            // Scene view cameras do not perform subsequent blur and effect processing. Directly return particles for convenient editing operations.
            // シーンビューカメラは後続のブラーとエフェクト処理を行いません。編集操作を容易にするために粒子を直接返します。
            if (isSceneView)
                return;
            #endregion
            
            #region 模糊处理 // Blur processing // ブラー処理
            
            // 最终模糊纹理句柄，给到之后的处理步骤。
            // Final blur texture handle for subsequent processing steps.
            // 後続の処理ステップに渡される最終ブラーテクスチャハンドル。
            TextureHandle blurThFinal;
            
            // 多次迭代模糊。 // Multiple iterations of blur. // ブラーの複数回反復。
            if (_settings.Blur.Iterations > 0)
            {
                // ----- 创建纹理句柄 // Create texture handles // テクスチャハンドルを作成 ---- //
                // 模糊纹理描述。 // Blur texture description. // ブラーテクスチャの説明。
                TextureDesc blurDesc = mainDesc;
                // 这里使用当前相机尺寸四分之一的尺寸来提升性能。// 注意。缩放尺寸也会影响模糊的效果。
                // Here use quarter size of current camera dimensions to improve performance. // Note: Scaling size also affects blur effects.
                // ここでは、パフォーマンスを向上させるために現在のカメラの4分の1のサイズを使用します。// 注意：スケールサイズはブラー効果にも影響します。
                blurDesc.width = cameraData.cameraTargetDescriptor.width / (int)_settings.Blur.ScaleFactor;
                blurDesc.height = cameraData.cameraTargetDescriptor.height / (int)_settings.Blur.ScaleFactor;
                
                blurDesc.name = GetName("Blur Left");
                TextureHandle blurThLeft = renderGraph.CreateTexture(blurDesc);
                blurDesc.name = GetName("Blur Right");
                TextureHandle blurThRight = renderGraph.CreateTexture(blurDesc);
                blurDesc.name = GetName("Blur Core");
                TextureHandle blurThCore = renderGraph.CreateTexture(blurDesc);
                
                // 复制流体粒子纹理到第一个模糊纹理和源颜色纹理。因为尺寸不同不能直接使用 liquidParticleTh。
                // Copy fluid particle texture to first blur texture and source color texture. Cannot use liquidParticleTh directly due to different sizes.
                // 流体粒子テクスチャを最初のブラーテクスチャとソースカラーテクスチャにコピーします。サイズが異なるためliquidParticleThを直接使用できません。
                renderGraph.AddBlitPass(
                    liquidParticleTh, blurThLeft, 
                    Vector2.one, Vector2.zero, passName: GetName("Particles to Blur"));
                
                // ---- 添加绘制到 Pass // Add drawing to Pass // パスに描画を追加 ---- //
                // 计算核心保持使用哪次迭代的模糊纹理。
                // Calculate which iteration of blur texture to use for core keeping.
                // コア保持にどの反復のブラーテクスチャを使用するかを計算します。
                int coreKeepIteration = Mathf.Clamp((int)(_settings.Blur.Iterations *  (1 - _settings.Blur.CoreKeepIntensity)), 1, _settings.Blur.Iterations - 1);
                bool coreKeep = coreKeepIteration < _settings.Blur.Iterations;
                
                TextureHandle blurThMain = blurThLeft; // 模糊迭代的最终纹理句柄。 // Final texture handle for blur iterations. // ブラー反復の最終テクスチャハンドル。
                for (var i = 0; i < _settings.Blur.Iterations; ++i)
                {
                    // 交替使用两个模糊纹理句柄进行模糊。
                    // Alternately use two blur texture handles for blurring.
                    // 2つのブラーテクスチャハンドルを交互に使用してブラーします。
                    PassBlur(
                        renderGraph, 
                        i % 2 == 0 ? blurThLeft : blurThRight, i % 2 == 0 ? blurThRight : blurThLeft, 
                        i, GetName(GetBlurIndexName(i)));
                    
                    // 选择某次迭代的模糊图作为核心保持图。
                    // Select the blur image from a certain iteration as the core keep image.
                    // 特定の反復のブラー画像をコア保持画像として選択します。
                    if (coreKeep && i == coreKeepIteration)
                    {
                        renderGraph.AddBlitPass(
                            blurThRight, blurThCore, 
                            Vector2.one, Vector2.zero, passName: GetName("Blur: Get Blur Core"));
                    }
                    
                    // 记录最后一张模糊图作为最终模糊图。
                    // Record the last blur image as the final blur image.
                    // 最後のブラー画像を最終ブラー画像として記録します。
                    if (i == _settings.Blur.Iterations - 1)
                    {
                        blurThMain = i % 2 == 0 ? blurThRight : blurThLeft;
                    }
                }

                // ---- 核心保持图叠加 // Core keep image overlay // コア保持画像オーバーレイ ---- //
                if (coreKeep)
                {
                    // 将前期模糊的一张图作为核心保持图，和最后一张模糊图进行叠加，得到最终的模糊图。
                    // 这样在模糊迭代多且强度大时，也能保持粒子核心的形状，防止孤立粒子被过度模糊透明度过低而被裁剪掉。
                    // 更接近 SDF 的效果。
                    // Use an early-stage blur image as the core keep image and overlay it with the final blur image to get the final blur image.
                    // This way, even with many blur iterations and high intensity, the particle core shape can be maintained, preventing isolated particles from being over-blurred with low transparency and being clipped.
                    // Closer to SDF effects.
                    // 早期のブラー画像をコア保持画像として使用し、最終的なブラー画像とオーバーレイして最終的なブラー画像を取得します。
                    // このように、ブラー反復が多く強度が高い場合でも、粒子のコア形状を維持でき、孤立した粒子が過度にぼかされて透明度が低くなりクリップされることを防げます。
                    // SDFエフェクトにより近い効果です。
                    
                    blurDesc.name = GetName("Blur Combine Core");
                    TextureHandle blurThCombineCore = renderGraph.CreateTexture(blurDesc);
                    using (var builder = renderGraph.AddRasterRenderPass<PassData>("Blur: Combine Core ", out var passData))
                    {
                        // 设置渲染目标纹理句柄和声明使用纹理句柄。
                        // Set render target texture handle and declare texture handle usage.
                        // レンダーターゲットテクスチャハンドルを設定し、テクスチャハンドル使用を宣言します。
                        builder.SetRenderAttachment(blurThCombineCore, 0, AccessFlags.Write);
                        builder.UseTexture(blurThMain, AccessFlags.Read);
                        builder.UseTexture(blurThCore, AccessFlags.Read);
                        passData.Mpb = _mpbCombineCore;
                        passData.CombineMaterial = MaterialBlurCombineTwo;
                        passData.CombineMainTh = blurThMain;
                        passData.CombineCoreTh = blurThCore;

                        // 设置绘制方法。 // Set drawing method. // 描画メソッドを設定します。
                        builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassCombineCore(data, context));
                    }
                
                    // ---- 叠加后模糊 // Blur after overlay // オーバーレイ後のブラー ---- //
                    // 这一步使得核心保持图和最后的模糊图更好地融合在一起。
                    // This step makes the core keep image and the final blur image blend better together.
                    // このステップにより、コア保持画像と最終的なブラー画像がより良く融合します。
                    blurDesc.name = GetName("Blur: Final");
                    blurThFinal = renderGraph.CreateTexture(blurDesc);
                    PassBlur(renderGraph, blurThCombineCore, blurThFinal, 0, GetName("Blur: Final"));
                }
                else
                {
                    // 不进行核心保持时，直接使用最后一张模糊图作为最终模糊图。
                    // When not performing core keeping, directly use the last blur image as the final blur image.
                    // コア保持を行わない場合、最後のブラー画像を最終ブラー画像として直接使用します。
                    blurThFinal = blurThMain;
                }
            }
            else
            {
                // 不进行模糊时，直接使用流体粒子纹理作为最终模糊纹理。
                // When not performing blur, directly use fluid particle texture as the final blur texture.
                // ブラーを行わない場合、流体粒子テクスチャを最終ブラーテクスチャとして直接使用します。
                blurThFinal = liquidParticleTh;
            }
            #endregion

            #region 流体阻挡物 // Fluid obstructor // 流体障害物
            
            // 创建阻挡纹理。用于之后的水体效果处理Shader。一般是挡板、管道、地形、容器等。
            // Create obstructor texture. Used for subsequent water effect processing shaders. Generally for baffles, pipes, terrain, containers, etc.
            // 阻挡纹理一般不需要模糊处理。

            // ---- 创建流体阻挡纹理 // Create fluid obstructor texture // 流体阻挡テクスチャを作成 ---- //
            TextureDesc liquidObstructorDesc = mainDesc;
            liquidObstructorDesc.name = GetName("liquid 2d Obstructor");
            TextureHandle liquidObstructorTh = renderGraph.CreateTexture(liquidObstructorDesc);

            using (var builder = renderGraph.AddRasterRenderPass(GetName("liquid 2d Obstructor"), out PassData passData))
            {
                // 设置渲染目标纹理为流体阻挡纹理。 // Set render target texture to fluid obstructor texture. // レンダーターゲットテクスチャを流体阻挡テクスチャに設定します。
                builder.SetRenderAttachment(liquidObstructorTh, 0, AccessFlags.Write);

                // 获取所有 Liquid Obstructor Layer Mask层的 Renderer 列表。
                // Get the Renderer list of all Liquid Obstructor Layer Mask layers.
                // Liquid Obstructor Layer Mask レイヤーのすべてのレンダラーリストを取得します。
                var drawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
                var param = new RendererListParams(renderingData.cullResults, drawSettings, _obstructorFilteringSettings);
                passData.ObstructorRendererListHandle = renderGraph.CreateRendererList(param);
                builder.UseRendererList(passData.ObstructorRendererListHandle);
                
                builder.SetRenderFunc(
                    (PassData data, RasterGraphContext context) => 
                    {
                        // context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1, 0);
                        context.cmd.DrawRendererList(data.ObstructorRendererListHandle);
                    }
                );
            }
            #endregion

            #region 流体遮挡物 // Fluid occluder // 流体オクルーダー

            // 创建遮挡纹理。用于之后的水体效果处理Shader。一般是正面的遮挡物，但不会阻碍流体的流动。
            // Create cloner texture. Used for subsequent water effect processing shaders. Generally for front cloners, but will not hinder fluid flow.
            // クローンターテクスチャを作成します。後で水エフェクト処理シェーダーに使用されます。一般的には前面のクローンですが、流体の流れを妨げることはありません。
            
            // ---- 创建流体遮挡纹理 // Create fluid occluder texture // 流体オクルーダーテクスチャを作成 ---- //
            TextureDesc liquidOccluderDesc = mainDesc;
            liquidOccluderDesc.name = GetName("liquid 2d Occluder");
            TextureHandle liquidOccluderTh = renderGraph.CreateTexture(liquidOccluderDesc);
            
            bool isHaveOccluder = _occluderFilteringSettings.layerMask != 0;
            if (isHaveOccluder)
            {
                using (var builder = renderGraph.AddRasterRenderPass(GetName("liquid 2d Occluder"), out PassData passData))
                {
                    builder.SetRenderAttachment(liquidOccluderTh, 0, AccessFlags.Write);
                
                    var drawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
                    var param = new RendererListParams(renderingData.cullResults, drawSettings, _occluderFilteringSettings);
                    passData.OccluderRendererListHandle = renderGraph.CreateRendererList(param);
                    builder.UseRendererList(passData.OccluderRendererListHandle);
                
                    builder.SetRenderFunc(
                        (PassData data, RasterGraphContext context) => 
                        {
                            context.cmd.DrawRendererList(data.OccluderRendererListHandle);
                        }
                    );
                }
            }
            
            #endregion
            
            #region 水体效果处理 // Water effect processing // 水エフェクト処理

            // ---- 添加绘制到 Pass // Add drawing to Pass // パスに描画を追加 ---- //
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("liquid 2d Effect"), out var passData))
            {
                // 通道数据设置。 // Pass data setup.  // パスデータ設定。
                passData.CameraData = cameraData;
                passData.Settings = _settings;
                
                passData.MaterialEffect = _materialEffect;
                passData.Mpb = _mpbEffect;
                passData.SourceTh = grabAsBgSourceTh; // 当前相机纹理作为背景图。 // Current camera texture as background image. // 現在のカメラテクスチャを背景画像として。
                passData.BlurFinalTh = blurThFinal; // 最终模糊纹理。 // Final blur texture. // 最終ブラーテクスチャ。
                passData.ObstructorTh = liquidObstructorTh; // 流体阻挡纹理。 // Fluid obstructor texture. // 流体阻挡テクスチャ。

                passData.IsHaveOccluder = isHaveOccluder;
                if (isHaveOccluder)
                {
                    passData.OccluderTh = liquidOccluderTh;
                }

                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                // Set render target texture handle and declare usage texture handle.
                // レンダーターゲットテクスチャハンドルを設定し、使用テクスチャハンドルを宣言します。
                builder.SetRenderAttachment(sourceTextureHandle, 0, AccessFlags.Write);
                builder.UseTexture(passData.SourceTh, AccessFlags.Read);
                builder.UseTexture(passData.BlurFinalTh, AccessFlags.Read);
                builder.UseTexture(passData.ObstructorTh, AccessFlags.Read);
                if (isHaveOccluder)
                {
                    builder.UseTexture(passData.OccluderTh, AccessFlags.Read);
                }
                
                // 设置绘制方法。 // Set drawing method. // 描画メソッドを設定します。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassEffect(data, context));
            }
            #endregion

#if UNITY_EDITOR
            #region Display Overlay（Editor only）// Display Overlay (Editor only) // Display Overlay（Editor 専用）

            // Effect Pass 之后，将 Liquid2DParticleDisplay 粒子叠加绘制到相机颜色缓冲，使其覆盖在水体效果之上。
            // After the Effect Pass, draw Liquid2DParticleDisplay particles into the camera colour buffer so they appear above the water effect.
            // Effect Pass の後、Liquid2DParticleDisplay の粒子を相機カラーバッファに描画し、水体エフェクトの上に重ねます。
            var activeDisplays = Object.FindObjectsByType<Liquid2DDebugParticleDisplay>(
                FindObjectsSortMode.None);
            if (activeDisplays.Length > 0)
            {
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(
                    GetName("Particle Display Overlay"), out var passData))
                {
                    passData.Displays = activeDisplays;
                    builder.SetRenderAttachment(sourceTextureHandle, 0, AccessFlags.Write);
                    builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                    {
                        foreach (var d in data.Displays)
                            d.ExecuteDraw(context.cmd);
                    });
                }
            }

            #endregion
#endif
        }
        
        /// <summary>
        /// 流体粒子绘制 Pass。将所有注册的流体粒子绘制到流体绘制。
        /// Fluid particle drawing Pass. Draw all registered fluid particles to fluid rendering.
        /// 流体粒子描画Pass。登録されたすべての流体粒子を流体レンダリングに描画。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        private static void ExecutePassParticle(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;

            // GPU 常驻路径：程序化绘制直读 GPU 缓冲，绕过 CPU 逐粒子矩阵与回读。 // GPU-resident path: procedural draw from GPU buffers. // GPU 常駐パス。
            if (data.GPUMode)
            {
                ExecutePassParticleGpu(data, cmd);
                return;
            }

            Camera cam = data.CameraData.camera;
            // 使用非分配重载填充复用的视锥平面缓存。 // Fill the reusable frustum planes cache using the non-allocating overload. // 非割り当てオーバーロードで再利用する視錐台平面キャッシュを充填します。
            GeometryUtility.CalculateFrustumPlanes(cam, data.FrustumPlanes);

            var mpb = data.Mpb;
            var matrices = data.MatricesCache;
            var colors = data.ColorArrayCache;

            // 从模拟器（SoA）取数据绘制，绕过 Transform。按描述符（typeId）分组使用 GPU Instancing 批量绘制。
            // Read data from the simulation (SoA) and draw, bypassing Transform. Group by descriptor (typeId) and batch via GPU Instancing.
            // シミュレータ（SoA）からデータを取得して描画し、Transform を迂回します。記述子（typeId）でグループ化し GPU インスタンシングで描画。
            string nameTag = data.Settings.NameTag;
            if (!Liquid2DSimulation.TryGetRenderData(out var store, out var active, out var activeCount, out var descriptors))
                return;

            var positions = store.positions;
            var radiiArr = store.radii;
            var colorArr = store.colors;
            var typeArr = store.typeId;

            for (int t = 0; t < descriptors.Count; t++)
            {
                var d = descriptors[t];
                // 跳过无效组（缺少贴图或材质则无法绘制）。 // Skip invalid groups (cannot draw without sprite or material). // 無効なグループをスキップ。
                if (d == null || !d.IsValid()) continue;
                var settings = d.RenderSettings;

                // 当描述符 NameTag 不为空时，仅由相同 NameTag 的 Feature 渲染。
                // When the descriptor NameTag is not empty, only the Feature with the same NameTag renders it.
                // 記述子の NameTag が空でない場合、同じ NameTag の Feature のみが描画します。
                if (!string.IsNullOrEmpty(settings.NameTag) && !settings.NameTag.Equals(nameTag)) continue;

                mpb.Clear();
                mpb.SetTexture(ShaderIds.MainTexId, settings.Sprite.texture);

                int count = 0; // 当前批次已填充的粒子数量。 // Number of particles in the current batch. // 現在のバッチの粒子数。
                for (int k = 0; k < activeCount; k++)
                {
                    int slot = active[k];
                    if (typeArr[slot] != t) continue;

                    float2 p = positions[slot];
                    // 可视直径 = 物理直径 × 描述符渲染倍率。metaball 融合需要远大于物理半径的可视 blob，
                    // 故渲染尺寸与物理半径解耦：radius 只管物理（碰撞/邻居/堆积），renderScale 单独管可视大小。
                    // Visual diameter = physical diameter × descriptor render multiplier; metaball fusion needs visual blobs
                    // much larger than the physics radius, so visual size is decoupled from physics radius: radius drives
                    // physics (collision/neighbor/packing) only, while renderScale drives the visual size.
                    // 可視直径 = 物理直径 × 記述子の描画倍率。メタボール融合には物理半径より大きい可視 blob が必要なため、
                    // 描画サイズは物理半径から分離：radius は物理（衝突/近傍/堆積）のみ、renderScale が可視サイズを担う。
                    float diameter = radiiArr[slot] * 2f * d.RenderScale;
                    var center = new Vector3(p.x, p.y, 0f);

                    // 视锥剔除。 // Frustum cull. // 視錐台カリング。
                    var bounds = new Bounds(center, new Vector3(diameter, diameter, diameter));
                    if (!GeometryUtility.TestPlanesAABB(data.FrustumPlanes, bounds)) continue;

                    matrices[count] = Matrix4x4.TRS(center, Quaternion.identity, new Vector3(diameter, diameter, 1f));
                    float4 c = colorArr[slot];
                    colors[count] = new Vector4(c.x, c.y, c.z, c.w);
                    count++;

                    if (count == MaxInstancesPerBatch)
                    {
                        mpb.SetVectorArray(ShaderIds.ColorId, colors);
                        cmd.DrawMeshInstanced(data.QuadMesh, 0, settings.Material, 0, matrices, count, mpb);
                        count = 0;
                    }
                }

                // 绘制剩余的最后一批。 // Draw the remaining last batch. // 残りの最後のバッチを描画します。
                if (count > 0)
                {
                    mpb.SetVectorArray(ShaderIds.ColorId, colors);
                    cmd.DrawMeshInstanced(data.QuadMesh, 0, settings.Material, 0, matrices, count, mpb);
                }
            }
        }

        /// <summary>
        /// GPU 常驻路径的粒子绘制。按描述符（typeId）逐类用 DrawProcedural 绘制，直读 SphGpuSolver 的常驻 GPU 缓冲，
        /// Shader 内按 slot 取位置/颜色/半径并剔除非本类实例。绕过 CPU 逐粒子矩阵构建与每帧回读。
        /// Particle draw for the GPU-resident path. Draws per descriptor (typeId) via DrawProcedural, reading SphGpuSolver's
        /// resident GPU buffers directly; the shader fetches position/color/radius by slot and culls non-matching types.
        /// Bypasses CPU per-particle matrix building and per-frame readback.
        /// GPU 常駐パスの粒子描画。記述子ごとに DrawProcedural で描画し、常駐 GPU バッファを直読。
        /// </summary>
        private static void ExecutePassParticleGpu(PassData data, RasterCommandBuffer cmd)
        {
            var descriptors = data.GPUDescriptors;
            if (descriptors == null || data.GPUCount <= 0 || data.GPUMaterial == null) return;

            string nameTag = data.Settings.NameTag;
            var mpb = data.Mpb;

            for (int t = 0; t < descriptors.Count; t++)
            {
                var d = descriptors[t];
                if (d == null || !d.IsValid()) continue;
                var settings = d.RenderSettings;

                // NameTag 不为空时仅由同名 Feature 渲染（与 CPU 路径一致）。 // Same nameTag gating as the CPU path. // CPU パスと同じ nameTag ゲート。
                if (!string.IsNullOrEmpty(settings.NameTag) && !settings.NameTag.Equals(nameTag)) continue;

                mpb.Clear();
                mpb.SetBuffer(ShaderIds.PositionsBuf, data.GPUPositions);
                mpb.SetBuffer(ShaderIds.ColorsBuf, data.GPUColors);
                mpb.SetBuffer(ShaderIds.RadiiBuf, data.GPURadii);
                mpb.SetBuffer(ShaderIds.TypeIdsBuf, data.GPUTypeIds);
                mpb.SetBuffer(ShaderIds.ActiveIdxBuf, data.GPUActive);
                mpb.SetTexture(ShaderIds.MainTexId, settings.Sprite.texture);
                mpb.SetInteger(ShaderIds.TargetType, t);
                mpb.SetFloat(ShaderIds.RenderScale, d.RenderScale);

                // 6 顶点/实例（两三角拼四边形），实例数 = 活动粒子数；Shader 内按 typeId 剔除非本类。
                // 6 verts/instance (quad), instances = active particle count; shader culls non-matching typeIds.
                // 6 頂点/インスタンス、インスタンス数 = アクティブ粒子数。
                cmd.DrawProcedural(Matrix4x4.identity, data.GPUMaterial, 0, MeshTopology.Triangles, 6, data.GPUCount, mpb);
            }
        }

        private void PassBlur(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, int iteration, string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass(passName, out PassData passData))
            {
                // 通道数据设置。 // Pass data setup.  // パスデータ設定。
                passData.Settings = _settings;

                passData.MaterialBlur = _materialBlur;
                passData.BlurSource = source;
                passData.BlurIteration = iteration;
                passData.Mpb = _mpbBlur;

                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                // Set render target texture handle and declare usage texture handle.
                // レンダーターゲットテクスチャハンドルを設定し、使用テクスチャハンドルを宣言します。
                builder.SetRenderAttachment(destination, 0, AccessFlags.Write);
                builder.UseTexture(source, AccessFlags.Read);
            
                // 设置绘制方法。 // Set drawing method. // 描画メソッドを設定します。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassBlur(data, context));
            }
        }
        
        /// <summary>
        /// 模糊 Pass。对输入的流体纹理进行模糊处理，并输出到下一个模糊纹理。
        /// Blur Pass. Apply blur processing to input fluid texture and output to next blur texture.
        /// ブラーPass。入力流体テクスチャにブラー処理を適用し、次のブラーテクスチャに出力。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        private static void ExecutePassBlur(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            // 模糊偏移强度递增，并乘以缩放比例。 // Blur offset intensity increases, multiplied by scaling factor. // ブラーオフセット強度が増加し、スケーリングファクター。
            float offset = 
                (0.5f + data.BlurIteration * data.Settings.Blur.BlurSpread) * 3f / (int)data.Settings.Blur.ScaleFactor;

            // 设置模糊材质属性块，传入当前模糊材质和偏移强度。
            // Set blur material property block, pass in current blur material and offset intensity.
            // ブラーマテリアルプロパティブロックを設定し、現在のブラーマテリアルとオフセット強度を渡します。
            MaterialPropertyBlock mpb = data.Mpb;
            mpb.Clear();
            mpb.SetTexture(ShaderIds.MainTexId,data.BlurSource); // 传入当前模糊纹理。 // Pass in current blur texture. // 現在のブラーテクスチャを渡します。
            mpb.SetFloat(ShaderIds.BlurOffsetId, offset); // 设置模糊偏移强度。 // Set blur offset intensity. // ブラーオフセット強度を設定します。
            // 是否忽略背景色。 // Whether to ignore background color. // 背景色を無視するかどうか。
            SetKeyword(data.MaterialBlur, "_IGNORE_BG_COLOR", data.Settings.Blur.IgnoreBgColor);

            // 绘制一个全屏三角形，使用模糊材质，并传入属性块。
            // Draw a full-screen triangle using blur material and pass in property block.
            // ブラー材質を使用して全画面三角形を描画し、プロ
            cmd.DrawProcedural(
                Matrix4x4.identity, data.MaterialBlur, 0,
                MeshTopology.Triangles, 3, 1, mpb);
        }

        /// <summary>
        /// 核心保持叠加 Pass。将核心保持图与最终模糊图叠加。
        /// Core-keep combine Pass. Overlays the core-keep image with the final blur image.
        /// コア保持合成Pass。コア保持画像と最終ブラー画像を合成します。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        private static void ExecutePassCombineCore(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;

            MaterialPropertyBlock mpb = data.Mpb;
            mpb.Clear();
            mpb.SetTexture(ShaderIds.MainTexId, data.CombineMainTh);
            mpb.SetTexture(ShaderIds.SecondTex, data.CombineCoreTh);
            cmd.DrawProcedural(
                Matrix4x4.identity, data.CombineMaterial, 0,
                MeshTopology.Triangles, 3, 1, mpb);
        }

        /// <summary>
        /// 水体效果 Pass。将模糊后的流体纹理处理并绘制到当前相机的渲染目标上。
        /// Water effect Pass. Process blurred fluid texture and draw to current camera render target.
        /// 水エフェクトPass。ブラーされた流体テクスチャを処理し、現在のカメラのレンダーターゲットに描画。
        /// </summary>
        /// <param name="data"></param>
        /// <param name="context"></param>
        private static void ExecutePassEffect(PassData data, RasterGraphContext context)
        {
            var cmd = context.cmd;
            
            // 设置外描边材质属性块，传入绘制RT。
            // Set outline material property block, pass in draw RT.
            // アウライン材質プロパティブロックを設定し、描画RTを渡します。
            MaterialPropertyBlock mpb = data.Mpb;
            mpb.Clear();
            mpb.SetTexture(ShaderIds.MainTexId, data.BlurFinalTh); // 流体纹理。 //Fluid texture. //流体テクスチャ。
            mpb.SetTexture(ShaderIds.ObstructorTex, data.ObstructorTh); // 流体阻挡纹理。 //Fluid obstructor texture. //流体阻害テクスチャ。
            mpb.SetFloat(ShaderIds.Cutoff, data.Settings.Cutoff); // 裁剪阈值。 //Cutoff threshold. //カットオフ閾値。
            mpb.SetTexture(ShaderIds.BackgroundTex, data.SourceTh); // 背景纹理。用于扰动采样。 //Background texture. Used for distortion sampling. //背景テクスチャ。歪みサンプリングに使用。

            // 流体遮挡纹理。 // Fluid occluder texture. // 流体オクルーダーテクスチャ。
            SetKeyword(data.MaterialEffect, "_OCCLUDER_ENABLE", data.IsHaveOccluder);
            if (data.IsHaveOccluder)
            {
                mpb.SetTexture(ShaderIds.OccluderTex, data.OccluderTh);
            }

            // 透明度调整参数。 // Opacity adjustment parameters. // 透明度調整パラメータ。
            SetKeyword(data.MaterialEffect, "_OPACITY_MULTIPLY", data.Settings.OpacityMode == EOpacityMode.Multiply);
            SetKeyword(data.MaterialEffect, "_OPACITY_REPLACE", data.Settings.OpacityMode == EOpacityMode.Replace);
            mpb.SetFloat(ShaderIds.OpacityValue, data.Settings.OpacityValue); // 透明度值。 // Opacity value. //透明度値。
            
            // 覆盖颜色。透明度值是强度。 // Cover color. Opacity value is intensity. // カバー色。透明度値は強度です。
            mpb.SetColor(ShaderIds.CoverColorId, data.Settings.CoverColor);

            SetKeyword(data.MaterialEffect, "_EDGE_ENABLE", data.Settings.Edge.Enable);
            if (data.Settings.Edge.Enable)
            {
                float cutoff = data.Settings.Cutoff;
                float edgeRange = data.Settings.Edge.EdgeRange;
                float edgeIntensity = data.Settings.Edge.EdgeIntensity;
                
                // 计算边缘参数。 // Calculate edge parameters. // エッジパラメータを計算します。
                float edgeStart = cutoff;
                float edgeEnd = cutoff + edgeRange * (1 - cutoff);
                float edgeMixStart = Mathf.Lerp(edgeStart, edgeEnd, edgeIntensity * 0.999f);
            
                mpb.SetFloat(ShaderIds.EdgeStart, edgeStart); // 边缘开始位置。 // Edge start position. // エッジ開始位置。
                mpb.SetFloat(ShaderIds.EdgeEnd, edgeEnd); // 边缘结束位置。 // Edge end position. // エッジ終了位置。
                // 边缘混合开始位置。用于 smoothstep 计算。 // Edge mix start position. Used for smoothstep calculation. // エッジミックス開始位置。smoothstep計算に使用。
                mpb.SetFloat(ShaderIds.EdgeMixStart, edgeMixStart);
                mpb.SetColor(ShaderIds.EdgeColor, data.Settings.Edge.EdgeColor); // 边缘颜色。 // Edge color. // エッジカラー。

                bool lerpBlend = data.Settings.Edge.BlendType == Liquid2DRenderFeatureSettings.EdgeSettings.EdgeBlendType.Lerp;
                SetKeyword(data.MaterialEffect, "_EDGE_BLEND_SA_OMSA", !lerpBlend);
                SetKeyword(data.MaterialEffect, "_EDGE_BLEND_LERP", lerpBlend);
            }

            // 像素风格化。 // Pixel stylization. // ピクセルスタイリゼーション。
            SetKeyword(data.MaterialEffect, "_PIXEL_ENABLE", data.Settings.Pixel.Enable);
            if (data.Settings.Pixel.Enable)
            {
                // 计算像素化尺寸。 // Calculate pixelation size. // ピクセル化サイズを計算します。
                float aspect = (float)Screen.width / Screen.height; // 屏幕宽高比。 // Screen aspect ratio. // 画面のアスペクト比。
                int pixelWidthCount = Screen.width / data.Settings.Pixel.PixelSize; // 水平像素块数量。 // Number of horizontal pixel blocks. // 水平ピクセルブロックの数。
                Vector2 pixelSize = new Vector2(pixelWidthCount, pixelWidthCount / aspect); // 计算垂直像素块数量。 // Calculate number of vertical pixel blocks. // 垂直ピクセルブロックの数を計算します。
                mpb.SetVector(ShaderIds.PixelSize, pixelSize); // 传入像素化尺寸。 // Pass in pixelation size. // ピクセル化サイズを渡します。

                // 是否使背景像素化。在水体透明时背景色也会像素化。
                // Whether to pixelate the background. When the water is transparent, the background color will also be pixelated.
                // 背景がピクセル化されるかどうか。水が透明な場合、背景色もピクセル化されます。
                SetKeyword(data.MaterialEffect, "_PIXEL_BG", data.Settings.Pixel.PixelBg);
            }

            // 水体扰动纹理和强度。 // Water distortion texture and intensity. // 水の歪みテクスチャと強度。
            bool distortEnable = data.Settings.Distort.Enable
                                 // 完全不透明时不进行扰动。 // No distortion when completely opaque. // 完全に不透明な場合は歪みを行いません。
                                 && !(data.Settings.OpacityMode == EOpacityMode.Replace && data.Settings.OpacityValue >= 1f);
            SetKeyword(data.MaterialEffect, "_DISTORT_ENABLE", distortEnable);
            if (distortEnable)
            {
                mpb.SetFloat(ShaderIds.Magnitude, data.Settings.Distort.Magnitude);
                mpb.SetFloat(ShaderIds.Frequency, data.Settings.Distort.Frequency);
                mpb.SetFloat(ShaderIds.Amplitude, data.Settings.Distort.Amplitude);
                mpb.SetVector(ShaderIds.DistortSpeed, data.Settings.Distort.DistortSpeed);
                mpb.SetVector(ShaderIds.DistortTimeFactors, data.Settings.Distort.DistortTimeFactors);
                mpb.SetFloat(ShaderIds.NoiseCoordOffset, data.Settings.Distort.NoiseCoordOffset);
            }
            
            // 绘制一个全屏三角形，使用外描边材质，并传入属性块。
            // Draw a full-screen triangle using the outline material and pass in the property block.
            // 全画面三角形をアウトラインマテリアルで描画し、プロパティブロックを渡します。
            cmd.DrawProcedural(Matrix4x4.identity, data.MaterialEffect, 0, MeshTopology.Triangles, 3, 1,
                mpb);
        }
        
        #region Grab as Bg Pass

        private const string ShaderPathGrabAsBg = "Custom/URP/2D/GrabAsBg";
        private Material _materialGrabAsBg;
        private Material MaterialGrabAsBg
        {
            get
            {
                if (!_materialGrabAsBg)
                {
                    _materialGrabAsBg = CoreUtils.CreateEngineMaterial(ShaderPathGrabAsBg);
                }
                return _materialGrabAsBg;
            }
        }

        /// <summary>
        /// 抓取源纹理作为背景 Pass。将源纹理拷贝到目标纹理，并将 alpha 设为 0。
        /// Grab source texture as background Pass. Copy source texture to target texture and set alpha to 0.
        /// ソーステクスチャを背景として取得するPass。ソーステクスチャをターゲットテクスチャにコピーし、アルファを0に設定。
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
                passData.GrabAsBgMaterial = MaterialGrabAsBg;
                passData.Mpb = _mpbGrabAsBg;
                passData.SourceTh = source;
                passData.GrabAsBgEdgeColor = _settings.Blur.BlurBgColor;
                passData.GrabAsBgEdgeColorIntensity = _settings.Blur.BlurBgColorIntensity;
            
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(renderAttachment1, 0, AccessFlags.Write);
                builder.SetRenderAttachment(renderAttachment2, 1, AccessFlags.Write);
                builder.UseTexture(source, AccessFlags.Read);
            
                // 设置绘制方法。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    var material = data.GrabAsBgMaterial;
                    
                    // material.SetColor(ShaderIds.ColorId, data.grabAsBgEdgeColor);
                    // material.SetFloat(ShaderIds.ColorIntensityId, data.grabAsBgEdgeColorIntensity);
                    // Blitter.BlitTexture(cmd, sourceTh, Vector2.one, material, 0);
                    
                    MaterialPropertyBlock mpb = data.Mpb;
                    mpb.Clear();
                    mpb.SetTexture(ShaderIds.MainTexId, data.SourceTh);
                    mpb.SetColor(ShaderIds.ColorId, data.GrabAsBgEdgeColor);
                    mpb.SetFloat(ShaderIds.ColorIntensityId, data.GrabAsBgEdgeColorIntensity);
                    // 绘制一个全屏三角形，使用外描边材质，并传入属性块。
                    cmd.DrawProcedural(Matrix4x4.identity, material, 0, MeshTopology.Triangles, 3, 1,
                        mpb);
                });
            }
        }

        #endregion

        #region Layer

        private FilteringSettings _obstructorFilteringSettings;
        
        private FilteringSettings _occluderFilteringSettings;
        
        /// <summary>
        /// 设置阻挡层过滤设置。
        /// </summary>
        private void SetObstructorFilteringSettings()
        {
            _obstructorFilteringSettings = new FilteringSettings
            ( 
                RenderQueueRange.all,
                ~0,
                _settings.ObstructorRenderingLayerMask
            );
        }
        
        private void SetOccluderFilteringSettings()
        {
            _occluderFilteringSettings = new FilteringSettings 
            (
                RenderQueueRange.all,
                ~0,
                _settings.OccluderRenderingLayerMask
            );
        }

        #endregion
        
        #region Volume
        // 通过 Volume 设置，你可以在运行时动态修改流体效果的参数。
        // Through Volume settings, you can dynamically modify fluid effect parameters at runtime.
        // Volume設定により、ランタイムで流体エフェクトのパラメータを動的に変更できます。 
        
        /// <summary>
        /// 更新设置。
        /// 支持通过 Volume 系统重载当前 Renderer Feature 的配置。
        /// 如果没有 Volume 或 Volume 未启用，则使用默认设置。
        /// 
        /// Update settings.
        /// If there is no Volume or Volume is not enabled, use default settings.
        /// Support overriding current Renderer Feature configuration through Volume system.
        /// 
        /// 設定を更新。
        /// Volumeシステムを通じて現在のRenderer Featureの設定をオーバーライドすることをサポート。
        /// Volumeがないか、Volumeが有効でない場合は、デフォルト設定を使用。
        /// </summary>
        private void UpdateSettings()
        {
            if (!IsValidMat)
                return;
             
            // 获取 Volume。 // Get Volume. // Volumeを取得。
            Liquid2DVolume volumeComponent = VolumeManager.instance.stack.GetComponent<Liquid2DVolume>();
            // 每帧从当前 Volume 组件实时解析数据，避免缓存导致运行时 Volume 变更后读到旧值。
            // Resolve data from the current Volume component each frame to avoid reading stale values after a runtime Volume change.
            // 毎フレーム現在のVolumeコンポーネントからデータを解決し、ランタイムでのVolume変更後に古い値を読むことを回避します。
            Liquid2DVolumeData volumeData = null;
            // 判断 Volume 是否启用且当前组件是否启用且数据有效。
            // Check if Volume is enabled and current component is enabled and data is valid.
            // Volumeが有効で、現在のコンポーネントが有効で、データが有効かを判断。
            bool isActive =
                volumeComponent
                && volumeComponent.IsActive.value
                && volumeComponent.Liquid2DVolumeDataList.overrideState
                && volumeComponent.GetData(_settings.NameTag, out volumeData)
                && volumeData is { IsActive: true };

            // ---- 重载设置。 // Override settings. // 設定をオーバーライド。 ---- //
            // 2D流体层遮罩。只会渲染设定的流体层的粒子。
            // 2D Liquid layer mask. Only particles of the set fluid layer will be rendered.
            // 2D流体レイヤーマスク。設定された流体レイヤーの粒子のみがレンダリングされます。
            _settings.NameTag = isActive ? volumeData.NameTag : _settingsDefault.NameTag;
            
            // 阻挡层遮罩。 // Obstructor layer mask. // 阻害レイヤーマスク。
            _settings.ObstructorRenderingLayerMask = isActive ? volumeData.ObstructorRenderingLayerMask : _settingsDefault.ObstructorRenderingLayerMask;
            if (_obstructorFilteringSettings.layerMask != _settings.ObstructorRenderingLayerMask)
            {
                SetObstructorFilteringSettings();
            }
            // 遮挡层遮罩。 // Occlusion layer mask. // オクルージョンレイヤーマスク。
            _settings.OccluderRenderingLayerMask = isActive ? volumeData.OccluderRenderingLayerMask : _settingsDefault.OccluderRenderingLayerMask;
            if (_occluderFilteringSettings.layerMask != _settings.OccluderRenderingLayerMask)
            {
                SetOccluderFilteringSettings();
            }
            
            _settings.Cutoff = isActive ? volumeData.Cutoff : _settingsDefault.Cutoff;
            
            _settings.OpacityMode = isActive ? volumeData.OpacityMode : _settingsDefault.OpacityMode;
            _settings.OpacityValue = isActive ? volumeData.OpacityValue : _settingsDefault.OpacityValue;
            _settings.CoverColor = isActive ? volumeData.CoverColor : _settingsDefault.CoverColor;
            
            // ---- 边缘设置 // Edge settings // エッジ設定 ---- //
            _settings.Edge.Enable = isActive ? volumeData.Edge.Enable : _settingsDefault.Edge.Enable;
            _settings.Edge.EdgeRange = isActive ? volumeData.Edge.EdgeRange : _settingsDefault.Edge.EdgeRange;
            _settings.Edge.EdgeIntensity = isActive ? volumeData.Edge.EdgeIntensity : _settingsDefault.Edge.EdgeIntensity;
            _settings.Edge.EdgeColor = isActive ? volumeData.Edge.EdgeColor : _settingsDefault.Edge.EdgeColor;
            _settings.Edge.BlendType = isActive ? volumeData.Edge.BlendType : _settingsDefault.Edge.BlendType;

            // ---- 模糊设置 // Blur settings // ブラー設定 ---- //
            _settings.Blur.Iterations = isActive ? volumeData.Blur.Iterations : _settingsDefault.Blur.Iterations;
            _settings.Blur.BlurSpread = isActive ? volumeData.Blur.BlurSpread : _settingsDefault.Blur.BlurSpread;
            _settings.Blur.CoreKeepIntensity = isActive ? volumeData.Blur.CoreKeepIntensity : _settingsDefault.Blur.CoreKeepIntensity;
            _settings.Blur.ScaleFactor = isActive ? volumeData.Blur.ScaleFactor : _settingsDefault.Blur.ScaleFactor;
            _settings.Blur.IgnoreBgColor = isActive ? volumeData.Blur.IgnoreBgColor : _settingsDefault.Blur.IgnoreBgColor;
            // 模糊背景色和强度。实际上在 Blur 前作为底图进行混合。默认的底图是当前相机的场景纹理（alpha为0）。
            // Blur background color and intensity. In fact, it is blended as a base map before Blur. The default base map is the current camera scene texture (alpha is 0).
            // ブラーバックグラウンドカラーと強度。実際には、Blurの前にベースマップとしてブレンドされます。デフォルトのベースマップは現在のカメラシーンテクスチャ（アルファは0）です。
            _settings.Blur.BlurBgColor = isActive ? volumeData.Blur.BlurBgColor : _settingsDefault.Blur.BlurBgColor;
            _settings.Blur.BlurBgColorIntensity = isActive ? volumeData.Blur.BlurBgColorIntensity : _settingsDefault.Blur.BlurBgColorIntensity;
            
            // ---- 水体扰动设置 // Water distortion settings // 水の歪み設定 ---- //
            _settings.Distort.Enable = isActive ? volumeData.Distort.Enable : _settingsDefault.Distort.Enable;
            _settings.Distort.Frequency = isActive ? volumeData.Distort.Frequency : _settingsDefault.Distort.Frequency;
            _settings.Distort.Amplitude = isActive ? volumeData.Distort.Amplitude : _settingsDefault.Distort.Amplitude;
            _settings.Distort.DistortSpeed = isActive ? volumeData.Distort.DistortSpeed : _settingsDefault.Distort.DistortSpeed;
            _settings.Distort.DistortTimeFactors = isActive ? volumeData.Distort.DistortTimeFactors : _settingsDefault.Distort.DistortTimeFactors;
            _settings.Distort.NoiseCoordOffset = isActive ? volumeData.Distort.NoiseCoordOffset : _settingsDefault.Distort.NoiseCoordOffset;
            
            // ---- 像素化 // Pixelation // ピクセル化 ---- //
            _settings.Pixel.Enable = isActive ? volumeData.Pixel.Enable : _settingsDefault.Pixel.Enable;
            _settings.Pixel.PixelSize = isActive ? volumeData.Pixel.PixelSize : _settingsDefault.Pixel.PixelSize;
            _settings.Pixel.PixelBg = isActive ? volumeData.Pixel.PixelBg : _settingsDefault.Pixel.PixelBg;
        }

        #endregion

        #region Tools

        #region Clone Pass

        private const string ShaderPathClone = "Custom/URP/2D/Clone";
        private Material _materialClone;

        /// <summary>
        /// 克隆材质。直接将源纹理拷贝到目标纹理。
        /// Clone material. Directly copy source texture to target texture.
        /// クローンマテリアル。ソーステクスチャを直接ターゲットテクスチャにコピー。
        /// </summary>
        private Material MaterialClone
        {
            get
            {
                if (!_materialClone)
                {
                    _materialClone = CoreUtils.CreateEngineMaterial(ShaderPathClone);
                }
                return _materialClone;
            }
        }

        /// <summary>
        /// 复制 Pass。将源纹理拷贝到目标纹理。
        /// Copy Pass. Copy source texture to target texture.
        /// コピーPass。ソーステクスチャをターゲットテクスチャにコピー。
        /// </summary>
        /// <param name="renderGraph"></param>
        /// <param name="source"></param>
        /// <param name="renderAttachment"></param>
        /// <param name="passNameSet"></param>
        private void PassClone(RenderGraph renderGraph, TextureHandle source, TextureHandle renderAttachment, string passNameSet  = "liquid 2d Clone")
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passNameSet, out var passData))
            {
                passData.CloneMaterial = MaterialClone;
                passData.CloneSourceTh = source;
            
                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                builder.SetRenderAttachment(renderAttachment, 0, AccessFlags.Write);
                builder.UseTexture(source, AccessFlags.Read);
            
                // 设置绘制方法。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) =>
                {
                    var cmd = context.cmd;
                    var material = data.CloneMaterial;
                    var sourceTh = data.CloneSourceTh;
                    
                    Blitter.BlitTexture(cmd, sourceTh, Vector2.one, material, 0);
                });
            }
        }

        #endregion
        
        /// <summary>
        /// 生成一个简单的四边形网格。用于全屏渲染。
        /// Generate a simple quad mesh. Used for full-screen rendering.
        /// シンプルな四辺形メッシュを生成します。全画面レンダリングに使用されます。
        /// </summary>
        /// <returns></returns>
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

        // Pass 名缓存。按 name 缓存完整 Pass 名，避免每帧字符串插值分配；nameTag 变化时清空。
        // Pass name cache. Caches full pass names by name to avoid per-frame string interpolation allocation; cleared when nameTag changes.
        // Pass名キャッシュ。nameをキーに完全なPass名をキャッシュし、毎フレームの文字列補間割り当てを回避。nameTag変更時にクリア。
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

        // 模糊迭代内层名（"Blur: i"）缓存，避免每帧为循环索引分配字符串。
        // Cache for blur iteration inner names ("Blur: i") to avoid per-frame string allocation for the loop index.
        // ブラー反復の内部名（"Blur: i"）キャッシュ。ループインデックスのための毎フレームの文字列割り当てを回避。
        private readonly List<string> _blurIndexNames = new List<string>();

        private string GetBlurIndexName(int i)
        {
            while (_blurIndexNames.Count <= i)
                _blurIndexNames.Add($"Blur: {_blurIndexNames.Count}");
            return _blurIndexNames[i];
        }

        /// <summary>
        /// 仅在关键字状态变化时切换，避免每帧冗余的材质变体重算。
        /// Toggle a shader keyword only when its state changes, avoiding redundant per-frame material variant recomputation.
        /// キーワード状態が変化したときのみ切り替え、毎フレームの冗長なマテリアルバリアント再計算を回避します。
        /// </summary>
        private static void SetKeyword(Material material, string keyword, bool enable)
        {
            if (material.IsKeywordEnabled(keyword) == enable) return;
            if (enable) material.EnableKeyword(keyword);
            else material.DisableKeyword(keyword);
        }

        #endregion
    }
}
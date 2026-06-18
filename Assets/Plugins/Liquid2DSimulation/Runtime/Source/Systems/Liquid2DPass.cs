using System;
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
            CoreUtils.Destroy(_materialClone);
            _materialClone = null;
            CoreUtils.Destroy(_materialGrabAsBg);
            _materialGrabAsBg = null;
        }

        private class PassData
        {
            public UniversalCameraData cameraData;
            public Liquid2DRenderFeatureSettings settings;
            
            public TextureHandle sourceTh;
            
            public Material grabAsBgMaterial;
            public Color grabAsBgEdgeColor;
            public float grabAsBgEdgeColorIntensity;
            
            public Material cloneMaterial;
            public TextureHandle cloneSourceTh;

            // 复用的 MaterialPropertyBlock 引用（指向 Pass 实例上的复用实例）。
            // Reference to a reusable MaterialPropertyBlock (points to the reusable instance on the Pass).
            // 再利用するMaterialPropertyBlockへの参照（Pass上の再利用インスタンスを指す）。
            public MaterialPropertyBlock mpb;

            // 流体粒子绘制 Pass 相关。 // Fluid particle drawing Pass related. // 流体粒子描画Pass関連。
            // 引用 Pass 实例上的复用缓存，不在此分配。 // Reference reusable caches on the Pass instance; not allocated here. // Pass上の再利用キャッシュを参照し、ここでは割り当てません。
            public Matrix4x4[] matricesCache;
            public Vector4[] colorArrayCache;
            public Plane[] frustumPlanes;
            public Mesh quadMesh;

            // 模糊 Pass 相关。 // Blur Pass related. // ブラーPass関連。
            public Material materialBlur;
            public TextureHandle blurSource;
            public int blurIteration;

            // 核心保持叠加 Pass 相关。 // Core-keep combine Pass related. // コア保持合成Pass関連。
            public Material combineMaterial;
            public TextureHandle combineMainTh;
            public TextureHandle combineCoreTh;
            
            // 流体阻挡 Pass 相关。 // Fluid Obstructor Pass related. // 流体オブストラクターパス関連。
            public RendererListHandle obstructorRendererListHandle;
            public TextureHandle obstructorTh;
            
            // 流体遮挡 Pass 相关。 // Fluid occluder Pass related. // 流体オクルーダーパス関連。
            public bool isHaveOccluder;
            public RendererListHandle occluderRendererListHandle;
            public TextureHandle occluderTh;
            
            // 水体效果 Pass 相关。 // Water effect Pass related. // 水体エフェクトPass関連。
            public Material materialEffect;
            public TextureHandle blurFinalTh;
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
                passData.cameraData = cameraData;
                passData.settings = _settings;

                passData.quadMesh = _quadMesh; // 用于绘制流体粒子的四边形网格。
                passData.matricesCache = _matricesCache;
                passData.colorArrayCache = _colorArrayCache;
                passData.frustumPlanes = _frustumPlanes;
                passData.mpb = _mpbParticle;

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
            if (_settings.blur.iterations > 0)
            {
                // ----- 创建纹理句柄 // Create texture handles // テクスチャハンドルを作成 ---- //
                // 模糊纹理描述。 // Blur texture description. // ブラーテクスチャの説明。
                TextureDesc blurDesc = mainDesc;
                // 这里使用当前相机尺寸四分之一的尺寸来提升性能。// 注意。缩放尺寸也会影响模糊的效果。
                // Here use quarter size of current camera dimensions to improve performance. // Note: Scaling size also affects blur effects.
                // ここでは、パフォーマンスを向上させるために現在のカメラの4分の1のサイズを使用します。// 注意：スケールサイズはブラー効果にも影響します。
                blurDesc.width = cameraData.cameraTargetDescriptor.width / (int)_settings.blur.scaleFactor;
                blurDesc.height = cameraData.cameraTargetDescriptor.height / (int)_settings.blur.scaleFactor;
                
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
                int coreKeepIteration = Mathf.Clamp((int)(_settings.blur.iterations *  (1 - _settings.blur.coreKeepIntensity)), 1, _settings.blur.iterations - 1);
                bool coreKeep = coreKeepIteration < _settings.blur.iterations;
                
                TextureHandle blurThMain = blurThLeft; // 模糊迭代的最终纹理句柄。 // Final texture handle for blur iterations. // ブラー反復の最終テクスチャハンドル。
                for (var i = 0; i < _settings.blur.iterations; ++i)
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
                    if (i == _settings.blur.iterations - 1)
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
                        passData.mpb = _mpbCombineCore;
                        passData.combineMaterial = MaterialBlurCombineTwo;
                        passData.combineMainTh = blurThMain;
                        passData.combineCoreTh = blurThCore;

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

            using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("liquid 2d Obstructor"), out PassData passData))
            {
                // 设置渲染目标纹理为流体阻挡纹理。 // Set render target texture to fluid obstructor texture. // レンダーターゲットテクスチャを流体阻挡テクスチャに設定します。
                builder.SetRenderAttachment(liquidObstructorTh, 0, AccessFlags.Write);

                // 获取所有 Liquid Obstructor Layer Mask层的 Renderer 列表。
                // Get the Renderer list of all Liquid Obstructor Layer Mask layers.
                // Liquid Obstructor Layer Mask レイヤーのすべてのレンダラーリストを取得します。
                var drawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
                var param = new RendererListParams(renderingData.cullResults, drawSettings, _obstructorFilteringSettings);
                passData.obstructorRendererListHandle = renderGraph.CreateRendererList(param);
                builder.UseRendererList(passData.obstructorRendererListHandle);
                
                builder.SetRenderFunc(
                    (PassData data, RasterGraphContext context) => 
                    {
                        // context.cmd.ClearRenderTarget(RTClearFlags.Color, Color.clear, 1, 0);
                        context.cmd.DrawRendererList(data.obstructorRendererListHandle);
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
                using (var builder = renderGraph.AddRasterRenderPass<PassData>(GetName("liquid 2d Occluder"), out PassData passData))
                {
                    builder.SetRenderAttachment(liquidOccluderTh, 0, AccessFlags.Write);
                
                    var drawSettings = RenderingUtils.CreateDrawingSettings(_shaderTagId, renderingData, cameraData, lightData, cameraData.defaultOpaqueSortFlags);
                    var param = new RendererListParams(renderingData.cullResults, drawSettings, _occluderFilteringSettings);
                    passData.occluderRendererListHandle = renderGraph.CreateRendererList(param);
                    builder.UseRendererList(passData.occluderRendererListHandle);
                
                    builder.SetRenderFunc(
                        (PassData data, RasterGraphContext context) => 
                        {
                            context.cmd.DrawRendererList(data.occluderRendererListHandle);
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
                passData.cameraData = cameraData;
                passData.settings = _settings;
                
                passData.materialEffect = _materialEffect;
                passData.mpb = _mpbEffect;
                passData.sourceTh = grabAsBgSourceTh; // 当前相机纹理作为背景图。 // Current camera texture as background image. // 現在のカメラテクスチャを背景画像として。
                passData.blurFinalTh = blurThFinal; // 最终模糊纹理。 // Final blur texture. // 最終ブラーテクスチャ。
                passData.obstructorTh = liquidObstructorTh; // 流体阻挡纹理。 // Fluid obstructor texture. // 流体阻挡テクスチャ。

                passData.isHaveOccluder = isHaveOccluder;
                if (isHaveOccluder)
                {
                    passData.occluderTh = liquidOccluderTh;
                }

                // 设置渲染目标纹理句柄和声明使用纹理句柄。
                // Set render target texture handle and declare usage texture handle.
                // レンダーターゲットテクスチャハンドルを設定し、使用テクスチャハンドルを宣言します。
                builder.SetRenderAttachment(sourceTextureHandle, 0, AccessFlags.Write);
                builder.UseTexture(passData.sourceTh, AccessFlags.Read);
                builder.UseTexture(passData.blurFinalTh, AccessFlags.Read);
                builder.UseTexture(passData.obstructorTh, AccessFlags.Read);
                if (isHaveOccluder)
                {
                    builder.UseTexture(passData.occluderTh, AccessFlags.Read);
                }
                
                // 设置绘制方法。 // Set drawing method. // 描画メソッドを設定します。
                builder.SetRenderFunc((PassData data, RasterGraphContext context) => ExecutePassEffect(data, context));
            }
            #endregion
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
            Camera cam = data.cameraData.camera;
            // 使用非分配重载填充复用的视锥平面缓存。 // Fill the reusable frustum planes cache using the non-allocating overload. // 非割り当てオーバーロードで再利用する視錐台平面キャッシュを充填します。
            GeometryUtility.CalculateFrustumPlanes(cam, data.frustumPlanes);

            var mpb = data.mpb;
            var matrices = data.matricesCache;
            var colors = data.colorArrayCache;

            // 绘制所有流体粒子到 流体绘制RT。这里使用 GPU Instancing 来批量绘制。
            // Draw all fluid particles to fluid rendering RT. Here we use GPU Instancing for batch drawing.
            // すべての流体粒子を流体レンダリングRTに描画します。ここではGPUインスタンシングを使用してバッチ描画を行います。
            string nameTag = data.settings.nameTag;
            foreach (var kvp in Liquid2DFeature.ParticlesDic)
            {
                var settings = kvp.Key;
                var list = kvp.Value;
                // 跳过无效组（缺少贴图或材质则无法绘制）。 // Skip invalid groups (cannot draw without sprite or material). // 無効なグループをスキップ（スプライトまたはマテリアルがないと描画できません）。
                if (list.Count == 0 || !settings.sprite || !settings.material) continue;

                mpb.Clear();
                mpb.SetTexture(ShaderIds.MainTexId, settings.sprite.texture);

                // 填充流体粒子绘制用数据，按 1023 为上限分批绘制。
                // Fill in fluid particle drawing data, drawing in batches capped at 1023.
                // 流体粒子描画用データを入力し、1023を上限にバッチ描画します。
                int count = 0; // 当前批次已填充的粒子数量。 // Number of particles filled in the current batch. // 現在のバッチに充填された粒子数。
                for (int i = 0; i < list.Count; i++)
                {
                    var item = list[i];

                    // 跳过无效或禁用的粒子。 // Skip invalid or disabled particles. // 無効または無効になっている粒子をスキップします。
                    if (!item || !item.isActiveAndEnabled) continue;

                    // 当粒子 NameTag 不为空时，渲染相同 NameTag 的对象。
                    // When the particle NameTag is not empty, render objects with the same NameTag.
                    // 粒子のNameTagが空でない場合、同じNameTagを持つオブジェクトをレンダリングします。
                    if (!string.IsNullOrEmpty(item.RenderSettings.nameTag) && !item.RenderSettings.nameTag.Equals(nameTag)) continue;

                    var ts = item.TransformGet;

                    // 计算粒子的包围盒，不在相机视锥体内的不渲染。使用 lossyScale 以正确处理带缩放的父节点。
                    // Calculate the bounding box of the particle, do not render those not in the camera frustum. Use lossyScale to correctly handle scaled parents.
                    // 粒子の境界ボックスを計算し、カメラの視錐台内にないものは描画しません。スケールされた親を正しく扱うため lossyScale を使用します。
                    var bounds = new Bounds(ts.position, ts.lossyScale);
                    if (!GeometryUtility.TestPlanesAABB(data.frustumPlanes, bounds)) continue;

                    // 填充矩阵和颜色数据。使用 localToWorldMatrix 以包含父级层级的缩放/旋转。
                    // Fill in matrix and color data. Use localToWorldMatrix to include parent hierarchy scale/rotation.
                    // 行列と色データを入力します。親階層のスケール/回転を含めるため localToWorldMatrix を使用します。
                    matrices[count] = ts.localToWorldMatrix;
                    colors[count] = item.RenderSettings.color;
                    count++;

                    // 当前批次填满，先绘制一批再继续。 // Current batch is full; draw this batch before continuing. // 現在のバッチが満杯になったら、このバッチを描画してから続行します。
                    if (count == MaxInstancesPerBatch)
                    {
                        mpb.SetVectorArray(ShaderIds.ColorId, colors);
                        cmd.DrawMeshInstanced(data.quadMesh, 0, settings.material, 0, matrices, count, mpb);
                        count = 0;
                    }
                }

                // 绘制剩余的最后一批。 // Draw the remaining last batch. // 残りの最後のバッチを描画します。
                if (count > 0)
                {
                    mpb.SetVectorArray(ShaderIds.ColorId, colors);
                    cmd.DrawMeshInstanced(data.quadMesh, 0, settings.material, 0, matrices, count, mpb);
                }
            }
        }

        private void PassBlur(RenderGraph renderGraph, TextureHandle source, TextureHandle destination, int iteration, string passName)
        {
            using (var builder = renderGraph.AddRasterRenderPass<PassData>(passName, out PassData passData))
            {
                // 通道数据设置。 // Pass data setup.  // パスデータ設定。
                passData.settings = _settings;

                passData.materialBlur = _materialBlur;
                passData.blurSource = source;
                passData.blurIteration = iteration;
                passData.mpb = _mpbBlur;

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
                (0.5f + data.blurIteration * data.settings.blur.blurSpread) * 3f / (int)data.settings.blur.scaleFactor;

            // 设置模糊材质属性块，传入当前模糊材质和偏移强度。
            // Set blur material property block, pass in current blur material and offset intensity.
            // ブラーマテリアルプロパティブロックを設定し、現在のブラーマテリアルとオフセット強度を渡します。
            MaterialPropertyBlock mpb = data.mpb;
            mpb.Clear();
            mpb.SetTexture(ShaderIds.MainTexId,data.blurSource); // 传入当前模糊纹理。 // Pass in current blur texture. // 現在のブラーテクスチャを渡します。
            mpb.SetFloat(ShaderIds.BlurOffsetId, offset); // 设置模糊偏移强度。 // Set blur offset intensity. // ブラーオフセット強度を設定します。
            // 是否忽略背景色。 // Whether to ignore background color. // 背景色を無視するかどうか。
            SetKeyword(data.materialBlur, "_IGNORE_BG_COLOR", data.settings.blur.ignoreBgColor);

            // 绘制一个全屏三角形，使用模糊材质，并传入属性块。
            // Draw a full-screen triangle using blur material and pass in property block.
            // ブラー材質を使用して全画面三角形を描画し、プロ
            cmd.DrawProcedural(
                Matrix4x4.identity, data.materialBlur, 0,
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

            MaterialPropertyBlock mpb = data.mpb;
            mpb.Clear();
            mpb.SetTexture(ShaderIds.MainTexId, data.combineMainTh);
            mpb.SetTexture(ShaderIds.SecondTex, data.combineCoreTh);
            cmd.DrawProcedural(
                Matrix4x4.identity, data.combineMaterial, 0,
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
            MaterialPropertyBlock mpb = data.mpb;
            mpb.Clear();
            mpb.SetTexture(ShaderIds.MainTexId, data.blurFinalTh); // 流体纹理。 //Fluid texture. //流体テクスチャ。
            mpb.SetTexture(ShaderIds.ObstructorTex, data.obstructorTh); // 流体阻挡纹理。 //Fluid obstructor texture. //流体阻害テクスチャ。
            mpb.SetFloat(ShaderIds.Cutoff, data.settings.cutoff); // 裁剪阈值。 //Cutoff threshold. //カットオフ閾値。
            mpb.SetTexture(ShaderIds.BackgroundTex, data.sourceTh); // 背景纹理。用于扰动采样。 //Background texture. Used for distortion sampling. //背景テクスチャ。歪みサンプリングに使用。

            // 流体遮挡纹理。 // Fluid occluder texture. // 流体オクルーダーテクスチャ。
            SetKeyword(data.materialEffect, "_OCCLUDER_ENABLE", data.isHaveOccluder);
            if (data.isHaveOccluder)
            {
                mpb.SetTexture(ShaderIds.OccluderTex, data.occluderTh);
            }

            // 透明度调整参数。 // Opacity adjustment parameters. // 透明度調整パラメータ。
            SetKeyword(data.materialEffect, "_OPACITY_MULTIPLY", data.settings.opacityMode == EOpacityMode.Multiply);
            SetKeyword(data.materialEffect, "_OPACITY_REPLACE", data.settings.opacityMode == EOpacityMode.Replace);
            mpb.SetFloat(ShaderIds.OpacityValue, data.settings.opacityValue); // 透明度值。 // Opacity value. //透明度値。
            
            // 覆盖颜色。透明度值是强度。 // Cover color. Opacity value is intensity. // カバー色。透明度値は強度です。
            mpb.SetColor(ShaderIds.CoverColorId, data.settings.coverColor);

            SetKeyword(data.materialEffect, "_EDGE_ENABLE", data.settings.edge.enable);
            if (data.settings.edge.enable)
            {
                float cutoff = data.settings.cutoff;
                float edgeRange = data.settings.edge.edgeRange;
                float edgeIntensity = data.settings.edge.edgeIntensity;
                
                // 计算边缘参数。 // Calculate edge parameters. // エッジパラメータを計算します。
                float edgeStart = cutoff;
                float edgeEnd = cutoff + edgeRange * (1 - cutoff);
                float edgeMixStart = Mathf.Lerp(edgeStart, edgeEnd, edgeIntensity * 0.999f);
            
                mpb.SetFloat(ShaderIds.EdgeStart, edgeStart); // 边缘开始位置。 // Edge start position. // エッジ開始位置。
                mpb.SetFloat(ShaderIds.EdgeEnd, edgeEnd); // 边缘结束位置。 // Edge end position. // エッジ終了位置。
                // 边缘混合开始位置。用于 smoothstep 计算。 // Edge mix start position. Used for smoothstep calculation. // エッジミックス開始位置。smoothstep計算に使用。
                mpb.SetFloat(ShaderIds.EdgeMixStart, edgeMixStart);
                mpb.SetColor(ShaderIds.EdgeColor, data.settings.edge.edgeColor); // 边缘颜色。 // Edge color. // エッジカラー。

                bool lerpBlend = data.settings.edge.blendType == Liquid2DRenderFeatureSettings.Edge.EdgeBlendType.Lerp;
                SetKeyword(data.materialEffect, "_EDGE_BLEND_SA_OMSA", !lerpBlend);
                SetKeyword(data.materialEffect, "_EDGE_BLEND_LERP", lerpBlend);
            }

            // 像素风格化。 // Pixel stylization. // ピクセルスタイリゼーション。
            SetKeyword(data.materialEffect, "_PIXEL_ENABLE", data.settings.pixel.enable);
            if (data.settings.pixel.enable)
            {
                // 计算像素化尺寸。 // Calculate pixelation size. // ピクセル化サイズを計算します。
                float aspect = (float)Screen.width / Screen.height; // 屏幕宽高比。 // Screen aspect ratio. // 画面のアスペクト比。
                int pixelWidthCount = Screen.width / data.settings.pixel.pixelSize; // 水平像素块数量。 // Number of horizontal pixel blocks. // 水平ピクセルブロックの数。
                Vector2 pixelSize = new Vector2(pixelWidthCount, pixelWidthCount / aspect); // 计算垂直像素块数量。 // Calculate number of vertical pixel blocks. // 垂直ピクセルブロックの数を計算します。
                mpb.SetVector(ShaderIds.PixelSize, pixelSize); // 传入像素化尺寸。 // Pass in pixelation size. // ピクセル化サイズを渡します。

                // 是否使背景像素化。在水体透明时背景色也会像素化。
                // Whether to pixelate the background. When the water is transparent, the background color will also be pixelated.
                // 背景がピクセル化されるかどうか。水が透明な場合、背景色もピクセル化されます。
                SetKeyword(data.materialEffect, "_PIXEL_BG", data.settings.pixel.pixelBg);
            }

            // 水体扰动纹理和强度。 // Water distortion texture and intensity. // 水の歪みテクスチャと強度。
            bool distortEnable = data.settings.distort.enable
                                 // 完全不透明时不进行扰动。 // No distortion when completely opaque. // 完全に不透明な場合は歪みを行いません。
                                 && !(data.settings.opacityMode == EOpacityMode.Replace && data.settings.opacityValue >= 1f);
            SetKeyword(data.materialEffect, "_DISTORT_ENABLE", distortEnable);
            if (distortEnable)
            {
                mpb.SetFloat(ShaderIds.Magnitude, data.settings.distort.magnitude);
                mpb.SetFloat(ShaderIds.Frequency, data.settings.distort.frequency);
                mpb.SetFloat(ShaderIds.Amplitude, data.settings.distort.amplitude);
                mpb.SetVector(ShaderIds.DistortSpeed, data.settings.distort.distortSpeed);
                mpb.SetVector(ShaderIds.DistortTimeFactors, data.settings.distort.distortTimeFactors);
                mpb.SetFloat(ShaderIds.NoiseCoordOffset, data.settings.distort.noiseCoordOffset);
            }
            
            // 绘制一个全屏三角形，使用外描边材质，并传入属性块。
            // Draw a full-screen triangle using the outline material and pass in the property block.
            // 全画面三角形をアウトラインマテリアルで描画し、プロパティブロックを渡します。
            cmd.DrawProcedural(Matrix4x4.identity, data.materialEffect, 0, MeshTopology.Triangles, 3, 1,
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
                passData.grabAsBgMaterial = MaterialGrabAsBg;
                passData.mpb = _mpbGrabAsBg;
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
                    
                    MaterialPropertyBlock mpb = data.mpb;
                    mpb.Clear();
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
                _settings.obstructorRenderingLayerMask
            );
        }
        
        private void SetOccluderFilteringSettings()
        {
            _occluderFilteringSettings = new FilteringSettings 
            (
                RenderQueueRange.all,
                ~0,
                _settings.occluderRenderingLayerMask
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
                && volumeComponent.isActive.value
                && volumeComponent.liquid2DVolumeDataList.overrideState
                && volumeComponent.GetData(_settings.nameTag, out volumeData)
                && volumeData != null && volumeData.isActive;

            // ---- 重载设置。 // Override settings. // 設定をオーバーライド。 ---- //
            // 2D流体层遮罩。只会渲染设定的流体层的粒子。
            // 2D Liquid layer mask. Only particles of the set fluid layer will be rendered.
            // 2D流体レイヤーマスク。設定された流体レイヤーの粒子のみがレンダリングされます。
            _settings.nameTag = isActive ? volumeData.nameTag : _settingsDefault.nameTag;
            
            // 阻挡层遮罩。 // Obstructor layer mask. // 阻害レイヤーマスク。
            _settings.obstructorRenderingLayerMask = isActive ? volumeData.obstructorRenderingLayerMask : _settingsDefault.obstructorRenderingLayerMask;
            if (_obstructorFilteringSettings.layerMask != _settings.obstructorRenderingLayerMask)
            {
                SetObstructorFilteringSettings();
            }
            // 遮挡层遮罩。 // Occlusion layer mask. // オクルージョンレイヤーマスク。
            _settings.occluderRenderingLayerMask = isActive ? volumeData.occluderRenderingLayerMask : _settingsDefault.occluderRenderingLayerMask;
            if (_occluderFilteringSettings.layerMask != _settings.occluderRenderingLayerMask)
            {
                SetOccluderFilteringSettings();
            }
            
            _settings.cutoff = isActive ? volumeData.cutoff : _settingsDefault.cutoff;
            
            _settings.opacityMode = isActive ? volumeData.opacityMode : _settingsDefault.opacityMode;
            _settings.opacityValue = isActive ? volumeData.opacityValue : _settingsDefault.opacityValue;
            _settings.coverColor = isActive ? volumeData.coverColor : _settingsDefault.coverColor;
            
            // ---- 边缘设置 // Edge settings // エッジ設定 ---- //
            _settings.edge.enable = isActive ? volumeData.edge.enable : _settingsDefault.edge.enable;
            _settings.edge.edgeRange = isActive ? volumeData.edge.edgeRange : _settingsDefault.edge.edgeRange;
            _settings.edge.edgeIntensity = isActive ? volumeData.edge.edgeIntensity : _settingsDefault.edge.edgeIntensity;
            _settings.edge.edgeColor = isActive ? volumeData.edge.edgeColor : _settingsDefault.edge.edgeColor;
            _settings.edge.blendType = isActive ? volumeData.edge.blendType : _settingsDefault.edge.blendType;

            // ---- 模糊设置 // Blur settings // ブラー設定 ---- //
            _settings.blur.iterations = isActive ? volumeData.blur.iterations : _settingsDefault.blur.iterations;
            _settings.blur.blurSpread = isActive ? volumeData.blur.blurSpread : _settingsDefault.blur.blurSpread;
            _settings.blur.coreKeepIntensity = isActive ? volumeData.blur.coreKeepIntensity : _settingsDefault.blur.coreKeepIntensity;
            _settings.blur.scaleFactor = isActive ? volumeData.blur.scaleFactor : _settingsDefault.blur.scaleFactor;
            _settings.blur.ignoreBgColor = isActive ? volumeData.blur.ignoreBgColor : _settingsDefault.blur.ignoreBgColor;
            // 模糊背景色和强度。实际上在 Blur 前作为底图进行混合。默认的底图是当前相机的场景纹理（alpha为0）。
            // Blur background color and intensity. In fact, it is blended as a base map before Blur. The default base map is the current camera scene texture (alpha is 0).
            // ブラーバックグラウンドカラーと強度。実際には、Blurの前にベースマップとしてブレンドされます。デフォルトのベースマップは現在のカメラシーンテクスチャ（アルファは0）です。
            _settings.blur.blurBgColor = isActive ? volumeData.blur.blurBgColor : _settingsDefault.blur.blurBgColor;
            _settings.blur.blurBgColorIntensity = isActive ? volumeData.blur.blurBgColorIntensity : _settingsDefault.blur.blurBgColorIntensity;
            
            // ---- 水体扰动设置 // Water distortion settings // 水の歪み設定 ---- //
            _settings.distort.enable = isActive ? volumeData.distort.enable : _settingsDefault.distort.enable;
            _settings.distort.frequency = isActive ? volumeData.distort.frequency : _settingsDefault.distort.frequency;
            _settings.distort.amplitude = isActive ? volumeData.distort.amplitude : _settingsDefault.distort.amplitude;
            _settings.distort.distortSpeed = isActive ? volumeData.distort.distortSpeed : _settingsDefault.distort.distortSpeed;
            _settings.distort.distortTimeFactors = isActive ? volumeData.distort.distortTimeFactors : _settingsDefault.distort.distortTimeFactors;
            _settings.distort.noiseCoordOffset = isActive ? volumeData.distort.noiseCoordOffset : _settingsDefault.distort.noiseCoordOffset;
            
            // ---- 像素化 // Pixelation // ピクセル化 ---- //
            _settings.pixel.enable = isActive ? volumeData.pixel.enable : _settingsDefault.pixel.enable;
            _settings.pixel.pixelSize = isActive ? volumeData.pixel.pixelSize : _settingsDefault.pixel.pixelSize;
            _settings.pixel.pixelBg = isActive ? volumeData.pixel.pixelBg : _settingsDefault.pixel.pixelBg;
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
        /// 生成一个简单的四边形网格。用于全屏渲染。
        /// Generate a simple quad mesh. Used for full-screen rendering.
        /// シンプルな四辺形メッシュを生成します。全画面レンダリングに使用されます。
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

        // Pass 名缓存。按 name 缓存完整 Pass 名，避免每帧字符串插值分配；nameTag 变化时清空。
        // Pass name cache. Caches full pass names by name to avoid per-frame string interpolation allocation; cleared when nameTag changes.
        // Pass名キャッシュ。nameをキーに完全なPass名をキャッシュし、毎フレームの文字列補間割り当てを回避。nameTag変更時にクリア。
        private readonly Dictionary<string, string> _nameCache = new Dictionary<string, string>();
        private string _nameCacheTag;

        private string GetName(string name)
        {
            if (_nameCacheTag != _settings.nameTag)
            {
                _nameCache.Clear();
                _nameCacheTag = _settings.nameTag;
            }

            if (!_nameCache.TryGetValue(name, out var full))
            {
                full = $"[Liquid 2D] [{_settings.nameTag}] {name}";
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
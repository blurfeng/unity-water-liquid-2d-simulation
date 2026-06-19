using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 测试用粒子显示组件（正式流程是走 Renderer 2D Data 中配置的 Feature）。运行时流体粒子独立可视化组件。通过 <c>Graphics.DrawMeshInstancedIndirect</c> 将所有活跃粒子渲染到场景，
    /// 数据直接来自 <see cref="Liquid2DSimulation"/> SoA，不依赖 URP Render Feature。
    /// 支持模拟颜色与速度渐变两种着色模式；配套着色器 <c>Custom/URP/2D/Liquid2DParticleDisplay</c>。
    /// Runtime standalone fluid-particle visualization. Renders all active particles via
    /// <c>Graphics.DrawMeshInstancedIndirect</c>, reading data directly from <see cref="Liquid2DSimulation"/> SoA
    /// without a URP Render Feature. Supports simulation-colour and velocity-gradient shading modes;
    /// companion shader: <c>Custom/URP/2D/Liquid2DParticleDisplay</c>.
    /// ランタイム流体パーティクル独立可視化。<c>Graphics.DrawMeshInstancedIndirect</c> でアクティブ粒子を描画。
    /// URP Render Feature 不要で <see cref="Liquid2DSimulation"/> SoA から直接データを取得。
    /// シミュレーション色・速度グラデーションの 2 モードに対応；専用シェーダー <c>Custom/URP/2D/Liquid2DParticleDisplay</c>。
    /// </summary>
    [AddComponentMenu("Liquid2D/Liquid2D Particle Display")]
    public class Liquid2DParticleDisplay : MonoBehaviour
    {
        // ── 总开关 Master toggle マスタースイッチ ──────────────────────────────
        [SerializeField, LocalizationTooltip(
            "总开关：关闭后停止绘制，GPU 缓冲保留不释放。",
            "Master toggle: stops rendering when off; GPU buffers are retained.",
            "マスタースイッチ：オフで描画停止、GPU バッファは保持されます。")]
        private bool displayEnabled = true;

        // ── 渲染资源 Rendering resources 描画リソース ──────────────────────────
        [Header("Rendering")]
        [SerializeField, LocalizationTooltip(
            "粒子 Quad 网格（建议：XY 平面单位正方形，UV [0,1]×[0,1]）。",
            "Particle quad mesh (recommended: unit square on XY plane, UV [0,1]×[0,1]).",
            "パーティクル Quad メッシュ（推奨：XY 平面の単位正方形、UV [0,1]×[0,1]）。")]
        private Mesh mesh;

        [SerializeField, LocalizationTooltip(
            "使用 Custom/URP/2D/Liquid2DParticleDisplay 着色器的材质。",
            "Material using the Custom/URP/2D/Liquid2DParticleDisplay shader.",
            "Custom/URP/2D/Liquid2DParticleDisplay シェーダーを使用するマテリアル。")]
        private Material material;

        [SerializeField, LocalizationTooltip(
            "可视尺寸全局倍率（最终可视半径 = radius × renderScale × 此值）。",
            "Global visual scale multiplier (final visual radius = radius × renderScale × this).",
            "可視サイズ全体倍率（最終可視半径 = radius × renderScale × この値）。")]
        private float scale = 0.4f;

        // ── 颜色模式 Colour mode カラーモード ──────────────────────────────────
        [Header("Colour")]
        [SerializeField, LocalizationTooltip(
            "颜色模式：Simulation=粒子模拟自身颜色；VelocityGradient=按速度大小映射渐变色。",
            "Colour mode: Simulation = per-particle simulation colour; VelocityGradient = map speed to gradient.",
            "カラーモード：Simulation=粒子自身の色；VelocityGradient=速さでグラデーション色。")]
        private ColourMode colourMode = ColourMode.Simulation;

        [SerializeField, LocalizationTooltip(
            "速度渐变色（ColourMode = VelocityGradient 时生效）。",
            "Velocity colour gradient (active when ColourMode = VelocityGradient).",
            "速度グラデーション色（ColourMode = VelocityGradient のとき有効）。")]
        private Gradient colourMap;

        [SerializeField, Min(2), LocalizationTooltip(
            "渐变贴图宽度（像素）。",
            "Gradient texture width in pixels.",
            "グラデーションテクスチャの幅（ピクセル）。")]
        private int gradientResolution = 64;

        [SerializeField, LocalizationTooltip(
            "速度渐变上限：速度达到或超过此值时显示渐变末端颜色。",
            "Velocity gradient upper bound: at or above this speed the gradient end colour is shown.",
            "速度グラデーション上限：この速度以上でグラデーション末端色を表示。")]
        private float velocityDisplayMax = 10f;

        // ── 颜色模式枚举 ColourMode enum カラーモード列挙 ──────────────────────
        /// <summary>
        /// 粒子颜色显示模式。
        /// Particle colour display mode.
        /// パーティクルカラー表示モード。
        /// </summary>
        public enum ColourMode
        {
            /// <summary>使用粒子模拟自身颜色（RGBA）。 // Use per-particle simulation colour (RGBA). // 粒子自身のシミュレーション色（RGBA）。</summary>
            Simulation = 0,
            /// <summary>按速度大小映射渐变色图。 // Map speed to gradient colour map. // 速さでグラデーションカラーマップにマッピング。</summary>
            VelocityGradient = 1,
        }

        // ── 内部状态 Internal state 内部状態 ────────────────────────────────────
        private GraphicsBuffer _positionBuffer;
        private GraphicsBuffer _velocityBuffer;
        private GraphicsBuffer _colorBuffer;
        private GraphicsBuffer _scaleBuffer;
        private GraphicsBuffer _argsBuffer;

        private Vector2[] _positions;
        private Vector2[] _velocities;
        private Vector4[] _colors;
        private float[]   _scales;
        private readonly uint[] _argsData = new uint[5];

        private int       _bufferCapacity = -1;
        private Texture2D _gradientTexture;
        private bool      _gradientDirty = true;

        private static readonly int _idPositions   = Shader.PropertyToID("_Positions");
        private static readonly int _idVelocities  = Shader.PropertyToID("_Velocities");
        private static readonly int _idColors      = Shader.PropertyToID("_Colors");
        private static readonly int _idScales      = Shader.PropertyToID("_Scales");
        private static readonly int _idColourMap   = Shader.PropertyToID("_ColourMap");
        private static readonly int _idVelocityMax = Shader.PropertyToID("_VelocityMax");
        private static readonly int _idColourMode  = Shader.PropertyToID("_ColourMode");

        private void LateUpdate()
        {
            if (!displayEnabled) return;
            if (mesh == null || material == null) return;
            if (!Application.isPlaying) return;

            if (!Liquid2DSimulation.TryGetRenderData(
                    out Liquid2DParticleStore store,
                    out NativeArray<int> active,
                    out int activeCount,
                    out IReadOnlyList<Liquid2DParticleDescriptor> descriptors))
                return;

            EnsureBufferCapacity(activeCount);
            FillArrays(store, active, activeCount, descriptors);
            UploadBuffers(activeCount);
            ApplyMaterial(activeCount);

            var bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);
            Graphics.DrawMeshInstancedIndirect(mesh, 0, material, bounds, _argsBuffer);
        }

        // ── 容量管理 Capacity management 容量管理 ─────────────────────────────

        private void EnsureBufferCapacity(int needed)
        {
            if (_positionBuffer != null && _bufferCapacity >= needed) return;

            ReleaseBuffers();
            // 向上取2的幂次以减少频繁重建。 // Round up to next power-of-two to reduce rebuilds. // 再構築を減らすため次の 2 の累乗に切り上げ。
            int cap = Mathf.Max(64, Mathf.NextPowerOfTwo(needed));
            _positions  = new Vector2[cap];
            _velocities = new Vector2[cap];
            _colors     = new Vector4[cap];
            _scales     = new float[cap];

            // stride: float2=8, float4=16, float=4 bytes.
            _positionBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 8);
            _velocityBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 8);
            _colorBuffer    = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 16);
            _scaleBuffer    = new GraphicsBuffer(GraphicsBuffer.Target.Structured, cap, 4);
            _argsBuffer     = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 5 * sizeof(uint));
            _bufferCapacity = cap;
        }

        // ── 数据填充 Data fill データ充填 ────────────────────────────────────

        private void FillArrays(
            Liquid2DParticleStore store,
            NativeArray<int> active,
            int activeCount,
            IReadOnlyList<Liquid2DParticleDescriptor> descriptors)
        {
            for (int i = 0; i < activeCount; i++)
            {
                int slot = active[i];

                float2 pos = store.positions[slot];
                float2 vel = store.velocities[slot];
                float4 col = store.colors[slot];
                float  rad = store.radii[slot];

                // 从描述符取 renderScale（默认 1）。
                // Look up renderScale from descriptor (default 1).
                // 記述子から renderScale を取得（既定 1）。
                float renderScale = 1f;
                int   tid = store.typeId[slot];
                if (descriptors != null && tid >= 0 && tid < descriptors.Count && descriptors[tid] != null)
                    renderScale = descriptors[tid].renderScale;

                _positions[i]  = new Vector2(pos.x, pos.y);
                _velocities[i] = new Vector2(vel.x, vel.y);
                _colors[i]     = new Vector4(col.x, col.y, col.z, col.w);
                _scales[i]     = rad * renderScale * scale; // 可视半径 = 物理半径 × renderScale × 全局系数。 // visual radius. // 可視半径。
            }
        }

        // ── GPU 上传 GPU upload GPU アップロード ──────────────────────────────

        private void UploadBuffers(int activeCount)
        {
            _positionBuffer.SetData(_positions,  0, 0, activeCount);
            _velocityBuffer.SetData(_velocities, 0, 0, activeCount);
            _colorBuffer.SetData(   _colors,     0, 0, activeCount);
            _scaleBuffer.SetData(   _scales,     0, 0, activeCount);

            // DrawMeshInstancedIndirect args: [indexCount, instanceCount, indexStart, baseVertex, startInstance]
            _argsData[0] = mesh.GetIndexCount(0);
            _argsData[1] = (uint)activeCount;
            _argsData[2] = mesh.GetIndexStart(0);
            _argsData[3] = mesh.GetBaseVertex(0);
            _argsData[4] = 0u;
            _argsBuffer.SetData(_argsData);
        }

        // ── 材质参数 Material params マテリアルパラメータ ─────────────────────

        private void ApplyMaterial(int activeCount)
        {
            material.SetBuffer(_idPositions,  _positionBuffer);
            material.SetBuffer(_idVelocities, _velocityBuffer);
            material.SetBuffer(_idColors,     _colorBuffer);
            material.SetBuffer(_idScales,     _scaleBuffer);
            material.SetFloat(_idVelocityMax, velocityDisplayMax);
            material.SetInt(_idColourMode,    (int)colourMode);

            if (colourMode == ColourMode.VelocityGradient)
            {
                if (_gradientDirty)
                {
                    _gradientDirty = false;
                    BakeGradient(ref _gradientTexture, gradientResolution, colourMap);
                }
                material.SetTexture(_idColourMap, _gradientTexture);
            }
        }

        // ── 工具 Utility ユーティリティ ───────────────────────────────────────

        /// <summary>
        /// 将 Gradient 烘焙成 Texture2D（参照 ParticleDisplay2D.TextureFromGradient）。
        /// Bake a Gradient into a Texture2D (mirrors ParticleDisplay2D.TextureFromGradient).
        /// Gradient を Texture2D にベイク（ParticleDisplay2D.TextureFromGradient を参照）。
        /// </summary>
        public static void BakeGradient(ref Texture2D texture, int width,
            Gradient gradient, FilterMode filterMode = FilterMode.Bilinear)
        {
            width = Mathf.Max(2, width);
            if (texture == null || texture.width != width)
            {
                texture = new Texture2D(width, 1, TextureFormat.RGBA32, false)
                {
                    wrapMode   = TextureWrapMode.Clamp,
                    filterMode = filterMode,
                };
            }

            gradient ??= new Gradient();
            var cols = new Color[width];
            for (int i = 0; i < width; i++)
                cols[i] = gradient.Evaluate(i / (width - 1f));

            texture.SetPixels(cols);
            texture.Apply();
        }

        // ── Unity 回调 Unity callbacks Unity コールバック ──────────────────────

        private void OnValidate()
        {
            _gradientDirty = true;
        }

        private void OnDestroy()
        {
            ReleaseBuffers();
            if (_gradientTexture != null)
            {
                Destroy(_gradientTexture);
                _gradientTexture = null;
            }
        }

        private void ReleaseBuffers()
        {
            _positionBuffer?.Dispose(); _positionBuffer = null;
            _velocityBuffer?.Dispose(); _velocityBuffer = null;
            _colorBuffer?.Dispose();    _colorBuffer    = null;
            _scaleBuffer?.Dispose();    _scaleBuffer    = null;
            _argsBuffer?.Dispose();     _argsBuffer     = null;
            _bufferCapacity = -1;
        }
    }
}


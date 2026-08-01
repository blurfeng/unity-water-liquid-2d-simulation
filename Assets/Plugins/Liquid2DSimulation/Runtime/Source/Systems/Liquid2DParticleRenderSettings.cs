using System;
using UnityEngine;
using Fs.Liquid2D.Localization;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体粒子渲染器设置。
    /// Fluid particle renderer settings.
    /// 流体粒子レンダラー設定。
    /// </summary>
    [Serializable]
    public class Liquid2DParticleRenderSettings
    {
        [LocalizationTooltip(
             "流体粒子贴图。",
             "Fluid particle sprite texture.",
             "流体パーティクルスプライトテクスチャ。")]
        public Sprite Sprite;

        [LocalizationTooltip(
             "流体粒子材质。",
             "Fluid particle material.",
             "流体パーティクルマテリアル。")]
        public Material Material;
    
        [ColorUsage(true, true), LocalizationTooltip(
             "流体粒子颜色。仅 ColorMode=Simple（简单模式）时作为每粒子基础色（并叠加运行时混色）；Gradient 模式下不使用此颜色（由渐变决定）。",
             "Fluid particle color. Used as the per-particle base color (with runtime mixing) only when ColorMode=Simple; ignored in Gradient mode (the gradient decides the color).",
             "流体パーティクルカラー。ColorMode=Simple（シンプル）のときのみ粒子ごとの基礎色として使用（実行時混色を加味）。Gradient モードでは使用されません（グラデーションが色を決定）。")]
        public Color Color = new Color(0f, 1f, 4f, 1f);

        [LocalizationTooltip(
             "粒子颜色模式。Simple（简单）=使用上方 Color（+运行时混色），默认行为；Gradient=按每粒子标量（速度/空气混入量）采样下方渐变，此时不使用 Color 与运行时混色。",
             "Particle color mode. Simple = use the Color above (+ runtime mixing), the default behavior; Gradient = sample the gradient below by a per-particle scalar (speed / trapped-air), which ignores Color and runtime mixing.",
             "粒子カラーモード。Simple（シンプル）= 上の Color を使用（+実行時混色）、既定動作。Gradient = 下の Gradient を粒子ごとのスカラー（速度/混入空気量）でサンプリング（Color と実行時混色は不使用）。")]
        public EParticleColorMode ColorMode = EParticleColorMode.Simple;

        [LocalizationTooltip(
             "颜色渐变（仅 ColorMode=Gradient 生效）。按每粒子标量从左(0)到右(1)采样，支持 HDR。",
             "Color gradient (effective only when ColorMode=Gradient). Sampled left(0)→right(1) by a per-particle scalar; HDR supported.",
             "カラーグラデーション（ColorMode=Gradient のときのみ有効）。粒子ごとのスカラーで左(0)→右(1)をサンプリング、HDR 対応。")]
        public Gradient ColorGradient = new Gradient();

        [LocalizationTooltip(
             "渐变标量来源（仅 Gradient 生效）。Speed=按速度大小；Foam=按空气混入量（SPH 密度亏空，模拟真实泡沫在表面/飞溅处产生）。",
             "Gradient scalar source (Gradient mode only). Speed = by velocity magnitude; Foam = by trapped-air (SPH density deficit, mimics real foam forming at surface/spray).",
             "グラデーションのスカラーソース（Gradient のみ）。Speed = 速度の大きさ、Foam = 混入空気量（SPH 密度不足、表面/飛沫での実際の泡立ちを模倣）。")]
        public EGradientColorSource GradientSource = EGradientColorSource.Speed;

        [Min(0f), LocalizationTooltip(
             "Speed / FoamWithSpeed 模式：速度归一化下限（世界单位/秒）。速度低于此值视作 0（t=0），使缓慢移动的流体不产生颜色/泡沫，表现更稳定。应小于 Speed Max。",
             "Speed / FoamWithSpeed mode: speed normalization lower bound (world units/sec). Speed below this maps to 0 (t=0), so slowly moving fluid produces no color/foam, appearing more stable. Should be less than Speed Max.",
             "Speed / FoamWithSpeed モード：速度正規化の下限（ワールド単位/秒）。これ未満の速度は 0（t=0）とみなし、ゆっくり動く流体は色/泡を出さず安定します。Speed Max より小さくします。")]
        public float GradientSpeedMin = 0.2f;

        [Min(0.0001f), LocalizationTooltip(
             "Speed / FoamWithSpeed 模式：速度归一化上限（世界单位/秒）。t = saturate((|velocity| − Speed Min) / (Speed Max − Speed Min))。FoamWithSpeed 下为「泡沫达到满强度所需的速度」。",
             "Speed / FoamWithSpeed mode: speed normalization upper bound (world units/sec). t = saturate((|velocity| − Speed Min) / (Speed Max − Speed Min)). In FoamWithSpeed it is the speed at which foam reaches full strength.",
             "Speed / FoamWithSpeed モード：速度正規化の上限（ワールド単位/秒）。t = saturate((|velocity| − Speed Min) / (Speed Max − Speed Min))。FoamWithSpeed では泡が最大になる速度です。")]
        public float GradientSpeedMax = 10f;

        [Range(0f, 2f), LocalizationTooltip(
             "Foam 模式：起泡上界。密度比（密度/静止密度）低于此值开始起泡。典型自由表面≈0.5，内部≈1。",
             "Foam mode: foam upper bound. Foam begins where density ratio (density/rest) drops below this. Free surface≈0.5, interior≈1.",
             "Foam モード：泡立ちの上界。密度比（密度/静止密度）がこれを下回ると泡立ち開始。自由表面≈0.5、内部≈1。")]
        public float GradientFoamStart = 1.1f;

        [Range(0f, 2f), LocalizationTooltip(
             "Foam 模式：满泡下界。密度比低到此值泡沫拉满（t=1）。典型飞溅/水滴≈0.1~0.2。应小于 FoamStart。",
             "Foam mode: full-foam lower bound. Foam saturates (t=1) at this density ratio. Spray/droplet≈0.1~0.2. Should be less than FoamStart.",
             "Foam モード：満泡の下界。密度比がこの値まで下がると泡が最大（t=1）。飛沫/水滴≈0.1~0.2。FoamStart より小さくします。")]
        public float GradientFoamEnd = 1f;

        [Range(0f, 1f), LocalizationTooltip(
             "渐变时间平滑量（仅 ColorMode=Gradient 生效，逐流体独立）。对渲染用的密度与速度按 EMA 每帧平滑，消除 SPH 逐帧抖动引起的颜色闪烁。0=不平滑（可能闪烁）；越大越平滑但对变化响应越慢。不影响物理。",
             "Gradient temporal smoothing (effective only when ColorMode=Gradient; per-fluid). EMA-smooths the render density and speed each frame to remove color flicker from per-frame SPH jitter. 0 = no smoothing (may flicker); higher = smoother but slower to respond. Does not affect physics.",
             "グラデーションの時間平滑（ColorMode=Gradient のみ、流体ごと）。レンダー用の密度と速度を EMA で平滑し、SPH の毎フレームのちらつきを除去。0=平滑なし（ちらつく可能性）、大きいほど滑らかだが応答が遅い。物理には影響しません。")]
        public float GradientSmoothing = 0.93f;

        [LocalizationTooltip(
            "2D流体 Renderer Feature 名称标签，用于区分不同的 Renderer Feature 配置对应的流体粒子。如果你要使用 Volume 来控制流体效果，请确保名称标签唯一且和 Volume Profile 中的标签一致。",
            "2D fluid Renderer Feature name tag, used to distinguish fluid particles corresponding to different Renderer Feature configurations. If you want to use Volume to control fluid effects, please ensure that the name tag is unique and consistent with the tag in the Volume Profile.",
            "2D流体レンダラーフィーチャーの名前タグ。異なるレンダラーフィーチャー構成に対応する流体パーティクルを区別するために使用されます。ボリュームを使用して流体効果を制御する場合は、名前タグが一意であり、ボリュームプロファイルのタグと一致していることを確認してください。")]
        public string NameTag = "Liquid2D";

        // 无参构造函数：必须存在，否则 Unity 自动实例化本可序列化嵌套类时不会调用任何构造函数，
        // 上方字段初始化器（C# 中编译进构造函数）不执行，所有默认值（Color / ColorMode / GradientSpeedMax /
        // GradientFoamStart / GradientFoamEnd 等）会落成类型零值（如 Foam Start/End = 0，映射退化）。
        // Parameterless constructor: required. Without it, Unity auto-creates this serialized nested class without calling any
        // constructor, so the field initializers above (compiled into constructors in C#) never run and every default (Color /
        // ColorMode / GradientSpeedMax / GradientFoamStart / GradientFoamEnd, ...) falls back to the type zero (e.g. Foam
        // Start/End = 0, a degenerate mapping).
        // 引数なしコンストラクタ：必須。無いと Unity が本シリアライズ入れ子クラスをコンストラクタ無しで生成し、上のフィールド
        // 初期化子（C# ではコンストラクタに組み込まれる）が実行されず、既定値がすべて型の 0 に落ちます。
        public Liquid2DParticleRenderSettings() { }

        public Liquid2DParticleRenderSettings(Sprite sprite, Material material)
        {
            Sprite = sprite;
            Material = material;
        }

        /// <summary>
        /// 检查渲染器设置是否有效。
        /// Check if renderer settings are valid.
        /// レンダラー設定が有効かチェック。
        /// </summary>
        /// <returns></returns>
        public bool IsValid()
        {
            return Sprite && Material;
        }

        #region Gradient LUT 渐变查找表 // Gradient lookup table // グラデーション LUT

        // 渐变烘焙成查找表：CPU 绘制路径用 Color[] 直接按 t 取色；GPU 绘制路径用 256×1 RGBAHalf 纹理在 shader 内采样。
        // 懒烘焙 + 脏标记：仅在首次使用或渐变改动（编辑器 OnValidate 触发 Invalidate）时重建，避免每帧分配。
        // Gradient baked into a lookup table: the CPU draw path indexes a Color[] by t; the GPU draw path samples a 256×1
        // RGBAHalf texture in-shader. Lazy bake + dirty flag: rebuilt only on first use or gradient change (editor OnValidate
        // triggers Invalidate), avoiding per-frame allocation.
        // グラデーションを LUT に焼き込み：CPU は Color[]、GPU は 256×1 RGBAHalf テクスチャ。遅延焼き込み + ダーティフラグ。
        private const int GradientLutSize = 256;
        [NonSerialized] private Color[] _lutCpu;
        [NonSerialized] private Texture2D _lutGpu;
        [NonSerialized] private bool _lutDirty = true;

        /// <summary>标记渐变 LUT 失效（渐变/相关参数改动后调用，下次使用时重建）。 // Mark the gradient LUT dirty (rebuilt on next use). // LUT をダーティに。</summary>
        public void InvalidateGradientLut() => _lutDirty = true;

        /// <summary>
        /// 确保 LUT 已烘焙（须在主线程调用——会创建 Texture2D）。应在渲染 Pass 的 Record 阶段预热，勿在渲染执行回调内首次创建纹理。
        /// Ensure the LUT is baked (must be called on the main thread — creates a Texture2D). Warm it up in the render Pass's
        /// Record phase; do not first-create the texture inside a render exec callback.
        /// LUT が焼き込み済みであることを保証（メインスレッドで呼ぶ——Texture2D を作成）。
        /// </summary>
        public void EnsureGradientLut()
        {
            if (!_lutDirty && _lutCpu != null && _lutGpu) return;
            if (ColorGradient == null) ColorGradient = new Gradient();

            if (_lutCpu == null || _lutCpu.Length != GradientLutSize) _lutCpu = new Color[GradientLutSize];
            // 烘焙时即把手调的 sRGB 渐变色转成「上传值」（线性项目做 sRGB→linear），与 store 逐粒子色的上传边界、以及 _CoverColor 走的 SetColor 完全对齐，
            // 使渐变色与 CoverColor 在相同取值下表现一致。CPU 与 GPU 渐变路径共用本 LUT，一次烘焙即两端到位（绘制时不再逐粒子转换）。
            // Bake the authored sRGB gradient colors into upload values here (sRGB→linear in linear projects), aligned with the
            // per-particle store color's upload boundary and _CoverColor's SetColor, so gradient and cover colors match at the same
            // authored value. The CPU and GPU gradient paths share this LUT — one bake covers both (no per-particle conversion at draw).
            // 焼き込み時に手調整の sRGB 渐变色を「アップロード値」へ変換（線形項目では sRGB→linear）。store 粒子色や _CoverColor と整合。
            for (int i = 0; i < GradientLutSize; i++)
                _lutCpu[i] = Liquid2DColorSpace.ToGpuUpload(ColorGradient.Evaluate(i / (float)(GradientLutSize - 1)));

            if (!_lutGpu)
            {
                // linear=true：LUT 已在上方按色彩空间烘焙为「上传值」（线性项目为 linear），故纹理数据本身即为最终值，采样不再做 sRGB 转换。
                // linear=true: the LUT already holds upload-space values baked above (linear in linear projects), so the texture data is final and sampling does no sRGB conversion.
                // linear=true：LUT は上で「アップロード値」に焼き込み済み（線形項目では linear）、サンプリングで sRGB 変換なし。
                _lutGpu = new Texture2D(GradientLutSize, 1, TextureFormat.RGBAHalf, false, true)
                {
                    name = "Liquid2DGradientLut",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            _lutGpu.SetPixels(_lutCpu);
            _lutGpu.Apply(false);
            _lutDirty = false;
        }

        /// <summary>按标量 t（0..1）从 CPU LUT 取渐变色（CPU 绘制路径用）。 // Sample the gradient color from the CPU LUT by scalar t (0..1). // スカラー t で CPU LUT から色を取得。</summary>
        public Color EvaluateGradientCpu(float t)
        {
            EnsureGradientLut();
            int idx = Mathf.Clamp((int)(Mathf.Clamp01(t) * (GradientLutSize - 1) + 0.5f), 0, GradientLutSize - 1);
            return _lutCpu[idx];
        }

        /// <summary>获取渐变 LUT 纹理（GPU 绘制路径 shader 采样用）。 // Get the gradient LUT texture (for GPU shader sampling). // グラデーション LUT テクスチャを取得。</summary>
        public Texture2D GetGradientLut()
        {
            EnsureGradientLut();
            return _lutGpu;
        }

        #endregion
    }
}
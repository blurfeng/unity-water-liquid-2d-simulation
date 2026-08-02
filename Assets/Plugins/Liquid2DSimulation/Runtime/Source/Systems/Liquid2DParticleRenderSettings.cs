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
        public Color Color = new Color(0f, 0.4f, 1f, 1f);

        [LocalizationTooltip(
             "粒子颜色模式。Simple（简单）=使用上方 Color（+运行时混色），默认行为；Gradient=按每粒子标量（速度/空气混入量）采样下方渐变，此时不使用 Color 与运行时混色。",
             "Particle color mode. Simple = use the Color above (+ runtime mixing), the default behavior; Gradient = sample the gradient below by a per-particle scalar (speed / trapped-air), which ignores Color and runtime mixing.",
             "粒子カラーモード。Simple（シンプル）= 上の Color を使用（+実行時混色）、既定動作。Gradient = 下の Gradient を粒子ごとのスカラー（速度/混入空気量）でサンプリング（Color と実行時混色は不使用）。")]
        public EParticleColorMode ColorMode = EParticleColorMode.Simple;

        [GradientUsage(true), LocalizationTooltip(
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
             "Speed / DensityWithSpeed 模式：速度归一化下限（世界单位/秒）。速度低于此值视作 0，使缓慢移动的流体不出色/不起泡。应小于 Speed Max。",
             "Speed / DensityWithSpeed mode: speed normalization lower bound (world units/sec). Speed below this maps to 0, so slowly moving fluid produces no color/foam. Should be less than Speed Max.",
             "Speed / DensityWithSpeed モード：速度正規化の下限（ワールド単位/秒）。これ未満は 0 とみなし、ゆっくり動く流体は色/泡を出しません。Speed Max より小さく。")]
        public float GradientSpeedMin = 0.2f;

        [Min(0.0001f), LocalizationTooltip(
             "Speed / DensityWithSpeed 模式：速度归一化上限（世界单位/秒）。gate = saturate((|velocity| − Speed Min) / (Speed Max − Speed Min))。Speed 用于采色，DensityWithSpeed 用作速度门控。",
             "Speed / DensityWithSpeed mode: speed normalization upper bound (world units/sec). gate = saturate((|velocity| − Speed Min) / (Speed Max − Speed Min)). Speed uses it for color; DensityWithSpeed as the speed gate.",
             "Speed / DensityWithSpeed モード：速度正規化の上限（ワールド単位/秒）。gate = saturate((|velocity| − Speed Min) / (Speed Max − Speed Min))。")]
        public float GradientSpeedMax = 10f;

        [Range(0f, 2f), LocalizationTooltip(
             "Density / DensityWithImpact / DensityWithSpeed 模式：有效密度上界（起泡/有效区域的上界）。密度比高于此值视作内部/水底（不起泡/无效区域）。⚠关键：把它卡在你的「内部/水底密度比」略下方（水底往往≈1.0，则设 0.95~1.0），才能把内部排除；设太高（如 1.1）会把水底也框进来。可先用 Density 模式配渐变观察密度分布来定位。",
             "Density / DensityWithImpact / DensityWithSpeed mode:valid-density upper bound. Density ratio above this is treated as interior/bottom (no foam / invalid region). ⚠Key: set it just below your interior/bottom density ratio (the bottom is often ≈1.0, so use 0.95~1.0) to exclude the interior; too high (e.g. 1.1) lets the bottom in. Use Density mode with a gradient to inspect the density distribution.",
             "Density / DensityWithImpact / DensityWithSpeed モード：有効密度の上界。これを超える密度比は内部/水底（泡なし/無効）。⚠重要：内部/水底の密度比のすぐ下（水底は≈1.0 が多いので 0.95~1.0）に設定して内部を除外。高すぎ（1.1 等）だと水底も入る。")]
        public float GradientFoamStart = 1f;

        [Range(0f, 2f), LocalizationTooltip(
             "Density / DensityWithImpact / DensityWithSpeed 模式：满效密度下界。密度比低到此值区域门/泡沫拉满（=1）。应小于 FoamStart，且低于表面密度（表面往往≈0.9，可设 0.8~0.9 让表面拿到较强的门值）。",
             "Density / DensityWithImpact / DensityWithSpeed mode:full lower bound. The gate/foam saturates (=1) at this density ratio. Should be less than FoamStart, and below the surface density (surface is often ≈0.9, so 0.8~0.9 gives the surface a strong gate value).",
             "Density / DensityWithImpact / DensityWithSpeed モード：満の下界。この密度比でゲート/泡が最大（=1）。FoamStart より小さく、表面密度（≈0.9 が多い）より下に（0.8~0.9）。")]
        public float GradientFoamEnd = 0.85f;

        [Min(0.0001f), LocalizationTooltip(
             "DensityWithImpact 模式：冲击灵敏度（冲击项，最终还要乘以密度区域门）。冲击项 = saturate((平滑密度每步的上升量 / 静止密度) × 此值)，再与密度区域门(FoamStart/End)相乘得生成量。越大越容易起泡（弱冲击也起）；越小越只有强冲击才起泡。默认约 20，请按实际观感调（一般 8~60）。注意：上升量取自「平滑」密度，故 Gradient Smoothing 越大单步上升越小、冲击越弱，需相应调高此值。仅 DensityWithImpact 使用；不影响物理。",
             "DensityWithImpact mode: impact sensitivity (the impact term; the generation is impact × density-gate). impact = saturate((smoothed-density rise per step / rest density) × this), multiplied by the density gate (FoamStart/End). Higher = foams more easily; lower = only strong impacts. Default ~20; tune visually (typically 8~60). Note: rise is from the smoothed density, so a larger Gradient Smoothing makes it weaker — raise this accordingly. DensityWithImpact only; does not affect physics.",
             "DensityWithImpact モード：衝撃感度（衝撃項、生成量は 衝撃 × 密度領域ゲート）。衝撃 = saturate((平滑密度の1ステップ上昇量/静止密度)×この値)、密度ゲート(FoamStart/End)と乗算。既定 約20（概ね 8~60）。Gradient Smoothing が大きいほど弱くなる—相応に大きく。DensityWithImpact のみ使用。")]
        public float GradientImpactStrength = 20f;

        [Min(0f), LocalizationTooltip(
             "DensityWithImpact 模式：冲击死区（最小上升率下限）。冲击项 = saturate((平滑密度每步上升率 / 静止密度 − 此值) × ImpactStrength)。「每步上升率/静止密度」低于此值的区域完全不产泡，用于排除「密度从低到高但变化很小」的轻微扰动（避免看起来像冲击波的伪发白）；超过此值的部分从 0 起缓慢升起，无边界突跳。0=无死区（等同旧行为）。典型 0.002~0.02，按观感微调。注意：与 ImpactStrength 单位一致（都作用在归一化上升率上），死区在乘 ImpactStrength 之前扣除；Gradient Smoothing 越大单步上升越小，需相应调小此值。仅 DensityWithImpact 使用；不影响物理。",
             "DensityWithImpact mode: impact deadzone (minimum rise-rate floor). impact = saturate((smoothed-density rise per step / rest − this) × ImpactStrength). Regions whose per-step normalized rise is below this value produce no foam, excluding weak 'low-to-high but tiny' disturbances (avoids shockwave-like false whitening); above it foam rises from 0 with no boundary jump. 0 = no deadzone (legacy behavior). Typically 0.002~0.02; tune visually. Note: same unit as ImpactStrength (both act on the normalized rise); the deadzone is subtracted before multiplying by ImpactStrength. A larger Gradient Smoothing shrinks the per-step rise, so lower this accordingly. DensityWithImpact only; does not affect physics.",
             "DensityWithImpact モード：衝撃デッドゾーン（最小上昇率の下限）。衝撃 = saturate((平滑密度の1ステップ上昇率/静止密度 − この値)×ImpactStrength)。1ステップの正規化上昇率がこの値未満の領域は泡を出さず、「低→高だが変化が小さい」微弱な擾乱を除外（衝撃波的な偽の白飛びを回避）。超えた分は 0 から立ち上がり境界のポップなし。0=デッドゾーン無し（旧挙動と同一）。目安 0.002~0.02。ImpactStrength と同じ単位（正規化上昇率に作用）で、乗算前に差し引きます。Gradient Smoothing が大きいほど1ステップ上昇が小さくなるため相応に小さく。DensityWithImpact のみ使用。")]
        public float GradientImpactRiseMin = 0.003f;

        [Min(0f), LocalizationTooltip(
             "DensityWithImpact / DensityWithSpeed 模式：泡沫持久度（秒，时间常数）。生成泡沫后，即使不再产生也会按此时长逐渐消退（模拟卷入的空气逃逸）：约经过此时长衰减到 37%，约 3 倍时长基本消失。0=不持久（只在生成那一刻发白）；海浪白沫≈0.4~1.2；奶泡/洗涤泡≈3~8；啤酒顶泡≈8~20。仅这两个动态来源使用；不影响物理。",
             "DensityWithImpact / DensityWithSpeed mode: foam persistence (seconds, time constant). After foam is generated it fades over this duration even with no new generation (mimics entrained air escaping): decays to ~37% after this long, mostly gone after ~3×. 0 = not persistent; sea whitecaps≈0.4~1.2; milk/detergent foam≈3~8; beer head≈8~20. Both dynamic sources only; does not affect physics.",
             "DensityWithImpact / DensityWithSpeed モード：泡の持続時間（秒、時定数）。生成後、新たな生成が無くてもこの時間で徐々に消えます：約 37%、約 3 倍でほぼ消滅。0=非持続。波の白泡≈0.4~1.2、ミルク/洗剤泡≈3~8、ビールの泡≈8~20。動的 2 種のみ使用。")]
        public float GradientFoamPersistence = 0.6f;

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
            // 烘焙时即把手调的 sRGB 渐变色转成「上传值」（线性项目做 sRGB→linear），与 store 逐粒子色的上传边界完全对齐，
            // 使渐变色与 store 逐粒子色在相同取值下表现一致。CPU 与 GPU 渐变路径共用本 LUT，一次烘焙即两端到位（绘制时不再逐粒子转换）。
            // Bake the authored sRGB gradient colors into upload values here (sRGB→linear in linear projects), aligned with the
            // per-particle store color's upload boundary, so gradient and per-particle store colors match at the same authored
            // value. The CPU and GPU gradient paths share this LUT — one bake covers both (no per-particle conversion at draw).
            // 焼き込み時に手調整の sRGB 渐变色を「アップロード値」へ変換（線形項目では sRGB→linear）。store 粒子色の上传境界と整合。
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
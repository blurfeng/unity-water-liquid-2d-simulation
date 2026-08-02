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
             "透明度曲线（仅 ColorMode=Gradient 生效）。按与颜色相同的每粒子标量 t（0→1）采样，得到该粒子的最终透明度倍率（0=全透，1=不透）。与 ColorGradient 的 alpha（用作距离场覆盖度，决定流体形状）解耦：此曲线只影响最终渲染透明度、不改变流体形状。默认恒为 1（无变化，零开销）。可用于模拟海浪浪尖半透等按采样动态变化的透明度。",
             "Opacity curve (effective only when ColorMode=Gradient). Sampled by the same per-particle scalar t (0→1) as the color, yielding the particle's final opacity multiplier (0 = fully transparent, 1 = opaque). Decoupled from ColorGradient's alpha (which is the distance-field coverage that shapes the fluid): this curve affects only the final render opacity, not the fluid shape. Defaults to a constant 1 (no change, zero cost). Useful for sampling-driven opacity such as translucent wave crests.",
             "透明度カーブ（ColorMode=Gradient のときのみ有効）。色と同じ粒子ごとのスカラー t（0→1）でサンプリングし、その粒子の最終透明度倍率（0=完全透明、1=不透明）を得ます。ColorGradient の alpha（流体形状を決める距離場カバレッジ）とは分離され、このカーブは最終描画の透明度のみに作用し、流体形状は変えません。既定は定数 1（変化なし、ゼロコスト）。波の穂先の半透明などサンプリング駆動の透明度に利用できます。")]
        public AnimationCurve GradientOpacity = AnimationCurve.Constant(0f, 1f, 1f);

        [LocalizationTooltip(
             "渐变标量来源（仅 Gradient 生效）。Speed=按速度大小；Density=按当前密度亏空（表面恒有一层，静态）；DensityWithImpact=密度门×冲击（只在表面附近、被快速压实处起泡）；DensityWithSpeed=密度门×速度（表面附近运动越快越起泡）；Impact=纯冲击（无密度门，任意位置快速压实都发白）。",
             "Gradient scalar source (Gradient mode only). Speed = by velocity magnitude; Density = by current density deficit (always-on surface layer, static); DensityWithImpact = density gate × impact (foam only near the surface where rapidly compressed); DensityWithSpeed = density gate × speed (near-surface, faster = more foam); Impact = pure impact (no density gate, any location whitens on rapid compression).",
             "グラデーションのスカラーソース（Gradient のみ）。Speed=速度の大きさ；Density=現在の密度不足（表面常時、静的）；DensityWithImpact=密度ゲート×衝撃（表面付近の急圧縮のみ）；DensityWithSpeed=密度ゲート×速度（表面付近、速いほど泡）；Impact=純衝撃（門無し、任意位置の急圧縮で白化）。")]
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
             "DensityWithImpact 模式：冲击灵敏度（冲击项，最终还要乘以密度区域门）。冲击项 = saturate((平滑密度每步的上升量 / 静止密度) × 此值)，再与密度区域门(FoamStart/End)相乘得生成量。越大越容易起泡（弱冲击也起）；越小越只有强冲击才起泡。默认约 20，请按实际观感调（一般 8~60）。注意：上升量取自「平滑」密度，故 Gradient Smoothing 越大单步上升越小、冲击越弱，需相应调高此值。DensityWithImpact / Impact 用作冲击灵敏度。不影响物理。",
             "DensityWithImpact mode: impact sensitivity (the impact term; the generation is impact × density-gate). impact = saturate((smoothed-density rise per step / rest density) × this), multiplied by the density gate (FoamStart/End). Higher = foams more easily; lower = only strong impacts. Default ~20; tune visually (typically 8~60). Note: rise is from the smoothed density, so a larger Gradient Smoothing makes it weaker — raise this accordingly. Impact sensitivity for DensityWithImpact / Impact. Does not affect physics.",
             "DensityWithImpact モード：衝撃感度（衝撃項、生成量は 衝撃 × 密度領域ゲート）。衝撃 = saturate((平滑密度の1ステップ上昇量/静止密度)×この値)、密度ゲート(FoamStart/End)と乗算。既定 約20（概ね 8~60）。Gradient Smoothing が大きいほど弱くなる—相応に大きく。DensityWithImpact / Impact で使用。物理には影響しません。")]
        public float GradientImpactStrength = 20f;

        [Min(0f), LocalizationTooltip(
             "DensityWithImpact 模式：冲击死区（最小上升率下限）。冲击项 = saturate((平滑密度每步上升率 / 静止密度 − 此值) × ImpactStrength)。「每步上升率/静止密度」低于此值的区域完全不产泡，用于排除「密度从低到高但变化很小」的轻微扰动（避免看起来像冲击波的伪发白）；超过此值的部分从 0 起缓慢升起，无边界突跳。0=无死区（等同旧行为）。典型 0.002~0.02，按观感微调。注意：与 ImpactStrength 单位一致（都作用在归一化上升率上），死区在乘 ImpactStrength 之前扣除；Gradient Smoothing 越大单步上升越小，需相应调小此值。DensityWithImpact / Impact 用作上升死区。不影响物理。",
             "DensityWithImpact mode: impact deadzone (minimum rise-rate floor). impact = saturate((smoothed-density rise per step / rest − this) × ImpactStrength). Regions whose per-step normalized rise is below this value produce no foam, excluding weak 'low-to-high but tiny' disturbances (avoids shockwave-like false whitening); above it foam rises from 0 with no boundary jump. 0 = no deadzone (legacy behavior). Typically 0.002~0.02; tune visually. Note: same unit as ImpactStrength (both act on the normalized rise); the deadzone is subtracted before multiplying by ImpactStrength. A larger Gradient Smoothing shrinks the per-step rise, so lower this accordingly. The rise deadzone for DensityWithImpact / Impact. Does not affect physics.",
             "DensityWithImpact モード：衝撃デッドゾーン（最小上昇率の下限）。衝撃 = saturate((平滑密度の1ステップ上昇率/静止密度 − この値)×ImpactStrength)。1ステップの正規化上昇率がこの値未満の領域は泡を出さず、「低→高だが変化が小さい」微弱な擾乱を除外（衝撃波的な偽の白飛びを回避）。超えた分は 0 から立ち上がり境界のポップなし。0=デッドゾーン無し（旧挙動と同一）。目安 0.002~0.02。ImpactStrength と同じ単位（正規化上昇率に作用）で、乗算前に差し引きます。Gradient Smoothing が大きいほど1ステップ上昇が小さくなるため相応に小さく。DensityWithImpact / Impact で上昇デッドゾーンとして使用。物理には影響しません。")]
        public float GradientImpactRiseMin = 0.003f;

        [Min(0f), LocalizationTooltip(
             "DensityWithImpact / DensityWithSpeed 模式：泡沫持久度（秒，时间常数）。生成泡沫后，即使不再产生也会按此时长逐渐消退（模拟卷入的空气逃逸）：约经过此时长衰减到 37%，约 3 倍时长基本消失。0=不持久（只在生成那一刻发白）；海浪白沫≈0.4~1.2；奶泡/洗涤泡≈3~8；啤酒顶泡≈8~20。仅这两个动态来源使用；不影响物理。",
             "DensityWithImpact / DensityWithSpeed mode: foam persistence (seconds, time constant). After foam is generated it fades over this duration even with no new generation (mimics entrained air escaping): decays to ~37% after this long, mostly gone after ~3×. 0 = not persistent; sea whitecaps≈0.4~1.2; milk/detergent foam≈3~8; beer head≈8~20. Both dynamic sources only; does not affect physics.",
             "DensityWithImpact / DensityWithSpeed モード：泡の持続時間（秒、時定数）。生成後、新たな生成が無くてもこの時間で徐々に消えます：約 37%、約 3 倍でほぼ消滅。0=非持続。波の白泡≈0.4~1.2、ミルク/洗剤泡≈3~8、ビールの泡≈8~20。動的 2 種のみ使用。")]
        public float GradientFoamPersistence = 0.6f;

        [LocalizationTooltip(
             "DensityWithImpact / DensityWithSpeed / Impact 模式：按表面度重映射泡沫持久度的曲线（乘数）。X 轴 = 表面度（用 Foam Start/End 把密度比归一化铺满 [0,1]：X=0 水底/内部、X=1 水面；= saturate((Foam Start − 密度比)/(Foam Start − Foam End))）；Y 轴 = 乘在 Foam Persistence 上的倍率。最终持久度 = Foam Persistence × 曲线(表面度)。默认恒为 1（无影响、零开销）。要实现「水面持久、水底快速消散」：曲线左端 X=0(水底) = 较小值、右端 X=1(水面) = 较大值（如水底系数 1、水面系数 10），Foam Persistence 设为基础秒数。Y>1 更持久，Y<1 更快消散，Y=0 瞬间消散。用表面度而非原始密度比作 X，是为让水面~水底那段窄密度差铺满整条曲线、两端各放一个键即可。纯 Impact 会借用 Foam Start/End 作此密度带。仅这三个动态来源使用；需 Foam Persistence>0 才有意义；不影响物理/颜色/形状，只改变已生成泡沫的消退速度。",
             "DensityWithImpact / DensityWithSpeed / Impact mode: a curve (multiplier) that remaps foam persistence by surfaceness. X axis = surfaceness (density ratio normalized by Foam Start/End across the full [0,1]: X=0 bottom/interior, X=1 surface; = saturate((Foam Start − ratio)/(Foam Start − Foam End))); Y axis = multiplier on Foam Persistence. Final persistence = Foam Persistence × curve(surfaceness). Defaults to a constant 1 (no effect, zero cost). For \"surface persists, bottom dissipates fast\": left end X=0 (bottom) = a small value, right end X=1 (surface) = a larger value (e.g. bottom 1, surface 10), and set Foam Persistence to the base seconds. Y>1 = more persistent, Y<1 = fades faster, Y=0 = vanishes instantly. Using surfaceness (not raw ratio) as X spreads the narrow surface→bottom band across the whole curve — just one key at each end. Pure Impact borrows Foam Start/End as this density band. Used by these three dynamic sources only; only meaningful when Foam Persistence > 0; does not affect physics/color/shape — only how fast already-generated foam fades.",
             "DensityWithImpact / DensityWithSpeed / Impact モード：表面度で泡の持続度をリマップするカーブ（倍率）。X 軸 = 表面度（Foam Start/End で密度比を [0,1] 全域に正規化：X=0 水底/内部、X=1 表面；= saturate((Foam Start − 密度比)/(Foam Start − Foam End)))、Y 軸 = Foam Persistence への倍率。最終持続度 = Foam Persistence × カーブ(表面度)。既定は定数 1（影響なし、ゼロコスト）。「表面は持続、水底は速く消える」には左端 X=0(水底) を小さく、右端 X=1(表面) を大きく（例 水底 1、表面 10）、Foam Persistence を基礎秒数に。Y>1 で持続、Y<1 で速く消え、Y=0 で瞬時消滅。原始密度比でなく表面度を X にすることで、狭い密度差をカーブ全域に広げ両端に 1 つずつキーを置くだけで済みます。純 Impact は Foam Start/End をこの密度帯として借用。動的 3 種のみ使用。Foam Persistence>0 が前提。物理・色・形状は変えません。")]
        public AnimationCurve GradientFoamPersistenceCurve = AnimationCurve.Constant(0f, 1f, 1f);

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

        // 透明度 LUT（与颜色 LUT 共享同一标量 t 与脏标记）：CPU 绘制路径用 float[] 按 t 取倍率；GPU 绘制路径用 256×1 RHalf 纹理在 shader 内采样。
        // 与颜色 LUT 解耦，专门承载 GradientOpacity 曲线烘焙出的每粒子最终透明度倍率 O(t)。_gradientOpacityActive 缓存「曲线是否非恒 1」，
        // 恒 1 时整条独立透明度场链路可完全跳过（现有 Gradient 资产零开销、行为不变）。
        // Opacity LUT (shares the same scalar t and dirty flag as the color LUT): the CPU draw path indexes a float[] by t; the GPU
        // draw path samples a 256×1 RHalf texture in-shader. Decoupled from the color LUT; carries the per-particle final opacity
        // multiplier O(t) baked from the GradientOpacity curve. _gradientOpacityActive caches whether the curve deviates from a
        // constant 1; when it is a flat 1 the whole independent-opacity-field path is skipped (existing gradient assets: zero cost).
        // 透明度 LUT（色 LUT と同じ t・ダーティフラグを共有）。CPU は float[]、GPU は 256×1 RHalf。GradientOpacity から焼く O(t) を保持。
        [NonSerialized] private float[] _opacityLutCpu;
        [NonSerialized] private Texture2D _opacityLutGpu;
        [NonSerialized] private bool _gradientOpacityActive;
        // 透明度 LUT 上传用的复用 Color[] 暂存：一次 SetPixels 取代 256 次 SetPixel 的原生调用。 // Reused Color[] scratch for uploading the opacity LUT: one SetPixels instead of 256 SetPixel native calls. // 透明度 LUT アップロード用 Color[] 再利用。
        [NonSerialized] private Color[] _opacityLutColorScratch;

        /// <summary>标记渐变 LUT 失效（渐变/相关参数改动后调用，下次使用时重建）。同时置脏持久度曲线 LUT。 // Mark the gradient (and persistence-curve) LUT dirty. // LUT をダーティに。</summary>
        public void InvalidateGradientLut() { _lutDirty = true; _persistenceLutDirty = true; }

        /// <summary>
        /// 透明度曲线是否「非恒 1」（需要独立透明度场）。恒 1 时可完全跳过 MRT 透明度链路，现有资产零开销。惰性烘焙后可用。
        /// Whether the opacity curve is non-constant-1 (needs the independent opacity field). When flat 1, the whole MRT opacity
        /// path can be skipped (zero cost for existing assets). Valid after the lazy bake. // 透明度カーブが非定数1か（独立透明度場が要るか）。
        /// </summary>
        public bool GradientOpacityActive
        {
            get { EnsureGradientLut(); return _gradientOpacityActive; }
        }

        /// <summary>
        /// 确保 LUT 已烘焙（须在主线程调用——会创建 Texture2D）。应在渲染 Pass 的 Record 阶段预热，勿在渲染执行回调内首次创建纹理。
        /// Ensure the LUT is baked (must be called on the main thread — creates a Texture2D). Warm it up in the render Pass's
        /// Record phase; do not first-create the texture inside a render exec callback.
        /// LUT が焼き込み済みであることを保証（メインスレッドで呼ぶ——Texture2D を作成）。
        /// </summary>
        public void EnsureGradientLut()
        {
            if (!_lutDirty && _lutCpu != null && _lutGpu && _opacityLutCpu != null && _opacityLutGpu) return;
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

            // ---- 透明度 LUT 烘焙 // Bake the opacity LUT // 透明度 LUT の焼き込み ---- //
            // 透明度是线性的倍率（非颜色），不做任何色彩空间转换，直接烘焙曲线求值并裁剪到 [0,1]。同时记录曲线是否非恒 1。
            // Opacity is a linear multiplier (not a color): no color-space conversion; bake the raw curve value clamped to [0,1].
            // Also record whether the curve deviates from a constant 1. // 透明度は線形倍率（色ではない）。色空間変換なしで [0,1] にクランプして焼く。
            if (_opacityLutCpu == null || _opacityLutCpu.Length != GradientLutSize) _opacityLutCpu = new float[GradientLutSize];
            if (_opacityLutColorScratch == null || _opacityLutColorScratch.Length != GradientLutSize) _opacityLutColorScratch = new Color[GradientLutSize];
            var opacityCurve = GradientOpacity;
            bool active = false;
            for (int i = 0; i < GradientLutSize; i++)
            {
                float o = opacityCurve != null && opacityCurve.length > 0
                    ? Mathf.Clamp01(opacityCurve.Evaluate(i / (float)(GradientLutSize - 1)))
                    : 1f;
                _opacityLutCpu[i] = o;
                _opacityLutColorScratch[i] = new Color(o, 0f, 0f, 0f); // 同一循环内填充上传暂存（R=倍率）。 // fill the upload scratch in the same loop (R=multiplier). // アップロード暂存を同ループで充填。
                if (Mathf.Abs(o - 1f) > 1e-4f) active = true;
            }
            _gradientOpacityActive = active;

            if (!_opacityLutGpu)
            {
                // linear=true：透明度是线性倍率，采样不做 sRGB 转换。 // linear=true: opacity is a linear multiplier; sampling does no sRGB conversion. // linear=true。
                _opacityLutGpu = new Texture2D(GradientLutSize, 1, TextureFormat.RHalf, false, true)
                {
                    name = "Liquid2DOpacityLut",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.HideAndDontSave,
                };
            }
            _opacityLutGpu.SetPixels(_opacityLutColorScratch); // 一次上传（RHalf 只取 R）。 // single upload (RHalf keeps R). // 一括アップロード。
            _opacityLutGpu.Apply(false);

            _lutDirty = false;
        }

        /// <summary>把标量 t（0..1）映射为 LUT 索引（最近邻，四舍五入并裁剪）。CPU 绘制热循环里每粒子调用，配合 GetGradientLutCpu / GetOpacityLutCpu 直接按索引取值。 // Map scalar t (0..1) to a LUT index (nearest, rounded & clamped); use with GetGradientLutCpu / GetOpacityLutCpu. // スカラー t を LUT 索引へ。</summary>
        public static int GradientLutIndex(float t)
            => Mathf.Clamp((int)(Mathf.Clamp01(t) * (GradientLutSize - 1) + 0.5f), 0, GradientLutSize - 1);

        /// <summary>获取 CPU 渐变色 LUT 数组（按 <see cref="GradientLutIndex"/> 直接索引）。确保已烘焙。CPU 绘制热循环里每描述符取一次，避免每粒子的 EnsureGradientLut 与 Unity 对象判空开销。 // Get the CPU gradient color LUT array (index via GradientLutIndex); ensures baked. Fetch once per descriptor to avoid per-particle overhead. // CPU 色 LUT 配列を取得。</summary>
        public Color[] GetGradientLutCpu()
        {
            EnsureGradientLut();
            return _lutCpu;
        }

        /// <summary>获取 CPU 透明度 LUT 数组（按 <see cref="GradientLutIndex"/> 直接索引）。确保已烘焙。 // Get the CPU opacity LUT array (index via GradientLutIndex); ensures baked. // CPU 透明度 LUT 配列を取得。</summary>
        public float[] GetOpacityLutCpu()
        {
            EnsureGradientLut();
            return _opacityLutCpu;
        }

        /// <summary>获取渐变 LUT 纹理（GPU 绘制路径 shader 采样用）。 // Get the gradient LUT texture (for GPU shader sampling). // グラデーション LUT テクスチャを取得。</summary>
        public Texture2D GetGradientLut()
        {
            EnsureGradientLut();
            return _lutGpu;
        }

        /// <summary>获取透明度 LUT 纹理（GPU 绘制路径 shader 采样用）。 // Get the opacity LUT texture (for GPU shader sampling). // 透明度 LUT テクスチャを取得。</summary>
        public Texture2D GetOpacityLut()
        {
            EnsureGradientLut();
            return _opacityLutGpu;
        }

        /// <summary>
        /// 释放渐变/透明度 LUT 纹理。运行时创建的 <see cref="HideFlags.HideAndDontSave"/> 纹理不会被 GC 或 UnloadUnusedAssets 回收，
        /// 须显式销毁——由 <see cref="Liquid2DParticleDescriptor"/> 卸载（OnDisable）时调用，避免编辑器反复进出 Play 时纹理累积。
        /// CPU 侧 LUT 数组随 GC 回收无需处理。销毁后置脏，下次使用会重建。
        /// Release the gradient/opacity LUT textures. Runtime-created <see cref="HideFlags.HideAndDontSave"/> textures are not
        /// reclaimed by GC or UnloadUnusedAssets and must be destroyed explicitly — called from
        /// <see cref="Liquid2DParticleDescriptor"/> unload (OnDisable) to avoid texture accumulation across editor play/stop cycles.
        /// CPU-side LUT arrays are GC-managed. Marks dirty so the next use rebuilds.
        /// グラデーション/透明度 LUT テクスチャを解放（HideAndDontSave は GC/UnloadUnusedAssets で回収されないため明示破棄）。記述子の OnDisable から呼ぶ。
        /// </summary>
        public void DisposeGradientLut()
        {
            DestroyTexture(ref _lutGpu);
            DestroyTexture(ref _opacityLutGpu);
            _lutDirty = true;
        }

        // 运行时用 Destroy、编辑器（非 Play）用 DestroyImmediate 销毁纹理；已销毁/为空时安全跳过。 // Destroy the texture (Destroy at runtime, DestroyImmediate in edit mode); safe when already destroyed/null. // 実行時 Destroy / エディタ DestroyImmediate。
        private static void DestroyTexture(ref Texture2D tex)
        {
            if (!tex) { tex = null; return; }
            if (Application.isPlaying) UnityEngine.Object.Destroy(tex);
            else UnityEngine.Object.DestroyImmediate(tex);
            tex = null;
        }

        // ---- 持久度曲线 LUT（供求解器 CPU Job / GPU compute 按每粒子密度比重映射泡沫持久度） ---- //
        // 曲线不能在 Burst/HLSL 内直接 Evaluate，故惰性烘焙成 float[]（X=密度比[0,1] → Y=乘在 Foam Persistence 上的倍率）。
        // _persistenceCurveActive 缓存「曲线是否非恒 1」，恒 1 时求解器完全跳过整条重映射（现有资产零开销、逐位一致）。
        // 与颜色/透明度 LUT 独立：那两个供绘制路径且需 Texture2D；本 LUT 仅 CPU float[]（GPU 侧由求解器按类型展开后 SetData 上传），无纹理，可在 FixedUpdate 主线程烘焙。
        // Persistence-curve LUT (for the solver's CPU Job / GPU compute to remap foam persistence per-particle by density ratio).
        // AnimationCurve can't be Evaluated inside Burst/HLSL, so it's lazily baked into a float[] (X=density ratio[0,1] → Y=multiplier on
        // Foam Persistence). _persistenceCurveActive caches whether the curve is non-constant-1; when flat 1 the solver skips the whole
        // remap (zero cost, byte-identical). Independent of the color/opacity LUTs (those feed the draw path and need a Texture2D); this
        // one is CPU float[] only (the solver expands it per-type and SetData-uploads for GPU), no texture — safe to bake in FixedUpdate.
        public const int PersistenceCurveLutSize = 256;
        [NonSerialized] private float[] _persistenceCurveLutCpu;
        [NonSerialized] private bool _persistenceCurveActive;
        [NonSerialized] private bool _persistenceLutDirty = true;

        /// <summary>持久度曲线是否「非恒 1」（是否需要按密度重映射持久度）。恒 1 时求解器完全跳过、零开销。惰性烘焙后可用。 // Whether the persistence curve is non-constant-1 (needs the remap). // 持続度カーブが非定数1か。</summary>
        public bool PersistenceCurveActive { get { EnsurePersistenceCurveLut(); return _persistenceCurveActive; } }

        /// <summary>获取 CPU 持久度曲线 LUT（长度 <see cref="PersistenceCurveLutSize"/>，按密度比[0,1] 采样得倍率）。确保已烘焙。求解器每帧取一次填入按类型展开的缓冲。 // Get the CPU persistence-curve LUT (indexed by density ratio[0,1] → multiplier). // CPU 持続度カーブ LUT。</summary>
        public float[] GetPersistenceCurveLutCpu() { EnsurePersistenceCurveLut(); return _persistenceCurveLutCpu; }

        /// <summary>惰性烘焙持久度曲线到 float[]（仅 CPU、无纹理，可在 FixedUpdate 主线程调用）。X=密度比[0,1]，Y=倍率(≥0)；同时记录是否非恒 1。 // Lazily bake the persistence curve into a float[] (CPU only, no texture). // 持続度カーブを float[] へ焼き込み。</summary>
        public void EnsurePersistenceCurveLut()
        {
            if (!_persistenceLutDirty && _persistenceCurveLutCpu != null) return;
            if (_persistenceCurveLutCpu == null || _persistenceCurveLutCpu.Length != PersistenceCurveLutSize)
                _persistenceCurveLutCpu = new float[PersistenceCurveLutSize];
            var curve = GradientFoamPersistenceCurve;
            bool active = false;
            for (int i = 0; i < PersistenceCurveLutSize; i++)
            {
                // 倍率不为负（Y<0 无物理意义）；曲线为空时退化为恒 1。 // multiplier is non-negative; empty curve degenerates to constant 1. // 倍率は非負、空カーブは定数1。
                float y = curve != null && curve.length > 0
                    ? Mathf.Max(0f, curve.Evaluate(i / (float)(PersistenceCurveLutSize - 1)))
                    : 1f;
                _persistenceCurveLutCpu[i] = y;
                if (Mathf.Abs(y - 1f) > 1e-4f) active = true;
            }
            _persistenceCurveActive = active;
            _persistenceLutDirty = false;
        }

        #endregion
    }
}
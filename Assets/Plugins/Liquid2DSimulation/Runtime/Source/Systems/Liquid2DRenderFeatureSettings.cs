using System;
using Fs.Liquid2D.Localization;
using UnityEngine;

namespace Fs.Liquid2D
{
    public enum EScaleFactor
    {
        X1 = 1,
        X2 = 2,
        X4 = 4,
        X8 = 8
    }
    
    /// <summary>
    /// 透明度计算模式。 // Opacity calculation mode. // 透明度計算モード。
    /// </summary>
    public enum EOpacityMode
    {
        /// <summary>
        /// 默认模式，不会影响透明度。 // Default mode, does not affect opacity. // デフォルトモード、透明度に影響しません。
        /// </summary>
        Default,
        
        /// <summary>
        /// 透明度值作为倍率和原有透明度相乘。 // Opacity value is multiplied as a multiplier with the original opacity. // 透明度値は倍率として元の透明度と乗算されます。
        /// </summary>
        Multiply,
        
        /// <summary>
        /// 透明度值直接替代原有透明度。 // Opacity value directly replaces the original opacity. // 透明度値は元の透明度を直接置き換えます。
        /// </summary>
        Replace
    }
    
    /// <summary>
    /// 2D流体 Renderer Feature 设置。 // 2D fluid Renderer Feature settings. // 2D流体レンダラー機能の設定。
    /// </summary>
    [Serializable]
    public class Liquid2DRenderFeatureSettings
    {
        /// <summary>
        /// 模糊设置。 // Blur settings. // ブラー設定。
        /// </summary>
        [Serializable]
        public class Blur
        {
            [Range(0, 16), LocalizationTooltip(
                  "迭代次数，越大越模糊。", 
                 "Number of iterations, the larger the more blurred.", 
                 "反復回数、値が大きいほどぼやけます。")]
            public int iterations = 7;
        
            [Range(0.01f, 3f), LocalizationTooltip(
                  "每次迭代的模糊扩散度，越大越模糊。",
                 "Blur spread per iteration, larger values create more blur.",
                 "各反復でのブラー拡散度、値が大きいほどぼやけます。")]
            public float blurSpread = 0.8f;
        
            [Range(0, 1), LocalizationTooltip(
                  "核心保持强度，越大流体核心部分越清晰。迭代次数增加时，建议适当调大此值以保持流体核心清晰。",
                 "Core retention intensity, higher values keep the fluid core clearer. When increasing iterations, adjust this value to maintain fluid core clarity.",
                 "コア保持強度、値が大きいほど流体のコア部分がより鮮明になります。反復回数を増やす場合、流体コアの明瞭さを保つためにこの値を適度に調整してください。")]
            public float coreKeepIntensity = 0.4f;

            [LocalizationTooltip(
                 "渲染缩放比例，越大性能越好，但边界越不清晰。",
                 "Render scale factor, higher values improve performance but reduce edge clarity.",
                 "レンダースケール係数、値が大きいほどパフォーマンスが向上しますが、エッジの鮮明度が低下します。")]
            public EScaleFactor scaleFactor = EScaleFactor.X4;

            [LocalizationTooltip(
                 "是否忽略背景色。启用后，背景色将不参与模糊计算，模糊时仅使用流体颜色(但实际上任然会受到一些影响)。补充说明：模糊算法实际上是对每个像素点和边缘颜色进行混合，所以流体粒子颜色会受到背景色影响，越靠近边缘越明显。",
                 "Whether to ignore background color. When enabled, background color will not participate in blur calculation, only fluid color is used during blur (but will still be affected to some extent). Note: The blur algorithm actually blends each pixel point with edge colors, so fluid particle color will be affected by background color, especially near edges.",
                 "背景色を無視するかどうか。有効にすると、背景色はブラー計算に参加せず、ブラー時には流体色のみが使用されます（ただし、実際にはある程度影響を受けます）。補足：ブラーアルゴリズムは実際に各ピクセル点とエッジ色をブレンドするため、流体パーティクルの色は背景色の影響を受け、エッジに近づくほど顕著になります。")]
            public bool ignoreBgColor = false;
            
            [SerializeField, ColorUsage(true, true), LocalizationTooltip(
                  "流体模糊背景色。作为模糊时底图的颜色，最终影响整体水体的边缘色（默认为当前相机场景纹理颜色）。",
                 "Fluid blur background color. Used as the base color during blurring, ultimately affects the overall water body edge color (defaults to current camera scene texture color).",
                 "流体ブラー背景色。ブラー時のベース色として使用され、最終的に水体全体のエッジ色に影響します（デフォルトは現在のカメラシーンテクスチャ色）。")]
            public Color blurBgColor = Color.clear;
        
            [Range(0f, 1f), LocalizationTooltip(
                  "流体模糊背景色强度。0时不使用背景色（默认为当前相机场景纹理颜色），1为完全显示背景色。",
                 "Fluid blur background color intensity. 0 means no background color (defaults to current camera scene texture color), 1 means fully display background color.",
                 "流体ブラー背景色の強度。0の場合は背景色を使用しません（デフォルトは現在のカメラシーンテクスチャ色）、1の場合は背景色を完全に表示します。")]
            public float blurBgColorIntensity = 0f;
        }

        /// <summary>
        /// 扭曲设置。 // Distortion settings. // 歪み設定。
        /// </summary>
        [Serializable]
        public class Distort
        {
            [LocalizationTooltip(
                 "是否启用流体扭曲效果。",
                 "Whether to enable fluid distortion effect.",
                 "流体歪み効果を有効にするかどうか。")]
            public bool enable = true;
            
            [Range(0.0001f, 1f), LocalizationTooltip(
                  "扰动采样缩放。值越大，扰动越频繁。",
                 "Disturbance sampling scale. Larger values result in more frequent disturbances.",
                 "外乱サンプリングスケール。値が大きいほど外乱が頻繁になります。")]
            public float magnitude = 0.1f;
            
            [Range(1f, 500f), LocalizationTooltip(
                  "扰动频率。值越大，扰动越密集。",
                 "Disturbance frequency. Larger values result in denser disturbances.",
                 "外乱周波数。値が大きいほど外乱が密集します。")]
            public float frequency = 380f;
            
            [Range(0.0001f, 0.1f), LocalizationTooltip(
                  "扰动振幅。值越大，扰动越明显。",
                 "Disturbance amplitude. Larger values make disturbances more noticeable.",
                 "外乱振幅。値が大きいほど外乱が顕著になります。")]
            public float amplitude = 0.008f;
            
            [LocalizationTooltip(
                 "扰动速度。X控制水平扰动速度，Y控制垂直扰动速度。",
                 "Disturbance speed. X controls horizontal disturbance speed, Y controls vertical disturbance speed.",
                 "外乱速度。Xが水平外乱速度を制御し、Yが垂直外乱速度を制御します。")]
            public Vector2 distortSpeed = new Vector2(0.1f, 1f);
            
            [LocalizationTooltip(
                 "扰动时间系数。用于控制不同噪点的运动速度和方向。",
                 "Disturbance time factors. Used to control the movement speed and direction of different noise points.",
                 "外乱時間係数。異なるノイズ点の移動速度と方向を制御するために使用されます。")]
            public Vector4 distortTimeFactors = new  Vector4(0.3f, -0.4f, 0.1f, 0.5f);
            
            [LocalizationTooltip(
                 "噪点坐标偏移。用于避免噪点纹理的重复性。",
                 "Noise coordinate offset. Used to avoid repetition in noise textures.",
                 "ノイズ座標オフセット。ノイズテクスチャの繰り返しを避けるために使用されます。")]
            public float noiseCoordOffset = 4f;
        }

        /// <summary>
        /// 边缘设置。 // Edge settings. // エッジ設定。
        /// </summary>
        [Serializable]
        public class Edge
        {
            public enum EdgeBlendType
            {
                /// <summary>
                /// 颜色混合。
                /// 边缘颜色是流体颜色和边缘颜色的混合，混合比例由边缘强度决定。能更好的和水体融合。
                /// Color Blending.
                /// The edge color is a blend of the fluid color and the edge color, with the blend ratio determined by the edge intensity. It blends better with the water body.
                /// 色のブレンド。
                /// エッジカラーは流体カラーとエッジカラーのブレンドであり、ブレンド比率はエッジ強度によって決まります。水体とよりよく融合します。
                /// </summary>
                BlendSrcAlphaOneMinusSrcAlpha,
                
                /// <summary>
                /// 线性插值混合。
                /// 边缘颜色是流体颜色和边缘颜色的线性插值，插值比例由边缘强度决定。可以实现边缘透明化，和水体有明显区别的效果。
                /// Linear Interpolation Blending.
                /// The edge color is a linear interpolation of the fluid color and the edge color, with the interpolation ratio determined by the edge intensity. This can achieve edge transparency and a distinct effect from the water body.
                /// 線形補間ブレンド。
                /// エッジカラーは流体カラーとエッジカラーの線形補間であり、補間比率はエッジ強度によって決まります。エッジの透明度を実現し、水体とは明確に異なる効果を得ることができます。
                /// </summary>
                Lerp,
            }
            
            public bool enable = false;
            
            [Range(0f, 1f), LocalizationTooltip(
                  "液体边缘范围，越大边缘越宽。",
                 "Liquid edge range, larger values create wider edges.",
                 "液体エッジ範囲、値が大きいほどエッジが幅広くなります。")]
            public float edgeRange = 0.6f;
        
            [Range(0f, 1f), LocalizationTooltip(
                  "液体边缘强度。越大边缘越明显。",
                 "Liquid edge intensity. Larger values make edges more prominent.",
                 "液体エッジ強度。値が大きいほどエッジが目立ちます。")]
            public float edgeIntensity = 0.1f;
            
            [ColorUsage(true, true), LocalizationTooltip(
                  "液体边缘颜色。",
                 "Liquid edge color.",
                 "液体エッジカラー。")]
            public Color edgeColor = new Color(1f, 1f, 1f, 0.8f);
            
            [LocalizationTooltip(
                 "液体边缘混合类型。",
                 "Liquid edge blend type.",
                 "液体エッジブレンドタイプ。")]
            public EdgeBlendType blendType = EdgeBlendType.BlendSrcAlphaOneMinusSrcAlpha;
        }
        
        /// <summary>
        /// 像素化设置。 // Pixelation settings. // ピクセル化設定。
        /// </summary>
        [Serializable]
        public class Pixel
        {
            public bool enable = false;
            
            [Range(1, 32), LocalizationTooltip(
                  "像素化尺寸，值越大像素化效果越明显。",
                 "Pixelation size, larger values create more pronounced pixelation effects.",
                 "ピクセル化サイズ、値が大きいほどピクセル化効果が顕著になります。")]
            public int pixelSize = 6;

            [LocalizationTooltip(
                 "是否使用像素化背景色。开启后，在流体透明的情况下，背景色会被像素化。",
                 "Whether to use pixelated background color. When enabled, background color will be pixelated when fluid is transparent.",
                 "ピクセル化背景色を使用するかどうか。有効にすると、流体が透明な場合に背景色がピクセル化されます。")]
            public bool pixelBg = true;
        }
        
        [LocalizationTooltip(
             "2D流体 Renderer Feature 名称标签，用于区分不同的 Renderer Feature 配置。如果你要使用 Volume 来控制流体效果，请确保名称标签唯一且和 Volume Profile 中的标签一致。",
             "2D fluid Renderer Feature name tag, used to distinguish different Renderer Feature configurations. If you want to use Volume to control fluid effects, please ensure the name tag is unique and matches the tag in the Volume Profile.",
             "2D流体レンダラーフィーチャーの名前タグ、異なるレンダラーフィーチャー設定を区別するために使用されます。ボリュームで流体効果を制御したい場合は、名前タグが一意であり、ボリュームプロファイルのタグと一致していることを確認してください。")]
        public string nameTag = "Liquid2D";
        
        [LocalizationTooltip(
             "2D流体层遮罩。只会渲染设定的流体层的粒子。",
             "2D fluid layer mask. Only particles from specified fluid layers will be rendered.",
             "2D流体レイヤーマスク。指定された流体レイヤーからのパーティクルのみがレンダリングされます。")]
        public ELiquid2DLayer liquid2DLayerMask = ELiquid2DLayer.Water;
        
        [LocalizationTooltip(
             "液体阻挡层遮罩。指定哪些层的物体会阻挡液体效果。一般是挡板或容器等，他们会完全阻挡液体及时自身是透明的。相当于阻挡物的横截面。",
             "Liquid obstruction layer mask. Specifies which layers of objects will block liquid effects. Usually barriers or containers that completely block liquid even if they are transparent. Equivalent to the cross-section of obstructions.",
             "液体障害物レイヤーマスク。どのレイヤーのオブジェクトが液体効果をブロックするかを指定します。通常は障壁やコンテナなどで、自身が透明であっても液体を完全にブロックします。障害物の断面に相当します。")]
        public RenderingLayerMask obstructionRenderingLayerMask;

        // Tips:
        // 在2D游戏中，假设你有一个玻璃瓶用于装流体。玻璃瓶的横截面是阻挡层，这样流体就不会渲染在玻璃瓶的外面。
        // 但是本流体系统不会处理盖在流体上的正面玻璃瓶部分的渲染，因为流体系统只专注于处理流体效果自身。
        // 你应当将玻璃瓶的横截面的 Rendering Layer 设置为阻挡层。
        // 但是正面盖在流体上的玻璃瓶部分的渲染，应当由用户自行处理。比如创建新的 Renderer Feature 来渲染玻璃瓶。
        // 这个玻璃瓶的 Renderer Feature 应当在流体 Renderer Feature 之后执行。

        // Tips:
        // In a 2D game, suppose you have a glass bottle for holding fluid. The cross-section of the glass bottle is the obstruction layer, so the fluid will not be rendered outside the glass bottle.
        // However, the fluid system does not handle the rendering of the front part of the glass bottle that covers the fluid, as the fluid system focuses only on handling the fluid effect itself.
        // You should set the Rendering Layer of the cross-section of the glass bottle to the obstruction layer.
        // However, the rendering of the front part of the glass bottle that covers the fluid should be handled by the user. For example, create a new Renderer Feature to render the glass bottle.
        // This glass bottle Renderer Feature should be executed after the fluid Renderer Feature. 

        // Tips:
        // 2Dゲームでは、流体を保持するためのガラス瓶があるとします。ガラス瓶の断面は障害物レイヤーであり、流体はガラス瓶の外側にレンダリングされません。
        // ただし、流体を覆うガラス瓶の前面部分のレンダリングは流体システムでは処理されません。流体システムは流体効果自体の処理にのみ焦点を当てているためです。
        // ガラス瓶の断面のレンダリングレイヤーを障害物レイヤーに設定する必要があります。
        // ただし、流体を覆うガラス瓶の前面部分のレンダリングはユーザーが処理する必要があります。たとえば、ガラス瓶をレンダリングするための新しいレンダラーフィーチャーを作成します。
        // このガラス瓶のレンダラーフィーチャーは、流体レンダラーフィーチャーの後に実行する必要があります。
        
        [Range(0f, 1f), LocalizationTooltip(
              "流体透明边缘的裁剪阈值，越大边缘越锐利，水体范围膨胀越少。",
             "Clipping threshold for fluid transparent edges, larger values create sharper edges and less water body expansion.",
             "流体透明エッジのクリッピング閾値、値が大きいほどエッジが鋭く、水体の膨張が少なくなります。")]
        public float cutoff = 0.45f;
        
        [LocalizationTooltip(
             "透明度计算模式。",
             "Opacity calculation mode.",
             "透明度計算モード。")]
        public EOpacityMode opacityMode = EOpacityMode.Default;
        
        [Range(0f, 1f), LocalizationTooltip(
              "透明度值，根据模式作用到最终的流体颜色。",
             "Opacity value, applied to the final fluid color according to the mode.",
             "透明度値、モードに従って最終的な流体色に適用されます。")]
        public float opacityValue = 1f;
        
        [ColorUsage(true, true), LocalizationTooltip(
              "覆盖颜色会覆盖流体粒子自身的颜色，作为流体的整体色调。alpha为强度，1时完全覆盖粒子颜色，0时不覆盖。",
             "Cover color overrides the fluid particle's own color, serving as the overall tone of the fluid. Alpha represents intensity, 1 for complete color override, 0 for no override.",
             "カバーカラーは流体パーティクル自体の色を上書きし、流体の全体的なトーンとして機能します。アルファは強度を表し、1で完全な色の上書き、0で上書きなしです。")]
        public Color coverColor = Color.clear;
        
        [LocalizationTooltip(
             "流体模糊设置。",
             "Fluid blur settings.",
             "流体ブラー設定。")]
        public Blur blur;
        
        [LocalizationTooltip(
             "流体扭曲设置。",
             "Fluid distortion settings.",
             "流体歪み設定。")]
        public Distort distort;
        
        [LocalizationTooltip(
             "流体边缘设置。",
             "Fluid edge settings.",
             "流体エッジ設定。")]
        public Edge edge;
        
        [LocalizationTooltip(
             "流体像素化设置。用于生成像素化风格效果。",
             "Fluid pixelation settings. Used to generate pixelated style effects.",
             "流体ピクセル化設定。ピクセル化スタイル効果を生成するために使用されます。")]
        public Pixel pixel;
        
        public Liquid2DRenderFeatureSettings Clone()
        {
            // 先序列化再反序列化，得到一个全新的对象。
            // First serialize and then deserialize to get a completely new object.
            // 最初にシリアル化し、次に逆シリアル化して、完全に新しいオブジェクトを取得します。
            return JsonUtility.FromJson<Liquid2DRenderFeatureSettings>(JsonUtility.ToJson(this));
        }
    }
}
using System;
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
    /// 透明度计算模式。
    /// </summary>
    public enum EOpacityMode
    {
        /// <summary>
        /// 默认模式，不会影响透明度。
        /// </summary>
        Default,
        
        /// <summary>
        /// 透明度值作为倍率和原有透明度相乘。
        /// </summary>
        Multiply,
        
        /// <summary>
        /// 透明度值直接替代原有透明度。
        /// </summary>
        Replace
    }
    
    /// <summary>
    /// 2D流体 Renderer Feature 设置。
    /// </summary>
    [Serializable]
    public class Liquid2DRenderFeatureSettings
    {
        /// <summary>
        /// 模糊设置。
        /// </summary>
        [Serializable]
        public class Blur
        {
            [Range(0, 16), Tooltip("迭代次数，越大越模糊。")]
            public int iterations = 7;
        
            [Range(0.01f, 3f), Tooltip("每次迭代的模糊扩散度，越大越模糊。")]
            public float blurSpread = 0.8f;
        
            [Range(0, 1), Tooltip("核心保持强度，越大流体核心部分越清晰。迭代次数增加时，建议适当调大此值以保持流体核心清晰。")]
            public float coreKeepIntensity = 0.4f;

            [Tooltip("渲染缩放比例，越大性能越好，但边界越不清晰。")]
            public EScaleFactor scaleFactor = EScaleFactor.X4;

            [Tooltip("是否忽略背景色。启用后，背景色将不参与模糊计算，模糊时仅使用流体颜色(但实际上任然会受到一些影响)。补充说明：模糊算法实际上是对每个像素点和边缘颜色进行混合，所以流体粒子颜色会受到背景色影响，越靠近边缘越明显。")]
            public bool ignoreBgColor = false;
            
            [SerializeField, ColorUsage(true, true), Tooltip("流体模糊背景色。作为模糊时底图的颜色，最终影响整体水体的边缘色（默认为当前相机场景纹理颜色）。")]
            public Color blurBgColor = Color.clear;
        
            [Range(0f, 1f), Tooltip("流体模糊背景色强度。0时不使用背景色（默认为当前相机场景纹理颜色），1为完全显示背景色。")]
            public float blurBgColorIntensity = 0f;
        }

        /// <summary>
        /// 扭曲设置。
        /// </summary>
        [Serializable]
        public class Distort
        {
            [Tooltip("是否启用流体扭曲效果。")]
            public bool enable = true;
            
            [Range(0.0001f, 1f), Tooltip("扰动采样缩放。值越大，扰动越频繁。")]
            public float magnitude = 0.1f;
            
            [Range(1f, 500f), Tooltip("扰动频率。值越大，扰动越密集。")]
            public float frequency = 380f;
            
            [Range(0.0001f, 0.1f), Tooltip("扰动振幅。值越大，扰动越明显。")]
            public float amplitude = 0.008f;
            
            [Tooltip("扰动速度。X控制水平扰动速度，Y控制垂直扰动速度。")]
            public Vector2 distortSpeed = new Vector2(0.1f, 1f);
            
            [Tooltip("扰动时间系数。用于控制不同噪点的运动速度和方向。")]
            public Vector4 distortTimeFactors = new  Vector4(0.3f, -0.4f, 0.1f, 0.5f);
            
            [Tooltip("噪点坐标偏移。用于避免噪点纹理的重复性。")]
            public float noiseCoordOffset = 4f;
        }

        /// <summary>
        /// 边缘设置。
        /// </summary>
        [Serializable]
        public class Edge
        {
            public enum EdgeBlendType
            {
                /// <summary>
                /// 颜色混合。
                /// 边缘颜色是流体颜色和边缘颜色的混合，混合比例由边缘强度决定。
                /// 能更好的和水体融合。
                /// </summary>
                BlendSrcAlphaOneMinusSrcAlpha,
                
                /// <summary>
                /// 线性插值混合。
                /// 边缘颜色是流体颜色和边缘颜色的线性插值，插值比例由边缘强度决定。
                /// 可以实现边缘透明化，和水体有明显区别的效果。
                /// </summary>
                Lerp,
            }
            
            public bool enable = false;
            
            [Range(0f, 1f), Tooltip("液体边缘范围，越大边缘越宽。")]
            public float edgeRange = 0.6f;
        
            [Range(0f, 1f), Tooltip("液体边缘强度。越大边缘越明显。")]
            public float edgeIntensity = 0.1f;
            
            [ColorUsage(true, true), Tooltip("液体边缘颜色。")]
            public Color edgeColor = new Color(1f, 1f, 1f, 0.8f);
            
            [Tooltip("液体边缘混合类型。")]
            public EdgeBlendType blendType = EdgeBlendType.BlendSrcAlphaOneMinusSrcAlpha;
        }
        
        /// <summary>
        /// 像素化设置。
        /// </summary>
        [Serializable]
        public class Pixel
        {
            public bool enable = false;
            
            [Range(1, 32), Tooltip("像素化尺寸，值越大像素化效果越明显。")]
            public int pixelSize = 6;

            [Tooltip("是否使用像素化背景色。开启后，在流体透明的情况下，背景色会被像素化。")]
            public bool pixelBg = true;
        }
        
        [Tooltip("2D流体 Renderer Feature 名称标签，用于区分不同的 Renderer Feature 配置。")]
        public string nameTag = "Liquid2D";
        
        [Tooltip("2D流体层遮罩。只会渲染设定的流体层的粒子。")]
        public ELiquid2DLayer liquid2DLayerMask = ELiquid2DLayer.Water;
        
        [Tooltip("液体阻挡层遮罩。指定哪些层的物体会阻挡液体效果。一般是挡板或容器等，他们会完全阻挡液体及时自身是透明的。相当于阻挡物的横截面。")]
        public RenderingLayerMask obstructionRenderingLayerMask;
        
        // Tips:
        // 在2D游戏中，假设你有一个玻璃瓶用于装流体。玻璃瓶的横截面是阻挡层，这样流体就不会渲染在玻璃瓶的外面。
        // 但是本流体系统不会处理盖在流体上的正面玻璃瓶部分的渲染，因为流体系统只专注于处理流体效果自身。
        // 你应当将玻璃瓶的横截面的 Rendering Layer 设置为阻挡层。
        // 但是正面盖在流体上的玻璃瓶部分的渲染，应当由用户自行处理。比如创建新的 Renderer Feature 来渲染玻璃瓶。
        // 这个玻璃瓶的 Renderer Feature 应当在流体 Renderer Feature 之后执行。
        
        [Range(0f, 1f), Tooltip("流体透明边缘的裁剪阈值，越大边缘越锐利，水体范围膨胀越少。")]
        public float cutoff = 0.45f;
        
        [Tooltip("透明度计算模式。")]
        public EOpacityMode opacityMode = EOpacityMode.Default;
        
        [Range(0f, 1f), Tooltip("透明度值，根据模式作用到最终的流体颜色。")]
        public float opacityValue = 1f;
        
        [ColorUsage(true, true), Tooltip("覆盖颜色会覆盖流体粒子自身的颜色，作为流体的整体色调。alpha为强度，1时完全覆盖粒子颜色，0时不覆盖。")]
        public Color coverColor = Color.clear;
        
        [Tooltip("流体模糊设置。")]
        public Blur blur;
        
        [Tooltip("流体扭曲设置。")]
        public Distort distort;
        
        [Tooltip("流体边缘设置。")]
        public Edge edge;
        
        [Tooltip("流体像素化设置。用于生成像素化风格效果。")]
        public Pixel pixel;
        
        public Liquid2DRenderFeatureSettings Clone()
        {
            // 先序列化再反序列化，得到一个全新的对象。
            return JsonUtility.FromJson<Liquid2DRenderFeatureSettings>(
                JsonUtility.ToJson(this)
            );
        }
    }
}
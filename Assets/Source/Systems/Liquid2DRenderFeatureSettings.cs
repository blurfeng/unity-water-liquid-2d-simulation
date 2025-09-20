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
            
            [SerializeField, ColorUsage(true, true), Tooltip("流体模糊边缘色。作为模糊时底图的颜色，最终影响整体水体的边缘色（默认为当前相机场景纹理颜色）。")]
            public Color blurEdgeColor = Color.clear;
        
            [Range(0f, 1f), Tooltip("流体模糊边缘色强度。0时不显示边缘色（默认为当前相机场景纹理颜色），1为完全显示边缘色。")]
            public float blurEdgeColorIntensity = 0f;
        }

        [Serializable]
        public class Distort
        {
            [Tooltip("是否启用流体扭曲效果。")]
            public bool enable = false;
            
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
        
        [Tooltip("2D流体 Renderer Feature 名称标签，用于区分不同的 Renderer Feature 配置。")]
        public string nameTag = "Liquid2D";
        
        [Tooltip("2D流体层遮罩。只会渲染设定的流体层的粒子。")]
        public ELiquid2DLayer liquid2DLayerMask = ELiquid2DLayer.Water;
        
        [Tooltip("液体遮挡层遮罩。指定哪些层的物体会遮挡液体效果。")]
        public LayerMask obstructionLayerMask;
        
        [Range(0f, 1f), Tooltip("流体透明边缘的裁剪阈值，越大边缘越锐利，水体范围膨胀越少。")]
        public float cutoff = 0.45f;
        
        [Tooltip("透明度计算模式。")]
        public EOpacityMode opacityMode = EOpacityMode.Multiply;
        
        [Range(0f, 1f), Tooltip("透明度值，根据模式作用到最终的流体颜色。")]
        public float opacityValue = 1f;
        
        [ColorUsage(true, true), Tooltip("覆盖颜色会覆盖流体粒子自身的颜色，作为流体的整体色调。alpha为强度，1时完全覆盖粒子颜色，0时不覆盖。")]
        public Color coverColor = Color.clear;
        
        [Range(0f, 1f), Tooltip("液体边缘强度，越大边缘越宽。")]
        public float edgeIntensity = 0f;

        [ColorUsage(true, true), Tooltip("液体边缘颜色。")]
        public Color edgeColor = Color.white;

        [Tooltip("流体模糊设置。")]
        public Blur blur;
        
        [Tooltip("流体扭曲设置。")]
        public Distort distort;
        
        public Liquid2DRenderFeatureSettings Clone()
        {
            // 先序列化再反序列化，得到一个全新的对象。
            return JsonUtility.FromJson<Liquid2DRenderFeatureSettings>(
                JsonUtility.ToJson(this)
            );
        }
    }
}
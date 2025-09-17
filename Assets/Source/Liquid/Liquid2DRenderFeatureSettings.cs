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
    
    public enum EBlurSamplingMode
    {
        /// <summary>
        /// 4次采样，性能较好，模糊效果一般。会有斜向纹理。
        /// </summary>
        Four,
        
        /// <summary>
        /// 8次采样，性能较差，模糊效果较好。
        /// </summary>
        Eight
    }
    
    /// <summary>
    /// 2D流体 Renderer Feature 设置。
    /// </summary>
    [Serializable]
    public class Liquid2DRenderFeatureSettings
    {
        [Tooltip("2D流体 Renderer Feature 名称标签，用于区分不同的 Renderer Feature 配置。")]
        public string nameTag = "Liquid2D";
        
        [Tooltip("2D流体层遮罩。只会渲染设定的流体层的粒子。")]
        public ELiquid2DLayer liquid2DLayerMask = ELiquid2DLayer.Water;
        
        [Range(3, 16), Tooltip("迭代次数，越大越模糊。")]
        public int iterations = 7;
        
        [Range(0.01f, 3f), Tooltip("每次迭代的模糊扩散度，越大越模糊。")]
        public float blurSpread = 0.8f;

        [Tooltip("渲染缩放比例，越大性能越好，但边界越不清晰。")]
        public EScaleFactor scaleFactor = EScaleFactor.X4;

        [Range(0f, 1f), Tooltip("流体透明边缘的裁剪阈值，越大边缘越锐利，水体范围膨胀越少。")]
        public float cutoff = 0.2f;
        
        [Tooltip("液体遮挡层遮罩。指定哪些层的物体会遮挡液体效果。")]
        public LayerMask liquidOcclusionLayerMask;
        
        [SerializeField, ColorUsage(true, true), Tooltip("流体模糊边缘色。作为模糊时底图的颜色，最终影响整体水体的边缘色（默认为当前相机场景纹理颜色）。")]
        public Color blurEdgeColor = Color.clear;
        
        [Range(0f, 1f), Tooltip("流体模糊边缘色强度。0时不显示边缘色（默认为当前相机场景纹理颜色），1为完全显示边缘色。")]
        public float blurEdgeColorIntensity = 0f;

        public Liquid2DRenderFeatureSettings Clone()
        {
            return new Liquid2DRenderFeatureSettings
            {
                nameTag = nameTag,
                liquid2DLayerMask = liquid2DLayerMask,
                iterations = iterations,
                blurSpread = blurSpread
            };
        }
    }
}
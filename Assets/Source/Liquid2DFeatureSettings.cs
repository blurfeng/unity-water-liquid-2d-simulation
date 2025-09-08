using System;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D流体 Renderer Feature 设置。
    /// </summary>
    [Serializable]
    public class Liquid2DFeatureSettings
    {
        [Tooltip("2D流体 Renderer Feature 名称标签，用于区分不同的 Renderer Feature 配置。")]
        public string nameTag = "Liquid2D";
        
        [Tooltip("2D流体层遮罩。只会渲染设定的流体层的粒子。")]
        public ELiquid2DLayer liquid2DLayerMask = ELiquid2DLayer.Water;
        
        [Tooltip("迭代次数，越大越模糊。")]
        public int iterations = 7;
        
        [Tooltip("每次迭代的模糊扩散度，越大越模糊。")]
        public float blurSpread = 0.6f;

        public Liquid2DFeatureSettings Clone()
        {
            return new Liquid2DFeatureSettings
            {
                nameTag = nameTag,
                liquid2DLayerMask = liquid2DLayerMask,
                iterations = iterations,
                blurSpread = blurSpread
            };
        }
    }
}
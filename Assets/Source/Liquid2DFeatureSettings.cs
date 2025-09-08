using System;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D流体 Renderer Feature 设置。
    /// </summary>
    [Serializable]
    public class Liquid2DFeatureSettings
    {
        public ELiquid2DLayer liquid2DLayer = ELiquid2DLayer.Water;
        public int iterations = 7;
        public float blurSpread = 0.6f;

        public Liquid2DFeatureSettings Clone()
        {
            return new Liquid2DFeatureSettings
            {
                liquid2DLayer = liquid2DLayer,
                iterations = iterations,
                blurSpread = blurSpread
            };
        }
    }
}
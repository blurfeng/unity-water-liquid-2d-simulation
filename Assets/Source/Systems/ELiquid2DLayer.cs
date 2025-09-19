using System;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体渲染层。
    /// 你可以为流体粒子和 Renderer Feature 指定相同的渲染层，以实现粒子只与特定渲染器交互的效果。
    /// 这样你可以分别为不同材质或效果的流体粒子设置不同的渲染器，模糊迭代次数等，而不会互相影响。
    /// 例如，你可以创建一个只包含水粒子的渲染器，和一个只包含熔岩粒子的渲染器。
    /// 注意：
    /// 1. 流体粒子和 Renderer Feature 都可以指定多个渲染层。
    /// 2. 如果流体粒子和 Renderer Feature 没有指定相同的渲染层，则该粒子不会被该渲染器处理。
    /// </summary>
    [Flags]
    public enum ELiquid2DLayer
    {
        /// <summary>
        /// 默认。
        /// </summary>
        Default = 1 << 0,

        /// <summary>
        /// 水。
        /// </summary>
        Water = 1 << 1,

        /// <summary>
        /// 熔岩。
        /// </summary>
        Magma = 1 << 2,

        /// <summary>
        /// 沼泽。
        /// </summary>
        Marsh = 1 << 3,
    }

    public static class Liquid2DLayerUtil
    {
        /// <summary>
        /// 所有流体渲染层的名称数组。
        /// </summary>
        public static readonly string[] Liquid2DLayerNames;

        /// <summary>
        /// 是否有定义任何流体渲染层。
        /// </summary>
        public static bool IsHaveLayer => Liquid2DLayerNames.Length > 0;

        static Liquid2DLayerUtil()
        {
            // 每次编译时获取一次枚举名称数组。
            Liquid2DLayerNames = typeof(ELiquid2DLayer).GetEnumNames();
        }
    }
}
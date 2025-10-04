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
    /// 
    /// Fluid rendering layer.
    /// You can specify the same rendering layer for fluid particles and Renderer Feature to achieve the effect that particles only interact with specific renderers.
    /// This way you can set different renderers, blur iteration counts, etc. for fluid particles with different materials or effects without affecting each other.
    /// For example, you can create a renderer that contains only water particles, and a renderer that contains only lava particles.
    /// Note:
    /// 1. Both fluid particles and Renderer Feature can specify multiple rendering layers.
    /// 2. If fluid particles and Renderer Feature don't specify the same rendering layer, the particle won't be processed by that renderer.
    /// 
    /// 流体レンダリングレイヤー。
    /// 流体粒子とRenderer Featureに同じレンダリングレイヤーを指定して、粒子が特定のレンダラーとのみ相互作用する効果を実現できます。
    /// これにより、異なるマテリアルや効果の流体粒子に対して、互いに影響することなく異なるレンダラー、ブラー反復回数などを設定できます。
    /// 例えば、水粒子のみを含むレンダラーと溶岩粒子のみを含むレンダラーを作成できます。
    /// 1. 流体粒子とRenderer Featureの両方で複数のレンダリングレイヤーを指定できます。
    /// 2. 流体粒子とRenderer Featureが同じレンダリングレイヤーを指定しない場合、その粒子はそのレンダラーで処理されません。
    /// </summary>
    [Flags]
    public enum ELiquid2DLayer
    {
        /// <summary>
        /// 默认。 // Default. // デフォルト。
        /// </summary>
        Default = 1 << 0,

        /// <summary>
        /// 水。 // Water. // 水。
        /// </summary>
        Water = 1 << 1,

        /// <summary>
        /// 熔岩。 // Magma. // 溶岩。
        /// </summary>
        Magma = 1 << 2,

        /// <summary>
        /// 水。可以混合颜色。 // Water that can mix colors. // 色を混ぜることができる水。
        /// </summary>
        WaterMix = 1 << 3,
        
        /// <summary>
        /// 牛奶。 // Milk. // ミルク。
        /// </summary>
        Milk = 1 << 4,
        
        // 添加你需要的更多层。
    }
}
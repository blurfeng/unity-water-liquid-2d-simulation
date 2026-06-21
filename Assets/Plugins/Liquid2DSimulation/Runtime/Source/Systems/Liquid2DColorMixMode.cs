namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体粒子颜色混合算法模式（全局设置，由 <see cref="Liquid2DSimulation.ColorMixMode"/> 控制）。
    /// Fluid particle colour-mixing algorithm mode (global setting, controlled via <see cref="Liquid2DSimulation.ColorMixMode"/>).
    /// 流体パーティクルの色混合アルゴリズムモード（グローバル設定、<see cref="Liquid2DSimulation.ColorMixMode"/> で制御）。
    /// </summary>
    public enum Liquid2DColorMixMode
    {
        /// <summary>
        /// 线性 RGB 空间算术平均（旧行为）。蓝 + 黄 → 灰（RGB 互补色）。兼容旧资产。
        /// Linear-RGB arithmetic average (legacy behaviour). Blue + Yellow → Grey (RGB complementary). Backward compatible.
        /// 線形 RGB 算術平均（旧動作）。青＋黄→灰（RGB 補色）。旧アセット互換。
        /// </summary>
        LinearRgb = 0,

        /// <summary>
        /// Oklab 感知均匀色彩空间混合（默认）。颜色过渡自然，不出现灰色塌陷。
        /// Oklab perceptually-uniform colour-space mixing (default). Natural transitions, no grey collapse.
        /// Oklab 知覚均一色空間での混合（デフォルト）。自然な遷移、グレー崩壊なし。
        /// </summary>
        Oklab = 1,

        /// <summary>
        /// RYB 颜料色轮混合（美术三原色：红—黄—蓝）。蓝 + 黄 → 绿，模拟真实颜料。
        /// RYB pigment colour-wheel mixing (artistic primaries: red–yellow–blue). Blue + Yellow → Green; mimics real paint.
        /// RYB 顔料色相環混合（美術三原色：赤—黄—青）。青＋黄→緑、実際の絵具を模倣。
        /// </summary>
        Ryb = 2,
    }
}

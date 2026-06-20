using Fs.Liquid2D.Localization;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体粒子类型描述符。纯数据架构下取代「粒子 prefab」，定义一类粒子的外观（渲染设置）、混色规则、
    /// 物理材质与默认尺寸/寿命。生成时由 <see cref="Liquid2DSpawner"/> 引用，注册进 <see cref="Liquid2DSimulation"/> 得到 typeId，
    /// 渲染按描述符分组批量绘制。
    /// Fluid particle type descriptor. In the pure-data architecture it replaces the "particle prefab": it defines a
    /// particle class's appearance (render settings), mixing rules, physics material, and default size/lifetime.
    /// Referenced by <see cref="Liquid2DSpawner"/>, registered into <see cref="Liquid2DSimulation"/> for a typeId, and
    /// rendered in descriptor-grouped instanced batches.
    /// 流体パーティクル型記述子。純データアーキテクチャで「パーティクル prefab」を置き換え、外観（描画設定）、
    /// 混色ルール、物理マテリアル、既定サイズ/寿命を定義します。<see cref="Liquid2DSpawner"/> が参照し、
    /// <see cref="Liquid2DSimulation"/> に登録して typeId を得て、記述子ごとにまとめて描画されます。
    /// </summary>
    [CreateAssetMenu(fileName = "Liquid2DParticleDescriptor", menuName = "Liquid2D/Particle Descriptor", order = 0)]
    public class Liquid2DParticleDescriptor : ScriptableObject
    {
        [Min(0.001f), LocalizationTooltip(
             "粒子半径（世界单位）。影响碰撞/邻居半径与物理间距。",
             "Particle radius (world units). Affects collision/neighbor radius and physics spacing.",
             "粒子半径（ワールド単位）。衝突/近傍半径と物理間隔に影響します。")]
        public float Radius = 0.2f;

        [Min(0.1f), LocalizationTooltip(
             "渲染可视倍率：绘制 quad 直径 = 半径 × 2 × 此值。metaball 融合需要远大于物理半径的可视 blob，建议 4~8。",
             "Render visual multiplier: drawn quad diameter = radius × 2 × this. Metaball fusion needs visual blobs much larger than the physics radius; 4~8 recommended.",
             "描画倍率：quad 直径 = 半径 × 2 × この値。メタボール融合には物理半径より大きな可視 blob が必要（4~8 推奨）。")]
        public float RenderScale = 5f;

        [Min(0f), LocalizationTooltip(
             "默认生命时间（秒），0 表示无限。可被生成参数覆盖。",
             "Default lifetime (seconds), 0 = infinite. Can be overridden by spawn parameters.",
             "既定の寿命（秒）、0 は無限。生成パラメータで上書き可能。")]
        public float DefaultLifetime;
        
        [LocalizationTooltip(
             "流体粒子渲染设置（贴图/材质/默认颜色/nameTag）。",
             "Fluid particle render settings (sprite/material/default color/nameTag).",
             "流体粒子の描画設定（スプライト/マテリアル/既定色/nameTag）。")]
        public Liquid2DParticleRenderSettings RenderSettings;

        [LocalizationTooltip(
             "颜色混合设置。",
             "Color mixing settings.",
             "色の混合設定。")]
        public Liquid2DParticleMixSettings MixSettings = new Liquid2DParticleMixSettings();

        [LocalizationTooltip(
             "物理材质（质量/粘性/张力/摩擦等，用于模拟水/熔岩/泡沫/沙子）。",
             "Physics material (mass/viscosity/tension/friction, etc. to simulate water/lava/foam/sand).",
             "物理マテリアル（質量/粘性/張力/摩擦など。水/溶岩/泡/砂の表現用）。")]
        public Liquid2DParticleMaterial Material = new Liquid2DParticleMaterial();

        /// <summary>
        /// 运行时由 <see cref="Liquid2DSimulation"/> 分配的类型索引（typeId）。-1 表示未注册。
        /// Type index (typeId) assigned at runtime by <see cref="Liquid2DSimulation"/>. -1 means unregistered.
        /// ランタイムに <see cref="Liquid2DSimulation"/> が割り当てる型インデックス（typeId）。-1 は未登録。
        /// </summary>
        [System.NonSerialized] public int RuntimeTypeId = -1;

        /// <summary>
        /// 描述符是否可用于渲染（贴图与材质齐备）。
        /// Whether the descriptor is renderable (sprite and material present).
        /// 記述子が描画可能か（スプライトとマテリアルが揃っているか）。
        /// </summary>
        public bool IsValid() => RenderSettings != null && RenderSettings.IsValid();
    }
}

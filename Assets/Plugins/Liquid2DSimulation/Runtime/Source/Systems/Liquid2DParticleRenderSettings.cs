using System;
using UnityEngine;
using Fs.Liquid2D.Localization;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体粒子渲染器设置。
    /// Fluid particle renderer settings.
    /// 流体粒子レンダラー設定。
    /// </summary>
    [Serializable]
    public class Liquid2DParticleRenderSettings
    {
        [LocalizationTooltip(
             "流体粒子贴图。",
             "Fluid particle sprite texture.",
             "流体パーティクルスプライトテクスチャ。")]
        public Sprite sprite;
    
        [LocalizationTooltip(
             "流体粒子材质。",
             "Fluid particle material.",
             "流体パーティクルマテリアル。")]
        public Material material;
    
        [ColorUsage(true, true), LocalizationTooltip(
             "流体粒子颜色。",
             "Fluid particle color.",
             "流体パーティクルカラー。")]
        public Color color = new Color(0f, 1f, 4f, 1f);
        
        [LocalizationTooltip(
            "2D流体 Renderer Feature 名称标签，用于区分不同的 Renderer Feature 配置对应的流体粒子。如果你要使用 Volume 来控制流体效果，请确保名称标签唯一且和 Volume Profile 中的标签一致。",
            "2D fluid Renderer Feature name tag, used to distinguish fluid particles corresponding to different Renderer Feature configurations. If you want to use Volume to control fluid effects, please ensure that the name tag is unique and consistent with the tag in the Volume Profile.",
            "2D流体レンダラーフィーチャーの名前タグ。異なるレンダラーフィーチャー構成に対応する流体パーティクルを区別するために使用されます。ボリュームを使用して流体効果を制御する場合は、名前タグが一意であり、ボリュームプロファイルのタグと一致していることを確認してください。")]
        public string nameTag = "Liquid2D";
    
        /// <summary>
        /// 检查渲染器设置是否有效。
        /// Check if renderer settings are valid.
        /// レンダラー設定が有効かチェック。
        /// </summary>
        /// <returns></returns>
        public bool IsValid()
        {
            return sprite && material;
        }
    
        public bool Equals(Liquid2DParticleRenderSettings other)
        {
            if (other == null) return false;
            return sprite == other.sprite && material == other.material;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Liquid2DParticleRenderSettings);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = hash * 23 + (sprite ? sprite.GetHashCode() : 0);
                hash = hash * 23 + (material ? material.GetHashCode() : 0);
                return hash;
            }
        }
    }
}
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
    public class Liquid2dParticleSettings
    {
        [SerializeField, LocalizationTooltip("流体粒子贴图。",
             "Fluid particle sprite texture.",
             "流体パーティクルスプライトテクスチャ。")]
        public Sprite sprite;
    
        [SerializeField, LocalizationTooltip("流体粒子材质。",
             "Fluid particle material.",
             "流体パーティクルマテリアル。")]
        public Material material;
    
        [SerializeField, ColorUsage(true, true), LocalizationTooltip("流体粒子颜色。",
             "Fluid particle color.",
             "流体パーティクルカラー。")]
        public Color color = new Color(0f, 1f, 4f, 1f);
        
        [LocalizationTooltip("2D流体层遮罩。定义了这个流体粒子属于哪个层。",
             "2D fluid layer mask. Defines which layer this fluid particle belongs to.",
             "2D流体レイヤーマスク。この流体パーティクルがどのレイヤーに属するかを定義します。")]
        public ELiquid2DLayer liquid2DLayerMask = ELiquid2DLayer.Water;
    
        /// <summary>
        /// 检查渲染器设置是否有效。
        /// Check if renderer settings are valid.
        /// レンダラー設定が有効かチェック。
        /// </summary>
        /// <returns></returns>
        public bool IsValid()
        {
            return sprite != null && material != null;
        }
    
        public bool Equals(Liquid2dParticleSettings other)
        {
            if (other == null) return false;
            return sprite == other.sprite && material == other.material;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Liquid2dParticleSettings);
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
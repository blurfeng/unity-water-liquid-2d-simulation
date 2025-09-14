using System;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体粒子渲染器设置。
    /// </summary>
    [Serializable]
    public class Liquid2dParticleRendererSettings
    {
        [SerializeField, Tooltip("流体粒子贴图。")]
        public Sprite sprite;
    
        [SerializeField, Tooltip("流体粒子材质。")]
        public Material material;
    
        [SerializeField, ColorUsage(true, true), Tooltip("流体粒子颜色。")]
        public Color color = new Color(0f, 1f, 4f, 1f);
        
        [Tooltip("2D流体层遮罩。定义了这个流体粒子属于哪个层。")]
        public ELiquid2DLayer liquid2DLayerMask = ELiquid2DLayer.Water;
    
        /// <summary>
        /// 检查渲染器设置是否有效。
        /// </summary>
        /// <returns></returns>
        public bool IsValid()
        {
            return sprite != null && material != null;
        }
    
        public bool Equals(Liquid2dParticleRendererSettings other)
        {
            if (other == null) return false;
            return sprite == other.sprite && material == other.material;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as Liquid2dParticleRendererSettings);
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
using System;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// 2D液体渲染器。
/// 挂载此组件到流体粒子对象上以启用液体渲染。
/// </summary>
public class Liquid2dRenderer : MonoBehaviour
{
    [SerializeField]
    private Liquid2dParticleRendererSettings settings = new Liquid2dParticleRendererSettings();
    public Liquid2dParticleRendererSettings Settings => settings;
    
    private void Awake()
    {
        if (!IsValid()) return;
    }

    private void OnEnable()
    {
        if (!IsValid())
        {
            Debug.LogWarning("Liquid2dRenderer is not valid.");
            return;
        }
        
        Liquid2dFeature.RegisterLiquidParticle(this);
    }
    
    private void OnDisable()
    {
        if (!IsValid()) return;
        
        Liquid2dFeature.UnregisterLiquidParticle(this);
    }
    
    private bool IsValid()
    {
        if (!settings.IsValid())
            return false;

        return true;
    }
}

/// <summary>
/// 流体粒子渲染器设置。
/// </summary>
[Serializable]
public class Liquid2dParticleRendererSettings
{
    [SerializeField]
    public Sprite sprite;
    
    [SerializeField]
    public Material material;
    
    [SerializeField, ColorUsage(true, true)]
    public Color color = new Color(0f, 1f, 4f, 1f);
    
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
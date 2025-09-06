using System;
using UnityEngine;
using UnityEngine.Rendering;

[Serializable]
public class Liquid2dSettings
{
    public ERenderingLayerMask renderingLayerMask = ERenderingLayerMask.Liquid;
    public int iterations = 3;
    public float blurSpread = 0.6f;
    
    public Liquid2dSettings Clone()
    {
        return new Liquid2dSettings
        {
            renderingLayerMask = renderingLayerMask,
            iterations = iterations,
            blurSpread = blurSpread
        };
    }
}
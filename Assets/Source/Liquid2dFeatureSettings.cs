using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Serialization;

[Serializable]
public class Liquid2dFeatureSettings
{
    public ELiquid2dLayer liquid2dLayer = ELiquid2dLayer.Water;
    public int iterations = 7;
    public float blurSpread = 0.6f;
    
    public Liquid2dFeatureSettings Clone()
    {
        return new Liquid2dFeatureSettings
        {
            liquid2dLayer = liquid2dLayer,
            iterations = iterations,
            blurSpread = blurSpread
        };
    }
}
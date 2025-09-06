using System;
using UnityEngine.Rendering;

[Serializable]
public class UIntParameter : VolumeParameter<uint>
{
    public UIntParameter(uint value, bool overrideState = false) : base(value, overrideState) { }
}
using System;
using UnityEngine.Rendering;

namespace Fs.Liquid2D.Volumes
{
    [Serializable]
    public class UIntParameter : VolumeParameter<uint>
    {
        public UIntParameter(uint value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }
}
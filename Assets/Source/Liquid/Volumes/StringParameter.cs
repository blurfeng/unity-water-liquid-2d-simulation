using System;
using UnityEngine.Rendering;

namespace Fs.Liquid2D.Volumes
{
    [Serializable]
    public class StringParameter : VolumeParameter<string>
    {
        public StringParameter(string value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }
}
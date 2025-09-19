using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace Fs.Liquid2D.Volumes
{
    [Serializable]
    public class Liquid2DVolumeDataListParameter : VolumeParameter<List<Liquid2DVolumeData>>
    {
        public Liquid2DVolumeDataListParameter(List<Liquid2DVolumeData> value, bool overrideState = false)
            : base(value, overrideState) { }
    }
}
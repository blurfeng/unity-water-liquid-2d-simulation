using System.ComponentModel;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fs.Liquid2D.Volumes
{
    [DisplayName("Liquid2D")]
    public class Liquid2DVolume : VolumeComponent
    {
        public BoolParameter isActive = new BoolParameter(true, true);
        [Tooltip("迭代次数，越大越模糊。")]
        public IntParameter iterations = new IntParameter(3);
        [Tooltip("每次迭代的模糊扩散度，越大越模糊。")]
        public FloatParameter blurSpread = new FloatParameter(0.6f);
        
        [HideInInspector]
        public UIntParameter renderingLayerMask = new UIntParameter(2);
    }
}
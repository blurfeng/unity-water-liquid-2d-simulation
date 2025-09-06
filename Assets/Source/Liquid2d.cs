using UnityEngine;
using UnityEngine.Rendering;

namespace Volumes
{
    public class Liquid2d : VolumeComponent
    {
        public BoolParameter isActive = new BoolParameter(true, true);
        [Tooltip("迭代次数，越大越模糊。")]
        public IntParameter iterations = new IntParameter(3);
        [Tooltip("每次迭代的模糊扩散度，越大越模糊。")]
        public FloatParameter blurSpread = new FloatParameter(0.6f);
        // public ColorParameter color = new ColorParameter(new Color(4f,4f,2f, 1f), true, true, true);
        
        [HideInInspector]
        public UIntParameter renderingLayerMask = new UIntParameter(2);
    }
}
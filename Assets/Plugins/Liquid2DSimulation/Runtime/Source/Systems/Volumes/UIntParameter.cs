using System;
using UnityEngine.Rendering;

namespace Fs.Liquid2D.Volumes
{
    /// <summary>
    /// 无符号整数参数。
    /// 用于在 Volume 组件中支持 uint 类型的参数。
    /// </summary>
    [Serializable]
    public class UIntParameter : VolumeParameter<uint>
    {
        public UIntParameter(uint value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }
}
using System;
using UnityEngine.Rendering;

namespace Fs.Liquid2D.Volumes
{
    /// <summary>
    /// 字符串参数。
    /// 用于在 Volume 组件中支持字符串类型的参数。
    /// </summary>
    [Serializable]
    public class StringParameter : VolumeParameter<string>
    {
        public StringParameter(string value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }
}
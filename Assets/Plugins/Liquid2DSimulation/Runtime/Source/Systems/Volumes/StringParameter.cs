using System;
using UnityEngine.Rendering;

namespace Fs.Liquid2D.Volumes
{
    /// <summary>
    /// 字符串参数。
    /// 用于在 Volume 组件中支持字符串类型的参数。
    /// 
    /// String parameter.
    /// Used to support string type parameters in Volume components.
    /// 
    /// 文字列パラメータ。
    /// Volumeコンポーネントで文字列型パラメータをサポートするために使用。
    /// </summary>
    [Serializable]
    public class StringParameter : VolumeParameter<string>
    {
        public StringParameter(string value, bool overrideState = false) : base(value, overrideState)
        {
        }
    }
}
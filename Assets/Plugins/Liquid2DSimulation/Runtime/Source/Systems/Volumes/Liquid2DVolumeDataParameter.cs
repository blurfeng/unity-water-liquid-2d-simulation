using System;
using System.Collections.Generic;
using UnityEngine.Rendering;

namespace Fs.Liquid2D.Volumes
{
    /// <summary>
    /// 2D流体 Volume 数据列表参数。
    /// 用于在 Volume 组件中支持多组 Liquid2DVolumeData 配置。
    /// </summary>
    [Serializable]
    public class Liquid2DVolumeDataListParameter : VolumeParameter<List<Liquid2DVolumeData>>
    {
        public Liquid2DVolumeDataListParameter(List<Liquid2DVolumeData> value, bool overrideState = false)
            : base(value, overrideState) { }
    }
}
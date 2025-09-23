using System;
using System.Collections.Generic;
using System.ComponentModel;
using UnityEngine;
using UnityEngine.Rendering;

namespace Fs.Liquid2D.Volumes
{
    [DisplayName("Liquid2D")]
    public class Liquid2DVolume : VolumeComponent
    {
        [Tooltip("是否启用2D流体 Renderer Feature 配置重载。")]
        public BoolParameter isActive = new BoolParameter(true, true);
        
        /// <summary>
        /// 2D流体 Volume 数据列表。
        /// 因为 Volume 不支持添加相同类型的组件，所以这里使用列表来支持多组配置。
        /// 每个 Liquid2DVolumeData 对应一个 Renderer Feature 的配置。使用 nameTag 来区分不同的配置。
        /// 
        /// 2D fluid Volume data list.
        /// Since Volume doesn't support adding components of the same type, we use a list here to support multiple configurations.
        /// Each Liquid2DVolumeData corresponds to a Renderer Feature configuration. Use nameTag to distinguish different configurations.
        /// 
        /// 2D流体Volumeデータリスト。
        /// Volumeは同じタイプのコンポーネントの追加をサポートしていないため、ここではリストを使用して複数の設定をサポートします。
        /// 各Liquid2DVolumeDataはRenderer Featureの設定に対応します。nameTagを使用して異なる設定を区別します。
        /// </summary>
        public Liquid2DVolumeDataListParameter liquid2DVolumeDataList = new Liquid2DVolumeDataListParameter(
            new List<Liquid2DVolumeData>
            {
                new Liquid2DVolumeData("Liquid2D"),
            }, true);
        
        /// <summary>
        /// 根据名称标签获取对应的 Liquid2DVolumeData。
        /// Get corresponding Liquid2DVolumeData based on name tag.
        /// 名前タグに基づいて対応するLiquid2DVolumeDataを取得。
        /// </summary>
        /// <param name="nameTag"></param>
        /// <param name="data"></param>
        /// <returns></returns>
        public bool GetData(string nameTag, out Liquid2DVolumeData data)
        {
            data = null;
            
            var list = liquid2DVolumeDataList.value;
            if ( list != null && list.Count > 0)
            {
                foreach (var item in list)
                {
                    if (item.nameTag == nameTag)
                    {
                        data = item;
                        return true;
                    }
                }
            }
            
            return data != null;
        }
    }

    /// <summary>
    /// 2D流体 Volume 数据。
    /// 允许在运行时通过 Volume 系统重载 Liquid2DRenderFeatureSettings 的配置。
    /// </summary>
    [Serializable]
    public class Liquid2DVolumeData : Liquid2DRenderFeatureSettings
    {
        public Liquid2DVolumeData(string nameTag)
        {
            this.nameTag = nameTag;
        }
        
        [Tooltip("是否启用该配置。")]
        public bool isActive;
    }
}
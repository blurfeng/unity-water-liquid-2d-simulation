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
        
        public Liquid2DVolumeDataListParameter liquid2DVolumeDataList = new Liquid2DVolumeDataListParameter(
            new List<Liquid2DVolumeData>
            {
                new Liquid2DVolumeData("Liquid2D"),
            }, true);
        
        /// <summary>
        /// 根据名称标签获取对应的 Liquid2DVolumeData。
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

    [Serializable]
    public class Liquid2DVolumeData
    {
        public Liquid2DVolumeData(string nameTag)
        {
            this.nameTag = nameTag;
            
            isActive = true;
            iterations = 7;
            blurSpread = 0.6f;
            liquid2DLayerMask = ELiquid2DLayer.Water;
        }
        
        [Tooltip("是否启用该配置。")]
        public bool isActive;
        
        [Tooltip("2D流体 Renderer Feature 名称标签，用于区分不同的 Renderer Feature 配置。")]
        public string nameTag;
        
        [Range(3, 16), Tooltip("迭代次数，越大越模糊。")]
        public int iterations;
        
        [Range(0.01f, 1f), Tooltip("每次迭代的模糊扩散度，越大越模糊。")]
        public float blurSpread;
        
        [Tooltip("2D流体层遮罩。只会渲染设定的流体层的粒子。")]
        public ELiquid2DLayer liquid2DLayerMask;
    }
}
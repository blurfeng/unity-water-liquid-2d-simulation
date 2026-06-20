using System;
using Fs.Liquid2D.Localization;
using Fs.Liquid2D.Utility;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 用于喷射的流体粒子设置。
    /// Liquid particle settings for spawning.
    /// スポーン用の流体パーティクル設定。
    /// </summary>
    [Serializable]
    public class Liquid2DParticleConfig : IRandomData
    {
        [LocalizationTooltip("流体粒子描述符（定义外观/混色/材质/尺寸）。",
            "Fluid particle descriptor (defines appearance/mixing/material/size).",
            "流体パーティクル記述子（外観/混色/マテリアル/サイズを定義）。")]
        public Liquid2DParticleDescriptor Descriptor;

        [LocalizationTooltip("权重，决定被选中的概率。",
            "Weight, determines the probability of being selected.",
            "重み、選択される確率を決定します。")]
        public int Weight = 1;

        [LocalizationTooltip("生命周期，单位秒。大于0则覆盖描述符默认寿命并在到时后自动销毁。",
            "Lifetime in seconds. If greater than 0, overrides the descriptor default and auto-destroys after expiry.",
            "ライフタイム（秒）。0より大きい場合、記述子の既定を上書きし期限後に自動破棄。")]
        public float Lifetime;

        public int GetWeight()
        {
            return Weight;
        }

#if UNITY_EDITOR
        public void OnValidate()
        {
            if (Descriptor && !Descriptor.IsValid())
            {
                Debug.LogWarning($"Liquid2DParticleDescriptor {Descriptor.name} 缺少 Sprite/Material，渲染将被跳过。");
            }
        }
#endif
    }
}
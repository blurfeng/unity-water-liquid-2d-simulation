using UnityEngine;
using Fs.Liquid2D.Localization;

namespace Fs.Liquid2D
{
    public class DeadZone : MonoBehaviour
    {
        [LocalizationTooltip("需要销毁的目标层。",
             "Target layers to be destroyed.",
             "破棄する対象レイヤー。")] 
        public LayerMask layerMask;

        private void OnTriggerEnter2D(Collider2D other)
        {
            if (((1 << other.gameObject.layer) & layerMask.value) != 0)
            {
                Loader.Destroy(other.gameObject);
            }
        }
    }
}
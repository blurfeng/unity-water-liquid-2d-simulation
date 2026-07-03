using UnityEngine;
using Fs.Liquid2D.Localization;

namespace Fs.Liquid2D
{
    public class AutoRotator : MonoBehaviour
    {
        [SerializeField, LocalizationTooltip("旋转速度（度/秒）",
             "Rotation speed (degrees per second)",
             "回転速度（度/秒）")]
        private float rotateSpeed = 10f;

        private void Update()
        {
            transform.Rotate(0f, 0f, rotateSpeed * Time.deltaTime);
        }
    }
}
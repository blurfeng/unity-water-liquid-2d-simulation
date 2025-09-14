using UnityEngine;

namespace Fs.Liquid2D
{
    public class AutoRotator : MonoBehaviour
    {
        [Tooltip("旋转速度（度/秒）")]
        public float rotateSpeed = 10f;

        void Update()
        {
            transform.Rotate(0f, 0f, rotateSpeed * Time.deltaTime);
        }
    }
}
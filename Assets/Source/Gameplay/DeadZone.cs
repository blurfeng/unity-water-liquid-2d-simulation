using UnityEngine;

public class DeadZone : MonoBehaviour
{
    [Tooltip("需要销毁的目标层")]
    public LayerMask layerMask;

    private void OnTriggerEnter2D(Collider2D other)
    {
        if (((1 << other.gameObject.layer) & layerMask.value) != 0)
        {
            Destroy(other.gameObject);
        }
    }
}
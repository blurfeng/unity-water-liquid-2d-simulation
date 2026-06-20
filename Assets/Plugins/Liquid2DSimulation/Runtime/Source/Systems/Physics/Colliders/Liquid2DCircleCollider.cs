using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 圆形流体碰撞体。 // Circle fluid collider. // 円形流体コライダー。
    /// </summary>
    [AddComponentMenu("Liquid2D/Colliders/Liquid2D Circle Collider")]
    public class Liquid2DCircleCollider : Liquid2DCollider
    {
        [SerializeField, Min(0f), LocalizationTooltip("半径（受物体缩放影响）。", "Radius (scaled by transform).", "半径（スケール影響）。")]
        private float radius = 0.5f;

        public override Liquid2DColliderShape Shape => Liquid2DColliderShape.Circle;

        public override void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum)
        {
            float scale = Mathf.Abs(CachedTransform.lossyScale.x);
            data.shape = Liquid2DColliderShape.Circle;
            data.center = WorldCenter;
            data.radius = radius * scale;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            float scale = Mathf.Abs(transform.lossyScale.x);
            Gizmos.DrawWireSphere(transform.position, radius * scale);
        }
#endif
    }
}

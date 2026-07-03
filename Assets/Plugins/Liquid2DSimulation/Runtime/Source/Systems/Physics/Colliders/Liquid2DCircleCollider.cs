using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 圆形流体碰撞体。 // Circle fluid collider. // 円形流体コライダー。
    /// </summary>
    [AddComponentMenu("Liquid 2D/Colliders/Liquid 2D Circle Collider")]
    public class Liquid2DCircleCollider : Liquid2DCollider
    {
        [SerializeField, Min(0f), LocalizationTooltip("半径（受物体缩放影响）。", "Radius (scaled by transform).", "半径（スケール影響）。")]
        private float radius = 0.5f;

        public override Liquid2DColliderShape Shape => Liquid2DColliderShape.Circle;

        public override void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum)
        {
            // 半径仅按 lossyScale.x 缩放：假设 XY 均匀缩放。非均匀缩放（sx≠sy）下 Y 被忽略，碰撞圆与可视/作者意图不符——请保持均匀缩放。
            // Radius scales by lossyScale.x only, assuming uniform XY scale. Under non-uniform scale (sx≠sy) Y is ignored and the collision circle won't match the visual — keep scale uniform. // 半径は lossyScale.x のみ（均一スケール前提）。
            float scale = Mathf.Abs(CachedTransform.lossyScale.x);
            data.Shape = Liquid2DColliderShape.Circle;
            data.Center = WorldCenter;
            data.Radius = radius * scale;
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

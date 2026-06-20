using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 胶囊流体碰撞体（沿局部 X 轴的线段 + 半径）。 // Capsule fluid collider (segment along local X + radius). // カプセル流体コライダー。
    /// </summary>
    [AddComponentMenu("Liquid 2D/Colliders/Liquid 2D Capsule Collider")]
    public class Liquid2DCapsuleCollider : Liquid2DCollider
    {
        [SerializeField, Min(0f), LocalizationTooltip("线段全长（沿局部 X 轴）。", "Segment full length (along local X).", "線分の全長（ローカル X 軸）。")]
        private float length = 1f;

        [SerializeField, Min(0f), LocalizationTooltip("胶囊半径。", "Capsule radius.", "カプセル半径。")]
        private float radius = 0.25f;

        public override Liquid2DColliderShape Shape => Liquid2DColliderShape.Capsule;

        public override void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum)
        {
            float scale = Mathf.Abs(CachedTransform.lossyScale.x);
            data.Shape = Liquid2DColliderShape.Capsule;
            data.Center = WorldCenter;
            data.Rotation = ZRotationRadians;
            data.Size = new float2(0.5f * length * scale, 0f);
            data.Radius = radius * scale;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, Vector3.one * Mathf.Abs(transform.lossyScale.x));
            float hl = 0.5f * length;
            Gizmos.DrawWireSphere(new Vector3(-hl, 0f, 0f), radius);
            Gizmos.DrawWireSphere(new Vector3(hl, 0f, 0f), radius);
            Gizmos.DrawLine(new Vector3(-hl, radius, 0f), new Vector3(hl, radius, 0f));
            Gizmos.DrawLine(new Vector3(-hl, -radius, 0f), new Vector3(hl, -radius, 0f));
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}

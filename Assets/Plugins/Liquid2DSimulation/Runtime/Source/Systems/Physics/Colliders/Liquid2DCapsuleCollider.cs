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
            // 长度与半径均仅按 lossyScale.x 缩放：假设 XY 均匀缩放。非均匀缩放（sx≠sy）下 Y 被忽略，碰撞形状与可视/作者意图不符——请保持均匀缩放。
            // Length and radius scale by lossyScale.x only, assuming uniform XY scale. Under non-uniform scale (sx≠sy) Y is ignored and the collision shape won't match the visual — keep scale uniform. // 長さ/半径は lossyScale.x のみ（均一スケール前提）。
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

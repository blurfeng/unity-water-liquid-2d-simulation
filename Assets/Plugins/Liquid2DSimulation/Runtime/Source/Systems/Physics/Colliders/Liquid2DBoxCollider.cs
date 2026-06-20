using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 有向矩形盒（OBB）流体碰撞体，支持旋转。 // Oriented box (OBB) fluid collider, supports rotation. // 有向ボックス（OBB）流体コライダー。
    /// </summary>
    [AddComponentMenu("Liquid2D/Colliders/Liquid2D Box Collider")]
    public class Liquid2DBoxCollider : Liquid2DCollider
    {
        [SerializeField, LocalizationTooltip("盒尺寸（全尺寸，受物体缩放影响）。", "Box size (full size, scaled by transform).", "ボックスサイズ（全サイズ、スケール影響）。")]
        private Vector2 size = Vector2.one;

        public override Liquid2DColliderShape Shape => Liquid2DColliderShape.Box;

        public override void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum)
        {
            Vector3 ls = CachedTransform.lossyScale;
            data.Shape = Liquid2DColliderShape.Box;
            data.Center = WorldCenter;
            data.Rotation = ZRotationRadians;
            data.Size = new float2(0.5f * size.x * Mathf.Abs(ls.x), 0.5f * size.y * Mathf.Abs(ls.y));
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.8f, 1f, 0.6f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0f));
            Gizmos.matrix = Matrix4x4.identity;
        }
#endif
    }
}

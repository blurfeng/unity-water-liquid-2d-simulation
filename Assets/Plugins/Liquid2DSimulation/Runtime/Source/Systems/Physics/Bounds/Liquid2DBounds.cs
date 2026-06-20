using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D 流体边界容器。流体粒子不能离开此边界，超出时被推回内侧。支持旋转与缩放。
    /// 与 <see cref="Liquid2DBoxCollider"/> 相反：Box 把粒子推到外侧，Bounds 把粒子推到内侧。
    /// 2D fluid boundary container. Fluid particles cannot leave this boundary; they are pushed back inward when escaping.
    /// Supports rotation and scale. The inverse of <see cref="Liquid2DBoxCollider"/>: Box pushes particles outward, Bounds inward.
    /// 2D 流体境界コンテナ。粒子はこの境界を超えられず、超えると内側へ押し戻されます。
    /// <see cref="Liquid2DBoxCollider"/> と逆：Box は外へ、Bounds は内へ押します。
    /// </summary>
    [AddComponentMenu("Liquid2D/Physics/Liquid2D Bounds")]
    public class Liquid2DBounds : Liquid2DCollider
    {
        [SerializeField, LocalizationTooltip(
             "边界尺寸（全尺寸，受物体缩放影响）。粒子将被限制在此范围内。",
             "Boundary size (full size, scaled by transform). Particles are confined within this area.",
             "境界サイズ（全サイズ、スケール影響）。粒子はこの範囲内に制限されます。")]
        private Vector2 size = new Vector2(10f, 10f);

        public override Liquid2DColliderShape Shape => Liquid2DColliderShape.BoundsBox;

        public override void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum)
        {
            Vector3 ls = CachedTransform.lossyScale;
            data.shape = Liquid2DColliderShape.BoundsBox;
            data.center = WorldCenter;
            data.rotation = ZRotationRadians;
            data.size = new float2(0.5f * size.x * Mathf.Abs(ls.x), 0.5f * size.y * Mathf.Abs(ls.y));
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.7f);
            Gizmos.matrix = Matrix4x4.TRS(transform.position, transform.rotation, transform.lossyScale);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(size.x, size.y, 0f));

            // 四条向内指示箭头，提示此为内边界。 // Four inward arrows to indicate this is an interior boundary.
            float arrowLen = Mathf.Min(size.x, size.y) * 0.08f;
            float halfX = size.x * 0.5f;
            float halfY = size.y * 0.5f;
            DrawInwardArrow(new Vector3( halfX, 0f, 0f), Vector3.left,  arrowLen);
            DrawInwardArrow(new Vector3(-halfX, 0f, 0f), Vector3.right, arrowLen);
            DrawInwardArrow(new Vector3(0f,  halfY, 0f), Vector3.down,  arrowLen);
            DrawInwardArrow(new Vector3(0f, -halfY, 0f), Vector3.up,    arrowLen);

            Gizmos.matrix = Matrix4x4.identity;
        }

        private static void DrawInwardArrow(Vector3 origin, Vector3 dir, float len)
        {
            Vector3 tip = origin + dir * len;
            Gizmos.DrawLine(origin, tip);
            float headLen = len * 0.35f;
            Vector3 left  = (Quaternion.Euler(0f, 0f,  45f) * dir) * headLen;
            Vector3 right = (Quaternion.Euler(0f, 0f, -45f) * dir) * headLen;
            Gizmos.DrawLine(tip, tip - left);
            Gizmos.DrawLine(tip, tip - right);
        }
#endif
    }
}

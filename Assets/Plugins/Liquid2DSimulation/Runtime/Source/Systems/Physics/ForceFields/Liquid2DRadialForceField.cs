using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 径向力场：以本物体 Transform 位置为中心，持续对范围内粒子施加吸引（strength&gt;0）或排斥（strength&lt;0）力。
    /// 可用作场景中的固定吸引器 / 排斥器 / 抽水口等。强度可通过 <see cref="Liquid2DForceFieldSource.Strength"/> 运行时改写。
    /// Radial force field: centered on this object's Transform, continuously applies an attract (strength&gt;0) or repel
    /// (strength&lt;0) force to particles in range. Use as a static attractor / repeller / drain in a scene. Strength can be
    /// changed at runtime via <see cref="Liquid2DForceFieldSource.Strength"/>.
    /// 径方向力場：本オブジェクトの Transform 位置を中心に、範囲内の粒子へ引力/斥力を継続的に与えます。
    /// </summary>
    [AddComponentMenu("Liquid2D/Physics/Force Fields/Liquid2D Radial Force Field")]
    public class Liquid2DRadialForceField : Liquid2DForceFieldSource
    {
        public override bool TryGetField(out Liquid2DForceFieldData data)
        {
            Vector3 p = (CachedTransform ? CachedTransform : transform).position;
            data = new Liquid2DForceFieldData
            {
                Center = new float2(p.x, p.y),
                Radius = Radius,
                Strength = Strength,
                VelocityDamping = VelocityDamping,
            };
            ApplyAdvanced(ref data);
            return true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            Vector3 c = transform.position;
            // 绿=吸引 / 红=排斥。 // Green = attract / Red = repel. // 緑=引力 / 赤=斥力。
            Gizmos.color = Strength >= 0f ? new Color(0f, 1f, 0f, 0.5f) : new Color(1f, 0f, 0f, 0.5f);
            Gizmos.DrawWireSphere(c, Radius);
        }
#endif
    }
}

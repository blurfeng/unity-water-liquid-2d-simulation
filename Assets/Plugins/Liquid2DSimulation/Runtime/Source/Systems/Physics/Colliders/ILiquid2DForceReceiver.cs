using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 本帧由流体作用到某动态物体上的合力信息（双向耦合数据包）。包含净反作用冲量、接触质心与接触粒子数，
    /// 接收者据此驱动物体运动（冲走）、施加浮力/阻尼（漂浮）并在质心处产生力矩（翻倒/摇摆）。
    /// Aggregate fluid-to-body forces for this frame (two-way coupling payload): the net reaction impulse, the contact
    /// centroid, and the contact particle count. A receiver uses these to drive motion (wash away), apply buoyancy/drag
    /// (floating), and produce torque about the centroid (tipping/bobbing).
    /// 本フレームに流体が動的物体へ与える合力情報（双方向カップリングのデータ）。純反作用力積・接触質心・接触粒子数を含み、
    /// レシーバーはこれで物体を駆動（押し流す）し、浮力/減衰（浮遊）を与え、質心で力矩（転倒/揺動）を生みます。
    /// </summary>
    public struct Liquid2DBodyForce
    {
        /// <summary>本帧累积的净反作用冲量（世界坐标）。 // Net reaction impulse accumulated this frame (world space). // 純反作用力積（ワールド）。</summary>
        public float2 Impulse;

        /// <summary>接触质心（世界坐标）。仅当 <see cref="ContactCount"/> &gt; 0 时有效。 // Contact centroid (world). Valid only when ContactCount &gt; 0. // 接触質心（ワールド）。</summary>
        public float2 ContactCenter;

        /// <summary>本帧与该物体接触的流体粒子数（用于浮力/阻尼缩放，0 表示未接触）。 // Fluid particles contacting the body this frame (scales buoyancy/drag; 0 = no contact). // 接触粒子数。</summary>
        public int ContactCount;

        /// <summary>接触处的平均流体密度（按接触粒子的材质 Density 求平均，用于阿基米德浮力）。 // Average fluid mass density at contacts (for Archimedes buoyancy). // 接触処の平均流体密度。</summary>
        public float FluidDensity;

        /// <summary>本帧物理步长（秒），便于接收者把力换算为速度变化。 // This frame's physics timestep (s). // 本フレームの物理ステップ（秒）。</summary>
        public float Dt;
    }

    /// <summary>
    /// 流体反作用力接收者（双向耦合 seam）。动态碰撞体（同物体或父级挂了实现此接口的组件，如
    /// <see cref="Liquid2DRigidbodyBridge"/>）会在每帧末收到流体粒子累积的 <see cref="Liquid2DBodyForce"/>，
    /// 据此驱动物体运动（例如「水流冲走方块」、物体在水中漂浮）。未挂接收者的碰撞体即视为静态。
    /// Fluid reaction-force receiver (two-way coupling seam). A dynamic collider (with a component implementing this
    /// interface on the same object or a parent, e.g. <see cref="Liquid2DRigidbodyBridge"/>) receives the fluid particles'
    /// accumulated <see cref="Liquid2DBodyForce"/> at each frame's end and drives object motion (e.g. "water sweeps away a
    /// block", an object floating in water). A collider with no receiver is treated as static.
    /// 流体反作用力レシーバー（双方向カップリング seam）。実装コンポーネント（例 <see cref="Liquid2DRigidbodyBridge"/>）を
    /// 同オブジェクトまたは親に持つ動的コライダーは、毎フレーム末に累積 <see cref="Liquid2DBodyForce"/> を受け取ります。
    /// </summary>
    public interface ILiquid2DForceReceiver
    {
        /// <summary>
        /// 接收本帧由流体作用的合力信息（世界坐标）。
        /// Receive this frame's aggregate fluid forces (world space).
        /// 本フレームの流体合力情報を受け取ります（ワールド座標）。
        /// </summary>
        /// <param name="force">流体合力数据包。 // fluid force payload. // 流体合力データ。</param>
        void ApplyLiquidForces(in Liquid2DBodyForce force);
    }
}

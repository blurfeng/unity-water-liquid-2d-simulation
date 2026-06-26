using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 本帧由流体作用到某动态物体上的合力信息（双向耦合数据包）。包含接触处的平均流体速度、接触质心、接触粒子数与平均流体密度，
    /// 接收者据此用「相对速度阻力」驱动物体运动（冲走）、用阿基米德浮力实现漂浮/下沉，并可选地在质心处产生力矩（翻倒/摇摆）。
    /// Aggregate fluid-to-body forces for this frame (two-way coupling payload): the average fluid velocity at contacts, the
    /// contact centroid, the contact particle count, and the average fluid density. A receiver uses these to drive motion via
    /// relative-velocity drag (wash away), float/sink via Archimedes buoyancy, and optionally produce torque about the
    /// centroid (tipping/bobbing).
    /// 本フレームに流体が動的物体へ与える合力情報（双方向カップリングのデータ）。接触処の平均流体速度・接触質心・接触粒子数・平均
    /// 流体密度を含み、レシーバーは相対速度抗力で駆動（押し流す）し、アルキメデス浮力で浮沈させ、必要なら質心で力矩を生みます。
    /// </summary>
    public struct Liquid2DBodyForce
    {
        /// <summary>接触处的平均流体速度（世界坐标）。用于相对速度阻力：力 ∝ (流体速度 − 物体速度)。 // Average fluid velocity at contacts (world). For relative-velocity drag: force ∝ (fluidVel − bodyVel). // 接触処の平均流体速度（ワールド）。</summary>
        public float2 FluidVelocity;

        /// <summary>接触质心（世界坐标）。仅当 <see cref="ContactCount"/> &gt; 0 时有效。 // Contact centroid (world). Valid only when ContactCount &gt; 0. // 接触質心（ワールド）。</summary>
        public float2 ContactCenter;

        /// <summary>本帧与该物体接触的流体粒子数（用于浮力/阻力缩放，0 表示未接触）。 // Fluid particles contacting the body this frame (scales buoyancy/drag; 0 = no contact). // 接触粒子数。</summary>
        public int ContactCount;

        /// <summary>接触处的平均流体密度（按全部接触粒子的材质 Density 求平均，用于参考/兼容；阿基米德浮力请改用 <see cref="BuoyancyFluidDensity"/>）。 // Average fluid mass density over all contacts (reference/compat; use BuoyancyFluidDensity for Archimedes buoyancy). // 全接触の平均流体密度（参考・互換）。</summary>
        public float FluidDensity;

        /// <summary>
        /// 「物体下方」接触的流体粒子数（接触法线 n.y &lt; 0）。仅统计位于物体下方、向上托起物体的接触；压在物体顶部的粒子不计入，从而不产生虚假上浮。
        /// 接收者据此估算浮力的浸没比例（缩放阿基米德浮力）。
        /// Count of fluid particles contacting below the body (contact normal n.y &lt; 0). Only contacts below the body (pushing it up)
        /// are counted; particles resting on top are excluded, so they don't create spurious lift. Receivers use this for the
        /// buoyancy submerged fraction (scaling Archimedes buoyancy).
        /// 「物体下方」接触の粒子数（法線 n.y &lt; 0）。上に乗る粒子は除外し偽浮力を防ぐ。浮力の浸水率推定に用いる。
        /// </summary>
        public int BuoyancyContactCount;

        /// <summary>「物体下方」接触的平均流体密度（仅下方接触粒子的材质 Density 求平均），用于阿基米德浮力。 // Average fluid density over below-body contacts only, for Archimedes buoyancy. // 下方接触のみの平均流体密度（アルキメデス浮力用）。</summary>
        public float BuoyancyFluidDensity;

        /// <summary>
        /// 浮力接触粒子的排开体积之和（每粒子格子面积 (2r)²=4r²，2D 面积）。Submerge（淹没）模式下物体被流体包裹，接收者用「此值 / 物体体积」估算浸没比例
        /// （真实排开体积，避免按接触数估算时浅浸即饱和而上下弹跳）。Push 模式不使用此值（仍按下方接触数估算）。
        /// Sum of displaced area (per-particle cell area (2r)²=4r², 2D) over buoyancy contacts. In Submerge mode the body is enveloped, so
        /// receivers use (this / bodyVolume) as the submerged fraction (true displaced volume, avoiding the bobbing caused by count-based
        /// early saturation). Unused in Push mode (which still uses the below-contact count).
        /// 浮力接触の排除面積の和（セル面積 (2r)²=4r²）。Submerge では「この値 / 体積」で浸水率を推定（真の排除体積、弾みを防ぐ）。Push では未使用。
        /// </summary>
        public float BuoyancySubmergedVolume;

        /// <summary>
        /// Submerge（淹没）模式下「物体表面外壳层带」流体粒子的覆盖面积之和（每粒子格子面积 (2r)²=4r²）。接收者用「此值 / 物体体积」缩放 drag/阻尼——
        /// 物体被流体真正包裹时壳层饱满 → 正常受阻；空中只擦过零散粒子时壳层稀疏 → 阻力≈0 → 物体直接自由下落，不被稀疏重叠粒子拖慢。Push 模式不使用此值。
        /// Sum of covered area (per-particle cell area (2r)²=4r²) of fluid particles in the body's outer-shell band, in Submerge mode. The receiver scales drag/damping by
        /// (this / bodyVolume): truly enveloped → full shell → normal resistance; grazing scattered particles in the air → sparse shell → ~0 drag → free fall. Unused in Push mode.
        /// Submerge モードで物体の外殻層帯にある流体粒子の被覆面積の和（セル面積 (2r)²=4r²）。「この値 / 体積」で drag/減衰をスケール（空中の疎な殻層→抗力≈0→自由落下）。Push では未使用。
        /// </summary>
        public float ShellCoverageVolume;

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

        /// <summary>
        /// 上一帧的「内部覆盖率」(0~1)，表示本动态体被流体包裹的程度（≈1 为几乎全部覆盖）。由 <see cref="Liquid2DColliderRegistry"/> 每帧读取回填给碰撞器，
        /// 供求解器门控淹没模式「给四周流体施加力」（几乎全覆盖时才施力，避免在空中错误弹飞飞散的零散流体）。默认 1（不门控）。
        /// Last frame's interior-coverage fraction (0~1): how enveloped this dynamic body is by fluid (≈1 = almost fully covered). Read each frame by <see cref="Liquid2DColliderRegistry"/> and fed back to the collider,
        /// so the solver gates the Submerge "force on surrounding fluid" (apply only when nearly fully covered, avoiding flinging scattered airborne fluid). Default 1 (no gating).
        /// 前フレームの内部被覆率（0~1）。<see cref="Liquid2DColliderRegistry"/> が毎フレーム読み取りコライダーへ回填、四周施力のゲートに使用。既定 1。
        /// </summary>
        float SubmergeCoverage01 { get; }
    }
}

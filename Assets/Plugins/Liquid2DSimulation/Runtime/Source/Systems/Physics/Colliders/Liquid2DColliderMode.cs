namespace Fs.Liquid2D
{
    /// <summary>
    /// 碰撞体与流体粒子的交互模式。
    /// Interaction mode between the collider and fluid particles.
    /// コライダーと流体粒子の相互作用モード。
    /// </summary>
    public enum Liquid2DColliderMode : byte
    {
        /// <summary>
        /// 推离模式（默认）：将粒子推出碰撞体外，粒子无法穿透碰撞体。
        /// Push mode (default): push particles out of the collider; particles cannot penetrate.
        /// 押し出しモード（デフォルト）：粒子をコライダーの外に押し出す。
        /// </summary>
        Push = 0,

        /// <summary>
        /// 淹没模式：粒子可穿过碰撞体内部，碰撞体静止时流体自然覆盖之；碰撞体运动时按其法向速度排开流体产生水花/波纹，
        /// 同时保留双向耦合（碰撞体受流体阻力和浮力）。视觉表现为流体"覆盖"碰撞体。
        /// Submerge mode: particles pass through the collider interior; when the collider is static the fluid covers it
        /// naturally; when moving it displaces fluid by its normal velocity to produce splash/ripple effects. Two-way
        /// coupling (drag and buoyancy on the body) is preserved. Visually the fluid "covers" the collider.
        /// 水没モード：粒子はコライダー内部を通過でき、静止時は流体が自然に覆う。運動時は法線速度で流体を排除して
        /// 水しぶき・波紋を生む。双方向結合（抗力・浮力）は保持される。視覚的に流体がコライダーを「覆う」。
        /// </summary>
        Submerge = 1,
    }
}

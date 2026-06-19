using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体反作用力接收者（双向耦合 seam）。动态碰撞体若实现此接口，会在每帧末收到流体粒子累积的反作用冲量，
    /// 可据此驱动物体运动（例如「水流冲走方块」）。本期默认不挂接收者，冲量被丢弃；之后迭代再桥接到刚体。
    /// Fluid reaction-force receiver (two-way coupling seam). A dynamic collider implementing this interface receives the
    /// fluid particles' accumulated reaction impulse at each frame's end and can drive object motion (e.g. "water sweeps
    /// away a block"). By default no receiver is attached this phase, so impulses are discarded; a later iteration
    /// bridges to a rigid body.
    /// 流体反作用力レシーバー（双方向カップリング seam）。動的コライダーがこのインターフェースを実装すると、
    /// 毎フレーム末に流体粒子の累積反作用力積を受け取り、物体の運動を駆動できます（例：「水流が箱を押し流す」）。
    /// 今回は既定でレシーバーを付けず力積は破棄。後のイテレーションで剛体に橋渡しします。
    /// </summary>
    public interface ILiquid2DForceReceiver
    {
        /// <summary>
        /// 接收本帧累积的反作用冲量（世界坐标）。
        /// Receive the reaction impulse accumulated this frame (world space).
        /// このフレームに累積した反作用力積を受け取ります（ワールド座標）。
        /// </summary>
        /// <param name="impulse">冲量向量。 // impulse vector. // 力積ベクトル。</param>
        void ApplyLiquidImpulse(float2 impulse);
    }
}

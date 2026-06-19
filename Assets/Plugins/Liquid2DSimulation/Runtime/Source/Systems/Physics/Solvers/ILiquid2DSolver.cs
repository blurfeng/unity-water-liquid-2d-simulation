using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 一次求解所需的上下文。store 为托管侧 SoA 容器；其余为 Job 可用的 NativeArray 视图。
    /// Context for a single solve. store is the managed-side SoA container; the rest are Job-usable NativeArray views.
    /// 1回の解法に必要なコンテキスト。store はマネージド側 SoA コンテナ、他は Job が使える NativeArray ビュー。
    /// </summary>
    public struct Liquid2DSolveContext
    {
        /// <summary>粒子 SoA 存储。 // Particle SoA store. // 粒子 SoA ストア。</summary>
        public Liquid2DParticleStore store;

        /// <summary>紧凑的存活 slot 索引表（跳过空洞 slot）。 // Compact alive-slot index list (skips holes). // コンパクトな生存スロット索引表。</summary>
        public NativeArray<int> activeIndices;

        /// <summary>存活粒子数（activeIndices 的有效长度）。 // Number of alive particles (valid length of activeIndices). // 生存粒子数。</summary>
        public int activeCount;

        /// <summary>按 typeId 索引的材质数据。 // Material data indexed by typeId. // typeId で索引するマテリアルデータ。</summary>
        [ReadOnly] public NativeArray<Liquid2DMaterialData> materials;

        /// <summary>按 typeId 索引的混色参数。 // Mix parameters indexed by typeId. // typeId で索引する混色パラメータ。</summary>
        [ReadOnly] public NativeArray<Liquid2DMixData> mixData;

        /// <summary>碰撞体集合。 // Collider set. // コライダー集合。</summary>
        public Liquid2DColliderBuffer colliders;

        /// <summary>动态碰撞体的本帧累积冲量（双向耦合用，长度=动态体数）。 // Per-frame accumulated impulse for dynamic colliders (two-way coupling). // 動的コライダーのフレーム累積力積。</summary>
        public NativeArray<float2> colliderImpulse;

        /// <summary>当前时间（Time.time），用于混色节流等。 // Current time, for mix throttling etc. // 現在時刻（混色スロットリング等）。</summary>
        public float time;
    }

    /// <summary>
    /// 流体求解器接口。CPU/GPU 双模式 seam：本期实现 <see cref="PbfCpuSolver"/>，GPU 预留。
    /// Fluid solver interface. CPU/GPU dual-mode seam: <see cref="PbfCpuSolver"/> is implemented now, GPU reserved.
    /// 流体ソルバーインターフェース。CPU/GPU デュアルモード seam：今回 <see cref="PbfCpuSolver"/> を実装、GPU は予約。
    /// </summary>
    public interface ILiquid2DSolver : IDisposable
    {
        /// <summary>求解器运行平台。 // Solver platform. // ソルバーのプラットフォーム。</summary>
        Liquid2DSimulationMode Mode { get; }

        /// <summary>
        /// 推进一帧（内部按 substeps 子步进）。
        /// Advance one frame (internally substepped by substeps).
        /// 1フレーム進める（内部で substeps によりサブステップ）。
        /// </summary>
        void Step(in Liquid2DSolveContext ctx, in SolverParams p, float dt);
    }
}

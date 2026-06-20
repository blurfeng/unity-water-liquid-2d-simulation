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
        public Liquid2DParticleStore Store;

        /// <summary>紧凑的存活 slot 索引表（跳过空洞 slot）。 // Compact alive-slot index list (skips holes). // コンパクトな生存スロット索引表。</summary>
        public NativeArray<int> ActiveIndices;

        /// <summary>存活粒子数（activeIndices 的有效长度）。 // Number of alive particles (valid length of activeIndices). // 生存粒子数。</summary>
        public int ActiveCount;

        /// <summary>按 typeId 索引的材质数据。 // Material data indexed by typeId. // typeId で索引するマテリアルデータ。</summary>
        [ReadOnly] public NativeArray<Liquid2DMaterialData> Materials;

        /// <summary>按 typeId 索引的混色参数。 // Mix parameters indexed by typeId. // typeId で索引する混色パラメータ。</summary>
        [ReadOnly] public NativeArray<Liquid2DMixData> MixData;

        /// <summary>碰撞体集合。 // Collider set. // コライダー集合。</summary>
        public Liquid2DColliderBuffer Colliders;

        /// <summary>力场集合（吸引/排斥，外力阶段施加）。 // Force-field set (attract/repel, applied in external-forces stage). // 力場集合。</summary>
        public Liquid2DForceFieldBuffer ForceFields;

        /// <summary>动态碰撞体的本帧累积冲量（双向耦合用，长度=动态体数）。 // Per-frame accumulated impulse for dynamic colliders (two-way coupling). // 動的コライダーのフレーム累積力積。</summary>
        public NativeArray<float2> ColliderImpulse;

        /// <summary>销毁区域集合（区域内粒子本帧被回收）。 // Dead-zone set (particles inside are recycled this frame). // 破棄領域集合。</summary>
        public Liquid2DDeadZoneBuffer DeadZones;

        /// <summary>销毁区域数量（>0 时才执行/回读销毁标记）。 // Dead-zone count (kill marking runs/reads back only when >0). // 破棄領域数。</summary>
        public int DeadZoneCount;

        /// <summary>
        /// 销毁标记输出（按 active 索引 k，长度=activeCount）。求解器置 1 表示第 k 个活动粒子落入某销毁区域；
        /// Step 后由 <see cref="Liquid2DSimulation"/> 回收对应 slot。
        /// Kill flag output (indexed by active index k, length = activeCount). The solver sets 1 when the k-th active
        /// particle falls inside a dead zone; <see cref="Liquid2DSimulation"/> recycles the slot after Step.
        /// 破棄フラグ出力（active 索引 k、長さ=activeCount）。
        /// </summary>
        public NativeArray<byte> KillFlags;

        /// <summary>当前时间（Time.time），用于混色节流等。 // Current time, for mix throttling etc. // 現在時刻（混色スロットリング等）。</summary>
        public float Time;

        /// <summary>动态碰撞体数量（>0 时 GPU 才回读冲量）。 // Dynamic collider count (GPU reads impulse back only when >0). // 動的コライダー数。</summary>
        public int DynamicBodyCount;

        /// <summary>
        /// GPU 模式：自上次 Step 以来新生成的粒子 slot 列表（供 GPU 增量上传到常驻缓冲）。CPU 模式忽略。
        /// GPU mode: slots spawned since the last Step (for incremental upload into resident GPU buffers). Ignored on CPU.
        /// GPU モード：前回 Step 以降に生成された slot リスト（常駐バッファへの増分アップロード用）。
        /// </summary>
        public System.Collections.Generic.List<int> GPUPendingSpawns;
    }

    /// <summary>
    /// 流体求解器接口。CPU/GPU 双模式 seam：<see cref="SphCpuSolver"/>（CPU，已实现）与 SphGpuSolver（GPU，Phase 2）。
    /// Fluid solver interface. CPU/GPU dual-mode seam: <see cref="SphCpuSolver"/> (CPU, implemented) and SphGpuSolver (GPU, Phase 2).
    /// 流体ソルバーインターフェース。CPU/GPU デュアルモード seam：<see cref="SphCpuSolver"/>（CPU）と SphGpuSolver（GPU、Phase 2）。
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

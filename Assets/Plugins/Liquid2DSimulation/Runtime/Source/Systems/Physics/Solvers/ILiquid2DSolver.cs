using System;
using Unity.Collections;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// Impact 渐变来源的「冲击泡沫累加器」逐类型参数（预算好的常量）。字段顺序/布局必须与 Compute Shader
    /// （Liquid2DSph.compute）中的 <c>struct DynamicFoamParams</c> 逐字段一致（GPU 直接 SetData 上传）。
    /// 动态泡沫（DensityWithImpact / DensityWithSpeed）逐类型参数（预算好的常量）。生成量 = 密度区域门 × 动态因子：
    /// 区域门 = 密度亏空(FoamStart/FoamRangeInv，FoamStart 卡在内部/水底密度以下→排除内部)；动态因子 = Mode==1 ? 速度门控(saturate((平滑速度−SpeedMin)·SpeedRangeInv)) : 冲击(saturate((Δ平滑密度·InvRestDensity − ImpactRiseMin)·ImpactStrength))。
    /// F = max(F·Decay, 生成量)。内部/水底（区域门0）不产泡；平静/未压实（动态因子0）不产泡且已有泡消退。
    /// Per-type params for the dynamic foam (DensityWithImpact / DensityWithSpeed), precomputed. Field order/layout MUST match
    /// <c>struct DynamicFoamParams</c> in Liquid2DSph.compute (uploaded via SetData). generation = densityGate × dynamicFactor:
    /// densityGate = density deficit (FoamStart/FoamRangeInv); dynamicFactor = Mode==1 ? speedGate(saturate((smoothedSpeed−SpeedMin)·SpeedRangeInv)) : impact(saturate((Δsmoothed·InvRestDensity − ImpactRiseMin)·ImpactStrength)).
    /// F = max(F·Decay, generation).
    /// 動的泡（DensityWithImpact / DensityWithSpeed）の型ごとパラメータ。generation = 密度領域ゲート × 動的因子。フィールド順は compute の DynamicFoamParams と一致必須。
    /// </summary>
    public struct Liquid2DDynamicFoamParams
    {
        /// <summary>= 1/静止密度，把密度/变化量归一化为密度比。 // 1/rest density. // 静止密度の逆数。</summary>
        public float InvRestDensity;
        /// <summary>冲击灵敏度（Mode==0/DensityWithImpact）。 // impact sensitivity (Mode==0). // 衝撃感度。</summary>
        public float ImpactStrength;
        /// <summary>冲击死区：归一化上升率下限，低于此值冲击项为 0（Mode==0/DensityWithImpact）。 // impact deadzone: normalized rise-rate floor (Mode==0). // 衝撃デッドゾーン下限。</summary>
        public float ImpactRiseMin;
        /// <summary>每帧衰减系数 = exp(−fixedDeltaTime/持久度秒)；0=不持久（F=生成量）。 // per-step decay; 0 = not persistent. // 減衰係数。</summary>
        public float Decay;
        /// <summary>区域门上界（密度比）：密度比低于此值才算有效区域（应卡在内部/水底密度以下以排除内部）。 // gate upper bound. // 領域ゲート上界。</summary>
        public float FoamStart;
        /// <summary>= 1/(FoamStart − FoamEnd)，区域门归一化。 // gate normalization. // 領域ゲート正規化。</summary>
        public float FoamRangeInv;
        /// <summary>速度门控下限（Mode==1/DensityWithSpeed）。 // speed-gate lower bound (Mode==1). // 速度ゲート下限。</summary>
        public float SpeedMin;
        /// <summary>= 1/(SpeedMax − SpeedMin)，速度门控归一化（Mode==1）。 // speed-gate normalization (Mode==1). // 速度ゲート正規化。</summary>
        public float SpeedRangeInv;
        /// <summary>模式：0=冲击+密度门(DensityWithImpact)，1=速度门控+密度门(DensityWithSpeed)，2=纯冲击无密度门(Impact，门恒=1)。 // 0=impact+gate, 1=speed+gate, 2=pure impact (no gate). // 0=衝撃+ゲート, 1=速度+ゲート, 2=純衝撃(門無し)。</summary>
        public int Mode;
        /// <summary>持久度曲线是否启用（1/0）。1 时按每粒子密度比从 <see cref="Liquid2DSolveContext.RenderPersistenceLut"/> 采样倍率重映射持久度：decay = pow(Decay, 1/mul)；0 时直接用 Decay（零开销）。 // persistence-curve enabled (1/0); when 1, remap decay per-particle via the LUT. // 持続度カーブ有効フラグ。</summary>
        public int PersistenceCurveActive;
    }

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

        /// <summary>动态碰撞体的本帧接触流体速度之和（双向耦合用，长度=动态体数）。平均流体速度 = 此值 / 接触数，用于相对速度阻力。 // Per-frame sum of contacting fluid velocities for dynamic colliders (two-way coupling). Average fluid velocity = this / contactCount, for relative-velocity drag. // 動的コライダーの接触流体速度の和。</summary>
        public NativeArray<float2> ColliderVelSum;

        /// <summary>
        /// 动态碰撞体的本帧接触累积（浮力用，长度=动态体数）：xy=接触粒子位置之和，z=接触粒子数，w=接触流体密度之和。
        /// 接收者按 z 估算浸没比例（缩放浮力/阻力），按 xy/z 求接触质心（力矩），按 w/z 求平均流体密度（阿基米德浮力）。
        /// Per-frame contact accumulation for dynamic colliders (buoyancy; length = dynamic body count): xy = sum of contact
        /// particle positions, z = contact count, w = sum of contact fluid density. Receivers use z for the submerged fraction
        /// (scales buoyancy/drag), xy/z as the contact centroid (torque), and w/z as the average fluid density (buoyancy).
        /// 動的コライダーのフレーム接触累積（浮力用）：xy=接触位置の和、z=接触数、w=流体密度の和。
        /// </summary>
        public NativeArray<float4> ColliderContact;

        /// <summary>
        /// 动态碰撞体的本帧「浮力 + 壳层覆盖」接触累积（长度=动态体数）：x=浮力接触粒子数，y=浮力接触流体密度之和，z=浮力接触排开体积之和（Σ4r²，内部覆盖），w=壳层覆盖体积之和（Σ4r²，表面外壳层带）。
        /// Push 模式浮力仅统计物体下方接触（接触法线 n.y&lt;0），使压顶粒子不产生虚假上浮，接收者按 x/fullSubmersionContacts 估算浸没比例（Push 不用 w）；
        /// Submerge 模式区分「内部覆盖」粒子（计入 z，按 z/物体体积估算浸没比例驱动浮力，真实排开体积避免浅浸饱和弹跳）与「表面外壳层」粒子（计入 w，接收者按 w/物体体积缩放 drag/阻尼，空中稀疏壳层→阻力≈0→自由下落）。两模式均按 y/x 求平均流体密度。
        /// Per-frame buoyancy + shell-coverage accumulation for dynamic colliders (length = dynamic body count): x = buoyancy contact count,
        /// y = sum of buoyancy-contact fluid density, z = Σ4r² of interior coverage (buoyancy), w = Σ4r² of the outer-shell band (drag/damping scale).
        /// Push buoyancy counts only contacts below the body (n.y&lt;0; fraction = x/fullSubmersionContacts; Push ignores w). Submerge separates interior particles
        /// (into z; fraction = z/bodyVolume drives buoyancy — true displaced volume avoids bang-bang) from outer-shell particles (into w; receiver scales drag/damping by w/bodyVolume,
        /// so sparse shell in the air → ~0 drag → free fall). Both use y/x for avg density.
        /// 動的コライダーの「浮力 + 殻層被覆」接触累積：x=浮力接触数、y=密度和、z=内部排除体積（Σ4r²、浮力）、w=外殻層被覆体積（Σ4r²、drag/減衰スケール）。Push は下方接触のみ（w 未使用）、Submerge は内部=z・殻層=w。
        /// </summary>
        public NativeArray<float4> ColliderBuoyancy;

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

        /// <summary>全局颜色混合模式（0=LinearRgb, 1=Oklab, 2=Ryb）。 // Global color-mix mode (0=LinearRgb, 1=Oklab, 2=Ryb). // グローバル色混合モード。</summary>
        public int MixMode;

        /// <summary>
        /// 按类型（typeId）的渲染平滑 EMA 混合系数 k（= 1 − 该类型 GradientSmoothing，范围 0..1）。每帧把持久化的
        /// 「渲染密度」「渲染速度」按 EMA 更新：renderX = lerp(renderX, 本帧原始值, k[typeId])。k=1 无平滑；越小越平滑。
        /// 用于消除渐变模式因 SPH 逐帧抖动导致的颜色闪烁；逐流体独立配置；不影响物理（物理仍用原始 Densities/Velocities）。
        /// Per-type (typeId) render-smoothing EMA factors k (= 1 − that type's GradientSmoothing, range 0..1). Each frame the
        /// persisted render density/speed are updated by EMA: renderX = lerp(renderX, this-frame raw, k[typeId]). k=1 = no
        /// smoothing; smaller = smoother. Per-fluid; removes gradient-mode flicker from per-frame SPH jitter; does not affect
        /// physics (which still uses raw Densities/Velocities).
        /// 型ごと（typeId）のレンダー平滑 EMA 係数 k（= 1 − その型の GradientSmoothing）。密度/速度を EMA 更新。物理には影響しません。
        /// </summary>
        [ReadOnly] public NativeArray<float> RenderGradientK;

        /// <summary>
        /// 按类型（typeId）的动态泡沫（DensityWithImpact / DensityWithSpeed）累加器参数（预算好的常量，供求解器每帧更新持久化泡沫值 F）。
        /// 生成量 = 密度区域门 × 动态因子（冲击或速度，见 <see cref="Liquid2DDynamicFoamParams"/>）；F = max(F·Decay, 生成量)。
        /// 仅动态泡沫类型读取 F；其余类型参数使动态因子为 0，F 恒为 0 且不被渲染读取。不影响物理。
        /// Per-type (typeId) dynamic-foam (DensityWithImpact / DensityWithSpeed) accumulator params. generation = densityGate ×
        /// dynamicFactor; F = max(F·Decay, generation). Only dynamic-foam types read F; others produce 0. Does not affect physics.
        /// 型ごと（typeId）の動的泡累加器パラメータ。物理には影響しません。
        /// </summary>
        [ReadOnly] public NativeArray<Liquid2DDynamicFoamParams> RenderDynamicFoamParams;

        /// <summary>
        /// 按类型展开的持久度曲线 LUT（长度 = numTypes × <see cref="Liquid2DParticleRenderSettings.PersistenceCurveLutSize"/>）。
        /// 按密度比[0,1] 采样得倍率，重映射泡沫持久度（最终持久度 = Foam Persistence × 倍率）。仅 <see cref="Liquid2DDynamicFoamParams.PersistenceCurveActive"/>=1 的类型被读取。
        /// Per-type persistence-curve LUT (length = numTypes × PersistenceCurveLutSize), indexed by density ratio[0,1] → multiplier;
        /// remaps foam persistence. Only types with PersistenceCurveActive=1 read it. // 型ごとに展開した持続度カーブ LUT。
        /// </summary>
        [ReadOnly] public NativeArray<float> RenderPersistenceLut;

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

        /// <summary>
        /// 把渲染层取数用的粒子计数归零。供 <see cref="Liquid2DSimulation"/> 在活动粒子数为 0 而提前返回（不调用 Step）时调用，
        /// 避免 GPU 求解器的 _lastCount 停留旧值导致排空后仍残影渲染上一批粒子。CPU 求解器为空操作（数据每帧从 store 取）。
        /// Reset the render-facing particle count to zero. Called by <see cref="Liquid2DSimulation"/> when active count is 0 and
        /// it early-returns without calling Step, so the GPU solver's _lastCount doesn't keep a stale value and ghost-render the
        /// last batch after the fluid drains. No-op on the CPU solver (it reads from the store each frame).
        /// 描画用の粒子数を 0 にリセット。活動数 0 で Step を呼ばず早期 return する際に呼び、排空後の残影描画を防ぐ。
        /// </summary>
        void ResetRenderCount();
    }
}

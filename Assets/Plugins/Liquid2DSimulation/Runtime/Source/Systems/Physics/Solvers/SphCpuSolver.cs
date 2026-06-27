using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// SPH 双密度流体求解器的 CPU 实现（Unity Job System + Burst + 空间哈希）。
    /// 每子步执行 外力+预测 → 建哈希 → 密度(双) → 压力力 → 粘性 → 积分+碰撞；帧末在最终位置网格上做一次邻居混色，
    /// 并把动态碰撞体的接触采样（入射流体速度之和+接触位置/数/密度）规约回 colliderVelSum/colliderContact（双向耦合 seam）。
    /// CPU implementation of the SPH dual-density fluid solver (Unity Job System + Burst + spatial hash).
    /// Per substep: external forces+predict → build hash → dual density → pressure force → viscosity →
    /// integrate+collide. Once per frame it mixes neighbor colors (on a grid rebuilt from final positions) and reduces
    /// dynamic-collider contact samples (sum of incoming fluid velocities + contact pos/count/density) into
    /// colliderVelSum/colliderContact (two-way coupling seam).
    /// SPH デュアル密度流体ソルバーの CPU 実装（Unity Job System + Burst + 空間ハッシュ）。
    /// </summary>
    public sealed class SphCpuSolver : ILiquid2DSolver
    {
        public Liquid2DSimulationMode Mode => Liquid2DSimulationMode.Cpu;

        // CPU 求解器无常驻渲染缓冲（渲染层每帧从 store 取数），故为空操作。 // No resident render buffer on CPU (render reads the store each frame); no-op. // CPU は常駐描画バッファ無し、空操作。
        public void ResetRenderCount() { }

        // 按 slot 索引的持久临时缓存（容量随 store 增长）。 // Persistent per-slot scratch (grows with store capacity). // slot 索引の永続スクラッチ。
        private NativeArray<float2> _densities;
        private NativeArray<float2> _velNext;
        private int _capacity;

        private void EnsureCapacity(int cap)
        {
            if (_capacity >= cap && _densities.IsCreated) return;
            Dispose();
            _capacity = math.max(16, cap);
            _densities = new NativeArray<float2>(_capacity, Allocator.Persistent);
            _velNext = new NativeArray<float2>(_capacity, Allocator.Persistent);
        }

        public void Step(in Liquid2DSolveContext ctx, in SolverParams p, float dt)
        {
            int count = ctx.ActiveCount;
            if (count <= 0 || dt <= 0f) return;

            var store = ctx.Store;
            EnsureCapacity(store.Capacity);

            int substeps = math.max(1, p.Substeps);
            float subDt = dt / substeps;
            float invCell = 1f / math.max(1e-4f, p.H);
            int tableSize = NextPrime(math.max(16, count * 2));

            bool hasColliders = ctx.Colliders.IsCreated && ctx.Colliders.Count > 0;

            // 哈希网格缓冲尺寸在整个 Step 内恒定（tableSize/count 不变），循环外分配一次复用——每子步由 BuildHashGridJob
            // 全量重写所有元素（计数排序从头构建），无陈旧数据泄漏，并省去每子步的分配/释放。
            // Hash-grid buffer sizes are constant across the Step; allocate once and reuse — BuildHashGridJob fully overwrites
            // every element each substep (counting sort from scratch), so no stale data leaks, avoiding per-substep alloc/free.
            // ハッシュグリッドバッファは Step 全体でサイズ一定。ループ外で一度確保し再利用（毎子步 BuildHashGridJob が全量上書き）。
            var cellStart = new NativeArray<int>(tableSize + 1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
            var sortedSlots = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

            // try/finally 确保即使 Job 调度或 h.Complete() 抛异常（编辑器安全系统/NaN）也释放 TempJob 缓冲，避免泄漏。
            // try/finally so all TempJob buffers are freed even if a job schedule or Complete throws (editor safety system / NaN).
            // try/finally で Job 例外時も TempJob バッファを解放。
            try
            {
            for (int step = 0; step < substeps; step++)
            {
                bool lastStep = step == substeps - 1;

                JobHandle h = default;

                h = new ExternalForcesJob
                {
                    ActiveIndices = ctx.ActiveIndices, TypeId = store.typeId, GroupId = store.groupId, Materials = ctx.Materials,
                    Velocities = store.velocities, Positions = store.positions, Predicted = store.predicted,
                    ForceFields = ctx.ForceFields.Fields, ForceFieldCount = ctx.ForceFields.Count,
                    Gravity = p.Gravity, DT = subDt, PredictionFactor = p.PredictionFactor,
                }.Schedule(count, 64, h);

                h = new BuildHashGridJob
                {
                    Predicted = store.predicted, ActiveIndices = ctx.ActiveIndices, ActiveCount = count,
                    TableSize = tableSize, InvCellSize = invCell, CellStart = cellStart, SortedSlots = sortedSlots,
                }.Schedule(h);

                h = new DensityJob
                {
                    ActiveIndices = ctx.ActiveIndices, Predicted = store.predicted, CellStart = cellStart,
                    SortedSlots = sortedSlots, TableSize = tableSize, InvCellSize = invCell, H = p.H, Densities = _densities,
                }.Schedule(count, 64, h);

                h = new PressureForceJob
                {
                    ActiveIndices = ctx.ActiveIndices, Predicted = store.predicted, Densities = _densities,
                    TypeId = store.typeId, Materials = ctx.Materials, CellStart = cellStart, SortedSlots = sortedSlots,
                    TableSize = tableSize, InvCellSize = invCell, H = p.H, TargetDensity = p.TargetDensity,
                    PressureMultiplier = p.PressureMultiplier, NearPressureMultiplier = p.NearPressureMultiplier,
                    Velocities = store.velocities, DT = subDt,
                }.Schedule(count, 64, h);

                h = new SphViscosityJob
                {
                    ActiveIndices = ctx.ActiveIndices, Predicted = store.predicted, Velocities = store.velocities,
                    TypeId = store.typeId, Materials = ctx.Materials, CellStart = cellStart, SortedSlots = sortedSlots,
                    TableSize = tableSize, InvCellSize = invCell, H = p.H, ViscosityStrength = p.ViscosityStrength,
                    DT = subDt, VelNext = _velNext,
                }.Schedule(count, 64, h);

                h = new CopyVelocityJob
                {
                    ActiveIndices = ctx.ActiveIndices, VelNext = _velNext, Velocities = store.velocities,
                }.Schedule(count, 64, h);

                // 积分 + 碰撞（仅末子步、且存在动态体时才累积接触采样）。无动态体时碰撞照常发生，只是不分配 count 大小的
                // 接触缓冲、不跑串行规约（静态碰撞器无需双向耦合）。 // Accumulate contact samples only on the last substep AND when a dynamic body exists; collision still happens, we just skip the count-sized contact arrays and serial reduce. // 動的体がある時のみ接触採取。
                bool accumulate = lastStep && hasColliders && ctx.DynamicBodyCount > 0;
                int outLen = accumulate ? count : 1;
                NativeArray<float2> outVelSum = new NativeArray<float2>(outLen, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                NativeArray<int> outBody = new NativeArray<int>(outLen, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                NativeArray<float2> outContact = new NativeArray<float2>(outLen, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                NativeArray<float> outNormalY = new NativeArray<float>(outLen, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                NativeArray<float> outDensityFactor = new NativeArray<float>(outLen, Allocator.TempJob, NativeArrayOptions.ClearMemory);

                // try/finally 释放本子步接触采样缓冲（同样防 h.Complete() 抛异常泄漏）。 // Free this substep's contact arrays even on a Complete throw. // 接触バッファを解放。
                try
                {
                h = new SphIntegrateCollideJob
                {
                    ActiveIndices = ctx.ActiveIndices, Positions = store.positions, Velocities = store.velocities,
                    Radii = store.radii, TypeId = store.typeId, GroupId = store.groupId,
                    Materials = ctx.Materials,
                    Colliders = ctx.Colliders.Colliders, Points = ctx.Colliders.Points, DT = subDt,
                    Densities = _densities, TargetDensity = p.TargetDensity,
                    CollisionDamping = p.CollisionDamping, MaxSpeed = p.MaxSpeed,
                    HasColliders = (byte)(hasColliders ? 1 : 0),
                    Accumulate = (byte)(accumulate ? 1 : 0), OutVelSum = outVelSum, OutBody = outBody, OutContact = outContact,
                    OutNormalY = outNormalY, OutDensityFactor = outDensityFactor,
                }.Schedule(count, 64, h);

                // 末子步：在最终位置上重建网格并做一次邻居混色。 // Last substep: rebuild grid on final positions and mix colors. // 末サブステップで混色。
                if (lastStep)
                {
                    h = new BuildHashGridJob
                    {
                        Predicted = store.positions, ActiveIndices = ctx.ActiveIndices, ActiveCount = count,
                        TableSize = tableSize, InvCellSize = invCell, CellStart = cellStart, SortedSlots = sortedSlots,
                    }.Schedule(h);

                    h = new MixColorJob
                    {
                        ActiveIndices = ctx.ActiveIndices, Positions = store.positions, Velocities = store.velocities,
                        Colors = store.colors, TypeId = store.typeId, GroupId = store.groupId, Radii = store.radii,
                        MixData = ctx.MixData, CellStart = cellStart, SortedSlots = sortedSlots, TableSize = tableSize,
                        InvCellSize = invCell, Time = ctx.Time, MixMode = ctx.MixMode,
                        LastMixTime = store.lastMixTime, ColorsNext = store.colorsNext,
                    }.Schedule(count, 64, h);

                    // 销毁区域标记（在最终位置上）：求解后由 Liquid2DSimulation 回收命中粒子。
                    // Dead-zone marking (on final positions); Liquid2DSimulation recycles hit particles after solving.
                    // 破棄領域マーキング（最終位置）。求解後に Liquid2DSimulation が命中粒子を回収。
                    if (ctx.DeadZoneCount > 0 && ctx.KillFlags.IsCreated)
                    {
                        h = new DeadZoneKillJob
                        {
                            ActiveIndices = ctx.ActiveIndices, Positions = store.positions, GroupId = store.groupId,
                            DeadZones = ctx.DeadZones.Zones, Points = ctx.DeadZones.Points,
                            DeadZoneCount = ctx.DeadZoneCount, KillFlags = ctx.KillFlags,
                        }.Schedule(count, 64, h);
                    }
                }

                h.Complete();

                if (lastStep)
                {
                    store.SwapColorBuffers();

                    // 把每粒子接触采样规约回动态碰撞体（双向耦合 seam）。 // Reduce per-particle contact samples into dynamic colliders. // 接触サンプルを動的体に規約。
                    // NativeArray 是包裹指针的结构；因 ctx 为 in（只读）需先拷到本地变量才能调用其索引器 setter（共享同一缓冲）。
                    // NativeArray wraps a pointer; since ctx is `in` (readonly), copy to a local to call its indexer setter (same underlying buffer).
                    var velSumOut = ctx.ColliderVelSum;
                    var contactOut = ctx.ColliderContact;
                    var buoyOut = ctx.ColliderBuoyancy;
                    if (accumulate && velSumOut.IsCreated)
                    {
                        bool hasContact = contactOut.IsCreated;
                        bool hasBuoy = buoyOut.IsCreated;
                        var materials = ctx.Materials;
                        var typeIds = store.typeId;
                        var radii = store.radii;
                        for (int kk = 0; kk < count; kk++)
                        {
                            int body = outBody[kk];
                            if (body < 0 || body >= velSumOut.Length) continue;
                            int slot = ctx.ActiveIndices[kk];
                            float density = materials[typeIds[slot]].Density;
                            float r = radii[slot];
                            // 归类哨兵（见 collide job）：Submerge 内部覆盖=-2（全方向计浮力，不计 drag）、壳层=+2（仅 drag+壳层覆盖）；Push 为真实法线 y。
                            // Role sentinel (see collide job): Submerge interior=-2 (all-direction buoyancy, no drag), shell=+2 (drag + shell coverage only); Push = real normal y.
                            // 役割哨兵：Submerge 内部=-2（全方向浮力）、殻層=+2、Push=実法線 y。
                            float ny = outNormalY[kk];
                            bool interiorSub = ny == -2f; // Submerge 内部覆盖：全方向计浮力、不计 drag（避免空中重叠粒子拖住物体）。 // interior: buoyancy only. // 内部：浮力のみ、drag 除外。
                            bool shellSub = ny == 2f;     // Submerge 表面外壳层：计 drag/质心 + 壳层覆盖。 // shell: drag + shell coverage. // 殻層。
                            // 密度门控因子：缩放浮力排开体积/壳层覆盖（低密度粘着水滴→≈0→不计入浮力/阻尼/drag，物体照常自由下落）。 // density-gate factor scaling buoyancy displaced/shell volume (low-density droplet → ≈0). // 密度ゲート。
                            float df = outDensityFactor[kk];

                            // 入射流体速度 + 接触累积（drag 冲走 + 力矩质心）：Push 全部接触、Submerge 仅壳层；内部覆盖不计入 drag。
                            // Incoming fluid velocity + contact accumulation (drag wash + torque centroid): Push all contacts, Submerge shell only; interior excluded from drag.
                            // 入射流速 + 接触累積（drag）：Push は全接触、Submerge は殻層のみ。
                            if (!interiorSub)
                            {
                                velSumOut[body] += outVelSum[kk];
                                if (hasContact && body < contactOut.Length)
                                    contactOut[body] += new float4(outContact[kk].x, outContact[kk].y, 1f, density);
                            }
                            // 浮力累积（排开体积）：Submerge 内部覆盖（哨兵 -2）全方向计入=被流体包裹的整体浮力（z 同时作覆盖率信号，桥接器据 z/体积 门控空中误浮）；Push 仍仅下方接触（真实 n.y<0）。
                            // 哨兵 -2 与 Push 真实 n.y<0 均通过 `ny<0`；壳层 +2 排除。x=接触数、y=密度和、z=排开面积和(Σ4r²)。
                            // Buoyancy accum (displaced volume): Submerge interior (sentinel -2) all directions = whole-body buoyancy of an enveloped body (z doubles as the coverage signal; the bridge gates the airborne case by z/volume); Push still below-contacts only (real n.y<0).
                            // Sentinel -2 and Push real n.y<0 pass `ny<0`; shell +2 excluded. x=count, y=density sum, z=Σ4r².
                            // 浮力累積：Submerge 内部は全方向（整体浮力、z は被覆率信号兼用）、Push は下方のみ。
                            if (hasBuoy && body < buoyOut.Length && ny < 0f)
                                buoyOut[body] += new float4(1f, density, 4f * r * r * df, 0f);
                            // 壳层覆盖累积（w 通道，Σ4r²）：Submerge 壳层。桥接器用「壳层覆盖 / 物体体积」缩放 drag/阻尼——
                            // 空中零散粒子壳层稀疏→drag≈0→直接坠落；真正被流体包住→壳层饱满→正常受阻。
                            // Shell-coverage accum (w channel, Σ4r²): Submerge shell. The bridge scales drag/damping by (shell coverage / body volume) —
                            // sparse shell in the air → ~0 drag → free fall; truly enveloped → full shell → normal resistance.
                            // 殻層被覆累積（w、Σ4r²）：Submerge 殻層。橋は drag/減衰を殻層被覆でスケール。
                            if (hasBuoy && body < buoyOut.Length && shellSub)
                                buoyOut[body] += new float4(0f, 0f, 0f, 4f * r * r * df);
                        }
                    }
                }

                }
                finally
                {
                    if (outVelSum.IsCreated) outVelSum.Dispose();
                    if (outBody.IsCreated) outBody.Dispose();
                    if (outContact.IsCreated) outContact.Dispose();
                    if (outNormalY.IsCreated) outNormalY.Dispose();
                    if (outDensityFactor.IsCreated) outDensityFactor.Dispose();
                }
            } // for
            } // try
            finally
            {
                // 循环外一次性释放复用的哈希网格缓冲。 // Free the reused hash-grid buffers once after the loop. // 再利用バッファをループ後に解放。
                if (cellStart.IsCreated) cellStart.Dispose();
                if (sortedSlots.IsCreated) sortedSlots.Dispose();
            }
        }

        private static int NextPrime(int n)
        {
            if (n <= 2) return 2;
            if ((n & 1) == 0) n++;
            while (!IsPrime(n)) n += 2;
            return n;
        }

        private static bool IsPrime(int n)
        {
            if (n < 2) return false;
            if (n % 2 == 0) return n == 2;
            for (int i = 3; (long)i * i <= n; i += 2)
                if (n % i == 0) return false;
            return true;
        }

        public void Dispose()
        {
            if (_densities.IsCreated) _densities.Dispose();
            if (_velNext.IsCreated) _velNext.Dispose();
            _capacity = 0;
        }
    }
}

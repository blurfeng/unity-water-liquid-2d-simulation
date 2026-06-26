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

        // 按 slot 索引的持久临时缓存（容量随 store 增长）。 // Persistent per-slot scratch (grows with store capacity). // slot 索引の永続スクラッチ。
        private NativeArray<float2> _densities;
        private NativeArray<float2> _velNext;
        private int _capacity;

        // 每动态体「上一帧壳层接触数」（规约后更新、collide job 读取做物体级在水门控）。常驻、纯 CPU、无跨域通讯。 // Per-body last-frame shell contact count for the in-water gate (updated after reduce, read by the collide job). Persistent, CPU-only. // 前フレーム殻層接触数（in-water ゲート）。
        private NativeArray<int> _prevInWater;

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

            // 物体级在水门控用的持久数组（每动态体一个 int），按动态体数确保大小。 // Persistent array for the in-water gate (one int per dynamic body). // in-water ゲート用永続配列。
            int numBodies = math.max(1, ctx.ColliderVelSum.IsCreated ? ctx.ColliderVelSum.Length : 1);
            if (!_prevInWater.IsCreated || _prevInWater.Length < numBodies)
            {
                if (_prevInWater.IsCreated) _prevInWater.Dispose();
                _prevInWater = new NativeArray<int>(numBodies, Allocator.Persistent); // 零初始化：起始视为不在水里。 // zero-init = starts "not in water". // ゼロ初期化。
            }

            int substeps = math.max(1, p.Substeps);
            float subDt = dt / substeps;
            float invCell = 1f / math.max(1e-4f, p.H);
            int tableSize = NextPrime(math.max(16, count * 2));

            bool hasColliders = ctx.Colliders.IsCreated && ctx.Colliders.Count > 0;

            for (int step = 0; step < substeps; step++)
            {
                bool lastStep = step == substeps - 1;

                // 每子步重建哈希网格（TempJob，子步末 Complete 后释放）。 // Rebuild hash grid each substep. // サブステップごとに再構築。
                var cellStart = new NativeArray<int>(tableSize + 1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var sortedSlots = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

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

                // 积分 + 碰撞（仅末子步累积接触采样）。 // Integrate + collide (accumulate contact samples only on last substep). // 積分 + 衝突。
                bool accumulate = lastStep && hasColliders;
                int outLen = accumulate ? count : 1;
                NativeArray<float2> outVelSum = new NativeArray<float2>(outLen, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                NativeArray<int> outBody = new NativeArray<int>(outLen, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                NativeArray<float2> outContact = new NativeArray<float2>(outLen, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                NativeArray<float> outNormalY = new NativeArray<float>(outLen, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                NativeArray<float> outDensityFactor = new NativeArray<float>(outLen, Allocator.TempJob, NativeArrayOptions.ClearMemory);

                h = new SphIntegrateCollideJob
                {
                    ActiveIndices = ctx.ActiveIndices, Positions = store.positions, Velocities = store.velocities,
                    Radii = store.radii, TypeId = store.typeId, GroupId = store.groupId,
                    Materials = ctx.Materials,
                    Colliders = ctx.Colliders.Colliders, Points = ctx.Colliders.Points, DT = subDt,
                    Densities = _densities, TargetDensity = p.TargetDensity,
                    BodyPrevInWater = _prevInWater,
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
                            // 归类哨兵（见 collide job）：Submerge 内部=-2（仅浮力）、壳层=+2（仅 drag+壳层覆盖）；Push 为真实法线 y。
                            // Role sentinel (see collide job): Submerge interior=-2 (buoyancy only), shell=+2 (drag + shell coverage only); Push = real normal y.
                            // 役割哨兵：Submerge 内部=-2、殻層=+2、Push=実法線 y。
                            float ny = outNormalY[kk];
                            bool interiorSub = ny == -2f; // Submerge 内部覆盖：只计浮力，不计 drag（避免空中重叠粒子拖住物体）。 // interior: buoyancy only. // 内部：浮力のみ。
                            bool shellSub = ny == 2f;     // Submerge 表面外壳层：计 drag/质心 + 壳层覆盖。 // shell: drag + shell coverage. // 殻層。
                            // 密度门控因子：缩放排开体积/壳层覆盖（低密度粘着水滴→≈0→不计入浮力/阻尼/drag，物体照常自由下落）。 // density-gate factor scaling displaced/shell volume (low-density stuck droplets → ≈0 → no buoyancy/damping/drag, body free-falls). // 密度ゲート。
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
                            // 浮力累积（排开体积）：Push 仅下方接触（n.y<0）；Submerge 内部覆盖（哨兵 -2，<0 通过门控）。
                            // x=接触数、y=密度和、z=排开面积和（每粒子格子面积 (2r)²=4r²，满浸没时≈物体面积）。
                            // Buoyancy accum (displaced volume): Push below-contacts (n.y<0); Submerge interior (sentinel -2, passes <0). x=count, y=density sum, z=Σ4r².
                            // 浮力累積：Push 下方接触、Submerge 内部。x=接触数、y=密度和、z=Σ4r²。
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

                        // 更新「上一帧壳层接触数」供下一帧 collide job 做物体级在水门控（contactOut.z = 本帧壳层接触数）。
                        // Update per-body shell contact count for next frame's in-water gate (contactOut.z = this frame's shell contact count).
                        // 物体級在水ゲート用の前フレーム殻層接触数を更新。
                        if (hasContact && _prevInWater.IsCreated)
                            for (int b = 0; b < velSumOut.Length && b < _prevInWater.Length; b++)
                                _prevInWater[b] = b < contactOut.Length ? (int)contactOut[b].z : 0;
                    }
                }

                cellStart.Dispose();
                sortedSlots.Dispose();
                outDensityFactor.Dispose();
                outVelSum.Dispose();
                outBody.Dispose();
                outContact.Dispose();
                outNormalY.Dispose();
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
            if (_prevInWater.IsCreated) _prevInWater.Dispose();
            _capacity = 0;
        }
    }
}

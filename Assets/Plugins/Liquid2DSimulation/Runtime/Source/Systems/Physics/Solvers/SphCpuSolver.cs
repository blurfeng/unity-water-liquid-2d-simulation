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

                h = new SphIntegrateCollideJob
                {
                    ActiveIndices = ctx.ActiveIndices, Positions = store.positions, Velocities = store.velocities,
                    Radii = store.radii, TypeId = store.typeId, GroupId = store.groupId,
                    Materials = ctx.Materials,
                    Colliders = ctx.Colliders.Colliders, Points = ctx.Colliders.Points, DT = subDt,
                    CollisionDamping = p.CollisionDamping, MaxSpeed = p.MaxSpeed,
                    HasColliders = (byte)(hasColliders ? 1 : 0),
                    Accumulate = (byte)(accumulate ? 1 : 0), OutVelSum = outVelSum, OutBody = outBody, OutContact = outContact,
                    OutNormalY = outNormalY,
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
                        for (int kk = 0; kk < count; kk++)
                        {
                            int body = outBody[kk];
                            if (body < 0 || body >= velSumOut.Length) continue;
                            // 入射流体速度之和（平均流速=此值/接触数，相对速度阻力用）。 // Sum of incoming fluid velocities (avg = this / contactCount, for drag). // 入射流速の和。
                            velSumOut[body] += outVelSum[kk];
                            float density = materials[typeIds[ctx.ActiveIndices[kk]]].Density;
                            // 接触累积（全向，drag 浸没比例 + 力矩质心用）：xy=接触位置之和，z=接触计数，w=接触粒子流体密度之和（平均密度=w/z）。
                            // Contact accumulation (all directions; drag submersion + torque centroid): xy=sum pos, z=count, w=sum of fluid density (avg = w/z).
                            // 接触累積（全方向、drag 浸水率 + 力矩質心）：xy=位置和、z=接触数、w=流体密度和。
                            if (hasContact && body < contactOut.Length)
                                contactOut[body] += new float4(outContact[kk].x, outContact[kk].y, 1f, density);
                            // 浮力专用累积：仅当接触法线朝下（n.y<0，粒子在物体下方、向上托起物体）才计入，x=下方接触数、y=下方密度和。
                            // Buoyancy-only accumulation: counted only when the contact normal points down (n.y<0, particle below the body). x=count, y=density sum.
                            // 浮力専用累積：法線が下向き（n.y<0、粒子が物体の下）のみ計上。x=下方接触数、y=密度和。
                            if (hasBuoy && body < buoyOut.Length && outNormalY[kk] < 0f)
                                buoyOut[body] += new float2(1f, density);
                        }
                    }
                }

                cellStart.Dispose();
                sortedSlots.Dispose();
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
            _capacity = 0;
        }
    }
}

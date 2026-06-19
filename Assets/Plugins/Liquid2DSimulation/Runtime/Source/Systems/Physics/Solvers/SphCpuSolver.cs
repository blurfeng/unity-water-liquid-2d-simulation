using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// SPH 双密度流体求解器的 CPU 实现（Unity Job System + Burst + 空间哈希）。算法对齐参考 SebLague Fluid-Sim：
    /// 每子步执行 外力+预测 → 建哈希 → 密度(双) → 压力力 → 粘性 → 积分+碰撞；帧末在最终位置网格上做一次邻居混色，
    /// 并把动态碰撞体的反作用冲量规约回 colliderImpulse（双向耦合 seam）。
    /// CPU implementation of the SPH dual-density fluid solver (Unity Job System + Burst + spatial hash), aligned with
    /// SebLague's Fluid-Sim. Per substep: external forces+predict → build hash → dual density → pressure force → viscosity →
    /// integrate+collide. Once per frame it mixes neighbor colors (on a grid rebuilt from final positions) and reduces
    /// dynamic-collider reaction impulses into colliderImpulse (two-way coupling seam).
    /// SPH デュアル密度流体ソルバーの CPU 実装（Unity Job System + Burst + 空間ハッシュ）。SebLague Fluid-Sim と整合。
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
            int count = ctx.activeCount;
            if (count <= 0 || dt <= 0f) return;

            var store = ctx.store;
            EnsureCapacity(store.Capacity);

            int substeps = math.max(1, p.substeps);
            float subDt = dt / substeps;
            float invCell = 1f / math.max(1e-4f, p.h);
            int tableSize = NextPrime(math.max(16, count * 2));

            bool hasColliders = ctx.colliders.IsCreated && ctx.colliders.Count > 0;

            for (int step = 0; step < substeps; step++)
            {
                bool lastStep = step == substeps - 1;

                // 每子步重建哈希网格（TempJob，子步末 Complete 后释放）。 // Rebuild hash grid each substep. // サブステップごとに再構築。
                var cellStart = new NativeArray<int>(tableSize + 1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var sortedSlots = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                JobHandle h = default;

                h = new ExternalForcesJob
                {
                    activeIndices = ctx.activeIndices, typeId = store.typeId, materials = ctx.materials,
                    velocities = store.velocities, positions = store.positions, predicted = store.predicted,
                    gravity = p.gravity, dt = subDt, predictionFactor = p.predictionFactor,
                }.Schedule(count, 64, h);

                h = new BuildHashGridJob
                {
                    predicted = store.predicted, activeIndices = ctx.activeIndices, activeCount = count,
                    tableSize = tableSize, invCellSize = invCell, cellStart = cellStart, sortedSlots = sortedSlots,
                }.Schedule(h);

                h = new DensityJob
                {
                    activeIndices = ctx.activeIndices, predicted = store.predicted, cellStart = cellStart,
                    sortedSlots = sortedSlots, tableSize = tableSize, invCellSize = invCell, h = p.h, densities = _densities,
                }.Schedule(count, 64, h);

                h = new PressureForceJob
                {
                    activeIndices = ctx.activeIndices, predicted = store.predicted, densities = _densities,
                    typeId = store.typeId, materials = ctx.materials, cellStart = cellStart, sortedSlots = sortedSlots,
                    tableSize = tableSize, invCellSize = invCell, h = p.h, targetDensity = p.targetDensity,
                    pressureMultiplier = p.pressureMultiplier, nearPressureMultiplier = p.nearPressureMultiplier,
                    velocities = store.velocities, dt = subDt,
                }.Schedule(count, 64, h);

                h = new SphViscosityJob
                {
                    activeIndices = ctx.activeIndices, predicted = store.predicted, velocities = store.velocities,
                    typeId = store.typeId, materials = ctx.materials, cellStart = cellStart, sortedSlots = sortedSlots,
                    tableSize = tableSize, invCellSize = invCell, h = p.h, viscosityStrength = p.viscosityStrength,
                    dt = subDt, velNext = _velNext,
                }.Schedule(count, 64, h);

                h = new CopyVelocityJob
                {
                    activeIndices = ctx.activeIndices, velNext = _velNext, velocities = store.velocities,
                }.Schedule(count, 64, h);

                // 积分 + 碰撞（仅末子步累积耦合冲量）。 // Integrate + collide (accumulate coupling impulse only on last substep). // 積分 + 衝突。
                bool accumulate = lastStep && hasColliders;
                NativeArray<float2> outImpulse = accumulate
                    ? new NativeArray<float2>(count, Allocator.TempJob, NativeArrayOptions.ClearMemory)
                    : new NativeArray<float2>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                NativeArray<int> outBody = accumulate
                    ? new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory)
                    : new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);

                h = new SphIntegrateCollideJob
                {
                    activeIndices = ctx.activeIndices, positions = store.positions, velocities = store.velocities,
                    radii = store.radii, invMass = store.invMass, typeId = store.typeId, materials = ctx.materials,
                    colliders = ctx.colliders.colliders, points = ctx.colliders.points, dt = subDt,
                    collisionDamping = p.collisionDamping, maxSpeed = p.maxSpeed,
                    hasColliders = (byte)(hasColliders ? 1 : 0),
                    accumulate = (byte)(accumulate ? 1 : 0), outImpulse = outImpulse, outBody = outBody,
                }.Schedule(count, 64, h);

                // 末子步：在最终位置上重建网格并做一次邻居混色。 // Last substep: rebuild grid on final positions and mix colors. // 末サブステップで混色。
                if (lastStep)
                {
                    h = new BuildHashGridJob
                    {
                        predicted = store.positions, activeIndices = ctx.activeIndices, activeCount = count,
                        tableSize = tableSize, invCellSize = invCell, cellStart = cellStart, sortedSlots = sortedSlots,
                    }.Schedule(h);

                    h = new MixColorJob
                    {
                        activeIndices = ctx.activeIndices, positions = store.positions, velocities = store.velocities,
                        colors = store.colors, typeId = store.typeId, groupId = store.groupId, radii = store.radii,
                        mixData = ctx.mixData, cellStart = cellStart, sortedSlots = sortedSlots, tableSize = tableSize,
                        invCellSize = invCell, time = ctx.time, lastMixTime = store.lastMixTime, colorsNext = store.colorsNext,
                    }.Schedule(count, 64, h);
                }

                h.Complete();

                if (lastStep)
                {
                    store.SwapColorBuffers();

                    // 把每粒子冲量规约回动态碰撞体（双向耦合 seam）。 // Reduce per-particle impulses into dynamic colliders. // 力積を動的体に規約。
                    // NativeArray 是包裹指针的结构；因 ctx 为 in（只读）需先拷到本地变量才能调用其索引器 setter（共享同一缓冲）。
                    // NativeArray wraps a pointer; since ctx is `in` (readonly), copy to a local to call its indexer setter (same underlying buffer).
                    var impulseOut = ctx.colliderImpulse;
                    if (accumulate && impulseOut.IsCreated)
                    {
                        for (int kk = 0; kk < count; kk++)
                        {
                            int body = outBody[kk];
                            if (body >= 0 && body < impulseOut.Length)
                                impulseOut[body] += outImpulse[kk];
                        }
                    }
                }

                cellStart.Dispose();
                sortedSlots.Dispose();
                outImpulse.Dispose();
                outBody.Dispose();
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

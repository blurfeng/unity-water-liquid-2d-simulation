using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// PBF 流体求解器的 CPU 实现（Unity Job System + Burst + 空间哈希）。
    /// 每帧按 substeps 子步进，每子步执行：预测 → 建哈希 → 约束迭代(密度/λ/Δp) → 碰撞投影 → 定速 → 粘性；
    /// 帧末做一次邻居混色，并把动态碰撞体的反作用冲量规约回 colliderImpulse（双向耦合 seam）。
    /// CPU implementation of the PBF fluid solver (Unity Job System + Burst + spatial hash). Each frame substeps by
    /// substeps; per substep: predict → build hash → constraint iterations → collider projection → finalize velocity →
    /// viscosity. Once per frame it mixes neighbor colors and reduces dynamic-collider reaction impulses into
    /// colliderImpulse (two-way coupling seam).
    /// PBF 流体ソルバーの CPU 実装（Unity Job System + Burst + 空間ハッシュ）。
    /// </summary>
    public sealed class PbfCpuSolver : ILiquid2DSolver
    {
        public Liquid2DSimulationMode Mode => Liquid2DSimulationMode.Cpu;

        // 按 slot 索引的持久临时缓存（容量随 store 增长）。 // Persistent per-slot scratch (grows with store capacity). // slot 索引の永続スクラッチ。
        private NativeArray<float> _lambda;
        private NativeArray<float2> _deltaP;
        private NativeArray<float2> _velNext;
        private int _capacity;

        private void EnsureCapacity(int cap)
        {
            if (_capacity >= cap && _lambda.IsCreated) return;
            Dispose();
            _capacity = math.max(16, cap);
            _lambda = new NativeArray<float>(_capacity, Allocator.Persistent);
            _deltaP = new NativeArray<float2>(_capacity, Allocator.Persistent);
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
            float invSubDt = 1f / subDt;
            float invCell = 1f / math.max(1e-4f, p.h);
            int tableSize = NextPrime(math.max(16, count * 2));

            bool hasColliders = ctx.colliders.IsCreated && ctx.colliders.Count > 0;

            for (int step = 0; step < substeps; step++)
            {
                bool lastStep = step == substeps - 1;

                // 每子步重建哈希网格（按 TempJob 分配，子步末 Complete 后释放）。 // Rebuild hash grid each substep. // サブステップごとにハッシュ再構築。
                var cellStart = new NativeArray<int>(tableSize + 1, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                var sortedSlots = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);

                JobHandle h = default;

                h = new PredictJob
                {
                    activeIndices = ctx.activeIndices, typeId = store.typeId, materials = ctx.materials,
                    velocities = store.velocities, positions = store.positions, predicted = store.predicted,
                    gravity = p.gravity, dt = subDt,
                }.Schedule(count, 64, h);

                h = new BuildHashGridJob
                {
                    predicted = store.predicted, activeIndices = ctx.activeIndices, activeCount = count,
                    tableSize = tableSize, invCellSize = invCell, cellStart = cellStart, sortedSlots = sortedSlots,
                }.Schedule(h);

                for (int iter = 0; iter < math.max(1, p.constraintIterations); iter++)
                {
                    h = new DensityLambdaJob
                    {
                        activeIndices = ctx.activeIndices, predicted = store.predicted, typeId = store.typeId,
                        materials = ctx.materials, cellStart = cellStart, sortedSlots = sortedSlots,
                        tableSize = tableSize, invCellSize = invCell, h = p.h, restDensity = p.restDensity,
                        relaxEps = p.relaxEps, lambda = _lambda,
                    }.Schedule(count, 64, h);

                    h = new DeltaPositionJob
                    {
                        activeIndices = ctx.activeIndices, predicted = store.predicted, lambda = _lambda,
                        typeId = store.typeId, materials = ctx.materials, cellStart = cellStart, sortedSlots = sortedSlots,
                        tableSize = tableSize, invCellSize = invCell, h = p.h, restDensity = p.restDensity,
                        sCorrK = p.sCorrK, sCorrN = p.sCorrN, sCorrDqRatio = p.sCorrDqRatio, deltaP = _deltaP,
                    }.Schedule(count, 64, h);

                    h = new ApplyDeltaJob
                    {
                        activeIndices = ctx.activeIndices, deltaP = _deltaP, predicted = store.predicted,
                    }.Schedule(count, 64, h);
                }

                // 碰撞投影（仅末子步累积耦合冲量）。 // Collider projection (accumulate coupling impulse only on last substep). // コライダー投影。
                NativeArray<float2> outImpulse;
                NativeArray<int> outBody;
                bool accumulate = lastStep && hasColliders;
                if (accumulate)
                {
                    outImpulse = new NativeArray<float2>(count, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    outBody = new NativeArray<int>(count, Allocator.TempJob, NativeArrayOptions.UninitializedMemory);
                }
                else
                {
                    outImpulse = new NativeArray<float2>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                    outBody = new NativeArray<int>(1, Allocator.TempJob, NativeArrayOptions.ClearMemory);
                }

                if (hasColliders)
                {
                    h = new CollisionJob
                    {
                        activeIndices = ctx.activeIndices, positions = store.positions, typeId = store.typeId,
                        materials = ctx.materials, radii = store.radii, invMass = store.invMass,
                        colliders = ctx.colliders.colliders, points = ctx.colliders.points, predicted = store.predicted,
                        dt = subDt, accumulate = (byte)(accumulate ? 1 : 0), outImpulse = outImpulse, outBody = outBody,
                    }.Schedule(count, 64, h);
                }

                h = new FinalizeVelocityJob
                {
                    activeIndices = ctx.activeIndices, predicted = store.predicted, positions = store.positions,
                    velocities = store.velocities, invDt = invSubDt,
                }.Schedule(count, 64, h);

                h = new ViscosityJob
                {
                    activeIndices = ctx.activeIndices, positions = store.positions, velocities = store.velocities,
                    typeId = store.typeId, materials = ctx.materials, cellStart = cellStart, sortedSlots = sortedSlots,
                    tableSize = tableSize, invCellSize = invCell, h = p.h, globalViscosity = p.viscosity, velNext = _velNext,
                }.Schedule(count, 64, h);

                h = new CopyVelocityJob
                {
                    activeIndices = ctx.activeIndices, velNext = _velNext, velocities = store.velocities,
                }.Schedule(count, 64, h);

                // 末子步：在同一哈希网格上做一次邻居混色。 // Last substep: mix neighbor colors on the same grid. // 末サブステップで混色。
                if (lastStep)
                {
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
                    // NativeArray はポインタを包む構造体。ctx が in（読み取り専用）のため、ローカルにコピーしてからインデクサ setter を呼ぶ（同一バッファ）。
                    var impulseOut = ctx.colliderImpulse;
                    if (accumulate && impulseOut.IsCreated)
                    {
                        for (int k = 0; k < count; k++)
                        {
                            int body = outBody[k];
                            if (body >= 0 && body < impulseOut.Length)
                                impulseOut[body] += outImpulse[k];
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
            if (_lambda.IsCreated) _lambda.Dispose();
            if (_deltaP.IsCreated) _deltaP.Dispose();
            if (_velNext.IsCreated) _velNext.Dispose();
            _capacity = 0;
        }
    }
}

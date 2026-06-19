using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// SPH 双密度流体求解器的 GPU 实现（Compute Shader）。算法与 <see cref="SphCpuSolver"/> 完全一致（同一核函数、
    /// 同一计数排序空间哈希、同一碰撞/混色规则），计算在 GPU 上完成。
    ///
    /// 本期（Phase 2a）为“桥接”实现：每帧把 active 粒子集合从 SoA store gather 成紧凑数组上传到 GPU，dispatch 全部 kernel，
    /// 再把结果回读 scatter 回 store，从而复用现有 CPU 渲染路径（无需改渲染）。后续 Phase 2b 将改为 GPU 常驻 +
    /// DrawMeshInstancedIndirect 直读，去掉每帧的上传/回读开销。
    ///
    /// GPU implementation of the SPH dual-density solver (compute shader). Algorithm is identical to
    /// <see cref="SphCpuSolver"/> (same kernels, counting-sort hash, collision/mixing rules), computed on the GPU.
    /// Phase 2a is a "bridge": each frame it gathers the active set from the SoA store into compact arrays, uploads,
    /// dispatches all kernels, then reads back and scatters into the store so the existing CPU render path works unchanged.
    /// Phase 2b will switch to GPU-resident data + DrawMeshInstancedIndirect to remove the per-frame upload/readback.
    ///
    /// SPH デュアル密度ソルバーの GPU 実装（Compute Shader）。アルゴリズムは CPU 実装と完全一致。Phase 2a はブリッジ実装。
    /// </summary>
    public sealed class SphGpuSolver : ILiquid2DSolver
    {
        public Liquid2DSimulationMode Mode => Liquid2DSimulationMode.Gpu;

        private const float ImpulseScale = 256f; // 冲量定点量化系数。 // impulse fixed-point scale. // 力積定点係数。

        private readonly ComputeShader _cs;
        private readonly bool _valid;

        // Kernel 索引。 // Kernel indices. // カーネル索引。
        private readonly int _kExternal, _kClearGrid, _kCount, _kPrefix, _kScatter, _kDensity, _kPressure,
            _kViscosity, _kCopyVel, _kClearImpulse, _kIntegrate, _kMix, _kCopyColor;
        private int[] _allKernels;

        // GPU 缓冲。 // GPU buffers. // GPU バッファ。
        private ComputeBuffer _positions, _predicted, _velocities, _velNext, _densities, _colors, _colorsNext, _lastMix;
        private ComputeBuffer _radii, _invMass, _typeId, _groupId;
        private ComputeBuffer _materials, _mixDatas, _colliders, _points, _impulseX, _impulseY;
        private ComputeBuffer _bucketOf, _counts, _cellStart, _cursor, _sortedSlots;

        // 托管暂存（gather/scatter/upload，按需增长）。 // Managed scratch (grows on demand). // マネージドスクラッチ。
        private float2[] _pos, _vel; private float4[] _col;
        private float[] _radiiA, _invMassA, _lastMixA; private int[] _typeIdA, _groupIdA;
        private Liquid2DGpuCollider[] _colA; private Liquid2DGpuMixData[] _mixA; private float2[] _pointsA;
        private int[] _impXA, _impYA;

        public SphGpuSolver(ComputeShader cs)
        {
            _cs = cs;
            if (_cs == null)
            {
                Debug.LogError("[SphGpuSolver] Compute shader 'Liquid2DSph' not found; GPU mode unavailable.");
                _valid = false;
                return;
            }

            _kExternal = _cs.FindKernel("ExternalForces");
            _kClearGrid = _cs.FindKernel("ClearGrid");
            _kCount = _cs.FindKernel("CountBuckets");
            _kPrefix = _cs.FindKernel("PrefixSum");
            _kScatter = _cs.FindKernel("ScatterSlots");
            _kDensity = _cs.FindKernel("Density");
            _kPressure = _cs.FindKernel("Pressure");
            _kViscosity = _cs.FindKernel("Viscosity");
            _kCopyVel = _cs.FindKernel("CopyVelocity");
            _kClearImpulse = _cs.FindKernel("ClearImpulse");
            _kIntegrate = _cs.FindKernel("IntegrateCollide");
            _kMix = _cs.FindKernel("MixColor");
            _kCopyColor = _cs.FindKernel("CopyColor");
            _allKernels = new[]
            {
                _kExternal, _kClearGrid, _kCount, _kPrefix, _kScatter, _kDensity, _kPressure,
                _kViscosity, _kCopyVel, _kClearImpulse, _kIntegrate, _kMix, _kCopyColor,
            };
            _valid = true;
        }

        private static int Groups(int n) => Mathf.Max(1, (n + 63) / 64);

        private static void Ensure(ref ComputeBuffer b, int count, int stride)
        {
            if (b != null && b.count >= count) return;
            b?.Release();
            b = new ComputeBuffer(Mathf.Max(1, count), stride, ComputeBufferType.Structured);
        }

        private static void EnsureArray<T>(ref T[] a, int n) { if (a == null || a.Length < n) a = new T[Mathf.Max(16, n)]; }

        public void Step(in Liquid2DSolveContext ctx, in SolverParams p, float dt)
        {
            if (!_valid) return;
            int count = ctx.activeCount;
            if (count <= 0 || dt <= 0f) return;

            var store = ctx.store;
            int numTypes = Mathf.Max(1, ctx.materials.Length);
            int numColliders = ctx.colliders.IsCreated ? ctx.colliders.Count : 0;
            int numPoints = ctx.colliders.IsCreated ? Mathf.Max(1, ctx.colliders.points.Length) : 1;
            int numBodies = Mathf.Max(1, ctx.colliderImpulse.IsCreated ? ctx.colliderImpulse.Length : 1);
            int tableSize = NextPrime(math.max(16, count * 2));

            EnsureBuffers(count, tableSize, numTypes, numColliders, numPoints, numBodies);
            UploadParticles(ctx, store, count);
            UploadAux(ctx, numTypes, numColliders, numPoints);
            BindAll(numColliders, numBodies);
            SetConstants(p, count, tableSize, numColliders, numBodies);

            int substeps = math.max(1, p.substeps);
            float subDt = dt / substeps;
            _cs.SetFloat("dt", subDt);
            _cs.SetFloat("time", ctx.time);

            int gParticles = Groups(count);
            int gTable = Groups(tableSize);

            for (int step = 0; step < substeps; step++)
            {
                bool lastStep = step == substeps - 1;

                _cs.Dispatch(_kExternal, gParticles, 1, 1);

                // 主排序：按 predicted 分桶。 // Main sort: bucket by predicted. // 主ソート：predicted で分桶。
                _cs.SetBuffer(_kCount, "SortPositions", _predicted);
                SortPass(gParticles, gTable);

                _cs.Dispatch(_kDensity, gParticles, 1, 1);
                _cs.Dispatch(_kPressure, gParticles, 1, 1);
                _cs.Dispatch(_kViscosity, gParticles, 1, 1);
                _cs.Dispatch(_kCopyVel, gParticles, 1, 1);

                if (lastStep)
                    _cs.Dispatch(_kClearImpulse, Groups(numBodies), 1, 1);

                _cs.SetInt("accumulateImpulse", lastStep ? 1 : 0);
                _cs.Dispatch(_kIntegrate, gParticles, 1, 1);

                if (lastStep)
                {
                    // 混色排序：按最终 positions 分桶。 // Mix sort: bucket by final positions. // 混色ソート：positions で分桶。
                    _cs.SetBuffer(_kCount, "SortPositions", _positions);
                    SortPass(gParticles, gTable);
                    _cs.Dispatch(_kMix, gParticles, 1, 1);
                    _cs.Dispatch(_kCopyColor, gParticles, 1, 1);
                }
            }

            ReadbackAndScatter(ctx, store, count, numBodies);
        }

        private void SortPass(int gParticles, int gTable)
        {
            _cs.Dispatch(_kClearGrid, gTable, 1, 1);
            _cs.Dispatch(_kCount, gParticles, 1, 1);
            _cs.Dispatch(_kPrefix, 1, 1, 1);
            _cs.Dispatch(_kScatter, gParticles, 1, 1);
        }

        // Ensure 自身在容量足够时是空操作，故每帧调用开销很小；小缓冲始终保证至少 1 元素以避免未绑定。
        // Ensure no-ops when already large enough, so per-frame calls are cheap; small buffers keep ≥1 element to avoid unbound SRVs.
        // Ensure は十分大きければ無操作。小バッファは最低 1 要素を保証。
        private void EnsureBuffers(int count, int tableSize, int numTypes, int numColliders, int numPoints, int numBodies)
        {
            Ensure(ref _positions, count, 8); Ensure(ref _predicted, count, 8);
            Ensure(ref _velocities, count, 8); Ensure(ref _velNext, count, 8);
            Ensure(ref _densities, count, 8); Ensure(ref _colors, count, 16); Ensure(ref _colorsNext, count, 16);
            Ensure(ref _lastMix, count, 4); Ensure(ref _radii, count, 4); Ensure(ref _invMass, count, 4);
            Ensure(ref _typeId, count, 4); Ensure(ref _groupId, count, 4);
            Ensure(ref _bucketOf, count, 4); Ensure(ref _sortedSlots, count, 4);
            Ensure(ref _counts, tableSize, 4); Ensure(ref _cellStart, tableSize + 1, 4); Ensure(ref _cursor, tableSize, 4);
            Ensure(ref _materials, numTypes, 28); Ensure(ref _mixDatas, numTypes, 20);
            Ensure(ref _colliders, Mathf.Max(1, numColliders), 44);
            Ensure(ref _points, Mathf.Max(1, numPoints), 8);
            Ensure(ref _impulseX, numBodies, 4); Ensure(ref _impulseY, numBodies, 4);
        }

        private void UploadParticles(in Liquid2DSolveContext ctx, Liquid2DParticleStore store, int count)
        {
            EnsureArray(ref _pos, count); EnsureArray(ref _vel, count); EnsureArray(ref _col, count);
            EnsureArray(ref _radiiA, count); EnsureArray(ref _invMassA, count); EnsureArray(ref _lastMixA, count);
            EnsureArray(ref _typeIdA, count); EnsureArray(ref _groupIdA, count);

            var ai = ctx.activeIndices;
            for (int k = 0; k < count; k++)
            {
                int slot = ai[k];
                _pos[k] = store.positions[slot];
                _vel[k] = store.velocities[slot];
                _col[k] = store.colors[slot];
                _radiiA[k] = store.radii[slot];
                _invMassA[k] = store.invMass[slot];
                _lastMixA[k] = store.lastMixTime[slot];
                _typeIdA[k] = store.typeId[slot];
                _groupIdA[k] = store.groupId[slot];
            }

            _positions.SetData(_pos, 0, 0, count);
            _velocities.SetData(_vel, 0, 0, count);
            _colors.SetData(_col, 0, 0, count);
            _radii.SetData(_radiiA, 0, 0, count);
            _invMass.SetData(_invMassA, 0, 0, count);
            _lastMix.SetData(_lastMixA, 0, 0, count);
            _typeId.SetData(_typeIdA, 0, 0, count);
            _groupId.SetData(_groupIdA, 0, 0, count);
        }

        private void UploadAux(in Liquid2DSolveContext ctx, int numTypes, int numColliders, int numPoints)
        {
            // 材质（NativeArray 直传）。 // Materials (direct NativeArray upload). // マテリアル。
            if (ctx.materials.IsCreated && ctx.materials.Length > 0)
                _materials.SetData(ctx.materials, 0, 0, ctx.materials.Length);

            // 混色（转 GPU 结构）。 // Mix (convert to GPU struct). // 混色。
            EnsureArray(ref _mixA, numTypes);
            int mixN = ctx.mixData.IsCreated ? ctx.mixData.Length : 0;
            for (int i = 0; i < numTypes; i++)
                _mixA[i] = Liquid2DGpuMixData.From(i < mixN ? ctx.mixData[i] : Liquid2DMixData.Disabled);
            _mixDatas.SetData(_mixA, 0, 0, numTypes);

            // 碰撞体（转 GPU 结构）。 // Colliders (convert to GPU struct). // コライダー。
            if (numColliders > 0)
            {
                EnsureArray(ref _colA, numColliders);
                for (int i = 0; i < numColliders; i++)
                    _colA[i] = Liquid2DGpuCollider.From(ctx.colliders.colliders[i]);
                _colliders.SetData(_colA, 0, 0, numColliders);
            }

            // 碰撞体顶点（float2 直传）。 // Collider points (direct float2 upload). // 頂点。
            EnsureArray(ref _pointsA, numPoints);
            int pN = ctx.colliders.IsCreated ? ctx.colliders.points.Length : 0;
            for (int i = 0; i < numPoints; i++)
                _pointsA[i] = i < pN ? ctx.colliders.points[i] : float2.zero;
            _points.SetData(_pointsA, 0, 0, numPoints);
        }

        private void BindAll(int numColliders, int numBodies)
        {
            Bind("Positions", _positions); Bind("Predicted", _predicted); Bind("Velocities", _velocities);
            Bind("VelNext", _velNext); Bind("Densities", _densities); Bind("Colors", _colors);
            Bind("ColorsNext", _colorsNext); Bind("LastMixTime", _lastMix); Bind("Radii", _radii);
            Bind("InvMass", _invMass); Bind("TypeId", _typeId); Bind("GroupId", _groupId);
            Bind("Materials", _materials); Bind("MixDatas", _mixDatas); Bind("Colliders", _colliders);
            Bind("ColliderPoints", _points); Bind("ImpulseX", _impulseX); Bind("ImpulseY", _impulseY);
            Bind("BucketOf", _bucketOf); Bind("Counts", _counts); Bind("CellStart", _cellStart);
            Bind("Cursor", _cursor); Bind("SortedSlots", _sortedSlots);
        }

        // 把缓冲绑定到所有 kernel；kernel 未引用该名时 Unity 静默忽略。 // Bind to all kernels; unused names are silently ignored. // 全カーネルに束縛。
        private void Bind(string name, ComputeBuffer buf)
        {
            if (buf == null) return;
            for (int i = 0; i < _allKernels.Length; i++)
                _cs.SetBuffer(_allKernels[i], name, buf);
        }

        private void SetConstants(in SolverParams p, int count, int tableSize, int numColliders, int numBodies)
        {
            _cs.SetInt("numParticles", count);
            _cs.SetInt("tableSize", tableSize);
            _cs.SetInt("numColliders", numColliders);
            _cs.SetInt("numBodies", numBodies);
            _cs.SetFloat("h", p.h);
            _cs.SetFloat("invCellSize", 1f / math.max(1e-4f, p.h));
            _cs.SetFloat("targetDensity", p.targetDensity);
            _cs.SetFloat("pressureMultiplier", p.pressureMultiplier);
            _cs.SetFloat("nearPressureMultiplier", p.nearPressureMultiplier);
            _cs.SetFloat("viscosityStrength", p.viscosityStrength);
            _cs.SetFloat("collisionDamping", p.collisionDamping);
            _cs.SetFloat("maxSpeed", p.maxSpeed);
            _cs.SetFloat("predictionFactor", p.predictionFactor);
            _cs.SetVector("gravity", new Vector4(p.gravity.x, p.gravity.y, 0f, 0f));
            _cs.SetFloat("impulseScale", ImpulseScale);
        }

        private void ReadbackAndScatter(in Liquid2DSolveContext ctx, Liquid2DParticleStore store, int count, int numBodies)
        {
            _positions.GetData(_pos, 0, 0, count);
            _velocities.GetData(_vel, 0, 0, count);
            _colors.GetData(_col, 0, 0, count);
            _lastMix.GetData(_lastMixA, 0, 0, count);

            var ai = ctx.activeIndices;
            for (int k = 0; k < count; k++)
            {
                int slot = ai[k];
                store.positions[slot] = _pos[k];
                store.velocities[slot] = _vel[k];
                store.colors[slot] = _col[k];
                store.lastMixTime[slot] = _lastMixA[k];
            }

            // 冲量回读 + 反量化 → 写入 colliderImpulse（双向耦合 seam）。 // Impulse readback + dequantize. // 力積回読。
            var impulseOut = ctx.colliderImpulse;
            if (impulseOut.IsCreated && _impulseX != null)
            {
                EnsureArray(ref _impXA, numBodies); EnsureArray(ref _impYA, numBodies);
                _impulseX.GetData(_impXA, 0, 0, numBodies);
                _impulseY.GetData(_impYA, 0, 0, numBodies);
                for (int b = 0; b < numBodies && b < impulseOut.Length; b++)
                    impulseOut[b] += new float2(_impXA[b] / ImpulseScale, _impYA[b] / ImpulseScale);
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
            void R(ComputeBuffer b) => b?.Release();
            R(_positions); R(_predicted); R(_velocities); R(_velNext); R(_densities); R(_colors); R(_colorsNext);
            R(_lastMix); R(_radii); R(_invMass); R(_typeId); R(_groupId);
            R(_materials); R(_mixDatas); R(_colliders); R(_points); R(_impulseX); R(_impulseY);
            R(_bucketOf); R(_counts); R(_cellStart); R(_cursor); R(_sortedSlots);
            _positions = _predicted = _velocities = _velNext = _densities = _colors = _colorsNext = null;
            _lastMix = _radii = _invMass = _typeId = _groupId = null;
            _materials = _mixDatas = _colliders = _points = _impulseX = _impulseY = null;
            _bucketOf = _counts = _cellStart = _cursor = _sortedSlots = null;
        }
    }
}

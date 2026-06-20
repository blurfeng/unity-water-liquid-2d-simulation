using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// SPH 双密度流体求解器的 GPU 实现（Compute Shader，数据常驻）。算法与 <see cref="SphCpuSolver"/> 完全一致。
    ///
    /// 数据常驻：粒子缓冲按 slot 索引、跨帧驻留在 GPU，物理状态在 GPU 上演化，不再每帧上传/回读。每帧仅上传紧凑的
    /// ActiveIndices；新生成的粒子通过 ScatterSpawn 增量写入；store 扩容时先回读旧缓冲保状态再重建并全量重传。
    /// 渲染层（Liquid2DPass）通过 <see cref="TryGetRenderBuffers"/> 直读 GPU 缓冲（DrawProcedural），无需回读。
    /// 仅当存在动态碰撞体时才回读冲量（小数组，双向耦合用）；否则全程无 CPU 同步点。
    ///
    /// GPU implementation of the SPH dual-density solver (compute shader, resident data). Identical algorithm to
    /// <see cref="SphCpuSolver"/>. Particle buffers are slot-indexed and reside on the GPU; state evolves on the GPU with
    /// no per-frame upload/readback. Only the compact ActiveIndices is uploaded each frame; new particles are written
    /// incrementally via ScatterSpawn; on store growth the old buffers are read back to preserve state, then recreated and
    /// fully re-uploaded. The render layer reads GPU buffers directly via <see cref="TryGetRenderBuffers"/> (DrawProcedural).
    /// Impulse is read back only when dynamic colliders exist; otherwise there is no per-frame CPU sync point.
    ///
    /// SPH デュアル密度ソルバーの GPU 実装（Compute Shader、データ常駐）。CPU 実装と完全一致。
    /// </summary>
    public sealed class SphGpuSolver : ILiquid2DSolver
    {
        public Liquid2DSimulationMode Mode => Liquid2DSimulationMode.Gpu;

        private const float ImpulseScale = 256f;

        private readonly ComputeShader _cs;
        private readonly bool _valid;

        private readonly int _kSpawn, _kExternal, _kClearGrid, _kCount, _kPrefix, _kScatter, _kDensity, _kPressure,
            _kViscosity, _kCopyVel, _kClearImpulse, _kIntegrate, _kMix, _kCopyColor, _kDeadZone;
        private readonly int[] _allKernels;

        // 容量级缓冲（slot 索引，扩容时保状态重建）。 // Capacity-level buffers (slot-indexed). // 容量レベルバッファ。
        private ComputeBuffer _positions, _predicted, _velocities, _velNext, _densities, _colors, _colorsNext, _lastMix;
        private ComputeBuffer _radii, _invMass, _typeId, _groupId;
        // count 级。 // count-level.
        private ComputeBuffer _active, _bucketOf, _sortedSlots, _killFlags;
        // tableSize 级。 // table-level.
        private ComputeBuffer _counts, _cellStart, _cursor;
        // 辅助。 // aux.
        private ComputeBuffer _materials, _mixDatas, _colliders, _points, _impulseX, _impulseY, _forceFields;
        private ComputeBuffer _deadZones, _deadZonePoints;
        // 生成上传（仅动态状态；静态属性由 store 整块 SetData）。 // spawn upload (dynamic only). // 生成アップロード。
        private ComputeBuffer _upSlots, _upPos, _upVel, _upColor;

        private int _capacity;       // 当前容量级缓冲容量。 // current capacity-buffer size.
        private bool _needFullReupload;
        private int _lastCount;

        // 托管暂存。 // Managed scratch.
        private float2[] _pos, _vel; private float4[] _col; private float[] _lastMixA;
        private int[] _activeA;
        private Liquid2DGpuCollider[] _colA; private Liquid2DGpuMixData[] _mixA; private float2[] _pointsA;
        private Liquid2DGpuForceField[] _ffA;
        private Liquid2DGpuDeadZone[] _dzA; private float2[] _dzPointsA; private int[] _killA;
        private int[] _impXA, _impYA;
        private int[] _upSlotsA; private float2[] _upPosA, _upVelA; private float4[] _upColorA;

        public SphGpuSolver(ComputeShader cs)
        {
            _cs = cs;
            if (_cs == null) { Debug.LogError("[SphGpuSolver] Compute shader 'Liquid2DSph' not found; GPU mode unavailable."); _valid = false; return; }

            _kSpawn = _cs.FindKernel("ScatterSpawn");
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
            _kDeadZone = _cs.FindKernel("DeadZoneKill");
            _allKernels = new[]
            {
                _kSpawn, _kExternal, _kClearGrid, _kCount, _kPrefix, _kScatter, _kDensity, _kPressure,
                _kViscosity, _kCopyVel, _kClearImpulse, _kIntegrate, _kMix, _kCopyColor, _kDeadZone,
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
            var store = ctx.store;
            if (store == null) return;

            HandleCapacity(store);

            // 处理生成（含扩容后的全量重传），即使本帧 active 为 0 也要写入。 // Handle spawns (and full re-upload after grow). // 生成処理。
            DispatchSpawns(ctx, store, count);

            if (count <= 0 || dt <= 0f) { _lastCount = 0; return; }

            int numTypes = Mathf.Max(1, ctx.materials.IsCreated ? ctx.materials.Length : 1);
            int numColliders = ctx.colliders.IsCreated ? ctx.colliders.Count : 0;
            int numPoints = ctx.colliders.IsCreated ? Mathf.Max(1, ctx.colliders.points.Length) : 1;
            int numBodies = Mathf.Max(1, ctx.dynamicBodyCount);
            int numForceFields = ctx.forceFields.Count;
            int numDeadZones = ctx.deadZoneCount;
            int numDeadZonePoints = numDeadZones > 0 && ctx.deadZones.points.IsCreated ? Mathf.Max(1, ctx.deadZones.points.Length) : 1;
            int tableSize = NextPrime(math.max(16, count * 2));

            EnsureCountBuffers(count);
            EnsureTableBuffers(tableSize);
            EnsureAuxBuffers(numTypes, numColliders, numPoints, numBodies, numForceFields);
            EnsureDeadZoneBuffers(Mathf.Max(1, numDeadZones), numDeadZonePoints);

            UploadActiveIndices(ctx, count);
            UploadAux(ctx, numTypes, numColliders, numPoints, numForceFields);
            UploadDeadZones(ctx, numDeadZones, numDeadZonePoints);
            BindAll();
            SetConstants(p, count, tableSize, numColliders, numBodies, numForceFields);
            _cs.SetInt("numDeadZones", numDeadZones);

            int substeps = math.max(1, p.substeps);
            float subDt = dt / substeps;
            _cs.SetFloat("dt", subDt);
            _cs.SetFloat("time", ctx.time);

            int gP = Groups(count);
            int gT = Groups(tableSize);

            for (int step = 0; step < substeps; step++)
            {
                bool lastStep = step == substeps - 1;

                _cs.Dispatch(_kExternal, gP, 1, 1);

                _cs.SetBuffer(_kCount, "SortPositions", _predicted);
                SortPass(gP, gT);

                _cs.Dispatch(_kDensity, gP, 1, 1);
                _cs.Dispatch(_kPressure, gP, 1, 1);
                _cs.Dispatch(_kViscosity, gP, 1, 1);
                _cs.Dispatch(_kCopyVel, gP, 1, 1);

                if (lastStep) _cs.Dispatch(_kClearImpulse, Groups(numBodies), 1, 1);
                _cs.SetInt("accumulateImpulse", lastStep ? 1 : 0);
                _cs.Dispatch(_kIntegrate, gP, 1, 1);

                if (lastStep)
                {
                    _cs.SetBuffer(_kCount, "SortPositions", _positions);
                    SortPass(gP, gT);
                    _cs.Dispatch(_kMix, gP, 1, 1);
                    _cs.Dispatch(_kCopyColor, gP, 1, 1);

                    // 销毁区域标记（最终位置）：求解后回读 KillFlags 由 CPU 回收 slot。
                    // Dead-zone marking (final positions); KillFlags is read back so the CPU recycles slots after solve.
                    // 破棄領域マーキング（最終位置）。求解後 KillFlags を回読し CPU が slot を回収。
                    if (numDeadZones > 0) _cs.Dispatch(_kDeadZone, gP, 1, 1);
                }
            }

            // 仅有动态碰撞体时回读冲量（双向耦合）；否则不产生 CPU 同步点。 // Read impulse only when dynamic bodies exist. // 動的体がある時のみ回読。
            if (ctx.dynamicBodyCount > 0) ReadbackImpulse(ctx, numBodies);

            // 有销毁区域时回读销毁标记（小数组，count 字节级）。 // Read back kill flags when dead zones exist (small array). // 破棄領域がある時のみ回読。
            if (numDeadZones > 0) ReadbackKillFlags(ctx, count);

            _lastCount = count;
        }

        private void SortPass(int gP, int gT)
        {
            _cs.Dispatch(_kClearGrid, gT, 1, 1);
            _cs.Dispatch(_kCount, gP, 1, 1);
            _cs.Dispatch(_kPrefix, 1, 1, 1);
            _cs.Dispatch(_kScatter, gP, 1, 1);
        }

        // 容量级缓冲：扩容时先回读旧缓冲（保状态）再重建并标记全量重传。 // Grow: read back old buffers to preserve state, recreate, mark full re-upload. // 容量バッファ。
        private void HandleCapacity(Liquid2DParticleStore store)
        {
            int cap = store.Capacity;
            if (cap == _capacity && _positions != null) return;

            if (_positions != null && _capacity > 0)
                ReadbackResidentToStore(store, _capacity);

            void C(ref ComputeBuffer b, int stride) { b?.Release(); b = new ComputeBuffer(Mathf.Max(1, cap), stride, ComputeBufferType.Structured); }
            C(ref _positions, 8); C(ref _predicted, 8); C(ref _velocities, 8); C(ref _velNext, 8); C(ref _densities, 8);
            C(ref _colors, 16); C(ref _colorsNext, 16); C(ref _lastMix, 4);
            C(ref _radii, 4); C(ref _invMass, 4); C(ref _typeId, 4); C(ref _groupId, 4);
            _capacity = cap;
            _needFullReupload = true;
        }

        private void ReadbackResidentToStore(Liquid2DParticleStore store, int oldCap)
        {
            EnsureArray(ref _pos, oldCap); EnsureArray(ref _vel, oldCap); EnsureArray(ref _col, oldCap); EnsureArray(ref _lastMixA, oldCap);
            _positions.GetData(_pos, 0, 0, oldCap);
            _velocities.GetData(_vel, 0, 0, oldCap);
            _colors.GetData(_col, 0, 0, oldCap);
            _lastMix.GetData(_lastMixA, 0, 0, oldCap);
            for (int slot = 0; slot < oldCap; slot++)
            {
                store.positions[slot] = _pos[slot];
                store.velocities[slot] = _vel[slot];
                store.colors[slot] = _col[slot];
                store.lastMixTime[slot] = _lastMixA[slot];
            }
        }

        // 写入新生成粒子（增量=pending 列表）或扩容/首帧后的全部 active（full）。 // Write spawned particles (incremental) or all active after grow (full). // 新規粒子書込み。
        private void DispatchSpawns(in Liquid2DSolveContext ctx, Liquid2DParticleStore store, int count)
        {
            bool full = _needFullReupload;
            int n;
            if (full) { n = count; _needFullReupload = false; }
            else { var pending = ctx.gpuPendingSpawns; n = pending != null ? pending.Count : 0; }
            if (n <= 0) return;

            EnsureArray(ref _upSlotsA, n); EnsureArray(ref _upPosA, n); EnsureArray(ref _upVelA, n); EnsureArray(ref _upColorA, n);

            for (int m = 0; m < n; m++)
            {
                int slot = full ? ctx.activeIndices[m] : ctx.gpuPendingSpawns[m];
                if (slot < 0 || slot >= store.Capacity) { _upSlotsA[m] = 0; _upPosA[m] = default; _upVelA[m] = default; _upColorA[m] = default; continue; }
                _upSlotsA[m] = slot;
                _upPosA[m] = store.positions[slot];
                _upVelA[m] = store.velocities[slot];
                _upColorA[m] = store.colors[slot];
            }

            EnsureUploadBuffers(n);
            _upSlots.SetData(_upSlotsA, 0, 0, n); _upPos.SetData(_upPosA, 0, 0, n);
            _upVel.SetData(_upVelA, 0, 0, n); _upColor.SetData(_upColorA, 0, 0, n);

            // 静态属性（radius/invMass/typeId/groupId）整块上传（slot 索引，与 store 对齐；spawn 后不变，只在有生成时刷新）。
            // Static attributes uploaded whole (slot-indexed, aligned with store; immutable after spawn).
            // 静的属性を一括アップロード。
            UploadStaticAttributes(store, _capacity);

            BindSpawn();
            _cs.SetInt("numUploads", n);
            _cs.SetFloat("time", ctx.time);
            _cs.Dispatch(_kSpawn, Groups(n), 1, 1);
        }

        private void UploadStaticAttributes(Liquid2DParticleStore store, int cap)
        {
            if (cap <= 0) return;
            int c = Mathf.Min(cap, store.radii.Length);
            _radii.SetData(store.radii, 0, 0, c);
            _invMass.SetData(store.invMass, 0, 0, c);
            _typeId.SetData(store.typeId, 0, 0, c);
            _groupId.SetData(store.groupId, 0, 0, c);
        }

        private void BindSpawn()
        {
            _cs.SetBuffer(_kSpawn, "UploadSlots", _upSlots); _cs.SetBuffer(_kSpawn, "UploadPos", _upPos);
            _cs.SetBuffer(_kSpawn, "UploadVel", _upVel); _cs.SetBuffer(_kSpawn, "UploadColor", _upColor);
            _cs.SetBuffer(_kSpawn, "Positions", _positions); _cs.SetBuffer(_kSpawn, "Predicted", _predicted);
            _cs.SetBuffer(_kSpawn, "Velocities", _velocities); _cs.SetBuffer(_kSpawn, "Colors", _colors);
            _cs.SetBuffer(_kSpawn, "ColorsNext", _colorsNext); _cs.SetBuffer(_kSpawn, "LastMixTime", _lastMix);
            _cs.SetBuffer(_kSpawn, "Densities", _densities);
        }

        private void EnsureCountBuffers(int count)
        {
            Ensure(ref _active, count, 4); Ensure(ref _bucketOf, count, 4); Ensure(ref _sortedSlots, count, 4);
            Ensure(ref _killFlags, count, 4);
        }
        private void EnsureDeadZoneBuffers(int numDeadZones, int numDeadZonePoints)
        {
            Ensure(ref _deadZones, numDeadZones, 48); Ensure(ref _deadZonePoints, numDeadZonePoints, 8);
        }
        private void EnsureTableBuffers(int tableSize)
        {
            Ensure(ref _counts, tableSize, 4); Ensure(ref _cellStart, tableSize + 1, 4); Ensure(ref _cursor, tableSize, 4);
        }
        private void EnsureAuxBuffers(int numTypes, int numColliders, int numPoints, int numBodies, int numForceFields)
        {
            Ensure(ref _materials, numTypes, 28); Ensure(ref _mixDatas, numTypes, 20);
            Ensure(ref _colliders, Mathf.Max(1, numColliders), 44); Ensure(ref _points, Mathf.Max(1, numPoints), 8);
            Ensure(ref _impulseX, numBodies, 4); Ensure(ref _impulseY, numBodies, 4);
            Ensure(ref _forceFields, Mathf.Max(1, numForceFields), 36);
        }
        private void EnsureUploadBuffers(int n)
        {
            Ensure(ref _upSlots, n, 4); Ensure(ref _upPos, n, 8); Ensure(ref _upVel, n, 8); Ensure(ref _upColor, n, 16);
        }

        private void UploadActiveIndices(in Liquid2DSolveContext ctx, int count)
        {
            EnsureArray(ref _activeA, count);
            var ai = ctx.activeIndices;
            for (int k = 0; k < count; k++) _activeA[k] = ai[k];
            _active.SetData(_activeA, 0, 0, count);
        }

        private void UploadAux(in Liquid2DSolveContext ctx, int numTypes, int numColliders, int numPoints, int numForceFields)
        {
            if (ctx.materials.IsCreated && ctx.materials.Length > 0)
                _materials.SetData(ctx.materials, 0, 0, ctx.materials.Length);

            EnsureArray(ref _mixA, numTypes);
            int mixN = ctx.mixData.IsCreated ? ctx.mixData.Length : 0;
            for (int i = 0; i < numTypes; i++) _mixA[i] = Liquid2DGpuMixData.From(i < mixN ? ctx.mixData[i] : Liquid2DMixData.Disabled);
            _mixDatas.SetData(_mixA, 0, 0, numTypes);

            if (numColliders > 0)
            {
                EnsureArray(ref _colA, numColliders);
                for (int i = 0; i < numColliders; i++) _colA[i] = Liquid2DGpuCollider.From(ctx.colliders.colliders[i]);
                _colliders.SetData(_colA, 0, 0, numColliders);
            }

            EnsureArray(ref _pointsA, numPoints);
            int pN = ctx.colliders.IsCreated ? ctx.colliders.points.Length : 0;
            for (int i = 0; i < numPoints; i++) _pointsA[i] = i < pN ? ctx.colliders.points[i] : float2.zero;
            _points.SetData(_pointsA, 0, 0, numPoints);

            if (numForceFields > 0)
            {
                EnsureArray(ref _ffA, numForceFields);
                for (int i = 0; i < numForceFields; i++) _ffA[i] = Liquid2DGpuForceField.From(ctx.forceFields.fields[i]);
                _forceFields.SetData(_ffA, 0, 0, numForceFields);
            }
        }

        private void UploadDeadZones(in Liquid2DSolveContext ctx, int numDeadZones, int numDeadZonePoints)
        {
            if (numDeadZones > 0)
            {
                EnsureArray(ref _dzA, numDeadZones);
                for (int i = 0; i < numDeadZones; i++) _dzA[i] = Liquid2DGpuDeadZone.From(ctx.deadZones.zones[i]);
                _deadZones.SetData(_dzA, 0, 0, numDeadZones);
            }

            EnsureArray(ref _dzPointsA, numDeadZonePoints);
            int pN = numDeadZones > 0 && ctx.deadZones.points.IsCreated ? ctx.deadZones.points.Length : 0;
            for (int i = 0; i < numDeadZonePoints; i++) _dzPointsA[i] = i < pN ? ctx.deadZones.points[i] : float2.zero;
            _deadZonePoints.SetData(_dzPointsA, 0, 0, numDeadZonePoints);
        }

        private void BindAll()
        {
            Bind("Positions", _positions); Bind("Predicted", _predicted); Bind("Velocities", _velocities);
            Bind("VelNext", _velNext); Bind("Densities", _densities); Bind("Colors", _colors);
            Bind("ColorsNext", _colorsNext); Bind("LastMixTime", _lastMix); Bind("Radii", _radii);
            Bind("InvMass", _invMass); Bind("TypeId", _typeId); Bind("GroupId", _groupId);
            Bind("ActiveIndices", _active); Bind("Materials", _materials); Bind("MixDatas", _mixDatas);
            Bind("Colliders", _colliders); Bind("ColliderPoints", _points); Bind("ImpulseX", _impulseX); Bind("ImpulseY", _impulseY);
            Bind("ForceFields", _forceFields);
            Bind("DeadZones", _deadZones); Bind("DeadZonePoints", _deadZonePoints); Bind("KillFlags", _killFlags);
            Bind("BucketOf", _bucketOf); Bind("Counts", _counts); Bind("CellStart", _cellStart);
            Bind("Cursor", _cursor); Bind("SortedSlots", _sortedSlots);
        }

        private void Bind(string name, ComputeBuffer buf)
        {
            if (buf == null) return;
            for (int i = 0; i < _allKernels.Length; i++) _cs.SetBuffer(_allKernels[i], name, buf);
        }

        private void SetConstants(in SolverParams p, int count, int tableSize, int numColliders, int numBodies, int numForceFields)
        {
            _cs.SetInt("numParticles", count);
            _cs.SetInt("tableSize", tableSize);
            _cs.SetInt("numColliders", numColliders);
            _cs.SetInt("numBodies", numBodies);
            _cs.SetInt("numForceFields", numForceFields);
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

        private void ReadbackImpulse(in Liquid2DSolveContext ctx, int numBodies)
        {
            var impulseOut = ctx.colliderImpulse;
            if (!impulseOut.IsCreated || _impulseX == null) return;
            EnsureArray(ref _impXA, numBodies); EnsureArray(ref _impYA, numBodies);
            _impulseX.GetData(_impXA, 0, 0, numBodies);
            _impulseY.GetData(_impYA, 0, 0, numBodies);
            for (int b = 0; b < numBodies && b < impulseOut.Length; b++)
                impulseOut[b] += new float2(_impXA[b] / ImpulseScale, _impYA[b] / ImpulseScale);
        }

        // 回读销毁标记到 ctx.killFlags（按 k 索引，1=该活动粒子待回收）。 // Read kill flags back into ctx.killFlags (indexed by k). // 破棄フラグを回読。
        private void ReadbackKillFlags(in Liquid2DSolveContext ctx, int count)
        {
            var flagsOut = ctx.killFlags;
            if (!flagsOut.IsCreated || _killFlags == null || count <= 0) return;
            EnsureArray(ref _killA, count);
            _killFlags.GetData(_killA, 0, 0, count);
            for (int k = 0; k < count && k < flagsOut.Length; k++)
                flagsOut[k] = (byte)(_killA[k] != 0 ? 1 : 0);
        }

        /// <summary>
        /// 供渲染层直读 GPU 缓冲（DrawProcedural）。返回的缓冲在物理 Step 后保持有效。
        /// For the render layer to read GPU buffers directly (DrawProcedural). Buffers stay valid after the physics Step.
        /// 描画層が GPU バッファを直読するため。
        /// </summary>
        public bool TryGetRenderBuffers(out ComputeBuffer positions, out ComputeBuffer colors, out ComputeBuffer radii,
            out ComputeBuffer typeIds, out ComputeBuffer activeIndices, out ComputeBuffer velocities, out int count)
        {
            positions = _positions; colors = _colors; radii = _radii; typeIds = _typeId; activeIndices = _active;
            velocities = _velocities;
            count = _lastCount;
            return _valid && _positions != null && _active != null && _lastCount > 0;
        }

        /// <summary>
        /// 每帧把常驻 GPU 缓冲（position/velocity/color/lastMix）全量回读到 CPU store。
        /// 仅供调试/查询（Gizmos、GetPosition/GetVelocity）使用——这是一个同步 GPU→CPU 阻塞点，会严重降低性能。
        /// Full per-frame readback of resident GPU buffers (position/velocity/color/lastMix) into the CPU store.
        /// For debug/query use only (Gizmos, GetPosition/GetVelocity) — a synchronous GPU→CPU stall that severely hurts performance.
        /// 常駐 GPU バッファを毎フレーム CPU store へ全量回読（デバッグ/クエリ用、同期ストールで性能が大幅に低下）。
        /// </summary>
        public void ReadbackToStore(Liquid2DParticleStore store)
        {
            if (!_valid || _positions == null || _capacity <= 0 || store == null) return;
            int cap = Mathf.Min(_capacity, store.Capacity);
            if (cap <= 0) return;
            ReadbackResidentToStore(store, cap);
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
            for (int i = 3; (long)i * i <= n; i += 2) if (n % i == 0) return false;
            return true;
        }

        public void Dispose()
        {
            void R(ComputeBuffer b) => b?.Release();
            R(_positions); R(_predicted); R(_velocities); R(_velNext); R(_densities); R(_colors); R(_colorsNext); R(_lastMix);
            R(_radii); R(_invMass); R(_typeId); R(_groupId);
            R(_active); R(_bucketOf); R(_sortedSlots); R(_killFlags); R(_counts); R(_cellStart); R(_cursor);
            R(_materials); R(_mixDatas); R(_colliders); R(_points); R(_impulseX); R(_impulseY); R(_forceFields);
            R(_deadZones); R(_deadZonePoints);
            R(_upSlots); R(_upPos); R(_upVel); R(_upColor);
            _positions = null; _active = null;
            _capacity = 0; _lastCount = 0; _needFullReupload = false;
        }
    }
}

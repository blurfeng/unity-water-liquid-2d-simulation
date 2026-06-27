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
        #region PropertyToID

        private static readonly int _numDeadZones = Shader.PropertyToID("numDeadZones");
        private static readonly int _dt = Shader.PropertyToID("dt");
        private static readonly int _time = Shader.PropertyToID("time");
        private static readonly int _colorMixMode = Shader.PropertyToID("colorMixMode");
        private static readonly int _sortPositions = Shader.PropertyToID("SortPositions");
        private static readonly int _accumulateImpulse = Shader.PropertyToID("accumulateImpulse");
        private static readonly int _numUploads = Shader.PropertyToID("numUploads");
        private static readonly int _uploadSlots = Shader.PropertyToID("UploadSlots");
        private static readonly int _uploadVel = Shader.PropertyToID("UploadVel");
        private static readonly int _positions1 = Shader.PropertyToID("Positions");
        private static readonly int _velocities1 = Shader.PropertyToID("Velocities");
        private static readonly int _next = Shader.PropertyToID("ColorsNext");
        private static readonly int _densities1 = Shader.PropertyToID("Densities");
        private static readonly int _uploadPos = Shader.PropertyToID("UploadPos");
        private static readonly int _uploadColor = Shader.PropertyToID("UploadColor");
        private static readonly int _predicted1 = Shader.PropertyToID("Predicted");
        private static readonly int _colors1 = Shader.PropertyToID("Colors");
        private static readonly int _lastMixTime = Shader.PropertyToID("LastMixTime");
        private static readonly int _numParticles = Shader.PropertyToID("numParticles");
        private static readonly int _tableSize = Shader.PropertyToID("tableSize");
        private static readonly int _numColliders = Shader.PropertyToID("numColliders");
        private static readonly int _numBodies = Shader.PropertyToID("numBodies");
        private static readonly int _numForceFields = Shader.PropertyToID("numForceFields");
        private static readonly int _h = Shader.PropertyToID("h");
        private static readonly int _invCellSize = Shader.PropertyToID("invCellSize");
        private static readonly int _targetDensity = Shader.PropertyToID("targetDensity");
        private static readonly int _pressureMultiplier = Shader.PropertyToID("pressureMultiplier");
        private static readonly int _nearPressureMultiplier = Shader.PropertyToID("nearPressureMultiplier");
        private static readonly int _viscosityStrength = Shader.PropertyToID("viscosityStrength");
        private static readonly int _collisionDamping = Shader.PropertyToID("collisionDamping");
        private static readonly int _maxSpeed = Shader.PropertyToID("maxSpeed");
        private static readonly int _predictionFactor = Shader.PropertyToID("predictionFactor");
        private static readonly int _gravity = Shader.PropertyToID("gravity");
        private static readonly int _impulseScale = Shader.PropertyToID("impulseScale");

        #endregion
        
        public Liquid2DSimulationMode Mode => Liquid2DSimulationMode.Gpu;

        // 渲染层取数计数归零（排空时由 Liquid2DSimulation 提前返回路径调用，避免残影渲染上一批）。 // Zero the render-facing count (called on the drained early-return path to avoid ghost-rendering). // 描画用カウントを 0 に。
        public void ResetRenderCount() => _lastCount = 0;

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
        private ComputeBuffer _materials, _mixDatas, _colliders, _points, _forceFields;
        // 动态体接触累积（合并为单个缓冲，规避 D3D11 每 kernel 8 UAV 上限）：每体 _accumStride 个 int 通道，通道定义见 _accum* 常量与 shader ACCUM_*。
        // Per-body contact accumulation merged into one buffer (works around D3D11's 8-UAV-per-kernel limit): _accumStride int channels per body.
        // 動的体の接触累積を単一バッファに統合（D3D11 の 8 UAV 制限回避）。
        private ComputeBuffer _bodyAccum;

        // 合并累积缓冲的通道布局（与 shader 顶部 ACCUM_* 宏一致）。 // Channel layout of the merged buffer (matches shader ACCUM_* macros). // 統合バッファのチャンネル配置。
        private const int _accumStride = 10;
        private const int _accumVelX = 0, _accumVelY = 1, _accumContact = 2, _accumCentX = 3, _accumCentY = 4, _accumDensity = 5, _accumBuoyN = 6, _accumBuoyDensity = 7, _accumBuoyVolume = 8, _accumShellVolume = 9;

        private ComputeBuffer _deadZones, _deadZonePoints;
        // 生成上传（仅动态状态；静态属性由 store 整块 SetData）。 // spawn upload (dynamic only). // 生成アップロード。
        private ComputeBuffer _upSlots, _upPos, _upVel, _upColor;

        private int _capacity;       // 当前容量级缓冲容量。 // current capacity-buffer size.
        private bool _needFullReupload;
        private int _lastCount;
        private bool _grewThisStep;  // 本步是否发生扩容（供诊断埋点）。 // Whether a grow happened this step (for diagnostics). // 本ステップで扩容したか。

        // 临时诊断开关：开启后每帧回读颜色，定位首个「凭空变黑」的活动粒子并打印其上下文，然后停打。
        // Temporary diagnostic toggle: when on, reads colours back each frame, logs the first "spontaneously black" active
        // particle with its context, then stops logging. Leave OFF in production (per-frame readback is a sync stall).
        // 一時診断スイッチ：オンで毎フレーム色を回読し、最初の「突然黒くなった」粒子の文脈を出力後に停止。
        // 仅编辑器：同步全量回读不应进 release 构建（与本项目「编辑器专用代码 #if UNITY_EDITOR」约定一致）。 // Editor-only: the synchronous full readback must not ship in release builds. // エディタ専用。
#if UNITY_EDITOR
        public static bool DebugDetectBlackParticle;
        private bool _blackReported;
#endif

        // 托管暂存。 // Managed scratch.
        private float2[] _pos, _vel; private float4[] _col; private float[] _lastMixA;
        private int[] _activeA;
        private Liquid2DGpuCollider[] _colA; private Liquid2DGpuMixData[] _mixA; private float2[] _pointsA;
        private Liquid2DGpuForceField[] _ffA;
        private Liquid2DGpuDeadZone[] _dzA; private float2[] _dzPointsA; private int[] _killA;
        private int[] _accumA;
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
            int count = ctx.ActiveCount;
            var store = ctx.Store;
            if (store == null) return;

            _grewThisStep = false;
            HandleCapacity(ctx, store);

            // 处理生成（含扩容后的全量重传），即使本帧 active 为 0 也要写入。 // Handle spawns (and full re-upload after grow). // 生成処理。
            DispatchSpawns(ctx, store, count);

            if (count <= 0 || dt <= 0f) { _lastCount = 0; return; }

            int numTypes = Mathf.Max(1, ctx.Materials.IsCreated ? ctx.Materials.Length : 1);
            int numColliders = ctx.Colliders.IsCreated ? ctx.Colliders.Count : 0;
            int numPoints = ctx.Colliders.IsCreated ? Mathf.Max(1, ctx.Colliders.Points.Length) : 1;
            int numBodies = Mathf.Max(1, ctx.DynamicBodyCount);
            int numForceFields = ctx.ForceFields.Count;
            int numDeadZones = ctx.DeadZoneCount;
            int numDeadZonePoints = numDeadZones > 0 && ctx.DeadZones.Points.IsCreated ? Mathf.Max(1, ctx.DeadZones.Points.Length) : 1;
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
            _cs.SetInt(_numDeadZones, numDeadZones);

            int substeps = math.max(1, p.Substeps);
            float subDt = dt / substeps;
            _cs.SetFloat(_dt, subDt);
            _cs.SetFloat(_time, ctx.Time);
            _cs.SetInt(_colorMixMode, ctx.MixMode);

            int gP = Groups(count);
            int gT = Groups(tableSize);

            for (int step = 0; step < substeps; step++)
            {
                bool lastStep = step == substeps - 1;

                _cs.Dispatch(_kExternal, gP, 1, 1);

                _cs.SetBuffer(_kCount, _sortPositions, _predicted);
                SortPass(gP, gT);

                _cs.Dispatch(_kDensity, gP, 1, 1);
                _cs.Dispatch(_kPressure, gP, 1, 1);
                _cs.Dispatch(_kViscosity, gP, 1, 1);
                _cs.Dispatch(_kCopyVel, gP, 1, 1);

                if (lastStep) _cs.Dispatch(_kClearImpulse, Groups(numBodies), 1, 1);
                _cs.SetInt(_accumulateImpulse, lastStep ? 1 : 0);
                _cs.Dispatch(_kIntegrate, gP, 1, 1);

                if (lastStep)
                {
                    _cs.SetBuffer(_kCount, _sortPositions, _positions);
                    SortPass(gP, gT);
                    _cs.Dispatch(_kMix, gP, 1, 1);
                    _cs.Dispatch(_kCopyColor, gP, 1, 1);

                    // 销毁区域标记（最终位置）：求解后回读 KillFlags 由 CPU 回收 slot。
                    // Dead-zone marking (final positions); KillFlags is read back so the CPU recycles slots after solve.
                    // 破棄領域マーキング（最終位置）。求解後 KillFlags を回読し CPU が slot を回収。
                    if (numDeadZones > 0) _cs.Dispatch(_kDeadZone, gP, 1, 1);
                }
            }

            // 仅有动态碰撞体时回读接触采样（双向耦合）；否则不产生 CPU 同步点。 // Read contact samples only when dynamic bodies exist. // 動的体がある時のみ回読。
            if (ctx.DynamicBodyCount > 0) ReadbackImpulse(ctx, numBodies);

            // 有销毁区域时回读销毁标记（小数组，count 字节级）。 // Read back kill flags when dead zones exist (small array). // 破棄領域がある時のみ回読。
            if (numDeadZones > 0) ReadbackKillFlags(ctx, count);

#if UNITY_EDITOR
            if (DebugDetectBlackParticle) DebugScanBlack(ctx, count);
#endif

            _lastCount = count;
        }

        // 临时诊断：定位首个「凭空变黑」的活动粒子。回读 GPU 颜色，找到 RGB≈0 且 alpha 可见的活动 slot 即打印其
        // slot/typeId/颜色/本步是否扩容，然后停止（避免刷屏）。⚠ 每帧 GPU→CPU 回读，仅排障时开启。
        // Diagnostic: find the first "spontaneously black" active particle. Reads GPU colours, logs the first active slot whose
        // RGB≈0 with visible alpha (slot/typeId/colour/grew-this-step), then stops. ⚠ Per-frame readback; debugging only.
        // 診断：突然黒くなった活動粒子を特定し、文脈を出力後に停止。⚠ 毎フレーム回読、排障時のみ。
#if UNITY_EDITOR
        private void DebugScanBlack(in Liquid2DSolveContext ctx, int count)
        {
            if (_blackReported || _colors == null || count <= 0 || ctx.Store == null) return;
            EnsureArray(ref _col, _capacity);
            _colors.GetData(_col, 0, 0, _capacity);
            for (int k = 0; k < count; k++)
            {
                int slot = ctx.ActiveIndices[k];
                if (slot < 0 || slot >= _capacity) continue;
                float4 c = _col[slot];
                // 可疑 = 不透明的近黑（混色前的污染源）或近透明（清零后未被覆盖的 slot）。正常粒子 alpha≈1 且非黑。
                // Suspicious = opaque near-black (pre-mix contamination) or near-transparent (post-clear uncovered slot).
                // Legit particles are alpha≈1 and non-black.
                bool blackOpaque = c.x < 0.05f && c.y < 0.05f && c.z < 0.05f && c.w > 0.5f;
                bool nearlyTransparent = c.w < 0.5f;
                if (blackOpaque || nearlyTransparent)
                {
                    int tid = slot < ctx.Store.typeId.Length ? ctx.Store.typeId[slot] : -1;
                    Debug.LogError($"[SphGpuSolver] 异常粒子定位 suspicious particle: slot={slot} k={k} typeId={tid} color={c} " +
                                   $"kind={(blackOpaque ? "blackOpaque" : "transparent")} grewThisStep={_grewThisStep} " +
                                   $"activeCount={count} capacity={_capacity}");
                    _blackReported = true;
                    break;
                }
            }
        }
#endif

        private void SortPass(int gP, int gT)
        {
            _cs.Dispatch(_kClearGrid, gT, 1, 1);
            _cs.Dispatch(_kCount, gP, 1, 1);
            _cs.Dispatch(_kPrefix, 1, 1, 1);
            _cs.Dispatch(_kScatter, gP, 1, 1);
        }

        // 容量级缓冲：扩容时先回读旧缓冲（保状态）再重建并标记全量重传。 // Grow: read back old buffers to preserve state, recreate, mark full re-upload. // 容量バッファ。
        private void HandleCapacity(in Liquid2DSolveContext ctx, Liquid2DParticleStore store)
        {
            int cap = store.Capacity;
            if (cap == _capacity && _positions != null) return;

            _grewThisStep = true;
            // 回读保状态时必须跳过本帧 pending（刚生成、尚未上传到 GPU）的 slot：GPU 上对它们没有有效数据，
            // store 才是权威，若回读覆盖会把刚生成粒子的真实状态清成 GPU 上的陈旧/清零值（即「凭空黑/透明粒子」根因）。
            // The readback must skip this frame's pending slots (just spawned, not yet uploaded to GPU): the GPU has no valid
            // data for them, the store is authoritative. Overwriting would wipe the freshly-spawned state with the GPU's stale/
            // cleared value — the root cause of "black/transparent particles from nowhere".
            // 回読時は本フレームの pending（生成直後・未アップロード）slot をスキップ必須。GPU に有効データが無く store が権威。
            if (_positions != null && _capacity > 0)
                ReadbackResidentToStore(store, _capacity, ctx.GPUPendingSpawns);

            void C(ref ComputeBuffer b, int stride) { b?.Release(); b = new ComputeBuffer(Mathf.Max(1, cap), stride, ComputeBufferType.Structured); }
            C(ref _positions, 8); C(ref _predicted, 8); C(ref _velocities, 8); C(ref _velNext, 8); C(ref _densities, 8);
            C(ref _colors, 16); C(ref _colorsNext, 16); C(ref _lastMix, 4);
            C(ref _radii, 4); C(ref _invMass, 4); C(ref _typeId, 4); C(ref _groupId, 4);
            _capacity = cap;
            _needFullReupload = true;

            // 新建 ComputeBuffer 的显存内容是未定义的。全量重传只会覆盖「活动」slot；若有活动 slot 因某条边界路径
            // 未被 ScatterSpawn/重传覆盖，就会读到未初始化显存——表现为「凭空出现颜色≈黑的粒子」并经混色扩散。
            // 这里把所有逐 slot 缓冲清零，杜绝垃圾值泄漏（随后的全量重传仍会写入活动 slot 的真实数据）。
            // Freshly created ComputeBuffers contain undefined VRAM. The full re-upload only covers "active" slots; if any
            // active slot is missed by ScatterSpawn / re-upload via some edge path, it reads uninitialized memory — surfacing
            // as a near-black particle appearing from nowhere and spreading through colour mixing. Zero every per-slot buffer
            // here to prevent garbage leaking (the full re-upload below still writes the real data for active slots).
            // 新規 ComputeBuffer の VRAM は未定義。全 slot バッファをゼロ初期化し、未カバーの活動 slot がゴミ値（≈黒）を
            // 読むのを防ぎます（活動 slot の実データは後続の全量再アップロードで上書きされます）。
            ClearSlotBuffers(cap);
        }

        // 清零的复用零数组（仅 ClearSlotBuffers 使用）。 // Reused zero arrays (ClearSlotBuffers only). // ゼロ配列（再利用）。
        private float2[] _zero2; private float4[] _zero4; private float[] _zero1; private int[] _zeroI;

        // 把所有逐 slot 容量级缓冲清零，消除新建 ComputeBuffer 的未初始化显存。仅在扩容时调用（低频）。
        // Zero all per-slot capacity buffers to wipe the uninitialized VRAM of freshly created ComputeBuffers. Called only on grow (rare).
        // 全 slot 容量バッファをゼロ化。扩容時のみ呼び出し（低頻度）。
        private void ClearSlotBuffers(int cap)
        {
            EnsureArray(ref _zero2, cap); EnsureArray(ref _zero4, cap); EnsureArray(ref _zero1, cap); EnsureArray(ref _zeroI, cap);
            System.Array.Clear(_zero2, 0, cap); System.Array.Clear(_zero4, 0, cap);
            System.Array.Clear(_zero1, 0, cap); System.Array.Clear(_zeroI, 0, cap);
            _positions.SetData(_zero2, 0, 0, cap); _predicted.SetData(_zero2, 0, 0, cap); _velocities.SetData(_zero2, 0, 0, cap);
            _velNext.SetData(_zero2, 0, 0, cap); _densities.SetData(_zero2, 0, 0, cap);
            _colors.SetData(_zero4, 0, 0, cap); _colorsNext.SetData(_zero4, 0, 0, cap);
            _lastMix.SetData(_zero1, 0, 0, cap); _radii.SetData(_zero1, 0, 0, cap); _invMass.SetData(_zero1, 0, 0, cap);
            _typeId.SetData(_zeroI, 0, 0, cap); _groupId.SetData(_zeroI, 0, 0, cap);
        }

        // pending slot 跳过集合（回读保状态时复用，避免每次扩容分配）。 // Reused pending-slot skip set (avoids alloc per grow). // pending スキップ集合（再利用）。
        private readonly HashSet<int> _pendingSkip = new HashSet<int>();

        // 把常驻 GPU 缓冲回读到 CPU store（扩容保状态/调试全量回读）。skipPending 非空时跳过其中的 slot——
        // 它们是本帧刚生成、尚未上传到 GPU 的粒子，store 才是权威，回读会覆盖成 GPU 陈旧/清零值。
        // Read resident GPU buffers back into the CPU store (grow state-preserve / debug full readback). When skipPending is
        // non-null its slots are skipped — they are just-spawned particles not yet on the GPU, for which the store is
        // authoritative; reading back would overwrite them with the GPU's stale/cleared value.
        // 常駐 GPU バッファを CPU store へ回読。skipPending の slot はスキップ（生成直後・未アップロードで store が権威）。
        private void ReadbackResidentToStore(Liquid2DParticleStore store, int oldCap, List<int> skipPending = null)
        {
            EnsureArray(ref _pos, oldCap); EnsureArray(ref _vel, oldCap); EnsureArray(ref _col, oldCap); EnsureArray(ref _lastMixA, oldCap);
            _positions.GetData(_pos, 0, 0, oldCap);
            _velocities.GetData(_vel, 0, 0, oldCap);
            _colors.GetData(_col, 0, 0, oldCap);
            _lastMix.GetData(_lastMixA, 0, 0, oldCap);

            _pendingSkip.Clear();
            if (skipPending != null)
                for (int i = 0; i < skipPending.Count; i++) _pendingSkip.Add(skipPending[i]);

            for (int slot = 0; slot < oldCap; slot++)
            {
                if (_pendingSkip.Count > 0 && _pendingSkip.Contains(slot)) continue;
                store.positions[slot] = _pos[slot];
                store.velocities[slot] = _vel[slot];
                store.colors[slot] = _col[slot];
                store.lastMixTime[slot] = _lastMixA[slot];
            }
        }

        // 写入新生成粒子（增量=pending 列表）或扩容/首帧后的全部 active（full）。 // Write spawned particles (incremental) or all active after grow (full). // 新規粒子書込み。
        private void DispatchSpawns(in Liquid2DSolveContext ctx, Liquid2DParticleStore store, int count)
        {
            // 仅在有活动粒子时才执行全量重传；扩容恰逢 count==0 的帧不消费 _needFullReupload，留到下一非空帧再传，
            // 避免标志被空帧清掉后活动 slot 一直没被重传。 // Only do the full re-upload when there are active particles; a grow on a count==0 frame defers it instead of clearing the flag with nothing uploaded. // count==0 の帧では消費しない。
            bool full = _needFullReupload && count > 0;
            int n;
            if (full) n = count;
            else { var pending = ctx.GPUPendingSpawns; n = pending?.Count ?? 0; }
            if (n <= 0) return;
            if (full) _needFullReupload = false; // 已确定要派发全量重传，此时才清标志。 // clear only now that the full re-upload is committed. // 派遣確定後にクリア。

            EnsureArray(ref _upSlotsA, n); EnsureArray(ref _upPosA, n); EnsureArray(ref _upVelA, n); EnsureArray(ref _upColorA, n);

            for (int m = 0; m < n; m++)
            {
                int slot = full ? ctx.ActiveIndices[m] : ctx.GPUPendingSpawns[m];
                // 越界用哨兵 -1（ScatterSpawn 顶部 slot<0 直接返回，不写任何缓冲）。绝不能用 0——0 是合法且常被占用的 slot，
                // 会把 0 号真实粒子清成黑/透明。 // Sentinel -1 (ScatterSpawn early-returns on slot<0). Never 0 — slot 0 is a valid, commonly-occupied slot. // 越界は -1（0 は使用禁止）。
                if (slot < 0 || slot >= store.Capacity) { _upSlotsA[m] = -1; _upPosA[m] = default; _upVelA[m] = default; _upColorA[m] = default; continue; }
                _upSlotsA[m] = slot;
                _upPosA[m] = store.positions[slot];
                _upVelA[m] = store.velocities[slot];
                _upColorA[m] = store.colors[slot];
            }

            EnsureUploadBuffers(n);
            _upSlots.SetData(_upSlotsA, 0, 0, n); 
            _upPos.SetData(_upPosA, 0, 0, n);
            _upVel.SetData(_upVelA, 0, 0, n); 
            _upColor.SetData(_upColorA, 0, 0, n);

            // 静态属性（radius/invMass/typeId/groupId）整块上传（slot 索引，与 store 对齐；spawn 后不变，只在有生成时刷新）。
            // Static attributes uploaded whole (slot-indexed, aligned with store; immutable after spawn).
            // 静的属性を一括アップロード。
            UploadStaticAttributes(store, _capacity);

            BindSpawn();
            _cs.SetInt(_numUploads, n);
            _cs.SetFloat(_time, ctx.Time);
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
            _cs.SetBuffer(_kSpawn, _uploadSlots, _upSlots); _cs.SetBuffer(_kSpawn, _uploadPos, _upPos);
            _cs.SetBuffer(_kSpawn, _uploadVel, _upVel); _cs.SetBuffer(_kSpawn, _uploadColor, _upColor);
            _cs.SetBuffer(_kSpawn, _positions1, _positions); _cs.SetBuffer(_kSpawn, _predicted1, _predicted);
            _cs.SetBuffer(_kSpawn, _velocities1, _velocities); _cs.SetBuffer(_kSpawn, _colors1, _colors);
            _cs.SetBuffer(_kSpawn, _next, _colorsNext); _cs.SetBuffer(_kSpawn, _lastMixTime, _lastMix);
            _cs.SetBuffer(_kSpawn, _densities1, _densities);
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
            Ensure(ref _materials, numTypes, 36); Ensure(ref _mixDatas, numTypes, 20);
            Ensure(ref _colliders, Mathf.Max(1, numColliders), 88); Ensure(ref _points, Mathf.Max(1, numPoints), 8);
            Ensure(ref _bodyAccum, numBodies * _accumStride, 4);
            Ensure(ref _forceFields, Mathf.Max(1, numForceFields), 44);
        }
        private void EnsureUploadBuffers(int n)
        {
            Ensure(ref _upSlots, n, 4); Ensure(ref _upPos, n, 8); Ensure(ref _upVel, n, 8); Ensure(ref _upColor, n, 16);
        }

        private void UploadActiveIndices(in Liquid2DSolveContext ctx, int count)
        {
            EnsureArray(ref _activeA, count);
            var ai = ctx.ActiveIndices;
            for (int k = 0; k < count; k++) _activeA[k] = ai[k];
            _active.SetData(_activeA, 0, 0, count);
        }

        private void UploadAux(in Liquid2DSolveContext ctx, int numTypes, int numColliders, int numPoints, int numForceFields)
        {
            if (ctx.Materials is { IsCreated: true, Length: > 0 })
                _materials.SetData(ctx.Materials, 0, 0, ctx.Materials.Length);

            EnsureArray(ref _mixA, numTypes);
            int mixN = ctx.MixData.IsCreated ? ctx.MixData.Length : 0;
            for (int i = 0; i < numTypes; i++) _mixA[i] = Liquid2DGpuMixData.From(i < mixN ? ctx.MixData[i] : Liquid2DMixData.Disabled);
            _mixDatas.SetData(_mixA, 0, 0, numTypes);

            if (numColliders > 0)
            {
                EnsureArray(ref _colA, numColliders);
                for (int i = 0; i < numColliders; i++) _colA[i] = Liquid2DGpuCollider.From(ctx.Colliders.Colliders[i]);
                _colliders.SetData(_colA, 0, 0, numColliders);
            }

            EnsureArray(ref _pointsA, numPoints);
            int pN = ctx.Colliders.IsCreated ? ctx.Colliders.Points.Length : 0;
            for (int i = 0; i < numPoints; i++) _pointsA[i] = i < pN ? ctx.Colliders.Points[i] : float2.zero;
            _points.SetData(_pointsA, 0, 0, numPoints);

            if (numForceFields > 0)
            {
                EnsureArray(ref _ffA, numForceFields);
                for (int i = 0; i < numForceFields; i++) _ffA[i] = Liquid2DGpuForceField.From(ctx.ForceFields.Fields[i]);
                _forceFields.SetData(_ffA, 0, 0, numForceFields);
            }
        }

        private void UploadDeadZones(in Liquid2DSolveContext ctx, int numDeadZones, int numDeadZonePoints)
        {
            if (numDeadZones > 0)
            {
                EnsureArray(ref _dzA, numDeadZones);
                for (int i = 0; i < numDeadZones; i++) _dzA[i] = Liquid2DGpuDeadZone.From(ctx.DeadZones.Zones[i]);
                _deadZones.SetData(_dzA, 0, 0, numDeadZones);
            }

            EnsureArray(ref _dzPointsA, numDeadZonePoints);
            int pN = numDeadZones > 0 && ctx.DeadZones.Points.IsCreated ? ctx.DeadZones.Points.Length : 0;
            for (int i = 0; i < numDeadZonePoints; i++) _dzPointsA[i] = i < pN ? ctx.DeadZones.Points[i] : float2.zero;
            _deadZonePoints.SetData(_dzPointsA, 0, 0, numDeadZonePoints);
        }

        private void BindAll()
        {
            Bind("Positions", _positions); Bind("Predicted", _predicted); Bind("Velocities", _velocities);
            Bind("VelNext", _velNext); Bind("Densities", _densities); Bind("Colors", _colors);
            Bind("ColorsNext", _colorsNext); Bind("LastMixTime", _lastMix); Bind("Radii", _radii);
            Bind("InvMass", _invMass); Bind("TypeId", _typeId); Bind("GroupId", _groupId);
            Bind("ActiveIndices", _active); Bind("Materials", _materials); Bind("MixDatas", _mixDatas);
            Bind("Colliders", _colliders); Bind("ColliderPoints", _points);
            Bind("BodyAccum", _bodyAccum);
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
            _cs.SetInt(_numParticles, count);
            _cs.SetInt(_tableSize, tableSize);
            _cs.SetInt(_numColliders, numColliders);
            _cs.SetInt(_numBodies, numBodies);
            _cs.SetInt(_numForceFields, numForceFields);
            _cs.SetFloat(_h, p.H);
            _cs.SetFloat(_invCellSize, 1f / math.max(1e-4f, p.H));
            _cs.SetFloat(_targetDensity, p.TargetDensity);
            _cs.SetFloat(_pressureMultiplier, p.PressureMultiplier);
            _cs.SetFloat(_nearPressureMultiplier, p.NearPressureMultiplier);
            _cs.SetFloat(_viscosityStrength, p.ViscosityStrength);
            _cs.SetFloat(_collisionDamping, p.CollisionDamping);
            _cs.SetFloat(_maxSpeed, p.MaxSpeed);
            _cs.SetFloat(_predictionFactor, p.PredictionFactor);
            _cs.SetVector(_gravity, new Vector4(p.Gravity.x, p.Gravity.y, 0f, 0f));
            _cs.SetFloat(_impulseScale, ImpulseScale);
        }

        private void ReadbackImpulse(in Liquid2DSolveContext ctx, int numBodies)
        {
            // 一次性回读合并的接触累积缓冲（每体 _accumStride 个 int 通道），按通道去定点化后分发到 velSum/contact/buoyancy。
            // Read back the merged accumulation buffer once (_accumStride int channels per body), de-fixed-point and split into velSum/contact/buoyancy.
            // 統合累積バッファを一括回読し、通道ごとに分配。
            if (_bodyAccum == null) return;
            int n = numBodies * _accumStride;
            EnsureArray(ref _accumA, n);
            _bodyAccum.GetData(_accumA, 0, 0, n);

            var velSumOut = ctx.ColliderVelSum;
            var contactOut = ctx.ColliderContact;
            var buoyOut = ctx.ColliderBuoyancy;
            for (int b = 0; b < numBodies; b++)
            {
                int ab = b * _accumStride;
                // 入射流体速度之和（相对速度阻力用）。 // Sum of incoming fluid velocities (relative-velocity drag). // 入射流速の和。
                if (velSumOut.IsCreated && b < velSumOut.Length)
                    velSumOut[b] += new float2(_accumA[ab + _accumVelX] / ImpulseScale, _accumA[ab + _accumVelY] / ImpulseScale);
                // 全向接触：xy=接触位置之和，z=接触数，w=接触流体密度之和。 // All-direction contact: xy=sum pos, z=count, w=sum fluid density. // 全方向接触。
                if (contactOut.IsCreated && b < contactOut.Length)
                    contactOut[b] += new float4(_accumA[ab + _accumCentX] / ImpulseScale, _accumA[ab + _accumCentY] / ImpulseScale,
                        _accumA[ab + _accumContact], _accumA[ab + _accumDensity] / ImpulseScale);
                // 浮力 + 壳层覆盖：x=浮力接触数，y=浮力接触流体密度之和，z=浮力排开体积之和（Σ4r²，内部覆盖），w=壳层覆盖体积之和（Σ4r²，drag/阻尼缩放用）。
                // Buoyancy + shell coverage: x=count, y=sum density, z=Σ4r² (interior, buoyancy), w=Σ4r² (shell, drag/damping scale).
                // 浮力 + 殻層被覆：x=接触数、y=密度和、z=内部排除体積、w=殻層被覆体積。
                if (buoyOut.IsCreated && b < buoyOut.Length)
                    buoyOut[b] += new float4(_accumA[ab + _accumBuoyN], _accumA[ab + _accumBuoyDensity] / ImpulseScale,
                        _accumA[ab + _accumBuoyVolume] / ImpulseScale, _accumA[ab + _accumShellVolume] / ImpulseScale);
            }
        }

        // 回读销毁标记到 ctx.killFlags（按 k 索引，1=该活动粒子待回收）。 // Read kill flags back into ctx.killFlags (indexed by k). // 破棄フラグを回読。
        private void ReadbackKillFlags(in Liquid2DSolveContext ctx, int count)
        {
            var flagsOut = ctx.KillFlags;
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
            R(_materials); R(_mixDatas); R(_colliders); R(_points); R(_forceFields);
            R(_bodyAccum);
            R(_deadZones); R(_deadZonePoints);
            R(_upSlots); R(_upPos); R(_upVel); R(_upColor);
            _positions = null; _active = null;
            _capacity = 0; _lastCount = 0; _needFullReupload = false;
        }
    }
}

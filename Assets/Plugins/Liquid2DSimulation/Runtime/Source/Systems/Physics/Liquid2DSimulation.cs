using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D 流体模拟中枢（运行时单例）。取代旧 Liquid2DParticleManager：拥有粒子 SoA 存储、SPH 求解器、碰撞体缓冲、
    /// 描述符/材质表，在 FixedUpdate 驱动求解，并对外提供生成/销毁/查询 API。渲染层（Liquid2DPass）从这里取数据绕过 Transform。
    /// 2D fluid simulation hub (runtime singleton). Replaces the old Liquid2DParticleManager: owns the particle SoA store,
    /// SPH solver, collider buffer, descriptor/material tables; drives solving in FixedUpdate and exposes spawn/despawn/query
    /// APIs. The render layer (Liquid2DPass) reads data here, bypassing Transform.
    /// 2D 流体シミュレーションハブ（ランタイムシングルトン）。旧 Liquid2DParticleManager を置き換え。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class Liquid2DSimulation : MonoBehaviour
    {
        private static Liquid2DSimulation _instance;
        private static bool _isQuitting;

        /// <summary>
        /// 运行时单例（懒创建隐藏 GameObject 承载）。编辑模式/退出时返回 null。
        /// Runtime singleton (lazily creates a hidden GameObject). Returns null in edit mode / on quit.
        /// ランタイムシングルトン（非表示 GameObject を遅延生成）。
        /// </summary>
        public static Liquid2DSimulation Instance
        {
            get
            {
                if (_instance || _isQuitting || !Application.isPlaying) return _instance;
                var go = new GameObject("[Liquid2DSimulation]") { hideFlags = HideFlags.HideAndDontSave };
                _instance = go.AddComponent<Liquid2DSimulation>();
                return _instance;
            }
        }

        /// <summary>每个 nameTag 组的最大存活粒子数（&lt;=0 不限）。超出回收最旧。 // Max alive particles per nameTag group (&lt;=0 = unlimited). // nameTag グループごとの最大生存数。</summary>
        public static int MaxParticlesPerTag { get; set; }

        /// <summary>全局求解参数（由 Liquid2DPhysicsConfig 配置）。 // Global solver params (configured by Liquid2DPhysicsConfig). // グローバル解法パラメータ。</summary>
        public static SolverParams Params = SolverParams.Default;

        /// <summary>计算平台模式（GPU 预留）。 // Compute mode (GPU reserved). // 計算モード。</summary>
        public static Liquid2DSimulationMode Mode = Liquid2DSimulationMode.Cpu;

        /// <summary>
        /// GPU 模式下每帧把 GPU 数据全量回读到 CPU store。仅为让依赖 CPU store 的功能（Liquid2DDebugGizmos、
        /// GetPosition/GetVelocity 查询）在 GPU 模式下可用。⚠ 这是同步 GPU→CPU 阻塞点，会严重降低性能，默认关闭。
        /// Full per-frame GPU→CPU readback into the store in GPU mode, only so CPU-store consumers (Liquid2DDebugGizmos,
        /// GetPosition/GetVelocity) work under GPU mode. ⚠ Synchronous stall, severely hurts performance; off by default.
        /// GPU モードで毎フレーム GPU→CPU 全量回読（CPU store 依存機能の互換用）。⚠ 同期ストールで性能大幅低下、既定はオフ。
        /// </summary>
        public static bool GpuReadbackToStore = false;

        private Liquid2DParticleStore _store;
        private ILiquid2DSolver _solver;

        // 描述符表（按 typeId 索引）。 // Descriptor table (indexed by typeId). // 記述子表。
        private readonly List<Liquid2DParticleDescriptor> _descriptors = new List<Liquid2DParticleDescriptor>();
        private NativeArray<Liquid2DMaterialData> _materials;
        private NativeArray<Liquid2DMixData> _mixData;
        private bool _materialsDirty;

        // nameTag → groupId（0 为空标签通配）。 // nameTag → groupId (0 = empty-tag wildcard). // nameTag → groupId。
        private readonly Dictionary<string, int> _nameTagToGroup = new Dictionary<string, int>();
        private readonly Dictionary<int, List<int>> _groupSlots = new Dictionary<int, List<int>>();

        private NativeArray<int> _activeIndices;
        private int _activeCount;
        private bool _activeDirty;

        private readonly List<ILiquid2DForceReceiver> _dynamicReceivers = new List<ILiquid2DForceReceiver>();

        // GPU 模式：自上次 Step 以来新生成的粒子 slot（供 GPU 增量上传到常驻缓冲）。 // GPU mode: slots spawned since last Step. // GPU 増分アップロード用。
        private readonly List<int> _gpuPendingSpawns = new List<int>();

        public Liquid2DParticleStore Store => _store;
        public IReadOnlyList<Liquid2DParticleDescriptor> Descriptors => _descriptors;

        private void Awake()
        {
            _store = new Liquid2DParticleStore(2048);
            _activeIndices = new NativeArray<int>(_store.Capacity, Allocator.Persistent);
            CreateSolver();
            _nameTagToGroup[string.Empty] = 0;
        }

        // 上次创建求解器所依据的目标模式（用于运行时切换检测）。 // Desired mode the solver was last created for (runtime-switch detection). // ソルバー作成時の目標モード。
        private Liquid2DSimulationMode _createdMode = Liquid2DSimulationMode.Cpu;

        private void CreateSolver()
        {
            _solver?.Dispose();
            _createdMode = Mode;

            if (Mode == Liquid2DSimulationMode.Gpu)
            {
                var cs = Resources.Load<ComputeShader>("Liquid2DSph");
                if (cs != null && SystemInfo.supportsComputeShaders)
                {
                    _solver = new SphGpuSolver(cs);
                    return;
                }
                // 计算着色器缺失或硬件不支持，回退 CPU。 // Compute shader missing or unsupported; fall back to CPU. // CS 不在/未対応のため CPU へ。
                Debug.LogWarning("[Liquid2DSimulation] GPU mode unavailable (compute shader missing or unsupported); falling back to CPU.");
            }
            _solver = new SphCpuSolver();
        }

        #region Descriptor / group 描述符与分组 // 記述子とグループ

        /// <summary>
        /// 注册描述符并返回其 typeId（已注册则直接返回）。
        /// Register a descriptor and return its typeId (returns existing if already registered).
        /// 記述子を登録し typeId を返す。
        /// </summary>
        public int RegisterDescriptor(Liquid2DParticleDescriptor d)
        {
            if (!d) return -1;
            if (d.RuntimeTypeId >= 0 && d.RuntimeTypeId < _descriptors.Count && _descriptors[d.RuntimeTypeId] == d)
                return d.RuntimeTypeId;

            int id = _descriptors.Count;
            d.RuntimeTypeId = id;
            _descriptors.Add(d);
            _materialsDirty = true;
            return id;
        }

        private int GetGroup(string nameTag)
        {
            string key = string.IsNullOrEmpty(nameTag) ? string.Empty : nameTag;
            if (_nameTagToGroup.TryGetValue(key, out int g)) return g;
            g = _nameTagToGroup.Count; // 0 已被空标签占用。 // 0 is taken by the empty tag. // 0 は空タグ。
            _nameTagToGroup[key] = g;
            return g;
        }

        private void EnsureMaterials()
        {
            if (!_materialsDirty && _materials.IsCreated) return;
            int n = math.max(1, _descriptors.Count);
            if (_materials.IsCreated) _materials.Dispose();
            if (_mixData.IsCreated) _mixData.Dispose();
            _materials = new NativeArray<Liquid2DMaterialData>(n, Allocator.Persistent);
            _mixData = new NativeArray<Liquid2DMixData>(n, Allocator.Persistent);
            for (int i = 0; i < _descriptors.Count; i++)
            {
                var d = _descriptors[i];
                _materials[i] = d != null && d.Material != null ? d.Material.ToData() : Liquid2DMaterialData.Default;
                _mixData[i] = BuildMix(d != null ? d.MixSettings : null);
            }
            _materialsDirty = false;
        }

        private static Liquid2DMixData BuildMix(Liquid2DParticleMixSettings m)
        {
            if (m == null) return Liquid2DMixData.Disabled;
            return new Liquid2DMixData
            {
                Enabled = (byte)(m.MixColors ? 1 : 0),
                Speed = m.MixColorsSpeed,
                WithMovement = (byte)(m.MixColorsWithMovement ? 1 : 0),
                MaxSpeed = m.MixColorsWithMovementMaxSpeed,
                Interval = m.MixColorsWithContactParticlesInternal,
            };
        }

        #endregion

        #region Spawn / Despawn 生成与销毁 // 生成と破棄

        /// <summary>
        /// 生成一个流体粒子。lifetimeOverride &gt; 0 覆盖描述符默认寿命；&lt;= 0 用描述符默认（0 表示无限）。
        /// Spawn a fluid particle. lifetimeOverride &gt; 0 overrides the descriptor default; &lt;= 0 uses the descriptor default (0 = infinite).
        /// 流体粒子を生成。
        /// </summary>
        public Liquid2DParticleHandle Spawn(Liquid2DParticleDescriptor d, float2 position, float2 velocity,
            float sizeScale = 1f, float lifetimeOverride = -1f)
        {
            if (!d) return Liquid2DParticleHandle.Invalid;

            int typeId = RegisterDescriptor(d);
            int group = GetGroup(d.RenderSettings != null ? d.RenderSettings.NameTag : null);
            float radius = math.max(0.001f, d.Radius * sizeScale);
            float mass = d.Material != null ? d.Material.Mass : 1f;
            Color c = d.RenderSettings != null ? d.RenderSettings.Color : Color.white;
            float now = Time.time;

            float life = lifetimeOverride > 0f ? lifetimeOverride : d.DefaultLifetime;
            float lifeEnd = life > 0f ? now + life : -1f;

            var handle = _store.Allocate(position, velocity, new float4(c.r, c.g, c.b, c.a),
                radius, mass, typeId, group, lifeEnd, now);

            if (!_groupSlots.TryGetValue(group, out var list))
            {
                list = new List<int>();
                _groupSlots[group] = list;
            }
            list.Add(handle.Index);
            _activeDirty = true;

            EnforceCap(list, handle.Index);

            // GPU 常驻模式：记录新 slot，待下次 Step 增量上传。 // GPU resident mode: record new slot for incremental upload. // GPU 常駐：新 slot を記録。
            if (Mode == Liquid2DSimulationMode.Gpu) _gpuPendingSpawns.Add(handle.Index);
            return handle;
        }

        /// <summary>销毁一个粒子。 // Despawn a particle. // 粒子を破棄。</summary>
        public void Despawn(Liquid2DParticleHandle handle)
        {
            if (_store.IsAlive(handle)) FreeSlot(handle.Index);
        }

        private void EnforceCap(List<int> groupList, int justAddedSlot)
        {
            if (MaxParticlesPerTag <= 0) return;
            while (groupList.Count > MaxParticlesPerTag)
            {
                int oldest = groupList[0];
                if (oldest == justAddedSlot) break; // 不回收刚生成的。 // never recycle the just-spawned one. // 生成直後は回収しない。
                FreeSlot(oldest);
            }
        }

        private void FreeSlot(int slot)
        {
            if (slot < 0) return;
            int group = _store.groupId[slot];
            if (_groupSlots.TryGetValue(group, out var list)) list.Remove(slot);
            _store.FreeIndex(slot);
            _activeDirty = true;
        }

        #endregion

        #region Query 查询 // 照会

        /// <summary>查询粒子世界位置（句柄失效返回 zero）。 // Query particle world position. // 粒子の位置を照会。</summary>
        public float2 GetPosition(Liquid2DParticleHandle h) => _store != null ? _store.GetPosition(h) : float2.zero;
        public float2 GetVelocity(Liquid2DParticleHandle h) => _store != null ? _store.GetVelocity(h) : float2.zero;
        public bool IsAlive(Liquid2DParticleHandle h) => _store != null && _store.IsAlive(h);
        public void SetColor(Liquid2DParticleHandle h, Color c) => _store?.SetColor(h, c);

        #endregion

        #region Tick 求解驱动 // 解法駆動

        private void FixedUpdate()
        {
            if (_store == null) return;

            // 运行时切换 CPU/GPU 模式：目标模式变化时重建求解器。 // Runtime CPU/GPU switch: recreate solver when the desired mode changes. // 実行時の CPU/GPU 切替。
            if (_createdMode != Mode || _solver == null) CreateSolver();

            float now = Time.time;

            ExpireLifetimes(now);

            EnsureMaterials();
            EnsureActiveCapacity();
            int count = RebuildActiveIndices();
            if (count == 0) { _gpuPendingSpawns.Clear(); return; }

            if (Params.H <= 0f) Params = SolverParams.Default;

            var colliders = Liquid2DColliderRegistry.BuildBuffer(Allocator.TempJob, _dynamicReceivers, GetGroup);
            int bodyCount = math.max(1, _dynamicReceivers.Count);
            var impulse = new NativeArray<float2>(bodyCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var contact = new NativeArray<float4>(bodyCount, Allocator.TempJob, NativeArrayOptions.ClearMemory);
            var forceFields = Liquid2DForceFieldRegistry.BuildBuffer(Allocator.TempJob, GetGroup);

            // 销毁区域：扁平化为缓冲，并分配按 active 索引的 killFlags（求解器置位，Step 后回收 slot）。
            // Dead zones: flatten to a buffer and allocate active-indexed killFlags (solver sets them, slots recycled after Step).
            // 破棄領域：バッファに平坦化し、active 索引の killFlags を確保（ソルバーが設定、Step 後に slot 回収）。
            var deadZones = Liquid2DDeadZoneRegistry.BuildBuffer(Allocator.TempJob, GetGroup);
            int deadZoneCount = deadZones.Count;
            var killFlags = new NativeArray<byte>(math.max(1, count), Allocator.TempJob, NativeArrayOptions.ClearMemory);

            var ctx = new Liquid2DSolveContext
            {
                Store = _store,
                ActiveIndices = _activeIndices,
                ActiveCount = count,
                Materials = _materials,
                MixData = _mixData,
                Colliders = colliders,
                ColliderImpulse = impulse,
                ColliderContact = contact,
                ForceFields = forceFields,
                DeadZones = deadZones,
                DeadZoneCount = deadZoneCount,
                KillFlags = killFlags,
                Time = now,
                DynamicBodyCount = _dynamicReceivers.Count,
                GPUPendingSpawns = _gpuPendingSpawns,
            };

            // try/finally 确保即使 Step 抛异常也释放 TempJob 缓冲，避免泄漏。 // Ensure TempJob buffers are freed even if Step throws. // 例外時もバッファ解放。
            try
            {
                if (_solver == null) return;
                _solver.Step(ctx, Params, Time.fixedDeltaTime);

                // 增量上传列表本帧已消费，清空。 // Pending spawn list consumed this frame; clear. // 消費したのでクリア。
                _gpuPendingSpawns.Clear();

                // 可选：GPU 模式每帧回读到 CPU store（仅为兼容 Gizmos / 查询，开启会严重降速）。
                // Optional: per-frame GPU→CPU readback in GPU mode (compat for Gizmos/queries only; severe perf hit when on).
                // 任意：GPU モードで毎フレーム回読（Gizmos/クエリ互換用、性能大幅低下）。
                if (GpuReadbackToStore && _solver is SphGpuSolver gpuSolver)
                {
                    gpuSolver.ReadbackToStore(_store);
                    _activeDirty = true;
                }

                DispatchBodyForces(impulse, contact, Time.fixedDeltaTime);

                // 回收落入销毁区域的粒子（killFlags 由 CPU/GPU 求解器置位）。 // Recycle particles inside dead zones (killFlags set by CPU/GPU solver). // 破棄領域内の粒子を回収。
                if (deadZoneCount > 0) ApplyKills(killFlags, count);
            }
            finally
            {
                colliders.Colliders.Dispose();
                colliders.Points.Dispose();
                impulse.Dispose();
                contact.Dispose();
                if (forceFields.Fields.IsCreated) forceFields.Fields.Dispose();
                if (deadZones.Zones.IsCreated) deadZones.Zones.Dispose();
                if (deadZones.Points.IsCreated) deadZones.Points.Dispose();
                if (killFlags.IsCreated) killFlags.Dispose();
            }
        }

        private void ExpireLifetimes(float now)
        {
            int hw = _store.HighWater;
            var alive = _store.alive;
            var lifeEnd = _store.lifetimeEnd;
            for (int i = 0; i < hw; i++)
            {
                if (alive[i] == 0) continue;
                float end = lifeEnd[i];
                if (end > 0f && now >= end) FreeSlot(i);
            }
        }

        private void EnsureActiveCapacity()
        {
            if (_activeIndices.IsCreated && _activeIndices.Length >= _store.Capacity) return;
            if (_activeIndices.IsCreated) _activeIndices.Dispose();
            _activeIndices = new NativeArray<int>(_store.Capacity, Allocator.Persistent);
        }

        private int RebuildActiveIndices()
        {
            int hw = _store.HighWater;
            var alive = _store.alive;
            int c = 0;
            for (int i = 0; i < hw; i++)
                if (alive[i] == 1) _activeIndices[c++] = i;
            _activeCount = c;
            _activeDirty = false;
            return c;
        }

        // 把本帧累积的冲量 + 接触信息打包成 Liquid2DBodyForce 派发给各动态体的力接收者（双向耦合）。
        // Pack this frame's accumulated impulse + contact info into Liquid2DBodyForce and dispatch to each dynamic body's
        // force receiver (two-way coupling).
        // 本フレームの累積力積 + 接触情報を Liquid2DBodyForce にまとめ、各動的体のレシーバーへ派遣（双方向）。
        private void DispatchBodyForces(NativeArray<float2> impulse, NativeArray<float4> contact, float dt)
        {
            for (int b = 0; b < _dynamicReceivers.Count && b < impulse.Length; b++)
            {
                var r = _dynamicReceivers[b];
                if (r == null) continue;

                float2 imp = impulse[b];
                float4 con = b < contact.Length ? contact[b] : float4.zero;
                int contactCount = (int)con.z;
                if (math.lengthsq(imp) <= 1e-8f && contactCount <= 0) continue;

                float2 center = contactCount > 0 ? new float2(con.x, con.y) / contactCount : float2.zero;
                float fluidDensity = contactCount > 0 ? con.w / contactCount : 0f;
                r.ApplyLiquidForces(new Liquid2DBodyForce
                {
                    Impulse = imp,
                    ContactCenter = center,
                    ContactCount = contactCount,
                    FluidDensity = fluidDensity,
                    Dt = dt,
                });
            }
        }

        // 按 killFlags 回收落入销毁区域的粒子。killFlags[k] 对应本步活动索引 _activeIndices[k]。
        // Recycle particles flagged by killFlags. killFlags[k] maps to this step's active index _activeIndices[k].
        // killFlags に従って破棄領域内の粒子を回収。killFlags[k] は本ステップの _activeIndices[k] に対応。
        private void ApplyKills(NativeArray<byte> killFlags, int count)
        {
            if (!killFlags.IsCreated) return;
            for (int k = 0; k < count && k < killFlags.Length; k++)
                if (killFlags[k] != 0) FreeSlot(_activeIndices[k]);
        }

        #endregion

        #region Render data 渲染取数 // 描画用データ

        /// <summary>
        /// 供渲染层取数据。返回当前存活的 store、紧凑活动索引与描述符表（绕过 Transform）。
        /// 渲染时无求解 Job 在飞（FixedUpdate 内已 Complete），可安全读取。
        /// For the render layer. Returns the live store, compact active indices, and descriptor table (bypassing Transform).
        /// At render time no solve jobs are in flight (completed within FixedUpdate), so reads are safe.
        /// 描画層用。生存中の store・コンパクト active 索引・記述子表を返します。
        /// </summary>
        public static bool TryGetRenderData(out Liquid2DParticleStore store, out NativeArray<int> activeIndices,
            out int activeCount, out IReadOnlyList<Liquid2DParticleDescriptor> descriptors)
        {
            store = null; activeIndices = default; activeCount = 0; descriptors = null;
            var inst = _instance;
            if (inst == null || inst._store == null || inst._store.Count == 0) return false;

            if (inst._activeDirty)
            {
                inst.EnsureActiveCapacity();
                inst.RebuildActiveIndices();
            }

            store = inst._store;
            activeIndices = inst._activeIndices;
            activeCount = inst._activeCount;
            descriptors = inst._descriptors;
            return activeCount > 0;
        }

        /// <summary>
        /// GPU 模式：供渲染层直读常驻 GPU 缓冲（DrawProcedural）。仅在 GPU 求解器有效且有粒子时返回 true。
        /// descriptors 用于按 typeId 取贴图/材质/renderScale/nameTag；buffers 为 slot 索引，需配合 activeIndices 间接寻址。
        /// GPU mode: lets the render layer read resident GPU buffers directly (DrawProcedural). Returns true only when the
        /// GPU solver is active and has particles. Buffers are slot-indexed; use activeIndices to indirect.
        /// GPU モード：常駐 GPU バッファを直読するため。
        /// </summary>
        public static bool TryGetRenderBuffers(out ComputeBuffer positions, out ComputeBuffer colors,
            out ComputeBuffer radii, out ComputeBuffer typeIds, out ComputeBuffer activeIndices,
            out ComputeBuffer velocities, out int count, out IReadOnlyList<Liquid2DParticleDescriptor> descriptors)
        {
            positions = colors = radii = typeIds = activeIndices = velocities = null; count = 0; descriptors = null;
            var inst = _instance;
            if (inst == null) return false;
            descriptors = inst._descriptors;
            if (inst._solver is SphGpuSolver gpu)
                return gpu.TryGetRenderBuffers(out positions, out colors, out radii, out typeIds, out activeIndices, out velocities, out count);
            return false;
        }

        #endregion

        private void OnApplicationQuit() => _isQuitting = true;

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
            _solver?.Dispose();
            _store?.Dispose();
            if (_activeIndices.IsCreated) _activeIndices.Dispose();
            if (_materials.IsCreated) _materials.Dispose();
            if (_mixData.IsCreated) _mixData.Dispose();
        }

#if UNITY_EDITOR
        // 编辑器中「Play 模式下重新编译脚本」会触发域重载（domain reload）。本单例是运行时创建的
        // HideAndDontSave 对象，域重载时不会走 OnDestroy，导致 SphGpuSolver 的 ComputeBuffer / NativeArray 泄漏。
        // 在域重载前主动 DestroyImmediate 单例，触发 OnDestroy 完成释放。仅编辑器需要，构建中不存在域重载。
        // Recompiling scripts while in Play mode triggers a domain reload in the editor. This runtime-created
        // HideAndDontSave singleton does not get OnDestroy on a domain reload, leaking SphGpuSolver's ComputeBuffer /
        // NativeArray. Destroy the singleton before the reload so OnDestroy runs and releases everything.
        // Play モード中の再コンパイルはドメインリロードを起こし、HideAndDontSave のランタイム単例は OnDestroy が
        // 呼ばれず ComputeBuffer / NativeArray が漏れます。リロード前に破棄して解放します。
        [UnityEditor.InitializeOnLoadMethod]
        private static void RegisterDomainReloadCleanup()
        {
            UnityEditor.AssemblyReloadEvents.beforeAssemblyReload += () =>
            {
                if (_instance) DestroyImmediate(_instance.gameObject);
            };
        }
#endif
    }
}

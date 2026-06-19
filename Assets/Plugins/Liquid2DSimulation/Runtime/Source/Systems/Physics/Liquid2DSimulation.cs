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
        public static int MaxParticlesPerTag { get; set; } = 0;

        /// <summary>全局求解参数（由 Liquid2DPhysicsConfig 配置）。 // Global solver params (configured by Liquid2DPhysicsConfig). // グローバル解法パラメータ。</summary>
        public static SolverParams Params = SolverParams.Default;

        /// <summary>计算平台模式（GPU 预留）。 // Compute mode (GPU reserved). // 計算モード。</summary>
        public static Liquid2DSimulationMode Mode = Liquid2DSimulationMode.Cpu;

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

        public Liquid2DParticleStore Store => _store;
        public IReadOnlyList<Liquid2DParticleDescriptor> Descriptors => _descriptors;

        private void Awake()
        {
            _store = new Liquid2DParticleStore(2048);
            _activeIndices = new NativeArray<int>(_store.Capacity, Allocator.Persistent);
            CreateSolver();
            _nameTagToGroup[string.Empty] = 0;
        }

        private void CreateSolver()
        {
            _solver?.Dispose();
            if (Mode == Liquid2DSimulationMode.Gpu)
            {
                // GPU SPH 求解器将在 Phase 2 接入；当前回退到 CPU SPH。 // GPU SPH solver lands in Phase 2; fall back to CPU SPH for now. // GPU は Phase 2 で接続、当面は CPU にフォールバック。
                Debug.LogWarning("[Liquid2DSimulation] GPU mode is not implemented yet; falling back to CPU.");
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
                _materials[i] = d != null && d.material != null ? d.material.ToData() : Liquid2DMaterialData.Default;
                _mixData[i] = BuildMix(d != null ? d.mixSettings : null);
            }
            _materialsDirty = false;
        }

        private static Liquid2DMixData BuildMix(Liquid2DParticleMixSettings m)
        {
            if (m == null) return Liquid2DMixData.Disabled;
            return new Liquid2DMixData
            {
                enabled = (byte)(m.mixColors ? 1 : 0),
                speed = m.mixColorsSpeed,
                withMovement = (byte)(m.mixColorsWithMovement ? 1 : 0),
                maxSpeed = m.mixColorsWithMovementMaxSpeed,
                interval = m.mixColorsWithContactParticlesInternal,
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
            int group = GetGroup(d.renderSettings != null ? d.renderSettings.nameTag : null);
            float radius = math.max(0.001f, d.radius * sizeScale);
            float mass = d.material != null ? d.material.mass : 1f;
            Color c = d.renderSettings != null ? d.renderSettings.color : Color.white;
            float now = Time.time;

            float life = lifetimeOverride > 0f ? lifetimeOverride : d.defaultLifetime;
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
            float now = Time.time;

            ExpireLifetimes(now);

            EnsureMaterials();
            EnsureActiveCapacity();
            int count = RebuildActiveIndices();
            if (count == 0) return;

            if (Params.h <= 0f) Params = SolverParams.Default;

            var colliders = Liquid2DColliderRegistry.BuildBuffer(Allocator.TempJob, _dynamicReceivers);
            var impulse = new NativeArray<float2>(math.max(1, _dynamicReceivers.Count), Allocator.TempJob, NativeArrayOptions.ClearMemory);

            var ctx = new Liquid2DSolveContext
            {
                store = _store,
                activeIndices = _activeIndices,
                activeCount = count,
                materials = _materials,
                mixData = _mixData,
                colliders = colliders,
                colliderImpulse = impulse,
                time = now,
            };

            _solver.Step(ctx, Params, Time.fixedDeltaTime);

            DispatchImpulses(impulse);

            colliders.colliders.Dispose();
            colliders.points.Dispose();
            impulse.Dispose();
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

        private void DispatchImpulses(NativeArray<float2> impulse)
        {
            for (int b = 0; b < _dynamicReceivers.Count && b < impulse.Length; b++)
            {
                var r = _dynamicReceivers[b];
                if (r == null) continue;
                float2 imp = impulse[b];
                if (math.lengthsq(imp) > 1e-8f) r.ApplyLiquidImpulse(imp);
            }
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
    }
}

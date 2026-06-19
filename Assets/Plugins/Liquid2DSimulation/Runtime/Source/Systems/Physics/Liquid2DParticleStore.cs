using System;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体粒子 SoA（Structure of Arrays）存储。所有逐粒子数据以并列 NativeArray 保存，供 Burst Job 直接读写。
    /// slot 稳定：句柄的 Index 永远等于其 slot 下标，回收时仅置 alive=0 并自增 version，便于句柄失效检测；
    /// 求解时由外部（Liquid2DSimulation）每步重建紧凑的 active 索引表跳过空洞 slot。
    /// Fluid particle SoA (Structure of Arrays) store. All per-particle data lives in parallel NativeArrays for direct
    /// Burst Job read/write. Slots are stable: a handle's Index always equals its slot index; freeing only sets alive=0
    /// and bumps the version (for staleness detection). Each step an external compact active-index list (built by
    /// Liquid2DSimulation) skips holes.
    /// 流体パーティクル SoA ストア。すべての粒子データを並列 NativeArray に保持し、Burst Job が直接読み書きします。
    /// スロットは安定：ハンドルの Index は常にスロット添字と一致し、解放時は alive=0 とバージョン加算のみ。
    /// 解法時は外部（Liquid2DSimulation）が毎ステップ、穴を飛ばすコンパクトな active インデックス表を再構築します。
    /// </summary>
    public sealed class Liquid2DParticleStore : IDisposable
    {
        // 逐粒子数据（SoA）。internal 以便同程序集的求解器/Job 直接访问。
        // Per-particle data (SoA). internal so the same-assembly solver/jobs can access directly.
        // 粒子データ（SoA）。同アセンブリのソルバー/Job が直接アクセスできるよう internal。
        internal NativeArray<float2> positions;
        internal NativeArray<float2> velocities;
        internal NativeArray<float2> predicted;
        internal NativeArray<float4> colors;     // 当前颜色（混色读）。 // current color (mix reads). // 現在色（混色読み）。
        internal NativeArray<float4> colorsNext;  // 混色双缓冲（混色写）。 // mix double-buffer (mix writes). // 混色ダブルバッファ（混色書き）。
        internal NativeArray<float> radii;
        internal NativeArray<float> invMass;
        internal NativeArray<int> typeId;        // → 描述符/材质表。 // → descriptor/material table. // → 記述子/マテリアル表。
        internal NativeArray<int> groupId;       // nameTag → int。
        internal NativeArray<float> lifetimeEnd;  // 绝对销毁时间（Time.time）；<=0 表示无限。 // absolute destroy time; <=0 = infinite. // 絶対破棄時刻；<=0 は無限。
        internal NativeArray<float> lastMixTime;
        internal NativeArray<int> versions;
        internal NativeArray<byte> alive;        // 1=存活，0=空闲。 // 1=alive, 0=free. // 1=生存, 0=空き。

        private NativeList<int> _freeIndices;     // 高水位线以下的空闲 slot 栈。 // free-slot stack below the high-water mark. // 高水位線以下の空きスロットスタック。
        private int _capacity;
        private int _highWater;                   // 曾经被触碰过的 slot 数（[0,_highWater) 可能存活或空闲）。 // slots ever touched. // 触れたスロット数。
        private int _aliveCount;

        /// <summary>
        /// 当前存活粒子数。
        /// Current number of alive particles.
        /// 現在の生存粒子数。
        /// </summary>
        public int Count => _aliveCount;

        /// <summary>
        /// 已触碰 slot 的上界，遍历活动粒子时只需扫描 [0, HighWater)。
        /// Upper bound of touched slots; iterating active particles only needs to scan [0, HighWater).
        /// 触れたスロットの上限。アクティブ粒子の走査は [0, HighWater) のみで十分。
        /// </summary>
        public int HighWater => _highWater;

        /// <summary>
        /// 容量（slot 数）。
        /// Capacity (slot count).
        /// 容量（スロット数）。
        /// </summary>
        public int Capacity => _capacity;

        public Liquid2DParticleStore(int initialCapacity = 1024)
        {
            _capacity = math.max(16, initialCapacity);
            Allocate(_capacity);
            _freeIndices = new NativeList<int>(_capacity, Allocator.Persistent);
            _highWater = 0;
            _aliveCount = 0;
        }

        private void Allocate(int cap)
        {
            const Allocator a = Allocator.Persistent;
            positions = new NativeArray<float2>(cap, a);
            velocities = new NativeArray<float2>(cap, a);
            predicted = new NativeArray<float2>(cap, a);
            colors = new NativeArray<float4>(cap, a);
            colorsNext = new NativeArray<float4>(cap, a);
            radii = new NativeArray<float>(cap, a);
            invMass = new NativeArray<float>(cap, a);
            typeId = new NativeArray<int>(cap, a);
            groupId = new NativeArray<int>(cap, a);
            lifetimeEnd = new NativeArray<float>(cap, a);
            lastMixTime = new NativeArray<float>(cap, a);
            versions = new NativeArray<int>(cap, a);
            alive = new NativeArray<byte>(cap, a);
        }

        private static void Grow<T>(ref NativeArray<T> arr, int newCap) where T : struct
        {
            var next = new NativeArray<T>(newCap, Allocator.Persistent);
            NativeArray<T>.Copy(arr, next, arr.Length);
            arr.Dispose();
            arr = next;
        }

        private void GrowTo(int newCap)
        {
            Grow(ref positions, newCap);
            Grow(ref velocities, newCap);
            Grow(ref predicted, newCap);
            Grow(ref colors, newCap);
            Grow(ref colorsNext, newCap);
            Grow(ref radii, newCap);
            Grow(ref invMass, newCap);
            Grow(ref typeId, newCap);
            Grow(ref groupId, newCap);
            Grow(ref lifetimeEnd, newCap);
            Grow(ref lastMixTime, newCap);
            Grow(ref versions, newCap);
            Grow(ref alive, newCap);
            _capacity = newCap;
        }

        /// <summary>
        /// 分配一个粒子 slot 并初始化逐粒子数据，返回稳定句柄。
        /// Allocate a particle slot, initialize its per-particle data, and return a stable handle.
        /// 粒子スロットを割り当て、データを初期化し、安定したハンドルを返します。
        /// </summary>
        public Liquid2DParticleHandle Allocate(
            float2 position, float2 velocity, float4 color, float radius, float mass,
            int typeIndex, int group, float lifetimeAbsoluteEnd, float now)
        {
            int idx;
            if (_freeIndices.Length > 0)
            {
                idx = _freeIndices[_freeIndices.Length - 1];
                _freeIndices.RemoveAt(_freeIndices.Length - 1);
            }
            else if (_highWater < _capacity)
            {
                idx = _highWater++;
            }
            else
            {
                GrowTo(_capacity * 3 / 2 + 16);
                idx = _highWater++;
            }

            positions[idx] = position;
            velocities[idx] = velocity;
            predicted[idx] = position;
            colors[idx] = color;
            colorsNext[idx] = color;
            radii[idx] = radius;
            invMass[idx] = mass > 0f ? 1f / mass : 0f;
            typeId[idx] = typeIndex;
            groupId[idx] = group;
            lifetimeEnd[idx] = lifetimeAbsoluteEnd;
            lastMixTime[idx] = now;
            alive[idx] = 1;
            _aliveCount++;

            return new Liquid2DParticleHandle(idx, versions[idx]);
        }

        /// <summary>
        /// 回收一个 slot（按句柄校验版本）。返回是否真的回收了存活 slot。
        /// Free a slot (validated by handle version). Returns whether an alive slot was actually freed.
        /// スロットを解放（ハンドルのバージョンで検証）。実際に生存スロットを解放したかを返します。
        /// </summary>
        public bool Free(Liquid2DParticleHandle handle)
        {
            if (!IsAlive(handle)) return false;
            return FreeIndex(handle.Index);
        }

        /// <summary>
        /// 按 slot 下标直接回收（内部/批量回收用，调用方需保证下标存活）。
        /// Free directly by slot index (internal/bulk use; caller must ensure the slot is alive).
        /// スロット添字で直接解放（内部/一括用。呼び出し側が生存を保証）。
        /// </summary>
        internal bool FreeIndex(int idx)
        {
            if (idx < 0 || idx >= _highWater || alive[idx] == 0) return false;
            alive[idx] = 0;
            unchecked { versions[idx] = versions[idx] + 1; }
            _freeIndices.Add(idx);
            _aliveCount--;
            return true;
        }

        /// <summary>
        /// 句柄是否仍指向存活粒子（版本一致且 alive）。
        /// Whether the handle still points to an alive particle (matching version and alive).
        /// ハンドルが生存粒子を指すかどうか（バージョン一致かつ alive）。
        /// </summary>
        public bool IsAlive(Liquid2DParticleHandle handle)
        {
            int i = handle.Index;
            return i >= 0 && i < _highWater && alive[i] == 1 && versions[i] == handle.Version;
        }

        /// <summary>清空所有粒子（保留已分配容量）。 // Clear all particles (keep allocated capacity). // 全粒子をクリア（容量は保持）。</summary>
        public void Clear()
        {
            for (int i = 0; i < _highWater; i++)
            {
                if (alive[i] == 1)
                {
                    alive[i] = 0;
                    unchecked { versions[i] = versions[i] + 1; }
                }
            }
            _freeIndices.Clear();
            _highWater = 0;
            _aliveCount = 0;
        }

        #region Query / Mutate by handle 句柄查询/写入 // ハンドルでの照会/書き込み

        public float2 GetPosition(Liquid2DParticleHandle h) => IsAlive(h) ? positions[h.Index] : float2.zero;
        public float2 GetVelocity(Liquid2DParticleHandle h) => IsAlive(h) ? velocities[h.Index] : float2.zero;
        public Color GetColor(Liquid2DParticleHandle h)
        {
            if (!IsAlive(h)) return Color.clear;
            float4 c = colors[h.Index];
            return new Color(c.x, c.y, c.z, c.w);
        }

        public void SetPosition(Liquid2DParticleHandle h, float2 p) { if (IsAlive(h)) { positions[h.Index] = p; predicted[h.Index] = p; } }
        public void SetVelocity(Liquid2DParticleHandle h, float2 v) { if (IsAlive(h)) velocities[h.Index] = v; }
        public void AddVelocity(Liquid2DParticleHandle h, float2 dv) { if (IsAlive(h)) velocities[h.Index] += dv; }
        public void SetColor(Liquid2DParticleHandle h, Color c)
        {
            if (!IsAlive(h)) return;
            var v = new float4(c.r, c.g, c.b, c.a);
            colors[h.Index] = v;
            colorsNext[h.Index] = v;
        }
        public void SetLifetimeEnd(Liquid2DParticleHandle h, float absoluteEnd) { if (IsAlive(h)) lifetimeEnd[h.Index] = absoluteEnd; }

        #endregion

        /// <summary>
        /// 混色双缓冲交换（颜色已写入 colorsNext 后调用）。
        /// Swap the color double-buffer (call after colors have been written into colorsNext).
        /// 混色ダブルバッファを交換（色を colorsNext に書き込んだ後に呼ぶ）。
        /// </summary>
        internal void SwapColorBuffers()
        {
            (colors, colorsNext) = (colorsNext, colors);
        }

        public void Dispose()
        {
            if (positions.IsCreated) positions.Dispose();
            if (velocities.IsCreated) velocities.Dispose();
            if (predicted.IsCreated) predicted.Dispose();
            if (colors.IsCreated) colors.Dispose();
            if (colorsNext.IsCreated) colorsNext.Dispose();
            if (radii.IsCreated) radii.Dispose();
            if (invMass.IsCreated) invMass.Dispose();
            if (typeId.IsCreated) typeId.Dispose();
            if (groupId.IsCreated) groupId.Dispose();
            if (lifetimeEnd.IsCreated) lifetimeEnd.Dispose();
            if (lastMixTime.IsCreated) lastMixTime.Dispose();
            if (versions.IsCreated) versions.Dispose();
            if (alive.IsCreated) alive.Dispose();
            if (_freeIndices.IsCreated) _freeIndices.Dispose();
        }
    }
}

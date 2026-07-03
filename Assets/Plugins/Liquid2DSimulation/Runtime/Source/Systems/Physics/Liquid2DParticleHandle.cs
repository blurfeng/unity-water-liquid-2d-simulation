using System;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体粒子句柄。纯数据架构下，粒子不再是 GameObject，仅为 SoA 存储中的一个 slot。
    /// 句柄携带 slot 索引与版本号；slot 被回收复用后版本号自增，旧句柄随之失效，可安全检测悬垂引用。
    /// Fluid particle handle. In the pure-data architecture, a particle is no longer a GameObject but a slot in the
    /// SoA store. The handle carries the slot index and a version; when a slot is recycled the version is bumped, so
    /// stale handles are detectable and dangling references are safe.
    /// 流体パーティクルハンドル。純データアーキテクチャでは、パーティクルは GameObject ではなく SoA ストア内の
    /// スロットです。ハンドルはスロットインデックスとバージョンを保持し、スロット再利用時にバージョンが加算され、
    /// 古いハンドルは無効化されてダングリング参照を安全に検出できます。
    /// </summary>
    [Serializable]
    public readonly struct Liquid2DParticleHandle : IEquatable<Liquid2DParticleHandle>
    {
        /// <summary>
        /// SoA 存储中的 slot 索引。
        /// Slot index in the SoA store.
        /// SoA ストア内のスロットインデックス。
        /// </summary>
        public readonly int Index;

        /// <summary>
        /// slot 版本号。与存储中该 slot 的当前版本不一致时，句柄已失效。
        /// Slot version. When it differs from the slot's current version in the store, the handle is stale.
        /// スロットバージョン。ストア内の現在のバージョンと異なる場合、ハンドルは無効です。
        /// </summary>
        public readonly int Version;

        public Liquid2DParticleHandle(int index, int version)
        {
            Index = index;
            Version = version;
        }

        /// <summary>
        /// 无效句柄（未分配）。
        /// An invalid (unallocated) handle.
        /// 無効な（未割り当て）ハンドル。
        /// </summary>
        public static Liquid2DParticleHandle Invalid => new Liquid2DParticleHandle(-1, 0);

        /// <summary>
        /// 句柄是否指向一个曾经分配过的 slot（不代表当前仍存活，存活需用 Store.IsAlive 校验版本）。
        /// Whether the handle points to an ever-allocated slot (not necessarily still alive; use Store.IsAlive to verify the version).
        /// ハンドルが割り当て済みスロットを指すかどうか（生存しているとは限らず、Store.IsAlive でバージョン検証が必要）。
        /// </summary>
        public bool IsValid => Index >= 0;

        public bool Equals(Liquid2DParticleHandle other) => Index == other.Index && Version == other.Version;
        public override bool Equals(object obj) => obj is Liquid2DParticleHandle other && Equals(other);
        public override int GetHashCode() => (Index * 397) ^ Version;
        public override string ToString() => $"Particle(#{Index}, v{Version})";
    }
}

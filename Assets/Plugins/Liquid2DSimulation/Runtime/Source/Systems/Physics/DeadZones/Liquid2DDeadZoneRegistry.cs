using System;
using System.Collections.Generic;
using Unity.Collections;
using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体销毁区域注册表。所有启用的 <see cref="Liquid2DDeadZone"/> 在此登记；<see cref="Liquid2DSimulation"/> 每帧调用
    /// <see cref="BuildBuffer"/> 把它们扁平化为 Burst 可用的 <see cref="Liquid2DDeadZoneBuffer"/>，并用传入的解析器把
    /// 每个区域的 nameTag 解析为 groupId（空 nameTag → 销毁全部）。仿 <see cref="Liquid2DColliderRegistry"/>。
    /// Fluid dead-zone registry. All enabled <see cref="Liquid2DDeadZone"/> register here; <see cref="Liquid2DSimulation"/> calls
    /// <see cref="BuildBuffer"/> each frame to flatten them into a Burst-usable <see cref="Liquid2DDeadZoneBuffer"/>,
    /// resolving each zone's nameTag to a groupId via the supplied resolver (empty nameTag → kill all). Mirrors
    /// <see cref="Liquid2DColliderRegistry"/>.
    /// 流体破棄領域レジストリ。有効な <see cref="Liquid2DDeadZone"/> をここに登録し、毎フレーム <see cref="BuildBuffer"/> で
    /// Burst 用バッファに平坦化します。<see cref="Liquid2DColliderRegistry"/> を模倣。
    /// </summary>
    public static class Liquid2DDeadZoneRegistry
    {
        private static readonly List<Liquid2DDeadZone> _active = new List<Liquid2DDeadZone>();

        // 重用的托管暂存，避免每帧 GC。 // Reused managed scratch to avoid per-frame GC. // 毎フレーム GC を避ける再利用バッファ。
        private static readonly List<Liquid2DDeadZoneData> _dataScratch = new List<Liquid2DDeadZoneData>(16);
        private static readonly List<float2> _pointScratch = new List<float2>(64);

        /// <summary>当前注册的销毁区域数。 // Number of registered dead zones. // 登録された破棄領域数。</summary>
        public static int Count => _active.Count;

        public static void Register(Liquid2DDeadZone zone)
        {
            if (zone && !_active.Contains(zone)) _active.Add(zone);
        }

        public static void Unregister(Liquid2DDeadZone zone)
        {
            _active.Remove(zone);
        }

        /// <summary>
        /// 把当前所有销毁区域扁平化为 NativeArray 缓冲。<paramref name="groupResolver"/> 将每个区域的 nameTag 解析为
        /// groupId（空/ null → 0，并标记 matchAll）。返回的缓冲由调用方负责释放（建议 Allocator.TempJob）。
        /// Flatten all current dead zones into NativeArray buffers. <paramref name="groupResolver"/> resolves each zone's
        /// nameTag to a groupId (empty/null → 0, flagged as matchAll). The caller owns the returned buffers (TempJob).
        /// 全破棄領域を NativeArray バッファに平坦化します。バッファは呼び出し側が解放。
        /// </summary>
        public static Liquid2DDeadZoneBuffer BuildBuffer(Allocator allocator, Func<string, int> groupResolver)
        {
            _dataScratch.Clear();
            _pointScratch.Clear();

            for (int i = 0; i < _active.Count; i++)
            {
                var z = _active[i];
                if (!z || !z.isActiveAndEnabled) continue;

                var shape = new Liquid2DColliderData();
                z.Fill(ref shape, _pointScratch);

                string tag = z.NameTag;
                bool matchAll = string.IsNullOrEmpty(tag);
                int group = matchAll || groupResolver == null ? 0 : groupResolver(tag);

                _dataScratch.Add(new Liquid2DDeadZoneData
                {
                    shape = shape,
                    groupId = group,
                    matchAll = (byte)(matchAll ? 1 : 0),
                    invert = (byte)(z.BoundsMode ? 1 : 0),
                });
            }

            var buffer = new Liquid2DDeadZoneBuffer();
            int n = _dataScratch.Count;
            buffer.zones = new NativeArray<Liquid2DDeadZoneData>(n, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++) buffer.zones[i] = _dataScratch[i];

            int pcount = math.max(1, _pointScratch.Count); // 至少 1 长度避免零长 NativeArray 边角。 // at least length 1. // 最低長さ 1。
            buffer.points = new NativeArray<float2>(pcount, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < _pointScratch.Count; i++) buffer.points[i] = _pointScratch[i];

            return buffer;
        }
    }
}

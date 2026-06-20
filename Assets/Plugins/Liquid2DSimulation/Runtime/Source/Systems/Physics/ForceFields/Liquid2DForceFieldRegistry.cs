using System.Collections.Generic;
using Unity.Collections;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体力场注册表。所有启用的 <see cref="Liquid2DForceFieldSource"/> 在此登记；<see cref="Liquid2DSimulation"/> 每帧
    /// 调用 <see cref="BuildBuffer"/> 把它们扁平化为 Burst / GPU 可用的 <see cref="Liquid2DForceFieldBuffer"/>，在求解的
    /// 外力阶段对粒子施加吸引/排斥力。仿 <see cref="Liquid2DColliderRegistry"/>，支持任意多个力场同时作用。
    /// Fluid force-field registry. All enabled <see cref="Liquid2DForceFieldSource"/> register here; <see cref="Liquid2DSimulation"/>
    /// calls <see cref="BuildBuffer"/> each frame to flatten them into a Burst/GPU-usable <see cref="Liquid2DForceFieldBuffer"/>,
    /// applying attract/repel forces during the external-forces stage. Mirrors <see cref="Liquid2DColliderRegistry"/> and
    /// supports any number of simultaneous fields.
    /// 流体力場レジストリ。有効な <see cref="Liquid2DForceFieldSource"/> をここに登録し、毎フレーム Burst/GPU 用バッファへ
    /// 平坦化します。<see cref="Liquid2DColliderRegistry"/> と同様、任意数の力場の同時作用をサポート。
    /// </summary>
    public static class Liquid2DForceFieldRegistry
    {
        private static readonly List<Liquid2DForceFieldSource> _active = new List<Liquid2DForceFieldSource>();

        // 重用的托管暂存，避免每帧 GC。 // Reused managed scratch to avoid per-frame GC. // 毎フレーム GC を避ける再利用バッファ。
        private static readonly List<Liquid2DForceFieldData> _dataScratch = new List<Liquid2DForceFieldData>(16);

        /// <summary>当前注册的力场源数。 // Number of registered force-field sources. // 登録力場ソース数。</summary>
        public static int Count => _active.Count;

        public static void Register(Liquid2DForceFieldSource source)
        {
            if (source && !_active.Contains(source)) _active.Add(source);
        }

        public static void Unregister(Liquid2DForceFieldSource source)
        {
            _active.Remove(source);
        }

        /// <summary>
        /// 把当前所有启用且本帧生效的力场扁平化为 NativeArray 缓冲。<see cref="Liquid2DForceFieldSource.TryGetField"/> 返回
        /// false 的源（如鼠标未按下）会被跳过。返回的缓冲由调用方负责释放（建议 Allocator.TempJob）。
        /// Flatten all enabled, currently-active force fields into a NativeArray buffer. Sources whose
        /// <see cref="Liquid2DForceFieldSource.TryGetField"/> returns false (e.g. mouse not pressed) are skipped. The caller
        /// owns the returned buffer (Allocator.TempJob recommended).
        /// 有効かつ本フレーム作用する全力場を NativeArray バッファに平坦化します。TryGetField が false のソースはスキップ。
        /// </summary>
        public static Liquid2DForceFieldBuffer BuildBuffer(Allocator allocator)
        {
            _dataScratch.Clear();

            for (int i = 0; i < _active.Count; i++)
            {
                var s = _active[i];
                if (!s || !s.isActiveAndEnabled) continue;
                if (s.TryGetField(out var data) && data.Radius > 0f && data.Strength != 0f)
                    _dataScratch.Add(data);
            }

            var buffer = new Liquid2DForceFieldBuffer();
            int n = _dataScratch.Count;
            buffer.Fields = new NativeArray<Liquid2DForceFieldData>(n, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < n; i++) buffer.Fields[i] = _dataScratch[i];
            return buffer;
        }
    }
}

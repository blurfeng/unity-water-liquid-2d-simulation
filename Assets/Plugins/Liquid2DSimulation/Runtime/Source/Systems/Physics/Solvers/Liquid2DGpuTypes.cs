using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 碰撞体的 GPU 上传结构（与 Liquid2DSph.compute 的 GpuCollider 对齐）。把 <see cref="Liquid2DColliderData"/> 的
    /// enum/byte 字段转为 int，避免 GPU StructuredBuffer 的字节对齐问题。stride = 44 bytes。
    /// GPU-upload form of a collider (matches GpuCollider in Liquid2DSph.compute). Converts enum/byte fields of
    /// <see cref="Liquid2DColliderData"/> to int to avoid GPU StructuredBuffer byte-alignment issues. stride = 44 bytes.
    /// コライダーの GPU アップロード構造（Liquid2DSph.compute の GpuCollider と整合）。stride = 44 bytes。
    /// </summary>
    public struct Liquid2DGpuCollider
    {
        public int shape;
        public float2 center;
        public float2 size;
        public float rotation;
        public float radius;
        public int pointStart;
        public int pointCount;
        public int dynamic;
        public int bodyIndex;

        public static Liquid2DGpuCollider From(in Liquid2DColliderData c) => new Liquid2DGpuCollider
        {
            shape = (int)c.shape,
            center = c.center,
            size = c.size,
            rotation = c.rotation,
            radius = c.radius,
            pointStart = c.pointStart,
            pointCount = c.pointCount,
            dynamic = c.dynamic,
            bodyIndex = c.bodyIndex,
        };
    }

    /// <summary>
    /// 混色参数的 GPU 上传结构（与 Liquid2DSph.compute 的 MixData 对齐）。byte → int。stride = 20 bytes。
    /// GPU-upload form of mix params (matches MixData in Liquid2DSph.compute). byte → int. stride = 20 bytes.
    /// 混色パラメータの GPU アップロード構造（MixData と整合）。stride = 20 bytes。
    /// </summary>
    public struct Liquid2DGpuMixData
    {
        public int enabled;
        public float speed;
        public int withMovement;
        public float maxSpeed;
        public float interval;

        public static Liquid2DGpuMixData From(in Liquid2DMixData m) => new Liquid2DGpuMixData
        {
            enabled = m.enabled,
            speed = m.speed,
            withMovement = m.withMovement,
            maxSpeed = m.maxSpeed,
            interval = m.interval,
        };
    }
}

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
    /// 力场的 GPU 上传结构（与 Liquid2DSph.compute 的 ForceField 对齐）。enum → int。
    /// stride = 36 bytes（center8 + radius4 + strength4 + damping4 + swirl4 + gravityAttenuation4 + falloff4 + mode4）。
    /// GPU-upload form of a force field (matches ForceField in Liquid2DSph.compute). enum → int. stride = 36 bytes.
    /// 力場の GPU アップロード構造（Liquid2DSph.compute の ForceField と整合）。enum → int。stride = 36 bytes。
    /// </summary>
    public struct Liquid2DGpuForceField
    {
        public float2 center;
        public float radius;
        public float strength;
        public float velocityDamping;
        public float swirlStrength;
        public float gravityAttenuation;
        public float falloff;
        public int mode;

        public static Liquid2DGpuForceField From(in Liquid2DForceFieldData f) => new Liquid2DGpuForceField
        {
            center = f.center,
            radius = f.radius,
            strength = f.strength,
            velocityDamping = f.velocityDamping,
            swirlStrength = f.swirlStrength,
            gravityAttenuation = f.gravityAttenuation,
            falloff = f.falloff,
            mode = (int)f.mode,
        };
    }

    /// <summary>
    /// 销毁区域的 GPU 上传结构（与 Liquid2DSph.compute 的 GpuDeadZone 对齐）。形状字段同 <see cref="Liquid2DGpuCollider"/>，
    /// 末尾改为 groupId/matchAll/invert。enum/byte → int 避免对齐问题。stride = 48 bytes。
    /// GPU-upload form of a dead zone (matches GpuDeadZone in Liquid2DSph.compute). Shape fields mirror
    /// <see cref="Liquid2DGpuCollider"/>, ending with groupId/matchAll/invert instead. enum/byte → int. stride = 48 bytes.
    /// 破棄領域の GPU アップロード構造（GpuDeadZone と整合）。stride = 48 bytes。
    /// </summary>
    public struct Liquid2DGpuDeadZone
    {
        public int shape;
        public float2 center;
        public float2 size;
        public float rotation;
        public float radius;
        public int pointStart;
        public int pointCount;
        public int groupId;
        public int matchAll;
        public int invert;

        public static Liquid2DGpuDeadZone From(in Liquid2DDeadZoneData z) => new Liquid2DGpuDeadZone
        {
            shape = (int)z.shape.shape,
            center = z.shape.center,
            size = z.shape.size,
            rotation = z.shape.rotation,
            radius = z.shape.radius,
            pointStart = z.shape.pointStart,
            pointCount = z.shape.pointCount,
            groupId = z.groupId,
            matchAll = z.matchAll,
            invert = z.invert,
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

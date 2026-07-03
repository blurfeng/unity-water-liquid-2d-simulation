using Unity.Mathematics;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 碰撞体的 GPU 上传结构（与 Liquid2DSph.compute 的 GpuCollider 对齐）。把 <see cref="Liquid2DColliderData"/> 的
    /// enum/byte 字段转为 int，避免 GPU StructuredBuffer 的字节对齐问题。stride = 88 bytes。
    /// GPU-upload form of a collider (matches GpuCollider in Liquid2DSph.compute). Converts enum/byte fields of
    /// <see cref="Liquid2DColliderData"/> to int to avoid GPU StructuredBuffer byte-alignment issues. stride = 88 bytes.
    /// コライダーの GPU アップロード構造（Liquid2DSph.compute の GpuCollider と整合）。stride = 88 bytes。
    /// </summary>
    public struct Liquid2DGpuCollider
    {
        public int Shape;
        public float2 Center;
        public float2 Size;
        public float Rotation;
        public float Radius;
        public int PointStart;
        public int PointCount;
        public int Dynamic;
        public int BodyIndex;
        public int GroupId;
        public int MatchAll;
        public int ColliderMode;
        public float SubmergeCoupling;
        public float SubmergeSplashStrength;
        public float SubmergeSplashThreshold;
        public float SubmergeSplashRange;
        public float SubmergeFluidDensityThreshold;
        public float SubmergeCoverage;
        public float2 Velocity;

        public static Liquid2DGpuCollider From(in Liquid2DColliderData c) => new Liquid2DGpuCollider
        {
            Shape = (int)c.Shape,
            Center = c.Center,
            Size = c.Size,
            Rotation = c.Rotation,
            Radius = c.Radius,
            PointStart = c.PointStart,
            PointCount = c.PointCount,
            Dynamic = c.Dynamic,
            BodyIndex = c.BodyIndex,
            GroupId = c.GroupId,
            MatchAll = c.MatchAll,
            ColliderMode = c.ColliderMode,
            SubmergeCoupling = c.SubmergeCoupling,
            SubmergeSplashStrength = c.SubmergeSplashStrength,
            SubmergeSplashThreshold = c.SubmergeSplashThreshold,
            SubmergeSplashRange = c.SubmergeSplashRange,
            SubmergeFluidDensityThreshold = c.SubmergeFluidDensityThreshold,
            SubmergeCoverage = c.SubmergeCoverage,
            Velocity = c.Velocity,
        };
    }

    /// <summary>
    /// 力场的 GPU 上传结构（与 Liquid2DSph.compute 的 ForceField 对齐）。enum → int。
    /// stride = 36 bytes（center8 + radius4 + strength4 + damping4 + swirl4 + gravityAttenuation4 + falloff4 + mode4）。
    /// GPU-upload form of a force field (matches ForceField in Liquid2DSph.compute). enum → int. stride = 44 bytes.
    /// 力場の GPU アップロード構造（Liquid2DSph.compute の ForceField と整合）。enum → int。stride = 44 bytes。
    /// </summary>
    public struct Liquid2DGpuForceField
    {
        public float2 Center;
        public float Radius;
        public float Strength;
        public float VelocityDamping;
        public float SwirlStrength;
        public float GravityAttenuation;
        public float Falloff;
        public int Mode;
        public int GroupId;
        public int MatchAll;

        public static Liquid2DGpuForceField From(in Liquid2DForceFieldData f) => new Liquid2DGpuForceField
        {
            Center = f.Center,
            Radius = f.Radius,
            Strength = f.Strength,
            VelocityDamping = f.VelocityDamping,
            SwirlStrength = f.SwirlStrength,
            GravityAttenuation = f.GravityAttenuation,
            Falloff = f.Falloff,
            Mode = (int)f.Mode,
            GroupId = f.GroupId,
            MatchAll = f.MatchAll,
        };
    }

    /// <summary>
    /// 销毁区域的 GPU 上传结构。形状字段同 <see cref="Liquid2DGpuCollider"/>，
    /// 末尾改为 groupId/matchAll/invert。enum/byte → int 避免对齐问题。stride = 48 bytes。
    /// GPU-upload form of a dead zone (matches GpuDeadZone in Liquid2DSph.compute). Shape fields mirror
    /// <see cref="Liquid2DGpuCollider"/>, ending with groupId/matchAll/invert instead. enum/byte → int. stride = 48 bytes.
    /// 破棄領域の GPU アップロード構造（GpuDeadZone と整合）。stride = 48 bytes。
    /// </summary>
    public struct Liquid2DGpuDeadZone
    {
        public int Shape;
        public float2 Center;
        public float2 Size;
        public float Rotation;
        public float Radius;
        public int PointStart;
        public int PointCount;
        public int GroupId;
        public int MatchAll;
        public int Invert;

        public static Liquid2DGpuDeadZone From(in Liquid2DDeadZoneData z) => new Liquid2DGpuDeadZone
        {
            Shape = (int)z.Shape.Shape,
            Center = z.Shape.Center,
            Size = z.Shape.Size,
            Rotation = z.Shape.Rotation,
            Radius = z.Shape.Radius,
            PointStart = z.Shape.PointStart,
            PointCount = z.Shape.PointCount,
            GroupId = z.GroupId,
            MatchAll = z.MatchAll,
            Invert = z.Invert,
        };
    }

    /// <summary>
    /// 混色参数的 GPU 上传结构（与 Liquid2DSph.compute 的 MixData 对齐）。byte → int。stride = 20 bytes。
    /// GPU-upload form of mix params (matches MixData in Liquid2DSph.compute). byte → int. stride = 20 bytes.
    /// 混色パラメータの GPU アップロード構造（MixData と整合）。stride = 20 bytes。
    /// </summary>
    public struct Liquid2DGpuMixData
    {
        public int Enabled;
        public float Speed;
        public int WithMovement;
        public float MaxSpeed;
        public float Interval;

        public static Liquid2DGpuMixData From(in Liquid2DMixData m) => new Liquid2DGpuMixData
        {
            Enabled = m.Enabled,
            Speed = m.Speed,
            WithMovement = m.WithMovement,
            MaxSpeed = m.MaxSpeed,
            Interval = m.Interval,
        };
    }
}

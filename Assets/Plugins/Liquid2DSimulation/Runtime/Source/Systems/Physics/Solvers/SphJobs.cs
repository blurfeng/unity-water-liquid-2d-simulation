using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using UnityEngine;
using static Unity.Mathematics.math;
using float2 = Unity.Mathematics.float2;
using float3 = Unity.Mathematics.float3;
using float4 = Unity.Mathematics.float4;
using int2 = Unity.Mathematics.int2;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 逐 typeId 的混色参数（blittable，供 MixColorJob 读取）。
    /// Per-typeId mixing parameters (blittable, read by MixColorJob).
    /// typeId ごとの混色パラメータ（blittable、MixColorJob が読む）。
    /// </summary>
    public struct Liquid2DMixData
    {
        public byte Enabled;
        public float Speed;
        public byte WithMovement;
        public float MaxSpeed;
        public float Interval;

        public static Liquid2DMixData Disabled => new Liquid2DMixData { Enabled = 0, Speed = 0f, WithMovement = 0, MaxSpeed = 100f, Interval = 0.1f };
    }

    /// <summary>
    /// 2D SPH 双密度光滑核：
    /// 密度用 SpikyPow2、近密度用 SpikyPow3、粘性用 Poly6；压力梯度用对应导数核。
    /// 2D SPH dual-density smoothing kernels: density uses SpikyPow2, near-density uses
    /// SpikyPow3, viscosity uses Poly6; pressure gradient uses the matching derivative kernels.
    /// 2D SPH デュアル密度平滑核。密度 SpikyPow2、近密度 SpikyPow3、粘性 Poly6。
    /// </summary>
    internal static class KernelMath
    {
        public const float PI = 3.14159265358979f;

        public static float Poly6Coef(float h) => 4f / (PI * pow(h, 8f));
        public static float SpikyPow2Coef(float h) => 6f / (PI * pow(h, 4f));
        public static float SpikyPow3Coef(float h) => 10f / (PI * pow(h, 5f));
        public static float DerivSpikyPow2Coef(float h) => 12f / (PI * pow(h, 4f));
        public static float DerivSpikyPow3Coef(float h) => 30f / (PI * pow(h, 5f));

        /// <summary>Poly6 核值（粘性）。 // Poly6 value (viscosity). // Poly6 値（粘性）。</summary>
        public static float Poly6(float dst, float h, float coef)
        {
            if (dst >= h) return 0f;
            float v = h * h - dst * dst;
            return v * v * v * coef;
        }

        /// <summary>SpikyPow2 核值（密度）。 // SpikyPow2 value (density). // SpikyPow2 値（密度）。</summary>
        public static float SpikyPow2(float dst, float h, float coef)
        {
            if (dst >= h) return 0f;
            float v = h - dst;
            return v * v * coef;
        }

        /// <summary>SpikyPow3 核值（近密度）。 // SpikyPow3 value (near density). // SpikyPow3 値（近密度）。</summary>
        public static float SpikyPow3(float dst, float h, float coef)
        {
            if (dst >= h) return 0f;
            float v = h - dst;
            return v * v * v * coef;
        }

        /// <summary>SpikyPow2 导数（密度压力梯度幅值）。 // SpikyPow2 derivative. // SpikyPow2 導関数。</summary>
        public static float DerivSpikyPow2(float dst, float h, float coef)
        {
            if (dst > h) return 0f;
            float v = h - dst;
            return -v * coef;
        }

        /// <summary>SpikyPow3 导数（近密度压力梯度幅值）。 // SpikyPow3 derivative. // SpikyPow3 導関数。</summary>
        public static float DerivSpikyPow3(float dst, float h, float coef)
        {
            if (dst > h) return 0f;
            float v = h - dst;
            return -v * v * coef;
        }
    }

    /// <summary>
    /// 外力 + 预测：v += gravity·gravityScale·dt；叠加力场（吸引/排斥）后 predicted = pos + v·predictionFactor。
    /// External forces + prediction: v += gravity·gravityScale·dt; add force fields (attract/repel), then
    /// predicted = pos + v·predictionFactor. 
    /// 外力 + 予測。力場（引力/斥力）を加算（GPU ExternalForces kernel と同一式）。
    /// </summary>
    [BurstCompile]
    public struct ExternalForcesJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ActiveIndices;
        [ReadOnly] public NativeArray<int> TypeId;
        [ReadOnly] public NativeArray<int> GroupId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> Materials;
        [NativeDisableParallelForRestriction] public NativeArray<float2> Velocities;
        [ReadOnly] public NativeArray<float2> Positions;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float2> Predicted;
        [ReadOnly] public NativeArray<Liquid2DForceFieldData> ForceFields;
        public int ForceFieldCount;
        public float2 Gravity;
        public float DT;
        public float PredictionFactor;

        public void Execute(int k)
        {
            int i = ActiveIndices[k];
            float gs = Materials[TypeId[i]].GravityScale;
            int gi = GroupId[i];
            float2 pos = Positions[i];
            float2 v = Velocities[i];

            // 重力按力场重力衰减加权 + 累积径向/切向力场加速度 + 累积速度制动系数（结构对齐参考 ExternalForces）。
            // Gravity weighted by field gravity-attenuation + accumulated radial/swirl field accel + accumulated velocity
            // damping coefficient (structure matches the reference ExternalForces). Must stay in sync with the GPU kernel.
            // 重力を力場の重力減衰で重み付け + 径方向/接線方向の力場加速度を累積 + 速度制動係数を累積（参考と同構造）。
            float gWeight = 1f;
            float2 fieldAccel = float2.zero;
            float damp = 0f;

            for (int f = 0; f < ForceFieldCount; f++)
            {
                var ff = ForceFields[f];
                // 组过滤：matchAll（空 nameTag）作用全部，否则仅作用 groupId 匹配的粒子。 // Group filter. // グループ絞り込み。
                if (ff.MatchAll == 0 && ff.GroupId != gi) continue;
                float2 offset = ff.Center - pos;
                float sqrDst = lengthsq(offset);
                float r = ff.Radius;
                if (sqrDst >= r * r) continue;

                float dst = sqrt(sqrDst);
                float centreT = 1f - dst / r;                  // 线性，1=中心 0=边缘。 // linear, 1=center 0=edge. // 線形。
                float profile = ff.Mode == Liquid2DForceFieldMode.Constant
                    ? 1f
                    : (Mathf.Approximately(ff.Falloff, 1f) ? centreT : pow(centreT, ff.Falloff));

                float2 dir = dst > 1e-6f ? offset / dst : float2.zero;
                float2 perp = new float2(-dir.y, dir.x);       // 逆时针切向。 // CCW tangent. // 反時計回り接線。

                fieldAccel += dir * (profile * ff.Strength);
                fieldAccel += perp * (profile * ff.SwirlStrength);
                gWeight = min(gWeight, 1f - centreT * ff.GravityAttenuation);
                damp += centreT * ff.VelocityDamping;
            }

            v += (Gravity * gs * gWeight + fieldAccel) * DT;
            if (damp > 0f) v -= v * saturate(damp * DT);

            Velocities[i] = v;
            Predicted[i] = pos + v * PredictionFactor;
        }
    }

    /// <summary>
    /// 密度与近密度（在 predicted 上，遍历 3×3 cell，含自身）。
    /// Density and near-density (on predicted positions, scanning 3×3 cells, including self).
    /// 密度と近密度（predicted 上、3×3 cell、自身含む）。
    /// </summary>
    [BurstCompile]
    public struct DensityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ActiveIndices;
        [ReadOnly] public NativeArray<float2> Predicted;
        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<int> SortedSlots;
        public int TableSize;
        public float InvCellSize;
        public float H;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float2> Densities;

        public void Execute(int k)
        {
            int i = ActiveIndices[k];
            float2 pi = Predicted[i];
            float h2 = H * H;
            float c2 = KernelMath.SpikyPow2Coef(H);
            float c3 = KernelMath.SpikyPow3Coef(H);

            float density = 0f;
            float nearDensity = 0f;

            int2 ci = Liquid2DHashGrid.CellCoord(pi, InvCellSize);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 qc = ci + new int2(dx, dy);
                int b = Liquid2DHashGrid.Hash(qc, TableSize);
                int start = CellStart[b];
                int end = CellStart[b + 1];
                for (int s = start; s < end; s++)
                {
                    int j = SortedSlots[s];
                    if (!Liquid2DHashGrid.CellCoord(Predicted[j], InvCellSize).Equals(qc)) continue;
                    float r2 = lengthsq(pi - Predicted[j]);
                    if (r2 >= h2) continue;
                    float r = sqrt(r2);
                    density += KernelMath.SpikyPow2(r, H, c2);
                    nearDensity += KernelMath.SpikyPow3(r, H, c3);
                }
            }

            Densities[i] = new float2(density, nearDensity);
        }
    }

    /// <summary>
    /// 压力力：pressure = (density - targetDensity·targetDensityScale)·pressureMultiplier；
    /// nearPressure = nearDensity·nearPressureMultiplier·(0.5+cohesion)（cohesion 越高越易结团/表面张力）。
    /// 邻居梯度累加后 v += (pressureForce/density)·dt。
    /// Pressure force. Higher cohesion strengthens near-pressure (clumping / surface tension).
    /// 圧力力。cohesion が高いほど近圧力が強まり結団/表面張力に。
    /// </summary>
    [BurstCompile]
    public struct PressureForceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ActiveIndices;
        [ReadOnly] public NativeArray<float2> Predicted;
        [ReadOnly] public NativeArray<float2> Densities;
        [ReadOnly] public NativeArray<int> TypeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> Materials;
        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<int> SortedSlots;
        public int TableSize;
        public float InvCellSize;
        public float H;
        public float TargetDensity;
        public float PressureMultiplier;
        public float NearPressureMultiplier;
        [NativeDisableParallelForRestriction] public NativeArray<float2> Velocities;
        public float DT;

        // 由密度求该粒子的压力（含材质静止密度缩放）。 // Pressure from density (with material rest-density scale). // 密度から圧力。
        private float Pressure(float density, int slot)
        {
            float rho0 = TargetDensity * Materials[TypeId[slot]].TargetDensityScale;
            return (density - rho0) * PressureMultiplier;
        }

        private float NearPressure(float nearDensity, int slot)
        {
            var mat = Materials[TypeId[slot]];
            return nearDensity * NearPressureMultiplier * mat.NearPressureMultiplierScale * (0.5f + mat.Cohesion);
        }

        public void Execute(int k)
        {
            int i = ActiveIndices[k];
            float2 pi = Predicted[i];
            float densityI = Densities[i].x;
            float nearI = Densities[i].y;
            if (densityI <= 1e-8f) return;

            float pressureI = Pressure(densityI, i);
            float nearPressureI = NearPressure(nearI, i);

            float h2 = H * H;
            float cd2 = KernelMath.DerivSpikyPow2Coef(H);
            float cd3 = KernelMath.DerivSpikyPow3Coef(H);

            float2 pressureForce = float2.zero;

            int2 ci = Liquid2DHashGrid.CellCoord(pi, InvCellSize);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 qc = ci + new int2(dx, dy);
                int b = Liquid2DHashGrid.Hash(qc, TableSize);
                int start = CellStart[b];
                int end = CellStart[b + 1];
                for (int s = start; s < end; s++)
                {
                    int j = SortedSlots[s];
                    if (j == i) continue;
                    if (!Liquid2DHashGrid.CellCoord(Predicted[j], InvCellSize).Equals(qc)) continue;
                    float2 offset = Predicted[j] - pi;
                    float r2 = lengthsq(offset);
                    if (r2 >= h2) continue;
                    float r = sqrt(r2);
                    float2 dir = r > 1e-6f ? offset / r : new float2(0f, 1f);

                    float densityJ = Densities[j].x;
                    float nearJ = Densities[j].y;
                    if (densityJ <= 1e-8f || nearJ <= 1e-8f) continue;

                    float pressureJ = Pressure(densityJ, j);
                    float nearPressureJ = NearPressure(nearJ, j);

                    float sharedP = (pressureI + pressureJ) * 0.5f;
                    float sharedNear = (nearPressureI + nearPressureJ) * 0.5f;

                    pressureForce += dir * KernelMath.DerivSpikyPow2(r, H, cd2) * sharedP / densityJ;
                    pressureForce += dir * KernelMath.DerivSpikyPow3(r, H, cd3) * sharedNear / nearJ;
                }
            }

            Velocities[i] += (pressureForce / densityI) * DT;
        }
    }

    /// <summary>
    /// 粘性（在 predicted 上邻居搜索）：v_next = v + Σ(vj-vi)·Poly6 · (viscosityStrength + material.viscosity)·dt。
    /// 写入独立的 velNext 以避免并行读写竞争（随后 CopyVelocityJob 拷回）。
    /// Viscosity (neighbor search on predicted): writes to a separate velNext buffer to avoid parallel read/write races.
    /// 粘性。並行読み書き競合を避けるため velNext に書き、後で CopyVelocityJob で戻す。
    /// </summary>
    [BurstCompile]
    public struct SphViscosityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ActiveIndices;
        [ReadOnly] public NativeArray<float2> Predicted;
        [ReadOnly] public NativeArray<float2> Velocities;
        [ReadOnly] public NativeArray<int> TypeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> Materials;
        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<int> SortedSlots;
        public int TableSize;
        public float InvCellSize;
        public float H;
        public float ViscosityStrength;
        public float DT;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float2> VelNext;

        public void Execute(int k)
        {
            int i = ActiveIndices[k];
            float2 pi = Predicted[i];
            float2 vi = Velocities[i];
            float h2 = H * H;
            float c6 = KernelMath.Poly6Coef(H);
            float strength = ViscosityStrength + Materials[TypeId[i]].Viscosity;

            float2 force = float2.zero;
            int2 ci = Liquid2DHashGrid.CellCoord(pi, InvCellSize);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 qc = ci + new int2(dx, dy);
                int b = Liquid2DHashGrid.Hash(qc, TableSize);
                int start = CellStart[b];
                int end = CellStart[b + 1];
                for (int s = start; s < end; s++)
                {
                    int j = SortedSlots[s];
                    if (j == i) continue;
                    if (!Liquid2DHashGrid.CellCoord(Predicted[j], InvCellSize).Equals(qc)) continue;
                    float r2 = lengthsq(pi - Predicted[j]);
                    if (r2 >= h2) continue;
                    float r = sqrt(r2);
                    force += (Velocities[j] - vi) * KernelMath.Poly6(r, H, c6);
                }
            }

            VelNext[i] = vi + force * strength * DT;
        }
    }

    /// <summary>把 velNext 拷回 velocities（活动粒子）。 // Copy velNext back to velocities (active). // velNext を velocities に戻す。</summary>
    [BurstCompile]
    public struct CopyVelocityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ActiveIndices;
        [ReadOnly] public NativeArray<float2> VelNext;
        [NativeDisableParallelForRestriction] public NativeArray<float2> Velocities;

        public void Execute(int k)
        {
            int i = ActiveIndices[k];
            Velocities[i] = VelNext[i];
        }
    }

    /// <summary>
    /// 积分 + 碰撞：pos += v·dt；遍历碰撞体 <see cref="Liquid2DColliderMath.Project"/> 推出、沿法线反射速度
    /// （回弹 = restitution × collisionDamping）、切向摩擦（friction）；命中动态体时记录入射流体速度+接触位置（相对速度阻力/浮力用，双向耦合）。
    /// Integrate + collide: pos += v·dt; project out of colliders, reflect normal velocity (bounce = restitution ×
    /// collisionDamping), apply tangential friction; on dynamic-body hit, record incoming fluid velocity + contact position
    /// (for relative-velocity drag/buoyancy, two-way coupling).
    /// 積分 + 衝突。コライダーから押し出し、法線速度を反射、接線摩擦；動的体命中時は入射流速+接触位置を記録（相対速度抗力/浮力用）。
    /// </summary>
    [BurstCompile]
    public struct SphIntegrateCollideJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ActiveIndices;
        [NativeDisableParallelForRestriction] public NativeArray<float2> Positions;
        [NativeDisableParallelForRestriction] public NativeArray<float2> Velocities;
        [ReadOnly] public NativeArray<float> Radii;
        [ReadOnly] public NativeArray<int> TypeId;
        [ReadOnly] public NativeArray<int> GroupId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> Materials;
        [ReadOnly] public NativeArray<Liquid2DColliderData> Colliders;
        [ReadOnly] public NativeArray<float2> Points;
        public float DT;
        public float CollisionDamping;
        /// <summary>最大速度限制（0 = 不限制）。 // Max speed clamp (0 = unlimited). // 最大速度制限（0 = 無制限）。</summary>
        public float MaxSpeed;
        public byte HasColliders;
        public byte Accumulate;                            // 1 时记录接触采样。 // record contact samples when 1. // 1 のとき接触サンプル記録。
        [WriteOnly] public NativeArray<float2> OutVelSum;   // 长度 activeCount，命中动态体时记录入射流体速度（相对速度阻力用）。 // incoming fluid velocity on dynamic hit (relative-velocity drag). // 入射流体速度。
        [WriteOnly] public NativeArray<int> OutBody;        // 长度 activeCount，-1 表示无。 // length activeCount, -1 = none.
        [WriteOnly] public NativeArray<float2> OutContact;  // 长度 activeCount，命中动态体时的接触位置（浮力质心用）。 // contact pos on dynamic hit (buoyancy centroid). // 接触位置。
        [WriteOnly] public NativeArray<float> OutNormalY;   // 长度 activeCount，最后命中动态体的接触法线 y 分量（n.y<0=粒子在物体下方，浮力方向过滤用）。 // contact normal y of the last dynamic hit (n.y<0 = particle below body; buoyancy direction filter). // 接触法線 y。

        public void Execute(int k)
        {
            int i = ActiveIndices[k];
            float2 v = Velocities[i];

            // 速度限幅：防止压力爆炸导致粒子速度失控乱飞。
            // Speed clamp: prevents runaway particle velocities from pressure explosions.
            // 速度制限：圧力爆発によるパーティクル速度暴走を防ぐ。
            if (MaxSpeed > 0f)
            {
                float spd2 = dot(v, v);
                if (spd2 > MaxSpeed * MaxSpeed)
                    v = v * (MaxSpeed / sqrt(spd2));
            }

            float2 p = Positions[i] + v * DT;

            // 入射来流速度（碰撞反射前），作为该粒子处局部流速的估计，用于相对速度阻力。 // Incoming flow velocity (pre-collision), local fluid-velocity estimate for relative-velocity drag. // 入射流速。
            float2 vIncoming = v;
            float2 fluidVel = float2.zero;
            float2 contactPos = float2.zero;
            int hitBody = -1;
            float hitNormalY = 0f; // 最后命中动态体的接触法线 y（默认 0=不计浮力）。 // contact normal y of last dynamic hit (0 = no buoyancy). // 接触法線 y。

            if (HasColliders == 1)
            {
                int gi = GroupId[i];
                float pr = Radii[i];
                var mat = Materials[TypeId[i]];
                float e = saturate(mat.Restitution) * CollisionDamping;

                for (int ci = 0; ci < Colliders.Length; ci++)
                {
                    var col = Colliders[ci];
                    // 组过滤：matchAll（空 nameTag）作用全部，否则仅作用 groupId 匹配的粒子。 // Group filter. // グループ絞り込み。
                    if (col.MatchAll == 0 && col.GroupId != gi) continue;
                    if (!Liquid2DColliderMath.Project(col, Points, p, pr, out float2 corr, out float2 n)) continue;

                    p += corr;

                    // 速度反射：去掉指向表面内的法向分量，并按回弹系数反弹。 // Reflect velocity: remove inward normal component, bounce by e. // 速度反射。
                    float vn = dot(v, n);
                    if (vn < 0f) v -= (1f + e) * vn * n;

                    // 摩擦：阻尼切向分量。 // Friction: damp tangential component. // 摩擦：接線減衰。
                    if (mat.Friction > 0f)
                    {
                        float2 vt = v - dot(v, n) * n;
                        v -= vt * mat.Friction;
                    }

                    if (Accumulate == 1 && col.Dynamic == 1 && col.BodyIndex >= 0)
                    {
                        // 记录入射流体速度与接触位置（归属最后命中的动态体），供桥接器求平均流速做相对速度阻力、求质心。
                        // Record incoming fluid velocity and contact position (attributed to the last hit body) for the bridge's
                        // average-flow drag and centroid.
                        // 入射流速と接触位置を記録（最後に命中した動的体に帰属）。
                        fluidVel = vIncoming;
                        contactPos = p;
                        hitBody = col.BodyIndex;
                        hitNormalY = n.y;
                    }
                }
            }

            Positions[i] = p;
            Velocities[i] = v;

            if (Accumulate == 1)
            {
                OutVelSum[k] = fluidVel;
                OutBody[k] = hitBody;
                OutContact[k] = contactPos;
                OutNormalY[k] = hitNormalY;
            }
        }
    }

    /// <summary>
    /// 销毁区域标记：对每个活动粒子（最终位置）遍历销毁区域，命中组过滤且点落在实心形状内时置 killFlags[k]=1。
    /// 复用 <see cref="Liquid2DColliderMath.Project"/>（particleRadius=0 即"点在形状内"判定）。实际回收 slot 在
    /// <see cref="Liquid2DSimulation"/> 的 Step 后进行。
    /// Dead-zone marking: for each active particle (final position) scan dead zones; when the group filter passes and the
    /// point lies inside the solid shape, set killFlags[k]=1. Reuses <see cref="Liquid2DColliderMath.Project"/>
    /// (particleRadius=0 = point-inside test). Slots are actually recycled after Step in <see cref="Liquid2DSimulation"/>.
    /// 破棄領域マーキング。各活動粒子（最終位置）について破棄領域を走査し、グループ絞り込みを通過かつ点が実心形状内なら
    /// killFlags[k]=1 を設定。
    /// </summary>
    [BurstCompile]
    public struct DeadZoneKillJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ActiveIndices;
        [ReadOnly] public NativeArray<float2> Positions;
        [ReadOnly] public NativeArray<int> GroupId;
        [ReadOnly] public NativeArray<Liquid2DDeadZoneData> DeadZones;
        [ReadOnly] public NativeArray<float2> Points;
        public int DeadZoneCount;
        [WriteOnly] public NativeArray<byte> KillFlags;

        public void Execute(int k)
        {
            int i = ActiveIndices[k];
            float2 p = Positions[i];
            int gi = GroupId[i];

            byte kill = 0;
            for (int z = 0; z < DeadZoneCount; z++)
            {
                var dz = DeadZones[z];
                // 组过滤：matchAll（空 nameTag）销毁全部，否则仅销毁 groupId 匹配的粒子。
                // Group filter: matchAll (empty nameTag) kills all; otherwise only matching groupId.
                // グループ絞り込み：matchAll は全破棄、それ以外は groupId 一致のみ。
                if (dz.MatchAll == 0 && dz.GroupId != gi) continue;
                // particleRadius=0 → "点是否在实心形状内"。Bounds 模式（invert）反转为"在形状外"。
                // particleRadius=0 → point-inside-solid test. Bounds mode (invert) flips it to "outside the shape".
                // particleRadius=0 → 点が実心形状内か。Bounds モード（invert）は"形状外"に反転。
                bool inside = Liquid2DColliderMath.Project(dz.Shape, Points, p, 0f, out _, out _);
                if (dz.Invert != 0) inside = !inside;
                if (inside)
                {
                    kill = 1;
                    break;
                }
            }

            KillFlags[k] = kill;
        }
    }

    /// <summary>
    /// 邻居混色（Jacobi 双缓冲：读 colors，写 colorsNext）。按 groupId 兼容、接触距离、时间间隔节流。
    /// 在最终位置上重建的网格上执行（grid 由 positions 构建）。
    /// Neighbor color mixing (Jacobi double-buffer). Gated by groupId compatibility, contact distance, per-particle time
    /// interval. Runs on a grid rebuilt from final positions.
    /// 近傍混色（Jacobi ダブルバッファ）。groupId 互換・接触距離・時間間隔でゲート。最終位置のグリッド上で実行。
    /// </summary>
    [BurstCompile]
    public struct MixColorJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> ActiveIndices;
        [ReadOnly] public NativeArray<float2> Positions;
        [ReadOnly] public NativeArray<float2> Velocities;
        [ReadOnly] public NativeArray<float4> Colors;
        [ReadOnly] public NativeArray<int> TypeId;
        [ReadOnly] public NativeArray<int> GroupId;
        [ReadOnly] public NativeArray<float> Radii;
        [ReadOnly] public NativeArray<Liquid2DMixData> MixData;
        [ReadOnly] public NativeArray<int> CellStart;
        [ReadOnly] public NativeArray<int> SortedSlots;
        public int TableSize;
        public float InvCellSize;
        public float Time;
        public int MixMode;
        [NativeDisableParallelForRestriction] public NativeArray<float> LastMixTime;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float4> ColorsNext;

        // ---- Oklab 转换（Björn Ottosson 标准实现）。输入/输出均为线性 sRGB [0,1]。 ----
        // Oklab conversion (Björn Ottosson standard). Input/output: linear sRGB [0,1].
        // Oklab 変換（標準実装）。入出力は線形 sRGB [0,1]。
        private static float3 SrgbLinearToOklab(float3 c)
        {
            float l = 0.4122214708f * c.x + 0.5363325363f * c.y + 0.0514459929f * c.z;
            float m = 0.2119034982f * c.x + 0.6806995451f * c.y + 0.1073969566f * c.z;
            float s = 0.0883024619f * c.x + 0.2817188376f * c.y + 0.6299787005f * c.z;
            float lc = sign(l) * pow(max(abs(l), 1e-10f), 1f / 3f);
            float mc = sign(m) * pow(max(abs(m), 1e-10f), 1f / 3f);
            float sc = sign(s) * pow(max(abs(s), 1e-10f), 1f / 3f);
            return new float3(
                0.2104542553f * lc + 0.7936177850f * mc - 0.0040720468f * sc,
                1.9779984951f * lc - 2.4285922050f * mc + 0.4505937099f * sc,
                0.0259040371f * lc + 0.7827717662f * mc - 0.8086757660f * sc);
        }

        private static float3 OklabToSrgbLinear(float3 lab)
        {
            float lc = lab.x + 0.3963377774f * lab.y + 0.2158037573f * lab.z;
            float mc = lab.x - 0.1055613458f * lab.y - 0.0638541728f * lab.z;
            float sc = lab.x - 0.0894841775f * lab.y - 1.2914855480f * lab.z;
            float l = lc * lc * lc;
            float m = mc * mc * mc;
            float s = sc * sc * sc;
            return new float3(
                +4.0767416621f * l - 3.3077115913f * m + 0.2309699292f * s,
                -1.2684380046f * l + 2.6097574011f * m - 0.3413193965f * s,
                -0.0041960863f * l - 0.7034186147f * m + 1.7076147010f * s);
        }

        // ---- Gossett-Chen RYB 颜料三线性立方体（2004）。输入/输出：sRGB [0,1]。 ----
        // Gossett-Chen RYB pigment trilinear cube (2004). Input/output: sRGB [0,1].
        // Gossett-Chen RYB 顔料三線形立方体（2004）。入出力は sRGB [0,1]。
        private static float3 RybToRgb(float3 ryb)
        {
            // 8 个 RYB 角点对应的 RGB 校准值（艺术家直觉优先）。 // 8 RYB corners → calibrated RGB. // 8 角点の校正 RGB 値。
            float3 c000 = new float3(1f,     1f,     1f    ); // 白 white    白
            float3 c100 = new float3(1f,     0f,     0f    ); // 红 red      赤
            float3 c010 = new float3(1f,     1f,     0f    ); // 黄 yellow   黄
            float3 c001 = new float3(0.163f, 0.373f, 0.6f  ); // 蓝 blue     青 (Prussian)
            float3 c110 = new float3(1f,     0.5f,   0f    ); // 橙 orange   橙
            float3 c101 = new float3(0.5f,   0f,     0.5f  ); // 紫 purple   紫
            float3 c011 = new float3(0f,     0.66f,  0.2f  ); // 绿 green    緑
            float3 c111 = new float3(0.2f,   0.094f, 0f    ); // 黑 black    黒
            float r = ryb.x, y = ryb.y, b = ryb.z;
            float3 c00 = lerp(c000, c100, r);
            float3 c01 = lerp(c001, c101, r);
            float3 c10 = lerp(c010, c110, r);
            float3 c11 = lerp(c011, c111, r);
            float3 c0  = lerp(c00, c10, y);
            float3 c1  = lerp(c01, c11, y);
            return lerp(c0, c1, b);
        }

        // RGB → RYB 逆映射（Newton-Raphson + 解析雅可比，5 次迭代达机器精度）。
        // 精确逆是关键：不精确会让 RybToRgb(RgbToRyb(c))≠c，残差经"亮→RYB坐标减小→更亮"的正反馈累积，
        // 把混合色逐渐冲到纯白。Newton 把色域内残差降到 ~0，反馈增益归零，杜绝白色爬升。
        // RGB → RYB inverse (Newton-Raphson + analytic Jacobian, 5 iters → machine precision).
        // An accurate inverse is essential: inexactness makes RybToRgb(RgbToRyb(c))≠c, and the residual feeds a
        // "brighter → smaller RYB → brighter" positive feedback that drifts mixed colours toward pure white.
        // RGB → RYB 逆写像（Newton 法 + 解析ヤコビアン、5 回で機械精度）。白へのドリフトを根絶。
        private static float3 RgbToRyb(float3 rgb)
        {
            // 8 个 RYB 角点对应 RGB（与 RybToRgb 一致）。 // 8 RYB corners → RGB (matches RybToRgb). // 8 角点。
            float3 c000 = new float3(1f, 1f, 1f);
            float3 c100 = new float3(1f, 0f, 0f);
            float3 c010 = new float3(1f, 1f, 0f);
            float3 c001 = new float3(0.163f, 0.373f, 0.6f);
            float3 c110 = new float3(1f, 0.5f, 0f);
            float3 c101 = new float3(0.5f, 0f, 0.5f);
            float3 c011 = new float3(0f, 0.66f, 0.2f);
            float3 c111 = new float3(0.2f, 0.094f, 0f);

            float3 ryb = new float3(0.5f, 0.5f, 0.5f);
            for (int it = 0; it < 5; it++)
            {
                float r = ryb.x, y = ryb.y, b = ryb.z;
                float3 e00 = lerp(c000, c100, r), e01 = lerp(c001, c101, r);
                float3 e10 = lerp(c010, c110, r), e11 = lerp(c011, c111, r);
                float3 e0 = lerp(e00, e10, y), e1 = lerp(e01, e11, y);
                float3 f = lerp(e0, e1, b);                                  // = RybToRgb(ryb)
                // 雅可比三列（∂f/∂r, ∂f/∂y, ∂f/∂b）。 // Jacobian columns. // ヤコビ列。
                float3 dr = lerp(lerp(c100 - c000, c110 - c010, y), lerp(c101 - c001, c111 - c011, y), b);
                float3 dy = lerp(e10 - e00, e11 - e01, b);
                float3 db = e1 - e0;
                // Cramer 法则解 J·step = (f - rgb)。 // Solve J·step = (f - rgb) via Cramer. // Cramer で解く。
                float3 res = f - rgb;
                float3 vw = cross(dy, db);
                float det = dot(dr, vw);
                float3 step = abs(det) > 1e-6f
                    ? new float3(dot(res, vw), dot(dr, cross(res, db)), dot(dr, cross(dy, res))) / det
                    : res * 0.7f;                                           // 退化时回退梯度步。 // gradient fallback.
                ryb = clamp(ryb - step, 0f, 1f);
            }
            return ryb;
        }

        public void Execute(int k)
        {
            int i = ActiveIndices[k];
            float4 ci = Colors[i];
            var mi = MixData[TypeId[i]];

            if (mi.Enabled == 0 || mi.Speed <= 0f) { ColorsNext[i] = ci; return; }
            if (Time - LastMixTime[i] < mi.Interval) { ColorsNext[i] = ci; return; }

            int gi = GroupId[i];
            float2 pi = Positions[i];
            float ri = Radii[i];

            // Oklab 模式在 Lab 空间逐粒子累积；其余模式在 RGB 空间累积。
            // Oklab mode accumulates per-particle in Lab space; other modes accumulate in RGB.
            float4 accumRgb = float4.zero;
            float3 accumLab = float3.zero;
            float accumAlpha = 0f;
            float wsum = 0f;

            int2 cc = Liquid2DHashGrid.CellCoord(pi, InvCellSize);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 qc = cc + new int2(dx, dy);
                int b = Liquid2DHashGrid.Hash(qc, TableSize);
                int start = CellStart[b];
                int end = CellStart[b + 1];
                for (int s = start; s < end; s++)
                {
                    int j = SortedSlots[s];
                    if (j == i) continue;
                    if (!Liquid2DHashGrid.CellCoord(Positions[j], InvCellSize).Equals(qc)) continue;
                    // group 兼容：相等或任一为 0（空 nameTag 通配）。 // group compatible: equal or either is 0 (empty nameTag wildcard). // group 互換。
                    int gj = GroupId[j];
                    if (gi != gj && gi != 0 && gj != 0) continue;
                    if (MixData[TypeId[j]].Enabled == 0) continue;
                    float contact = ri + Radii[j];
                    if (lengthsq(pi - Positions[j]) >= contact * contact) continue;

                    float4 cj = Colors[j];
                    if (MixMode == 1) // Oklab：在 Lab 空间累积。 // Oklab: accumulate in Lab space. // Lab 空間で累積。
                    {
                        accumLab += SrgbLinearToOklab(cj.xyz);
                        accumAlpha += cj.w;
                    }
                    else if (MixMode == 2) // Ryb：先转 RYB 空间再累积，避免 RGB 平均破坏色相。 // Ryb: convert to RYB first to preserve hue. // RYB 空間に変換してから累積。
                    {
                        accumRgb += new float4(RgbToRyb(cj.xyz), cj.w);
                    }
                    else // LinearRgb
                    {
                        accumRgb += cj;
                    }
                    wsum += 1f;
                }
            }

            if (wsum <= 0f) { ColorsNext[i] = ci; return; }

            float speed = mi.Speed;
            if (speed < 1f && mi.WithMovement == 1)
            {
                float vf = saturate(lengthsq(Velocities[i]) / max(1e-4f, mi.MaxSpeed * mi.MaxSpeed));
                speed = saturate(speed + vf * (1f - speed));
            }

            float4 avg;
            switch (MixMode)
            {
                case 1: // Oklab
                {
                    float3 labSelf = SrgbLinearToOklab(ci.xyz);
                    float3 labAvg = (accumLab + labSelf) / (wsum + 1f);
                    float3 rgbAvg = clamp(OklabToSrgbLinear(labAvg), 0f, 1f);
                    avg = new float4(rgbAvg, (accumAlpha + ci.w) / (wsum + 1f));
                    break;
                }
                case 2: // Ryb (Gossett-Chen 三线性立方体 + 锚定增量，消除往返误差导致的同色漂移/闪烁。)
                {
                    // accumRgb.xyz = 邻居 RYB 之和。锚定到自身：同色时增量恒为 0，结果精确等于 ci。
                    // accumRgb.xyz = sum of neighbour RYBs. Anchor to self: identical colours give a zero delta → exact ci.
                    // accumRgb.xyz = 近傍 RYB の和。自身にアンカー：同色は増分 0 → ci に厳密一致。
                    float3 selfRyb = RgbToRyb(ci.xyz);
                    float3 mixedRyb = (accumRgb.xyz + selfRyb) / (wsum + 1f);
                    float3 deltaRgb = RybToRgb(mixedRyb) - RybToRgb(selfRyb);
                    avg = new float4(clamp(ci.xyz + deltaRgb, 0f, 1f), (accumRgb.w + ci.w) / (wsum + 1f));
                    break;
                }
                default: // 0: LinearRgb
                    avg = (accumRgb + ci) / (wsum + 1f);
                    break;
            }

            ColorsNext[i] = lerp(ci, avg, speed);
            LastMixTime[i] = Time;
        }
    }
}

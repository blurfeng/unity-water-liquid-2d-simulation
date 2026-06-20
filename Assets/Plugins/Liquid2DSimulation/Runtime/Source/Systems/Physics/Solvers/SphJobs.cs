using Unity.Burst;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using Unity.Mathematics;
using static Unity.Mathematics.math;
// 类型别名：避免 `using static math` 引入的 floatN(...) 方法在类型位置遮蔽对应类型（CS0119）。
// Type aliases: prevent floatN(...) methods from `using static math` shadowing the corresponding types in type position (CS0119).
// 型エイリアス：`using static math` の floatN(...) メソッドが型位置で型を隠すのを防ぐ（CS0119）。
using float2 = Unity.Mathematics.float2;
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
        public byte enabled;
        public float speed;
        public byte withMovement;
        public float maxSpeed;
        public float interval;

        public static Liquid2DMixData Disabled => new Liquid2DMixData { enabled = 0, speed = 0f, withMovement = 0, maxSpeed = 100f, interval = 0.1f };
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
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<int> typeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> materials;
        [NativeDisableParallelForRestriction] public NativeArray<float2> velocities;
        [ReadOnly] public NativeArray<float2> positions;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float2> predicted;
        [ReadOnly] public NativeArray<Liquid2DForceFieldData> forceFields;
        public int forceFieldCount;
        public float2 gravity;
        public float dt;
        public float predictionFactor;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float gs = materials[typeId[i]].gravityScale;
            float2 pos = positions[i];
            float2 v = velocities[i];

            // 重力按力场重力衰减加权 + 累积径向/切向力场加速度 + 累积速度制动系数（结构对齐参考 ExternalForces）。
            // Gravity weighted by field gravity-attenuation + accumulated radial/swirl field accel + accumulated velocity
            // damping coefficient (structure matches the reference ExternalForces). Must stay in sync with the GPU kernel.
            // 重力を力場の重力減衰で重み付け + 径方向/接線方向の力場加速度を累積 + 速度制動係数を累積（参考と同構造）。
            float gWeight = 1f;
            float2 fieldAccel = float2.zero;
            float damp = 0f;

            for (int f = 0; f < forceFieldCount; f++)
            {
                var ff = forceFields[f];
                float2 offset = ff.center - pos;
                float sqrDst = lengthsq(offset);
                float r = ff.radius;
                if (sqrDst >= r * r) continue;

                float dst = sqrt(sqrDst);
                float centreT = 1f - dst / r;                  // 线性，1=中心 0=边缘。 // linear, 1=center 0=edge. // 線形。
                float profile = ff.mode == Liquid2DForceFieldMode.Constant
                    ? 1f
                    : (ff.falloff == 1f ? centreT : pow(centreT, ff.falloff));

                float2 dir = dst > 1e-6f ? offset / dst : float2.zero;
                float2 perp = new float2(-dir.y, dir.x);       // 逆时针切向。 // CCW tangent. // 反時計回り接線。

                fieldAccel += dir * (profile * ff.strength);
                fieldAccel += perp * (profile * ff.swirlStrength);
                gWeight = min(gWeight, 1f - centreT * ff.gravityAttenuation);
                damp += centreT * ff.velocityDamping;
            }

            v += (gravity * gs * gWeight + fieldAccel) * dt;
            if (damp > 0f) v -= v * saturate(damp * dt);

            velocities[i] = v;
            predicted[i] = pos + v * predictionFactor;
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
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> predicted;
        [ReadOnly] public NativeArray<int> cellStart;
        [ReadOnly] public NativeArray<int> sortedSlots;
        public int tableSize;
        public float invCellSize;
        public float h;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float2> densities;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float2 pi = predicted[i];
            float h2 = h * h;
            float c2 = KernelMath.SpikyPow2Coef(h);
            float c3 = KernelMath.SpikyPow3Coef(h);

            float density = 0f;
            float nearDensity = 0f;

            int2 ci = Liquid2DHashGrid.CellCoord(pi, invCellSize);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 qc = ci + new int2(dx, dy);
                int b = Liquid2DHashGrid.Hash(qc, tableSize);
                int start = cellStart[b];
                int end = cellStart[b + 1];
                for (int s = start; s < end; s++)
                {
                    int j = sortedSlots[s];
                    if (!Liquid2DHashGrid.CellCoord(predicted[j], invCellSize).Equals(qc)) continue;
                    float r2 = lengthsq(pi - predicted[j]);
                    if (r2 >= h2) continue;
                    float r = sqrt(r2);
                    density += KernelMath.SpikyPow2(r, h, c2);
                    nearDensity += KernelMath.SpikyPow3(r, h, c3);
                }
            }

            densities[i] = new float2(density, nearDensity);
        }
    }

    /// <summary>
    /// 压力力：pressure = (density - targetDensity·restDensityScale)·pressureMultiplier；
    /// nearPressure = nearDensity·nearPressureMultiplier·(0.5+cohesion)（cohesion 越高越易结团/表面张力）。
    /// 邻居梯度累加后 v += (pressureForce/density)·dt。
    /// Pressure force. Higher cohesion strengthens near-pressure (clumping / surface tension).
    /// 圧力力。cohesion が高いほど近圧力が強まり結団/表面張力に。
    /// </summary>
    [BurstCompile]
    public struct PressureForceJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> predicted;
        [ReadOnly] public NativeArray<float2> densities;
        [ReadOnly] public NativeArray<int> typeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> materials;
        [ReadOnly] public NativeArray<int> cellStart;
        [ReadOnly] public NativeArray<int> sortedSlots;
        public int tableSize;
        public float invCellSize;
        public float h;
        public float targetDensity;
        public float pressureMultiplier;
        public float nearPressureMultiplier;
        [NativeDisableParallelForRestriction] public NativeArray<float2> velocities;
        public float dt;

        // 由密度求该粒子的压力（含材质静止密度缩放）。 // Pressure from density (with material rest-density scale). // 密度から圧力。
        private float Pressure(float density, int slot)
        {
            float rho0 = targetDensity * materials[typeId[slot]].restDensityScale;
            return (density - rho0) * pressureMultiplier;
        }

        private float NearPressure(float nearDensity, int slot)
        {
            float coh = materials[typeId[slot]].cohesion;
            return nearDensity * nearPressureMultiplier * (0.5f + coh);
        }

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float2 pi = predicted[i];
            float densityI = densities[i].x;
            float nearI = densities[i].y;
            if (densityI <= 1e-8f) return;

            float pressureI = Pressure(densityI, i);
            float nearPressureI = NearPressure(nearI, i);

            float h2 = h * h;
            float cd2 = KernelMath.DerivSpikyPow2Coef(h);
            float cd3 = KernelMath.DerivSpikyPow3Coef(h);

            float2 pressureForce = float2.zero;

            int2 ci = Liquid2DHashGrid.CellCoord(pi, invCellSize);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 qc = ci + new int2(dx, dy);
                int b = Liquid2DHashGrid.Hash(qc, tableSize);
                int start = cellStart[b];
                int end = cellStart[b + 1];
                for (int s = start; s < end; s++)
                {
                    int j = sortedSlots[s];
                    if (j == i) continue;
                    if (!Liquid2DHashGrid.CellCoord(predicted[j], invCellSize).Equals(qc)) continue;
                    float2 offset = predicted[j] - pi;
                    float r2 = lengthsq(offset);
                    if (r2 >= h2) continue;
                    float r = sqrt(r2);
                    float2 dir = r > 1e-6f ? offset / r : new float2(0f, 1f);

                    float densityJ = densities[j].x;
                    float nearJ = densities[j].y;
                    if (densityJ <= 1e-8f || nearJ <= 1e-8f) continue;

                    float pressureJ = Pressure(densityJ, j);
                    float nearPressureJ = NearPressure(nearJ, j);

                    float sharedP = (pressureI + pressureJ) * 0.5f;
                    float sharedNear = (nearPressureI + nearPressureJ) * 0.5f;

                    pressureForce += dir * KernelMath.DerivSpikyPow2(r, h, cd2) * sharedP / densityJ;
                    pressureForce += dir * KernelMath.DerivSpikyPow3(r, h, cd3) * sharedNear / nearJ;
                }
            }

            velocities[i] += (pressureForce / densityI) * dt;
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
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> predicted;
        [ReadOnly] public NativeArray<float2> velocities;
        [ReadOnly] public NativeArray<int> typeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> materials;
        [ReadOnly] public NativeArray<int> cellStart;
        [ReadOnly] public NativeArray<int> sortedSlots;
        public int tableSize;
        public float invCellSize;
        public float h;
        public float viscosityStrength;
        public float dt;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float2> velNext;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float2 pi = predicted[i];
            float2 vi = velocities[i];
            float h2 = h * h;
            float c6 = KernelMath.Poly6Coef(h);
            float strength = viscosityStrength + materials[typeId[i]].viscosity;

            float2 force = float2.zero;
            int2 ci = Liquid2DHashGrid.CellCoord(pi, invCellSize);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 qc = ci + new int2(dx, dy);
                int b = Liquid2DHashGrid.Hash(qc, tableSize);
                int start = cellStart[b];
                int end = cellStart[b + 1];
                for (int s = start; s < end; s++)
                {
                    int j = sortedSlots[s];
                    if (j == i) continue;
                    if (!Liquid2DHashGrid.CellCoord(predicted[j], invCellSize).Equals(qc)) continue;
                    float r2 = lengthsq(pi - predicted[j]);
                    if (r2 >= h2) continue;
                    float r = sqrt(r2);
                    force += (velocities[j] - vi) * KernelMath.Poly6(r, h, c6);
                }
            }

            velNext[i] = vi + force * strength * dt;
        }
    }

    /// <summary>把 velNext 拷回 velocities（活动粒子）。 // Copy velNext back to velocities (active). // velNext を velocities に戻す。</summary>
    [BurstCompile]
    public struct CopyVelocityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> velNext;
        [NativeDisableParallelForRestriction] public NativeArray<float2> velocities;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            velocities[i] = velNext[i];
        }
    }

    /// <summary>
    /// 积分 + 碰撞：pos += v·dt；遍历碰撞体 <see cref="Liquid2DColliderMath.Project"/> 推出、沿法线反射速度
    /// （回弹 = restitution × collisionDamping）、切向摩擦（friction）、动态体累积反作用冲量（双向耦合）。
    /// Integrate + collide: pos += v·dt; project out of colliders, reflect normal velocity (bounce = restitution ×
    /// collisionDamping), apply tangential friction, accumulate dynamic-body reaction impulse (two-way coupling).
    /// 積分 + 衝突。コライダーから押し出し、法線速度を反射、接線摩擦、動的体へ反作用力積を累積。
    /// </summary>
    [BurstCompile]
    public struct SphIntegrateCollideJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [NativeDisableParallelForRestriction] public NativeArray<float2> positions;
        [NativeDisableParallelForRestriction] public NativeArray<float2> velocities;
        [ReadOnly] public NativeArray<float> radii;
        [ReadOnly] public NativeArray<float> invMass;
        [ReadOnly] public NativeArray<int> typeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> materials;
        [ReadOnly] public NativeArray<Liquid2DColliderData> colliders;
        [ReadOnly] public NativeArray<float2> points;
        public float dt;
        public float collisionDamping;
        /// <summary>最大速度限制（0 = 不限制）。 // Max speed clamp (0 = unlimited). // 最大速度制限（0 = 無制限）。</summary>
        public float maxSpeed;
        public byte hasColliders;
        public byte accumulate;                            // 1 时记录冲量。 // record impulse when 1. // 1 のとき力積記録。
        [WriteOnly] public NativeArray<float2> outImpulse;  // 长度 activeCount。 // length activeCount.
        [WriteOnly] public NativeArray<int> outBody;        // 长度 activeCount，-1 表示无。 // length activeCount, -1 = none.

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float2 v = velocities[i];

            // 速度限幅：防止压力爆炸导致粒子速度失控乱飞。
            // Speed clamp: prevents runaway particle velocities from pressure explosions.
            // 速度制限：圧力爆発によるパーティクル速度暴走を防ぐ。
            if (maxSpeed > 0f)
            {
                float spd2 = dot(v, v);
                if (spd2 > maxSpeed * maxSpeed)
                    v = v * (maxSpeed / sqrt(spd2));
            }

            float2 p = positions[i] + v * dt;

            float2 totalImpulse = float2.zero;
            int hitBody = -1;

            if (hasColliders == 1)
            {
                float pr = radii[i];
                var mat = materials[typeId[i]];
                float e = saturate(mat.restitution) * collisionDamping;

                for (int ci = 0; ci < colliders.Length; ci++)
                {
                    var col = colliders[ci];
                    if (!Liquid2DColliderMath.Project(col, points, p, pr, out float2 corr, out float2 n)) continue;

                    p += corr;

                    // 速度反射：去掉指向表面内的法向分量，并按回弹系数反弹。 // Reflect velocity: remove inward normal component, bounce by e. // 速度反射。
                    float vn = dot(v, n);
                    if (vn < 0f) v -= (1f + e) * vn * n;

                    // 摩擦：阻尼切向分量。 // Friction: damp tangential component. // 摩擦：接線減衰。
                    if (mat.friction > 0f)
                    {
                        float2 vt = v - dot(v, n) * n;
                        v -= vt * mat.friction;
                    }

                    if (accumulate == 1 && col.dynamic == 1 && col.bodyIndex >= 0)
                    {
                        float mass = invMass[i] > 1e-6f ? 1f / invMass[i] : 0f;
                        totalImpulse += -n * (length(corr) * mass / max(dt, 1e-5f));
                        hitBody = col.bodyIndex;
                    }
                }
            }

            positions[i] = p;
            velocities[i] = v;

            if (accumulate == 1)
            {
                outImpulse[k] = totalImpulse;
                outBody[k] = hitBody;
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
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> positions;
        [ReadOnly] public NativeArray<int> groupId;
        [ReadOnly] public NativeArray<Liquid2DDeadZoneData> deadZones;
        [ReadOnly] public NativeArray<float2> points;
        public int deadZoneCount;
        [WriteOnly] public NativeArray<byte> killFlags;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float2 p = positions[i];
            int gi = groupId[i];

            byte kill = 0;
            for (int z = 0; z < deadZoneCount; z++)
            {
                var dz = deadZones[z];
                // 组过滤：matchAll（空 nameTag）销毁全部，否则仅销毁 groupId 匹配的粒子。
                // Group filter: matchAll (empty nameTag) kills all; otherwise only matching groupId.
                // グループ絞り込み：matchAll は全破棄、それ以外は groupId 一致のみ。
                if (dz.matchAll == 0 && dz.groupId != gi) continue;
                // particleRadius=0 → "点是否在实心形状内"。Bounds 模式（invert）反转为"在形状外"。
                // particleRadius=0 → point-inside-solid test. Bounds mode (invert) flips it to "outside the shape".
                // particleRadius=0 → 点が実心形状内か。Bounds モード（invert）は"形状外"に反転。
                bool inside = Liquid2DColliderMath.Project(dz.shape, points, p, 0f, out _, out _);
                if (dz.invert != 0) inside = !inside;
                if (inside)
                {
                    kill = 1;
                    break;
                }
            }

            killFlags[k] = kill;
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
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> positions;
        [ReadOnly] public NativeArray<float2> velocities;
        [ReadOnly] public NativeArray<float4> colors;
        [ReadOnly] public NativeArray<int> typeId;
        [ReadOnly] public NativeArray<int> groupId;
        [ReadOnly] public NativeArray<float> radii;
        [ReadOnly] public NativeArray<Liquid2DMixData> mixData;
        [ReadOnly] public NativeArray<int> cellStart;
        [ReadOnly] public NativeArray<int> sortedSlots;
        public int tableSize;
        public float invCellSize;
        public float time;
        [NativeDisableParallelForRestriction] public NativeArray<float> lastMixTime;
        [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<float4> colorsNext;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float4 ci = colors[i];
            var mi = mixData[typeId[i]];

            if (mi.enabled == 0 || mi.speed <= 0f)
            {
                colorsNext[i] = ci;
                return;
            }
            if (time - lastMixTime[i] < mi.interval)
            {
                colorsNext[i] = ci;
                return;
            }

            int gi = groupId[i];
            float2 pi = positions[i];
            float ri = radii[i];

            float4 accum = float4.zero;
            float wsum = 0f;
            int2 cc = Liquid2DHashGrid.CellCoord(pi, invCellSize);
            for (int dy = -1; dy <= 1; dy++)
            for (int dx = -1; dx <= 1; dx++)
            {
                int2 qc = cc + new int2(dx, dy);
                int b = Liquid2DHashGrid.Hash(qc, tableSize);
                int start = cellStart[b];
                int end = cellStart[b + 1];
                for (int s = start; s < end; s++)
                {
                    int j = sortedSlots[s];
                    if (j == i) continue;
                    if (!Liquid2DHashGrid.CellCoord(positions[j], invCellSize).Equals(qc)) continue;
                    // group 兼容：相等或任一为 0（空 nameTag 通配）。 // group compatible: equal or either is 0 (empty nameTag wildcard). // group 互換。
                    int gj = groupId[j];
                    if (gi != gj && gi != 0 && gj != 0) continue;
                    if (mixData[typeId[j]].enabled == 0) continue;
                    float contact = ri + radii[j];
                    if (lengthsq(pi - positions[j]) >= contact * contact) continue;
                    accum += colors[j];
                    wsum += 1f;
                }
            }

            if (wsum <= 0f)
            {
                colorsNext[i] = ci;
                return;
            }

            float4 avg = (accum + ci) / (wsum + 1f);
            float speed = mi.speed;
            if (speed < 1f && mi.withMovement == 1)
            {
                float vf = saturate(lengthsq(velocities[i]) / max(1e-4f, mi.maxSpeed * mi.maxSpeed));
                speed = saturate(speed + vf * (1f - speed));
            }
            colorsNext[i] = lerp(ci, avg, speed);
            lastMixTime[i] = time;
        }
    }
}

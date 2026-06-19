using Unity.Burst;
using Unity.Collections;
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
    /// 2D SPH 光滑核（poly6 密度核 + spiky 梯度核）。
    /// 2D SPH smoothing kernels (poly6 density + spiky gradient).
    /// 2D SPH 平滑核（poly6 密度核 + spiky 勾配核）。
    /// </summary>
    internal static class KernelMath
    {
        public const float PI = 3.14159265358979f;

        public static float Poly6Coef(float h) => 4f / (PI * pow(h, 8f));
        public static float SpikyGradCoef(float h) => -30f / (PI * pow(h, 5f));

        /// <summary>poly6 核值。 // poly6 value. // poly6 値。</summary>
        public static float Poly6(float r2, float h2, float coef)
        {
            if (r2 >= h2) return 0f;
            float d = h2 - r2;
            return coef * d * d * d;
        }

        /// <summary>spiky 梯度向量，rij=pi-pj。 // spiky gradient vector, rij=pi-pj. // spiky 勾配ベクトル。</summary>
        public static float2 SpikyGrad(float2 rij, float r, float h, float coef)
        {
            if (r >= h || r < 1e-6f) return float2.zero;
            float m = coef * (h - r) * (h - r);
            return m * (rij / r);
        }
    }

    /// <summary>预测：施加重力，predicted = pos + v*dt。 // Predict: apply gravity, predicted = pos + v*dt. // 予測。</summary>
    [BurstCompile]
    public struct PredictJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<int> typeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> materials;
        public NativeArray<float2> velocities;
        public NativeArray<float2> positions;
        [WriteOnly] public NativeArray<float2> predicted;
        public float2 gravity;
        public float dt;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float gs = materials[typeId[i]].gravityScale;
            float2 v = velocities[i] + gravity * gs * dt;
            velocities[i] = v;
            predicted[i] = positions[i] + v * dt;
        }
    }

    /// <summary>密度与 λ。 // Density and λ. // 密度と λ。</summary>
    [BurstCompile]
    public struct DensityLambdaJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> predicted;
        [ReadOnly] public NativeArray<int> typeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> materials;
        [ReadOnly] public NativeArray<int> cellStart;
        [ReadOnly] public NativeArray<int> sortedSlots;
        public int tableSize;
        public float invCellSize;
        public float h;
        public float restDensity;
        public float relaxEps;
        [WriteOnly] public NativeArray<float> lambda;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float2 pi = predicted[i];
            float h2 = h * h;
            float poly6 = KernelMath.Poly6Coef(h);
            float spiky = KernelMath.SpikyGradCoef(h);

            float density = 0f;
            float2 gradISum = float2.zero;
            float sumGrad2 = 0f;

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
                    float2 rij = pi - predicted[j];
                    float r2 = lengthsq(rij);
                    if (r2 >= h2) continue;
                    density += KernelMath.Poly6(r2, h2, poly6);
                    if (j == i) continue;
                    float r = sqrt(r2);
                    float2 grad = KernelMath.SpikyGrad(rij, r, h, spiky); // ∇_i W
                    gradISum += grad;
                    sumGrad2 += dot(grad, grad); // |∇_j C|² 项（j≠i）。 // |∇_j C|² term. // |∇_j C|² 項。
                }
            }

            float rho0 = restDensity * materials[typeId[i]].restDensityScale;
            float invRho0 = rho0 > 1e-6f ? 1f / rho0 : 0f;
            float c = density * invRho0 - 1f;
            // Σ|∇C|² = (1/ρ0²)(|Σ∇W|² + Σ|∇W|²)。 // sum of gradient magnitudes squared. // 勾配二乗和。
            float sumGradC2 = invRho0 * invRho0 * (dot(gradISum, gradISum) + sumGrad2);
            lambda[i] = -c / (sumGradC2 + relaxEps);
        }
    }

    /// <summary>位置修正 Δp（含人工压力 s_corr）。 // Position correction Δp (with artificial pressure). // 位置補正 Δp。</summary>
    [BurstCompile]
    public struct DeltaPositionJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> predicted;
        [ReadOnly] public NativeArray<float> lambda;
        [ReadOnly] public NativeArray<int> typeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> materials;
        [ReadOnly] public NativeArray<int> cellStart;
        [ReadOnly] public NativeArray<int> sortedSlots;
        public int tableSize;
        public float invCellSize;
        public float h;
        public float restDensity;
        public float sCorrK;
        public float sCorrN;
        public float sCorrDqRatio;
        [WriteOnly] public NativeArray<float2> deltaP;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float2 pi = predicted[i];
            float h2 = h * h;
            float poly6 = KernelMath.Poly6Coef(h);
            float spiky = KernelMath.SpikyGradCoef(h);
            float dq = sCorrDqRatio * h;
            float wDq = KernelMath.Poly6(dq * dq, h2, poly6);
            float invWDq = wDq > 1e-12f ? 1f / wDq : 0f;
            float li = lambda[i];

            // 内聚力越高，人工压力越强（结团/液滴）。 // Higher cohesion → stronger artificial pressure (clumping). // 内聚力が高いほど人工圧力が強い。
            float kCorr = sCorrK * (0.5f + materials[typeId[i]].cohesion);

            float2 sum = float2.zero;
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
                    float2 rij = pi - predicted[j];
                    float r2 = lengthsq(rij);
                    if (r2 >= h2) continue;
                    float r = sqrt(r2);
                    float2 grad = KernelMath.SpikyGrad(rij, r, h, spiky);
                    float w = KernelMath.Poly6(r2, h2, poly6);
                    float scorr = -kCorr * pow(max(0f, w * invWDq), sCorrN);
                    sum += (li + lambda[j] + scorr) * grad;
                }
            }

            float rho0 = restDensity * materials[typeId[i]].restDensityScale;
            float invRho0 = rho0 > 1e-6f ? 1f / rho0 : 0f;
            deltaP[i] = sum * invRho0;
        }
    }

    /// <summary>应用 Δp 到预测位置。 // Apply Δp to predicted positions. // Δp を予測位置に適用。</summary>
    [BurstCompile]
    public struct ApplyDeltaJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> deltaP;
        public NativeArray<float2> predicted;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            predicted[i] += deltaP[i];
        }
    }

    /// <summary>
    /// 碰撞体投影：把预测位置推出所有碰撞体；应用摩擦/回弹；可选累积反作用冲量（双向耦合）。
    /// Collider projection: push predicted positions out of all colliders; apply friction/restitution; optionally
    /// accumulate reaction impulse (two-way coupling).
    /// コライダー投影：予測位置を全コライダーの外へ押し出し、摩擦/反発を適用、任意で反作用力積を累積。
    /// </summary>
    [BurstCompile]
    public struct CollisionJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> positions;
        [ReadOnly] public NativeArray<int> typeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> materials;
        [ReadOnly] public NativeArray<float> radii;
        [ReadOnly] public NativeArray<float> invMass;
        [ReadOnly] public NativeArray<Liquid2DColliderData> colliders;
        [ReadOnly] public NativeArray<float2> points;
        public NativeArray<float2> predicted;
        public float dt;
        public byte accumulate; // 1 时记录冲量。 // record impulse when 1. // 1 のとき力積記録。
        [WriteOnly] public NativeArray<float2> outImpulse; // 长度 activeCount。 // length activeCount. // 長さ activeCount。
        [WriteOnly] public NativeArray<int> outBody;       // 长度 activeCount，-1 表示无。 // length activeCount, -1 = none. // 長さ activeCount。

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float2 p = predicted[i];
            float pr = radii[i];
            var mat = materials[typeId[i]];

            float2 totalImpulse = float2.zero;
            int hitBody = -1;

            for (int ci = 0; ci < colliders.Length; ci++)
            {
                var col = colliders[ci];
                if (Liquid2DColliderMath.Project(col, points, p, pr, out float2 corr, out float2 n))
                {
                    p += corr;

                    // 摩擦：阻尼相对起点的切向位移。 // Friction: damp tangential displacement relative to start. // 摩擦：始点に対する接線変位を減衰。
                    if (mat.friction > 0f)
                    {
                        float2 disp = p - positions[i];
                        float2 tangent = disp - dot(disp, n) * n;
                        p -= tangent * mat.friction;
                    }

                    if (accumulate == 1 && col.dynamic == 1 && col.bodyIndex >= 0)
                    {
                        float mass = invMass[i] > 1e-6f ? 1f / invMass[i] : 0f;
                        // 反作用冲量：沿穿透反方向，量级 ~ 质量×穿透/dt。 // Reaction impulse opposite to push-out. // 反作用力積。
                        totalImpulse += -n * (length(corr) * mass / max(dt, 1e-5f));
                        hitBody = col.bodyIndex;
                    }
                }
            }

            predicted[i] = p;

            if (accumulate == 1)
            {
                outImpulse[k] = totalImpulse;
                outBody[k] = hitBody;
            }
        }
    }

    /// <summary>定速：v=(predicted-pos)/dt; pos=predicted。 // Finalize velocity. // 速度確定。</summary>
    [BurstCompile]
    public struct FinalizeVelocityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> predicted;
        public NativeArray<float2> positions;
        [WriteOnly] public NativeArray<float2> velocities;
        public float invDt;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            velocities[i] = (predicted[i] - positions[i]) * invDt;
            positions[i] = predicted[i];
        }
    }

    /// <summary>XSPH 粘性（写入 velNext）。 // XSPH viscosity (writes velNext). // XSPH 粘性。</summary>
    [BurstCompile]
    public struct ViscosityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> positions;
        [ReadOnly] public NativeArray<float2> velocities;
        [ReadOnly] public NativeArray<int> typeId;
        [ReadOnly] public NativeArray<Liquid2DMaterialData> materials;
        [ReadOnly] public NativeArray<int> cellStart;
        [ReadOnly] public NativeArray<int> sortedSlots;
        public int tableSize;
        public float invCellSize;
        public float h;
        public float globalViscosity;
        [WriteOnly] public NativeArray<float2> velNext;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            float2 pi = positions[i];
            float2 vi = velocities[i];
            float h2 = h * h;
            float poly6 = KernelMath.Poly6Coef(h);
            float coeff = globalViscosity + materials[typeId[i]].viscosity;

            float2 accum = float2.zero;
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
                    if (!Liquid2DHashGrid.CellCoord(positions[j], invCellSize).Equals(qc)) continue;
                    float r2 = lengthsq(pi - positions[j]);
                    if (r2 >= h2) continue;
                    accum += (velocities[j] - vi) * KernelMath.Poly6(r2, h2, poly6);
                }
            }

            velNext[i] = vi + coeff * accum;
        }
    }

    /// <summary>把 velNext 拷回 velocities（活动粒子）。 // Copy velNext back to velocities (active). // velNext を velocities に戻す。</summary>
    [BurstCompile]
    public struct CopyVelocityJob : IJobParallelFor
    {
        [ReadOnly] public NativeArray<int> activeIndices;
        [ReadOnly] public NativeArray<float2> velNext;
        public NativeArray<float2> velocities;

        public void Execute(int k)
        {
            int i = activeIndices[k];
            velocities[i] = velNext[i];
        }
    }

    /// <summary>
    /// 邻居混色（Jacobi 双缓冲：读 colors，写 colorsNext）。按 groupId 兼容、接触距离、时间间隔节流。
    /// Neighbor color mixing (Jacobi double-buffer: read colors, write colorsNext). Gated by groupId compatibility,
    /// contact distance, and a per-particle time interval.
    /// 近傍混色（Jacobi ダブルバッファ）。groupId 互換・接触距離・時間間隔でゲート。
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
        public NativeArray<float> lastMixTime;
        [WriteOnly] public NativeArray<float4> colorsNext;

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

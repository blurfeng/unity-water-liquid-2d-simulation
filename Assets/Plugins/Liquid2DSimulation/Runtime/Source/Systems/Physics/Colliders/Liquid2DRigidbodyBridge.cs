using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体反作用力 → Unity <see cref="Rigidbody2D"/> 的桥接器。把每帧由流体累积的 <see cref="Liquid2DBodyForce"/>
    /// （净冲量 + 接触质心 + 接触数 + 平均流体密度）施加到刚体上，实现「水流冲走物体」「物体在水中漂浮/随波晃动」。
    /// 同物体（或父级）挂了本组件的 <see cref="Liquid2DCollider"/> 即被视为动态体；不挂则为静态。
    /// 浮力按阿基米德原理：浮力 = ρ流体 · g · 体积 · 浸没比例；物体密度（mass/体积）小于流体密度则上浮、大于则下沉。
    /// Bridges fluid reaction forces to a Unity <see cref="Rigidbody2D"/>. Applies the per-frame
    /// <see cref="Liquid2DBodyForce"/> (net impulse + contact centroid + count + avg fluid density) to the body, enabling
    /// "water sweeps away objects" and "objects floating / bobbing in water". A <see cref="Liquid2DCollider"/> on the same
    /// object (or a parent) carrying this component is treated as a dynamic body. Buoyancy follows Archimedes:
    /// force = fluidDensity · g · volume · submergedFraction; a body floats when its density (mass/volume) is below the
    /// fluid's, sinks when above.
    /// 流体反作用力を Unity <see cref="Rigidbody2D"/> へ橋渡し。アルキメデス浮力で物体密度により浮沈します。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Liquid2DRigidbodyBridge : MonoBehaviour, ILiquid2DForceReceiver
    {
        [SerializeField, Min(0f), LocalizationTooltip(
             "冲走力缩放。放大/缩小流体对物体的推动冲量（被水流冲走的强度）。",
             "Wash force scale. Scales the fluid's pushing impulse on the body (how strongly it gets swept away).",
             "押し流し力のスケール。流体が物体を押す力積を拡大/縮小します（流される強さ）。")]
        private float forceScale = 1f;

        [SerializeField, LocalizationTooltip(
             "在接触质心处施力以产生力矩（物体会因受力不均而旋转/翻倒）。⚠ 接触质心有噪声，开启易导致乱转；默认关闭，所有力施加在质心、只平移不旋转。",
             "Apply force at the contact centroid to produce torque (uneven force rotates/tips the body). ⚠ The centroid is noisy and can cause spinning; off by default (all forces at center of mass, translate only).",
             "接触質心で力を加え力矩を生む。⚠ 質心はノイズが多く回転を招きやすいため既定はオフ（全て質心で平行移動のみ）。")]
        private bool applyTorque;

        [SerializeField, Min(0f), LocalizationTooltip(
             "单步速度变化上限（世界单位/秒，0=不限制）。钳制流体每个物理步对本物体速度的改变量，防止 SPH 偶发尖峰把物体弹飞。",
             "Max speed change per step (world units/s; 0 = unlimited). Clamps how much the fluid can change this body's velocity per physics step, preventing SPH spikes from flinging it.",
             "1 ステップの速度変化上限（ワールド単位/秒、0=無制限）。流体が 1 物理ステップで与える速度変化を制限します。")]
        private float maxSpeedChange = 4f;

        [Header("Buoyancy")]
        [SerializeField, LocalizationTooltip(
             "启用阿基米德浮力。物体密度（mass/体积）小于流体密度则上浮、大于则下沉。",
             "Enable Archimedes buoyancy. A body floats when its density (mass/volume) is below the fluid's, sinks when above.",
             "アルキメデス浮力を有効化。物体密度（mass/体積）が流体密度より小さいと浮き、大きいと沈む。")]
        private bool useBuoyancy = true;

        [SerializeField, Min(0f), LocalizationTooltip(
             "物体体积（2D 面积，用作排开体积）。0=自动从 Liquid2DCollider 形状求和。手填可覆盖自动值。",
             "Body volume (2D area, the displaced volume). 0 = auto-sum from Liquid2DCollider shapes. Set a value to override.",
             "物体の体積（2D 面積、排除体積）。0=Liquid2DCollider 形状から自動合計。手入力で上書き。")]
        private float bodyVolume;

        [SerializeField, Min(1f), LocalizationTooltip(
             "视为「完全浸没」的接触粒子数。浸没比例 = 接触数 / 此值（封顶 1）。越小越容易判定为满浸、浮力越强。",
             "Contact particle count treated as 'fully submerged'. Submerged fraction = contactCount / this (capped at 1). Lower = reaches full submersion sooner, stronger buoyancy.",
             "「完全浸水」とみなす接触粒子数。浸水率 = 接触数 / この値（上限 1）。小さいほど早く満浸・浮力が強い。")]
        private float fullSubmersionContacts = 12f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "浸没线性阻尼。物体接触流体时按此系数衰减线速度，消除抖动、实现稳定漂浮。",
             "Submerged linear drag. Damps linear velocity by this coefficient while contacting fluid; removes jitter for stable floating.",
             "浸水時の線形減衰。流体接触中に線速度を減衰し、安定浮遊。")]
        private float submergedLinearDrag = 1f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "浸没角阻尼。物体接触流体时按此系数衰减角速度，抑制旋转抖动。",
             "Submerged angular drag. Damps angular velocity by this coefficient while contacting fluid; suppresses spin jitter.",
             "浸水時の角減衰。流体接触中に角速度を減衰し、回転の振動を抑えます。")]
        private float submergedAngularDrag = 1f;

        private Rigidbody2D _rb;
        private float _autoVolume = -1f; // 懒计算缓存（<0 表示未算）。 // lazy cache (<0 = not computed). // 遅延キャッシュ。

        // 复用的托管暂存（仅自动体积计算用，一次性）。 // Reused scratch for auto-volume (one-shot). // 自動体積用スクラッチ。
        private static readonly List<Liquid2DColliderData> _areaData = new List<Liquid2DColliderData>(8);
        private static readonly List<float2> _areaPoints = new List<float2>(32);

        protected virtual void Awake()
        {
            _rb = GetComponent<Rigidbody2D>();
        }

        /// <summary>有效体积：手填 &gt; 0 时用手填，否则自动从碰撞器求和（缓存）。 // Effective volume. // 有効体積。</summary>
        public float EffectiveVolume()
        {
            if (bodyVolume > 0f) return bodyVolume;
            if (_autoVolume < 0f) _autoVolume = ComputeAutoVolume();
            return _autoVolume;
        }

        /// <summary>物体密度（mass / 体积），用于与流体密度比较决定沉浮。 // Body density (mass/volume). // 物体密度。</summary>
        public float BodyDensity
        {
            get
            {
                float v = EffectiveVolume();
                float mass = _rb ? _rb.mass : (TryGetComponent(out Rigidbody2D rb) ? rb.mass : 1f);
                return v > 1e-6f ? mass / v : 0f;
            }
        }

        /// <summary>从所有子 <see cref="Liquid2DCollider"/> 形状求和世界面积（2D 体积）。 // Sum world area of child colliders. // 子コライダー面積合計。</summary>
        public float ComputeAutoVolume()
        {
            var cols = GetComponentsInChildren<Liquid2DCollider>(false);
            float area = 0f;
            for (int ci = 0; ci < cols.Length; ci++)
            {
                var col = cols[ci];
                if (!col) continue;
                col.EnsureInitialized();
                _areaData.Clear();
                _areaPoints.Clear();
                col.FillAll(_areaData, _areaPoints);
                for (int i = 0; i < _areaData.Count; i++)
                    area += Liquid2DColliderMath.ComputeArea(_areaData[i], _areaPoints);
            }
            return area;
        }

        /// <inheritdoc/>
        public void ApplyLiquidForces(in Liquid2DBodyForce force)
        {
            if (!_rb) return;

            float dt = force.Dt > 1e-6f ? force.Dt : Time.fixedDeltaTime;
            bool submerged = force.ContactCount > 0;
            Vector2 center = submerged ? new Vector2(force.ContactCenter.x, force.ContactCenter.y) : _rb.worldCenterOfMass;

            // 冲走：净反作用冲量。接触质心处施力以带动力矩。 // Wash: net reaction impulse, applied at the contact centroid for torque. // 押し流し。
            Vector2 impulse = new Vector2(force.Impulse.x, force.Impulse.y) * forceScale;
            // 安全钳制：限制单步速度变化（impulse / mass），防止 SPH 尖峰把物体弹飞。 // Safety clamp: cap per-step Δv. // 安全制限。
            if (maxSpeedChange > 0f)
            {
                float maxImp = maxSpeedChange * _rb.mass;
                if (impulse.sqrMagnitude > maxImp * maxImp) impulse = impulse.normalized * maxImp;
            }
            if (impulse.sqrMagnitude > 1e-10f)
            {
                if (applyTorque && submerged) _rb.AddForceAtPosition(impulse, center, ForceMode2D.Impulse);
                else _rb.AddForce(impulse, ForceMode2D.Impulse);
            }

            if (!submerged) return;

            // 浮力（阿基米德）：浮力 = ρ流体 · g · 体积 · 浸没比例；在质心处施加 → 物体密度决定沉浮，且自然产生扶正力矩。
            // Buoyancy (Archimedes): force = fluidDensity · g · volume · submergedFraction; applied at the centroid →
            // body density decides float/sink and yields a natural righting torque.
            // 浮力（アルキメデス）：浮力 = ρ流体 · g · 体積 · 浸水率。質心で施加。
            if (useBuoyancy && force.FluidDensity > 0f)
            {
                float gMag = Physics2D.gravity.magnitude;
                Vector2 up = gMag > 1e-6f ? -(Vector2)Physics2D.gravity / gMag : Vector2.up;
                float s = fullSubmersionContacts > 0f ? Mathf.Clamp01(force.ContactCount / fullSubmersionContacts) : 1f;
                float buoyForce = force.FluidDensity * gMag * EffectiveVolume() * s; // 阿基米德浮力幅值。 // Archimedes magnitude. // 浮力の大きさ。
                if (buoyForce > 0f) _rb.AddForceAtPosition(up * (buoyForce * dt), center, ForceMode2D.Impulse);
            }

            // 浸没阻尼：消除抖动，稳定漂浮。 // Submerged drag: removes jitter for stable floating. // 浸水減衰。
            if (submergedLinearDrag > 0f) _rb.linearVelocity *= Mathf.Clamp01(1f - submergedLinearDrag * dt);
            if (submergedAngularDrag > 0f) _rb.angularVelocity *= Mathf.Clamp01(1f - submergedAngularDrag * dt);
        }
    }
}

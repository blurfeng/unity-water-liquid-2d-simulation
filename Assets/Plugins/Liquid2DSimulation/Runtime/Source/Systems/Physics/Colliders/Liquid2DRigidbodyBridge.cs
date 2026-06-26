using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 流体作用力 → Unity <see cref="Rigidbody2D"/> 的桥接器。把每帧由流体累积的 <see cref="Liquid2DBodyForce"/>
    /// （平均流体速度 + 接触质心 + 接触数 + 平均流体密度）转成两类力施加到刚体上：
    /// ① 冲走 = 相对速度阻力（F ∝ 流体速度 − 物体速度，流体静止时趋 0），② 漂浮 = 阿基米德浮力。
    /// 同物体（或父级）挂了本组件的 <see cref="Liquid2DCollider"/> 即被视为动态体；不挂则为静态。
    /// 浮力按阿基米德原理：浮力 = ρ流体 · g · 体积 · 浸没比例；物体密度（mass/体积）小于流体密度则上浮、大于则下沉。
    /// Bridges fluid forces to a Unity <see cref="Rigidbody2D"/>. Each frame it turns the accumulated
    /// <see cref="Liquid2DBodyForce"/> (avg fluid velocity + contact centroid + count + avg fluid density) into two forces:
    /// (1) wash = relative-velocity drag (F ∝ fluidVel − bodyVel, → 0 when the fluid is still), and (2) float = Archimedes
    /// buoyancy. A <see cref="Liquid2DCollider"/> on the same object (or a parent) carrying this component is treated as a
    /// dynamic body. Buoyancy: force = fluidDensity · g · volume · submergedFraction; a body floats when its density
    /// (mass/volume) is below the fluid's, sinks when above.
    /// 流体力を Unity <see cref="Rigidbody2D"/> へ橋渡し。①相対速度抗力（押し流し）②アルキメデス浮力（浮沈）の 2 種類の力に変換。
    /// </summary>
    [RequireComponent(typeof(Rigidbody2D))]
    public class Liquid2DRigidbodyBridge : MonoBehaviour, ILiquid2DForceReceiver
    {
        [SerializeField, Min(0f), LocalizationTooltip(
             "阻力系数（冲走强度）。冲走力 = 此系数 · 浸没比例 · (流体速度 − 物体速度)。越大越快被水流带动；流体静止时趋于 0、不会无端推动物体。",
             "Drag coefficient (wash strength). Wash force = this · submergedFraction · (fluidVelocity − bodyVelocity). Larger = carried by the flow faster; tends to 0 when the fluid is still, so the body isn't pushed for no reason.",
             "抗力係数（押し流し強度）。押し流し力 = 係数 · 浸水率 · (流体速度 − 物体速度)。大きいほど流れに乗りやすい。流体静止時は 0 に近づき無駄に押されない。")]
        private float dragCoefficient = 3f;

        [SerializeField, LocalizationTooltip(
             "在接触质心处施加阻力以产生力矩（来流偏置一侧时物体会旋转/翻倒/摇摆）。关闭则在质心施力、只平移不旋转。",
             "Apply drag at the contact centroid to produce torque (off-center flow rotates/tips/bobs the body). Off = applied at center of mass, translate only.",
             "接触質心で抗力を加え力矩を生む（偏った来流で回転/転倒/揺動）。オフなら質心で施加し平行移動のみ。")]
        private bool applyTorque;

        [SerializeField, Min(0f), LocalizationTooltip(
             "单步速度变化上限（世界单位/秒，0=不限制）。钳制流体每个物理步对本物体速度的改变量，防止偶发尖峰把物体弹飞。相对速度阻力本身自限，通常不会触发。",
             "Max speed change per step (world units/s; 0 = unlimited). Clamps the fluid's velocity change on this body per physics step against rare spikes. Relative-velocity drag is self-limiting, so this rarely triggers.",
             "1 ステップの速度変化上限（ワールド単位/秒、0=無制限）。稀なスパイク対策。相対速度抗力は自己制限的で通常作動しません。")]
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
             "浸没线性阻尼。物体接触流体时按此系数衰减线速度，消除抖动、实现稳定漂浮。覆盖模式漂浮物建议 2~4 以加快收敛、避免上下弹跳。",
             "Submerged linear drag. Damps linear velocity by this coefficient while contacting fluid; removes jitter for stable floating. For floating bodies in Submerge mode, 2~4 is recommended to converge faster and avoid bobbing.",
             "浸水時の線形減衰。流体接触中に線速度を減衰し、安定浮遊。水没モードの浮体は 2~4 推奨（収束を速め弾みを防ぐ）。")]
        private float submergedLinearDrag = 2f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "浸没角阻尼。物体接触流体时按此系数衰减角速度，抑制旋转抖动。",
             "Submerged angular drag. Damps angular velocity by this coefficient while contacting fluid; suppresses spin jitter.",
             "浸水時の角減衰。流体接触中に角速度を減衰し、回転の振動を抑えます。")]
        private float submergedAngularDrag = 1f;

        [SerializeField, Range(0f, 1f), LocalizationTooltip(
             "覆盖模式浸没死区（覆盖率阈值）。物体被流体覆盖的真实面积比例低于此值时，视为「不在流体中」，跳过全部流体力（浮力/阻力/阻尼）→ 在空中擦过零散流体粒子时仍直接自由下落，不被稀疏接触拖慢。需小于你想漂浮的最低密度物体的浸没比例（如想浮起密度 0.1 的物体则须 < 0.1）。",
             "Submerge-mode submersion deadzone (coverage threshold). When the true covered-area fraction is below this, the body is treated as 'not in fluid' and all fluid forces (buoyancy/drag/damping) are skipped → it still free-falls through scattered fluid particles in the air, not slowed by sparse contact. Keep it below the submerged fraction of the lightest body you want to float (e.g. < 0.1 to float a density-0.1 body).",
             "水没モードの浸水デッドゾーン（被覆率閾値）。実被覆面積比がこれ未満なら「流体外」とみなし全流体力（浮力/抗力/減衰）をスキップ → 空中で散在粒子を擦っても自由落下。浮かせたい最軽量物体の浸水率より小さく設定（密度0.1を浮かすなら<0.1）。")]
        private float submergeForceDeadzone = 0.05f;

        private Rigidbody2D _rb;
        private float _autoVolume = -1f; // 懒计算缓存（<0 表示未算）。 // lazy cache (<0 = not computed). // 遅延キャッシュ。
        private int _isSubmerge = -1;    // 子碰撞器是否含淹没模式：-1 未判定，0 否，1 是。 // whether child colliders use Submerge: -1 unknown, 0 no, 1 yes. // 子コライダーが水没モードか。

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

        /// <summary>
        /// 子碰撞器是否含淹没（Submerge）模式（任一即视为是，缓存）。淹没模式按真实排开体积估算浮力浸没比例，避免浅浸即饱和导致弹跳。
        /// Whether any child collider uses Submerge mode (cached). Submerge estimates the buoyancy submerged fraction by true
        /// displaced volume, avoiding the bobbing caused by count-based early saturation.
        /// 子コライダーが水没モードを含むか（キャッシュ）。水没は真の排除体積で浮力浸水率を推定し、弾みを防ぐ。
        /// </summary>
        private bool IsSubmergeBody
        {
            get
            {
                if (_isSubmerge < 0)
                {
                    _isSubmerge = 0;
                    var cols = GetComponentsInChildren<Liquid2DCollider>(true);
                    for (int i = 0; i < cols.Length; i++)
                    {
                        if (cols[i] && cols[i].ColliderMode == Liquid2DColliderMode.Submerge) { _isSubmerge = 1; break; }
                    }
                }
                return _isSubmerge == 1;
            }
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
            if (force.ContactCount <= 0) return; // 无接触 = 不在流体中，无浮力/阻力。 // no contact = not in fluid. // 非接触。

            float dt = force.Dt > 1e-6f ? force.Dt : Time.fixedDeltaTime;
            // 浸没比例（0..1）。阻力/阻尼用「全向接触数」（四周浸没即受阻）；浮力用「物体下方接触数」（仅下方粒子托起物体），
            // 避免压在物体顶部的粒子产生虚假上浮——这正是「低密度方块被顶部流体一起带飞」问题的根因。
            // Submerged fraction (0..1). Drag/damping use all-direction contacts; buoyancy uses below-body contacts only (only
            // particles below lift the body), so particles resting on top don't create spurious lift.
            // 浸水率。抗力/減衰は全方向接触、浮力は下方接触のみ（上に乗る粒子の偽浮力を防ぐ）。
            float submerged = fullSubmersionContacts > 0f ? Mathf.Clamp01(force.ContactCount / fullSubmersionContacts) : 1f;
            // 浮力浸没比例：Push 用下方接触数 / fullSubmersionContacts；Submerge 物体被流体包裹，用真实排开体积 / 物体体积，
            // 使浮力随浸入深度线性变化（在密度对应深度处稳定平衡），避免按接触数估算时浅浸即饱和而上下弹跳。
            // Buoyancy submerged fraction: Push uses below-contacts / fullSubmersionContacts; Submerge (enveloped body) uses true
            // displaced volume / bodyVolume, so buoyancy scales linearly with depth (stable equilibrium at the density-matched depth),
            // avoiding the bobbing caused by count-based early saturation.
            // 浮力浸水率：Push は下方接触数、Submerge は真の排除体積 / 体積（深さに比例、安定平衡）。
            float submergedBuoyancy;
            if (IsSubmergeBody)
            {
                float vol = EffectiveVolume();
                submergedBuoyancy = vol > 1e-6f ? Mathf.Clamp01(force.BuoyancySubmergedVolume / vol) : 0f;
            }
            else
            {
                submergedBuoyancy = fullSubmersionContacts > 0f ? Mathf.Clamp01(force.BuoyancyContactCount / fullSubmersionContacts) : 1f;
            }

            // drag/阻尼缩放比例。Push 用全向接触数比例；Submerge 用「壳层覆盖率」= 表面外壳层带覆盖面积 / 物体体积——
            // 只有物体真正被流体包住时壳层才饱满（drag/阻尼正常），空中擦过零散粒子壳层稀疏 → drag/阻尼≈0 → 物体直接自由下落。
            // 这比按浮力(内部覆盖)缩放更准：物体从空中刚触到水面/落入流体块时，下方内部覆盖会先升高，但四周壳层尚未饱满 → 不会过早受阻。
            // Drag/damping scale. Push uses the all-direction contact-count fraction; Submerge uses the "shell coverage" = outer-shell band area / body volume —
            // the shell is full only when the body is truly enveloped (normal drag/damping); grazing scattered particles in the air → sparse shell → ~0 drag/damping → free fall.
            // This is sharper than scaling by buoyancy (interior coverage): entering fluid from above, interior-below coverage rises first but the surrounding shell isn't full yet → no premature resistance.
            // drag/減衰スケール。Push は全方向接触数比、Submerge は殻層被覆率（外殻層面積 / 体積）。真に包まれた時のみ殻層が満ちる→空中の疎な接触では抗力≈0→自由落下。
            float forceScale;
            if (IsSubmergeBody)
            {
                float vol = EffectiveVolume();
                forceScale = vol > 1e-6f ? Mathf.Clamp01(force.ShellCoverageVolume / vol) : 0f;
            }
            else
            {
                forceScale = submerged;
            }

            // 覆盖模式浸没死区：真实覆盖率低于阈值时视为「不在流体中」，跳过全部流体力 → 空中擦过零散粒子仍纯自由落体（A+B 中的 B）。
            // Submerge submersion deadzone: below the coverage threshold, treat as 'not in fluid' and skip all fluid forces → crisp free fall
            // through scattered particles in the air (the B in A+B).
            // 水没デッドゾーン：被覆率が閾値未満なら全流体力をスキップ → 純自由落下。
            if (IsSubmergeBody && submergedBuoyancy < submergeForceDeadzone) return;

            // 施力点：默认质心（只平移不旋转）；applyTorque 时用接触质心（来流偏置一侧 → 力矩，物体翻转/摇摆）。
            // Application point: center of mass by default (translate only); contact centroid when applyTorque (off-center flow → torque).
            // 作用点：既定は質心（平行移動のみ）、applyTorque 時は接触質心（偏った来流 → 力矩）。
            Vector2 applyPoint = applyTorque
                ? new Vector2(force.ContactCenter.x, force.ContactCenter.y)
                : _rb.worldCenterOfMass;

            // 冲走 = 相对速度阻力：F = dragCoefficient · 浸没比例 · (流体速度 − 物体在该点的速度)。
            // 流体静止时 (流体速度≈0、物体也慢) → F≈0，物体不会被无端推动，只受重力+浮力 → 密度决定沉浮；
            // 流体快速流动时 → F 顺流推动 → 自然「冲走」。这是物理上的粘性/拖曳力，自限且稳定。
            // Wash = relative-velocity drag: F = dragCoefficient · submergedFraction · (fluidVel − bodyVel at the point).
            // Still fluid → F≈0 (no spurious push; only gravity + buoyancy → density decides sink/float); fast flow → F carries the
            // body along (wash). This is the physical viscous/drag force — self-limiting and stable.
            // 押し流し = 相対速度抗力：F = 係数 · 浸水率 · (流体速度 − 物体速度)。静止流体で 0、速い流れで押し流す。自己制限的で安定。
            Vector2 fluidVel = new Vector2(force.FluidVelocity.x, force.FluidVelocity.y);
            Vector2 bodyVel = _rb.GetPointVelocity(applyPoint);
            Vector2 dragForce = dragCoefficient * forceScale * (fluidVel - bodyVel);

            // 安全钳制：限制单步速度变化（力·dt/质量 ≤ maxSpeedChange），防止偶发尖峰。 // Safety clamp on per-step Δv. // 安全制限。
            if (maxSpeedChange > 0f)
            {
                float maxF = maxSpeedChange * _rb.mass / Mathf.Max(1e-6f, dt);
                if (dragForce.sqrMagnitude > maxF * maxF) dragForce = dragForce.normalized * maxF;
            }
            if (dragForce.sqrMagnitude > 1e-10f)
                _rb.AddForceAtPosition(dragForce, applyPoint, ForceMode2D.Force);

            // 浮力（阿基米德）：浮力 = ρ流体 · g · 体积 · 下方浸没比例；在质心处施加 → 零力矩，物体密度（mass/体积）决定沉浮。
            // 仅用「物体下方」接触（force.BuoyancyFluidDensity / submergedBuoyancy），故压在顶部的流体不会把物体一起顶起。
            // Buoyancy (Archimedes): force = fluidDensity · g · volume · belowSubmergedFraction; at the center of mass → no torque,
            // body density (mass/volume) decides float/sink. Uses below-body contacts only, so fluid on top can't lift the body.
            // 浮力（アルキメデス）：浮力 = ρ流体 · g · 体積 · 下方浸水率。下方接触のみ使用し、上の流体で持ち上がらない。
            if (useBuoyancy && force.BuoyancyFluidDensity > 0f)
            {
                float gMag = Physics2D.gravity.magnitude;
                Vector2 up = gMag > 1e-6f ? -Physics2D.gravity / gMag : Vector2.up;
                float buoyForce = force.BuoyancyFluidDensity * gMag * EffectiveVolume() * submergedBuoyancy; // 阿基米德浮力幅值。 // magnitude. // 浮力の大きさ。
                if (buoyForce > 0f) _rb.AddForce(up * buoyForce, ForceMode2D.Force);
            }

            // 浸没附加阻尼（漂浮主稳定器）：消除残余抖动、稳定漂浮。按浸没比例缩放——Submerge 模式用「内部覆盖」浸没比例 submergedBuoyancy
            // （与浮力同源，部分覆盖阻尼小、完全覆盖阻尼满），这是上一轮调稳漂浮的关键，故阻尼仍用内部覆盖、不改用壳层覆盖，避免漂浮重新抖动；
            // 空中由死区门控（submergedBuoyancy<死区直接 return）保证不施加。Push 模式仍按全向接触数。
            // Extra submerged damping (floating's primary stabilizer): kills residual jitter for stable floating. Scaled by submersion — Submerge uses the
            // "interior coverage" fraction submergedBuoyancy (same source as buoyancy; small when partially covered, full when fully covered). This was the key to
            // the floating stability tuned earlier, so damping stays on interior coverage (NOT shell) to avoid re-introducing bobbing; in the air the deadzone
            // (return when submergedBuoyancy < deadzone) already prevents it. Push still scales by all-direction contact count.
            // 浸水附加減衰（浮遊の主安定器）：Submerge は内部被覆 submergedBuoyancy でスケール（殻層ではない、浮遊安定維持）。空中はデッドゾーンで抑止。Push は全方向接触数。
            float dampScale = IsSubmergeBody ? submergedBuoyancy : submerged;
            if (submergedLinearDrag > 0f) _rb.linearVelocity *= Mathf.Clamp01(1f - submergedLinearDrag * dampScale * dt);
            if (submergedAngularDrag > 0f) _rb.angularVelocity *= Mathf.Clamp01(1f - submergedAngularDrag * dampScale * dt);
        }
    }
}

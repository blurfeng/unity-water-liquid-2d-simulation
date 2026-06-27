using System.Collections.Generic;
using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 自研 2D 流体碰撞体基类。碰撞体数量少、需在场景中编辑，故保留为 MonoBehaviour（与纯数据粒子相反）。
    /// 启用时注册到 <see cref="Liquid2DColliderRegistry"/>，由 <see cref="Liquid2DSimulation"/> 每帧扁平化为 Burst 缓冲供求解器投影。
    /// Base class for the custom 2D fluid colliders. Colliders are few and authored in scenes, so they stay as
    /// MonoBehaviours (unlike pure-data particles). On enable they register into <see cref="Liquid2DColliderRegistry"/>;
    /// <see cref="Liquid2DSimulation"/> flattens them into a Burst buffer each frame for the solver to project against.
    /// 自作 2D 流体コライダーの基底クラス。コライダーは数が少なくシーンで編集するため MonoBehaviour のまま。
    /// </summary>
    public abstract class Liquid2DCollider : MonoBehaviour
    {
        [SerializeField, LocalizationTooltip(
             "作用的粒子组标签。留空=作用于全部粒子；填写后仅阻挡 nameTag 匹配的粒子（其它粒子穿过）。",
             "Particle group tag to act on. Empty = affects all particles; set = only blocks particles whose nameTag matches (others pass through).",
             "作用する粒子グループのタグ。空=全粒子に作用、設定時は nameTag 一致の粒子のみ阻止（他は通過）。")]
        private string nameTag = string.Empty;

        [SerializeField, LocalizationTooltip(
             "碰撞交互模式。Push（推离，默认）：将粒子持续推出碰撞体外，粒子无法穿透。Submerge（淹没）：粒子可穿过碰撞体，流体自然覆盖之；碰撞器运动时排开流体产生水花，双向耦合保持不变。",
             "Collision interaction mode. Push (default): continuously ejects particles from the collider; particles cannot penetrate. Submerge: particles pass through and fluid covers the collider; a moving collider displaces fluid to create splashes; two-way coupling is preserved.",
             "衝突相互作用モード。Push（押し出し、デフォルト）：粒子を常にコライダーの外へ押し出し、貫通不可。Submerge（水没）：粒子は通過でき流体がコライダーを覆う。運動時は流体を排除して水しぶきを生成、双方向結合は保持。")]
        private Liquid2DColliderMode _colliderMode = Liquid2DColliderMode.Push;

        [SerializeField, Range(0f, 1f), LocalizationTooltip(
             "淹没模式：位移耦合强度 k（0~1）。碰撞器运动时，把它覆盖的流体粒子推向自身速度的反方向，让水绕过物体回流、完成「物体与水的位置交换」（上浮时前方水向下/外避让、后方水回填）；随碰撞器速度从 0 渐入——静止时不扰动流体。纯耗散，保证稳定。建议 0.15~0.4。",
             "Submerge: displacement-coupling strength k (0~1). While moving, the collider pushes the fluid it covers toward the OPPOSITE of its own velocity, so fluid flows around the body — a position exchange (rising body: front fluid yields down/outward, rear backfills). Ramped in by collider speed — a still collider does not disturb the fluid. Purely dissipative, stable. 0.15~0.4 recommended.",
             "水没：変位結合強度 k（0~1）。運動時、覆う流体粒子を自身の速度の逆方向へ押し、物体を回り込み回流させ「位置交換」（上昇時は前方が下/外へ退き後方が埋める）。速度で 0 から漸入し静止時は不擾乱。純散逸で安定。0.15~0.4 推奨。")]
        private float _submergeCoupling = 0.25f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "淹没模式：飞溅强度。碰撞器速度超过下方阈值后，沿其速度反方向非线性注入额外速度产生喷溅（如向下砸向水面时水往上溅）。仅高速撞击的瞬间生效，不影响漂浮稳定。0=无飞溅。",
             "Submerge: splash strength. Above the threshold below, injects extra velocity nonlinearly OPPOSITE to the collider's velocity to spray fluid (e.g. slamming down into the surface sprays water up). Only the transient high-speed impact; does not affect floating stability. 0 = no splash.",
             "水没：水しぶき強度。下の閾値を超えると速度の逆方向へ非線形に追加速度を注入し飛沫を生成（下方への衝突で水は上へ）。高速衝突の瞬間のみで浮遊安定に影響なし。0=飛沫なし。")]
        private float _submergeSplashStrength = 1f;

        [SerializeField, Min(0f), LocalizationTooltip(
             "淹没模式：飞溅起始速度阈值（世界单位/秒）。碰撞器速度超过此值才溅水花——应高于「力场扰动漂浮物」的速度、低于「自由下落砸水面」的速度。同时作为速度耦合渐入的参考速度。",
             "Submerge: splash onset speed threshold (world units/s). The collider splashes only above this — set it above the speed of a force-field disturbance on a floating body and below a free-fall impact. Also the reference speed for the velocity coupling ramp.",
             "水没：飛沫開始の速度閾値（ワールド単位/秒）。これを超えると飛沫。浮体への力場擾乱速度より高く、自由落下衝突より低く設定。速度結合の漸入参照速度でもある。")]
        private float _submergeSplashThreshold = 4f;

        [SerializeField, Min(0.01f), LocalizationTooltip(
             "淹没模式：飞溅强度从 0 增至满的速度区间（世界单位/秒，自阈值起算）。越小则越接近阈值就达到最强飞溅。",
             "Submerge: speed range (world units/s, from the threshold) over which splash ramps from 0 to full. Smaller = reaches full splash sooner past the threshold.",
             "水没：飛沫が 0 から満まで増える速度区間（ワールド単位/秒、閾値から）。小さいほど閾値直後に最強へ。")]
        private float _submergeSplashRange = 6f;

        [SerializeField, Range(0f, 1f), LocalizationTooltip(
             "淹没模式：施力流体密度阈值（相对静止密度的比例）。只对 SPH 密度高于此值的流体粒子施加壳层位移/飞溅力——成片水体/水面密度高→正常受力；空中孤立的零散水滴密度低→不施力，避免方块在空中把靠近的水滴弹飞。自由水面约 0.5、孤立水滴约 0.1~0.2，取 0.35~0.45 可放过水面飞溅、挡住孤立水滴。0=不过滤。",
             "Submerge: fluid-density threshold for applying force (fraction of rest density). Only fluid particles whose SPH density exceeds this get the shell displacement/splash force — dense water bodies/surfaces get normal force; isolated airborne droplets (low density) get none, so a block in the air won't fling nearby droplets. Free surface ≈0.5, isolated droplet ≈0.1~0.2; 0.35~0.45 lets surface splash through while blocking lone droplets. 0 = no filtering.",
             "水没：施力する流体密度の閾値（静止密度比）。SPH 密度がこれを超える粒子のみ殻層の変位/飛沫力を受ける——密な水体/水面は通常、空中の孤立水滴（低密度）は受けず弾き飛ばさない。自由表面≈0.5、孤立滴≈0.1~0.2。0.35~0.45 推奨。0=無効。")]
        private float _submergeFluidDensityThreshold = 0.4f;

        protected Transform CachedTransform;

        // 缓存的力接收者（同物体或父级，OnEnable 解析）。 // Cached force receiver (same object or parent; resolved on enable). // 力レシーバーのキャッシュ。
        private ILiquid2DForceReceiver _forceReceiver;

        // 碰撞器线速度跟踪（上一帧世界中心 + 是否已记录），用于淹没模式按运动排开流体。
        // Collider linear-velocity tracking (previous world center + recorded flag), used in Submerge mode to displace fluid by motion.
        // コライダー線速度の追跡（前フレームのワールド中心 + 記録フラグ）。水没モードで運動により流体を排除するために使用。
        private float2 _prevCenter;
        private bool _hasPrevCenter;
        private float _lastSampleTime; // 上次采样的 fixedTime，用于检测中间跳过的固定步。 // fixedTime of the last sample, to detect skipped fixed steps. // 前回サンプルの fixedTime。

        /// <summary>作用的粒子组标签（空=作用于全部）。 // Particle group tag to act on (empty = all). // 作用する粒子グループのタグ（空=全部）。</summary>
        public string NameTag => nameTag;

        /// <summary>碰撞交互模式（Push=推离，Submerge=淹没）。 // Collision interaction mode (Push=eject, Submerge=pass-through with impulse). // 衝突相互作用モード。</summary>
        public Liquid2DColliderMode ColliderMode => _colliderMode;

        /// <summary>淹没模式：速度耦合强度 k（0~1）。 // Submerge: velocity-coupling strength k (0~1). // 水没：速度結合強度 k。</summary>
        public float SubmergeCoupling => _submergeCoupling;

        /// <summary>淹没模式：飞溅强度。 // Submerge: splash strength. // 水没：水しぶき強度。</summary>
        public float SubmergeSplashStrength => _submergeSplashStrength;

        /// <summary>淹没模式：飞溅起始速度阈值。 // Submerge: splash onset speed threshold. // 水没：飛沫開始速度閾値。</summary>
        public float SubmergeSplashThreshold => _submergeSplashThreshold;

        /// <summary>淹没模式：飞溅渐入速度区间。 // Submerge: splash ramp speed range. // 水没：飛沫漸入速度区間。</summary>
        public float SubmergeSplashRange => _submergeSplashRange;

        /// <summary>淹没模式：施力流体密度阈值（相对静止密度）。 // Submerge: fluid-density threshold for applying force (fraction of rest density). // 水没：施力密度閾値。</summary>
        public float SubmergeFluidDensityThreshold => _submergeFluidDensityThreshold;

        /// <summary>
        /// 是否动态（参与双向耦合）：当同物体或父级挂有 <see cref="ILiquid2DForceReceiver"/>（如
        /// <see cref="Liquid2DRigidbodyBridge"/>）时为 true。
        /// Whether dynamic (two-way coupling): true when an <see cref="ILiquid2DForceReceiver"/> (e.g.
        /// <see cref="Liquid2DRigidbodyBridge"/>) exists on the same object or a parent.
        /// 動的か（双方向）：同オブジェクトまたは親に <see cref="ILiquid2DForceReceiver"/> がある場合 true。
        /// </summary>
        public bool IsDynamic => _forceReceiver != null;

        /// <summary>形状类型。 // Shape type. // 形状タイプ。</summary>
        public abstract Liquid2DColliderShape Shape { get; }

        /// <summary>
        /// 确保 <see cref="CachedTransform"/> 已初始化（编辑器/未 OnEnable 时安全调用，供 <see cref="Fill"/> / 面积计算使用）。
        /// Ensure <see cref="CachedTransform"/> is initialized (safe in editor / before OnEnable, for <see cref="Fill"/> / area).
        /// <see cref="CachedTransform"/> の初期化を保証（エディタ/ OnEnable 前でも安全）。
        /// </summary>
        public void EnsureInitialized()
        {
            if (!CachedTransform) CachedTransform = transform;
        }

        protected virtual void OnEnable()
        {
            CachedTransform = transform;
            _forceReceiver = GetComponentInParent<ILiquid2DForceReceiver>();
            Liquid2DColliderRegistry.Register(this);
        }

        protected virtual void OnDisable()
        {
            Liquid2DColliderRegistry.Unregister(this);
            _hasPrevCenter = false; // 重置速度跟踪，避免重新启用时因位置跳变产生虚假速度。 // Reset velocity tracking so re-enabling doesn't yield a spurious velocity from a position jump. // 再有効化時の偽速度を防ぐ。
        }

        /// <summary>
        /// 计算并更新本碰撞器的线速度（由世界中心的逐帧位移除以固定步长得到），供淹没模式按运动排开流体。
        /// 每帧（构建求解缓冲时）由 <see cref="Liquid2DColliderRegistry"/> 调用一次以推进内部状态。
        /// Compute and update this collider's linear velocity (per-frame world-center displacement / fixed timestep), for
        /// Submerge mode to displace fluid by motion. Called once per frame by <see cref="Liquid2DColliderRegistry"/> while
        /// building the solver buffer to advance internal state.
        /// このコライダーの線速度を計算・更新（ワールド中心のフレーム間変位 / 固定ステップ）。水没モードで運動により流体を排除するために使用。
        /// </summary>
        public float2 ComputeVelocity()
        {
            EnsureInitialized();
            float2 c = WorldCenter;
            float2 vel = float2.zero;
            float dt = Time.fixedDeltaTime;
            float now = Time.fixedTime;
            // 仅当与上次采样恰好相隔一个固定步时才算速度。本方法每帧由 BuildBuffer 调用一次，但 Liquid2DSimulation 在池空时
            // 会提前返回、不构建缓冲，于是中间会跳过若干步；此时把多步位移当一步会得到虚高速度（喂给淹没模式的位移耦合/无界飞溅
            // 项 → 误把流体弹飞）。故间隔超过 ~1 步则视为重启（vel=0）。 // Only velocity across exactly one fixed step; skipped steps (empty-pool early-return) would make a multi-step displacement look like one → spurious huge velocity. Treat a gap as a restart. // ステップ飛びは再起動扱い（偽高速度を防ぐ）。
            if (_hasPrevCenter && dt > 0f && (now - _lastSampleTime) <= dt * 1.5f)
                vel = (c - _prevCenter) / dt;
            _prevCenter = c;
            _lastSampleTime = now;
            _hasPrevCenter = true;
            return vel;
        }

        /// <summary>
        /// 把当前世界状态写入 blittable 描述；多边形/边链把世界顶点追加到 pointsAccum 并设置 pointStart/pointCount。
        /// Write the current world state into the blittable description; polygon/edge chain append world vertices to
        /// pointsAccum and set pointStart/pointCount.
        /// 現在のワールド状態を blittable 記述に書き込む。多角形/エッジは頂点を pointsAccum に追加。
        /// </summary>
        public abstract void Fill(ref Liquid2DColliderData data, List<float2> pointsAccum);

        /// <summary>
        /// 将本碰撞体的所有子形状写入输出列表。默认实现调用 <see cref="Fill"/> 写入单个条目；包含多形状的碰撞体
        /// （如 <c>Liquid2DCustomCollider</c>）应重写此方法以写入多个 <see cref="Liquid2DColliderData"/> 条目。
        /// Write all sub-shapes of this collider into the output list. The default calls <see cref="Fill"/> once for a
        /// single entry; multi-shape colliders (e.g. <c>Liquid2DCustomCollider</c>) should override to append multiple
        /// <see cref="Liquid2DColliderData"/> entries.
        /// コライダーの全サブ形状を出力リストに書き込みます。デフォルトは <see cref="Fill"/> を 1 回呼ぶ。
        /// 多形状コライダー（例 <c>Liquid2DCustomCollider</c>）はこのメソッドをオーバーライドしてください。
        /// </summary>
        public virtual void FillAll(List<Liquid2DColliderData> dataOut, List<float2> pointsAccum)
        {
            var data = new Liquid2DColliderData();
            Fill(ref data, pointsAccum);
            dataOut.Add(data);
        }

        /// <summary>
        /// 碰撞体的力接收者（同物体或父级上的 <see cref="ILiquid2DForceReceiver"/>，OnEnable 缓存；为 null 时碰撞体为静态）。
        /// Force receiver of this collider (an <see cref="ILiquid2DForceReceiver"/> on the same object or a parent, cached on
        /// enable; the collider is static when null).
        /// コライダーの力レシーバー（同オブジェクトまたは親の実装、OnEnable でキャッシュ。null なら静的）。
        /// </summary>
        public virtual ILiquid2DForceReceiver ForceReceiver => _forceReceiver;

        protected float ZRotationRadians => CachedTransform.eulerAngles.z * Mathf.Deg2Rad;
        protected float2 WorldCenter => new float2(CachedTransform.position.x, CachedTransform.position.y);
    }
}

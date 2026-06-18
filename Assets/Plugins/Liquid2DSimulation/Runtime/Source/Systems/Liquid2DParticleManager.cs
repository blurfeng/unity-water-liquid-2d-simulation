using System.Collections.Generic;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 2D 流体粒子集中管理器。
    /// 统一对所有已激活的流体粒子进行逐帧 tick（生命周期等），
    /// 以消除上千个粒子各自 MonoBehaviour.Update 的调用派发开销（托管↔原生跨界成本）。
    /// 同时按 nameTag 维护存活上限（最旧回收），用于确定性封顶物理最坏开销。
    /// 这是一个运行时单例：玩法逻辑只在运行时需要，因此编辑模式下不进行 tick；
    /// 粒子在编辑模式的渲染可见性由 Liquid2DFeature 的注册独立保证，不依赖本管理器。
    /// 2D fluid particle central manager.
    /// Centrally ticks all active fluid particles each frame (lifetime, etc.) to eliminate the per-component
    /// MonoBehaviour.Update dispatch overhead (managed↔native boundary cost) of thousands of particles.
    /// Also enforces a per-nameTag alive cap (oldest-recycle) to deterministically bound worst-case physics cost.
    /// This is a runtime-only singleton: gameplay logic is only needed at runtime, so no tick happens in edit mode;
    /// particle render visibility in edit mode is guaranteed independently by Liquid2DFeature registration.
    /// 2D流体粒子集中マネージャー。
    /// アクティブなすべての流体粒子のフレーム毎の tick（ライフタイムなど）を一元的に行い、
    /// 数千の粒子それぞれの MonoBehaviour.Update 呼び出しのディスパッチコスト（マネージド↔ネイティブ境界コスト）を排除します。
    /// また、nameTag ごとの生存上限（最古回収）を適用し、物理の最悪コストを確定的に制限します。
    /// これはランタイム専用のシングルトンです。ゲームプレイロジックはランタイムでのみ必要なため、
    /// エディットモードでは tick を行いません。エディットモードでの粒子の描画可視性は Liquid2DFeature の登録によって独立して保証されます。
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public class Liquid2DParticleManager : MonoBehaviour
    {
        private static Liquid2DParticleManager _instance;
        private static bool _isQuitting;

        /// <summary>
        /// 获取管理器单例（运行时懒创建一个隐藏 GameObject 承载）。
        /// Get the manager singleton (lazily creates a hidden GameObject at runtime).
        /// マネージャーのシングルトンを取得（ランタイムで非表示のGameObjectを遅延生成）。
        /// </summary>
        public static Liquid2DParticleManager Instance
        {
            get
            {
                if (_instance || _isQuitting || !Application.isPlaying) return _instance;

                var go = new GameObject("[Liquid2DParticleManager]")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                _instance = go.AddComponent<Liquid2DParticleManager>();
                return _instance;
            }
        }

        /// <summary>
        /// 每个 nameTag 的最大存活粒子数。小于等于 0 表示不限制（默认）。
        /// 超出上限时，注册新粒子会回收该 nameTag 下最旧的粒子。可由 Liquid2DPhysicsConfig 设置。
        /// Max alive particles per nameTag. Less-or-equal 0 means no limit (default).
        /// When exceeded, registering a new particle recycles the oldest particle under that nameTag. Can be set by Liquid2DPhysicsConfig.
        /// nameTag ごとの最大生存粒子数。0以下は無制限（デフォルト）。
        /// 上限を超えると、新しい粒子の登録時にその nameTag の最も古い粒子を回収します。Liquid2DPhysicsConfig で設定可能。
        /// </summary>
        public static int MaxParticlesPerTag { get; set; } = 0;

        // 按 nameTag 分组的粒子列表，list 保持注册顺序（list[0] 为最旧）。
        // Particles grouped by nameTag; each list preserves registration order (list[0] is the oldest).
        // nameTag でグループ化された粒子リスト。各リストは登録順を保持します（list[0] が最古）。
        private readonly Dictionary<string, List<Liquid2DParticle>> _byTag =
            new Dictionary<string, List<Liquid2DParticle>>();

        // 粒子 → 其所属分组 list 的反查，用于 O(1) 注销（不受运行时 nameTag 变更影响）。
        // Particle → its owning group list, for O(1) unregistration (unaffected by runtime nameTag changes).
        // 粒子 → 所属グループリストの逆引き。O(1)の登録解除に使用（ランタイムの nameTag 変更の影響を受けません）。
        private readonly Dictionary<Liquid2DParticle, List<Liquid2DParticle>> _particleToList =
            new Dictionary<Liquid2DParticle, List<Liquid2DParticle>>();

        /// <summary>
        /// 注册粒子到集中 tick（仅运行时生效），并应用按 nameTag 的存活上限。
        /// Register a particle into the central tick (runtime only) and enforce the per-nameTag alive cap.
        /// 粒子を集中 tick に登録し（ランタイムのみ）、nameTag ごとの生存上限を適用します。
        /// </summary>
        /// <param name="particle"></param>
        public static void Register(Liquid2DParticle particle)
        {
            if (!particle || !Application.isPlaying) return;
            var instance = Instance;
            if (!instance) return;

            string tag = particle.RenderSettings != null ? (particle.RenderSettings.nameTag ?? "") : "";
            if (!instance._byTag.TryGetValue(tag, out var list))
            {
                list = new List<Liquid2DParticle>();
                instance._byTag[tag] = list;
            }

            list.Add(particle);
            instance._particleToList[particle] = list;

            instance.EnforceLimit(list, particle);
        }

        /// <summary>
        /// 从集中 tick 注销粒子。
        /// Unregister a particle from the central tick.
        /// 粒子を集中 tick から登録解除します。
        /// </summary>
        /// <param name="particle"></param>
        public static void Unregister(Liquid2DParticle particle)
        {
            if (!particle || _instance == null) return;
            if (_instance._particleToList.TryGetValue(particle, out var list))
            {
                list.Remove(particle);
                _instance._particleToList.Remove(particle);
            }
        }

        /// <summary>
        /// 应用存活上限：超出 MaxParticlesPerTag 时，回收该分组中最旧的粒子（不回收刚注册的粒子）。
        /// Enforce the alive cap: when exceeding MaxParticlesPerTag, recycle the oldest particle in the group (never the just-registered one).
        /// 生存上限を適用：MaxParticlesPerTag を超えた場合、グループ内で最古の粒子を回収します（登録直後の粒子は回収しません）。
        /// </summary>
        private void EnforceLimit(List<Liquid2DParticle> list, Liquid2DParticle justAdded)
        {
            if (MaxParticlesPerTag <= 0) return;

            while (list.Count > MaxParticlesPerTag)
            {
                var oldest = list[0];
                if (oldest == justAdded) break; // 安全兜底：避免回收刚注册的粒子。 // Safety: never recycle the just-added particle. // 安全策：登録直後の粒子は回収しません。

                // 先主动从管理器移除，再回收，避免依赖 Object.Destroy 的延迟 OnDisable 导致计数滞后、连续误回收。
                // Remove from the manager first, then recycle, to avoid relying on Object.Destroy's deferred OnDisable (which would lag the count and over-recycle).
                // Object.Destroy の遅延 OnDisable に依存して計数が遅れ、過剰回収するのを避けるため、先にマネージャーから削除してから回収します。
                list.RemoveAt(0);
                _particleToList.Remove(oldest);

                if (oldest) Loader.Destroy(oldest.gameObject);
            }
        }

        private void Update()
        {
            foreach (var kvp in _byTag)
            {
                var list = kvp.Value;
                // 倒序遍历，安全处理 tick 过程中粒子被回收/销毁（自销毁 → OnDisable → Unregister）导致的移除。
                // Iterate in reverse to safely handle removals caused by particles being recycled/destroyed during tick (self-destroy → OnDisable → Unregister).
                // tick 中に粒子が回収/破棄（自己破棄 → OnDisable → Unregister）されて削除される場合に備え、逆順で反復します。
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var particle = list[i];
                    if (!particle)
                    {
                        list.RemoveAt(i);
                        _particleToList.Remove(particle);
                        continue;
                    }

                    particle.Tick();
                }
            }
        }

        private void OnApplicationQuit()
        {
            _isQuitting = true;
        }

        private void OnDestroy()
        {
            if (_instance == this) _instance = null;
        }
    }
}

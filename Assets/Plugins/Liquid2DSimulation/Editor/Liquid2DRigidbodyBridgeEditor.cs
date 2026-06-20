using UnityEditor;
using UnityEngine;

namespace Fs.Liquid2D.Editor
{
    /// <summary>
    /// <see cref="Liquid2DRigidbodyBridge"/> 的自定义 Inspector：校验必需/推荐组件并给出提示，显示自动求得的体积与物体密度。
    /// - 必需：自身或子物体上的 <see cref="Liquid2DCollider"/>（缺失则流体无法推动此物体）。
    /// - 推荐：Unity <see cref="Collider2D"/>（缺失则不与普通场景碰撞，会穿过地面/墙）。
    /// Custom inspector for <see cref="Liquid2DRigidbodyBridge"/>: validates required/recommended components and shows the
    /// auto-computed volume and body density.
    /// <see cref="Liquid2DRigidbodyBridge"/> のカスタム Inspector：必須/推奨コンポーネントを検証し、体積と密度を表示。
    /// </summary>
    [CustomEditor(typeof(Liquid2DRigidbodyBridge))]
    public class Liquid2DRigidbodyBridgeEditor : UnityEditor.Editor
    {
        // 播放模式下选中对象时 Inspector 会每帧重绘；若每帧重算（GetComponentsInChildren 数组分配 + 几何重建 + 字符串插值）
        // 会产生持续 GC 卡顿、表现为流体粒子闪烁。故缓存展示数据，仅在编辑模式（重绘不频繁）每次刷新、播放模式按间隔节流刷新。
        // While selected in play mode the Inspector repaints every frame; recomputing each frame (array alloc + geometry rebuild +
        // string interpolation) causes constant GC hitches that look like fluid flicker. So cache the display and refresh every
        // call in edit mode (repaints are infrequent) but only on an interval in play mode.
        // 再生中は毎フレーム再描画されるため、毎フレームの再計算は GC ヒッチ（粒子のちらつき）の原因。表示をキャッシュしスロットリング刷新。
        private const double _refreshInterval = 0.25; // 播放模式刷新间隔（秒）。 // Play-mode refresh interval (s). // 再生中の刷新間隔。

        private bool _hasFluidCollider;
        private bool _hasUnityCollider;
        private string _infoText = string.Empty;
        private double _nextRefreshTime;

        private void OnEnable()
        {
            Refresh();
        }

        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            // 编辑模式：每次刷新（Inspector 仅在交互时重绘，开销可忽略）。播放模式：按间隔节流，期间只画缓存。
            // Edit mode: refresh every call (the Inspector repaints only on interaction). Play mode: throttle; draw the cache between refreshes.
            // 編集モードは毎回刷新、再生中は間隔で刷新。
            if (!Application.isPlaying || EditorApplication.timeSinceStartup >= _nextRefreshTime)
                Refresh();

            // 必需：流体碰撞器。 // Required: fluid collider. // 必須：流体コライダー。
            if (!_hasFluidCollider)
            {
                EditorGUILayout.HelpBox(
                    "缺少 Liquid2DCollider（自身或子物体）：流体无法推动此物体。请添加一个 Liquid2D Box/Circle/Capsule/Polygon Collider。\n" +
                    "Missing Liquid2DCollider (self or children): the fluid cannot push this body. Add a Liquid2D collider.",
                    MessageType.Error);
            }

            // 推荐：Unity 碰撞器。 // Recommended: Unity collider. // 推奨：Unity コライダー。
            if (!_hasUnityCollider)
            {
                EditorGUILayout.HelpBox(
                    "未找到 Unity Collider2D：物体不会与普通场景碰撞体（地面/墙/其它刚体）交互，会直接穿过。需要落地/挡墙时请添加。\n" +
                    "No Unity Collider2D: this body won't collide with normal scene colliders (ground/walls) and will pass through. Add one if needed.",
                    MessageType.Info);
            }

            // 体积 / 密度信息（缓存字符串，避免每帧插值分配）。 // Volume / density info (cached string, no per-frame alloc). // 体積/密度情報（キャッシュ）。
            EditorGUILayout.HelpBox(_infoText, MessageType.None);
        }

        // 重算并缓存展示数据；体积取 EffectiveVolume()（运行时缓存，由 bridge.OnValidate 在调参时失效）。
        // Recompute and cache the display; volume comes from EffectiveVolume() (runtime cache, invalidated by bridge.OnValidate on tuning).
        // 表示データを再計算・キャッシュ。体積は EffectiveVolume()（OnValidate で無効化される実行時キャッシュ）。
        private void Refresh()
        {
            _nextRefreshTime = EditorApplication.timeSinceStartup + _refreshInterval;

            var bridge = target as Liquid2DRigidbodyBridge;
            if (!bridge) return;

            _hasFluidCollider = bridge.GetComponentInChildren<Liquid2DCollider>(true);
            _hasUnityCollider = bridge.GetComponentInChildren<Collider2D>(true);

            var rb = bridge.GetComponent<Rigidbody2D>();
            float volume = bridge.EffectiveVolume();
            float mass = rb ? rb.mass : 0f;
            float density = volume > 1e-6f ? mass / volume : 0f;
            _infoText =
                $"自动体积(2D面积) Volume ≈ {volume:F3} | 质量 Mass = {mass:F3} | 物体密度 Density ≈ {density:F3}\n" +
                "物体密度 < 流体密度(描述器 Density) → 上浮；> → 下沉。Body density below the fluid's (descriptor Density) floats; above sinks.";
        }
    }
}

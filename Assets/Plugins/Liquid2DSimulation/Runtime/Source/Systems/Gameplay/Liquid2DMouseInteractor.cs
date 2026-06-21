using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

// 新版 InputSystem 可用时引入命名空间。
// Import the new InputSystem namespace when available.
// 新しい InputSystem が利用可能な場合に名前空間をインポートします。
#if ENABLE_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace Fs.Liquid2D
{
    /// <summary>
    /// 鼠标流体交互器：作为 <see cref="Liquid2DForceFieldSource"/> 的使用范例 / 正式玩法的输入驱动。每帧判断鼠标按键，
    /// 按住左键时把范围内粒子吸向鼠标、按住右键时推开。鼠标位置经相机
    /// 转为世界坐标作为力场中心；未按下任何键时本帧不施力（被注册表跳过）。
    /// 同时兼容 Unity 新版 InputSystem（<c>ENABLE_INPUT_SYSTEM</c>）与旧版 Input Manager
    /// （<c>ENABLE_LEGACY_INPUT_MANAGER</c>），在 "Both" 模式下优先使用新版。
    /// <br/>
    /// Mouse fluid interactor: a usage example of <see cref="Liquid2DForceFieldSource"/> / input driver for real gameplay.
    /// Each frame it reads mouse buttons — hold LMB to pull particles toward the cursor, hold RMB to push them away.
    /// The cursor is converted to world space via the camera as the field center; when no button is held it applies
    /// no force this frame (skipped by the registry).
    /// Compatible with both the new InputSystem (<c>ENABLE_INPUT_SYSTEM</c>) and the legacy Input Manager
    /// (<c>ENABLE_LEGACY_INPUT_MANAGER</c>); when running in "Both" mode the new InputSystem takes priority.
    /// <br/>
    /// マウス流体インタラクター：<see cref="Liquid2DForceFieldSource"/> の使用例 / 本番ゲームプレイの入力ドライバー。
    /// 左ボタンで粒子をカーソルへ引き寄せ、右ボタンで押し出します。
    /// 新しい InputSystem（<c>ENABLE_INPUT_SYSTEM</c>）と旧来の Input Manager（<c>ENABLE_LEGACY_INPUT_MANAGER</c>）
    /// の両方に対応しており、"Both" モードでは新しい InputSystem が優先されます。
    /// </summary>
    [AddComponentMenu("Liquid 2D/Gameplay/Liquid 2D Mouse Interactor")]
    public class Liquid2DMouseInteractor : Liquid2DForceFieldSource
    {
        [Header("Mouse Interaction")]
        [SerializeField, LocalizationTooltip(
             "用于把鼠标屏幕坐标转为世界坐标的相机。留空则使用 Camera.main。",
             "Camera used to convert the mouse screen position to world space. Leave empty to use Camera.main.",
             "マウスのスクリーン座標をワールド座標に変換するカメラ。空の場合は Camera.main を使用します。")]
        private Camera interactionCamera;

        [SerializeField, LocalizationTooltip(
             "是否启用左键吸引（把粒子吸向鼠标）。",
             "Whether to enable left-button attraction (pull particles toward the cursor).",
             "左ボタンの引き寄せ（粒子をカーソルへ）を有効にするかどうか。")]
        private bool enablePull = true;

        [SerializeField, LocalizationTooltip(
             "是否启用右键排斥（把粒子从鼠标推开）。",
             "Whether to enable right-button repulsion (push particles away from the cursor).",
             "右ボタンの押し出し（粒子をカーソルから）を有効にするかどうか。")]
        private bool enablePush = true;

        // ── 输入读取辅助方法 ────────────────────────────────────────────────
        // Input helper methods / 入力ヘルパーメソッド

        /// <summary>
        /// 读取左键（吸引）是否按下。
        /// Returns true when the primary (left) mouse button is held.
        /// 主ボタン（左）が押されているかを返します。
        /// </summary>
        private bool IsPullPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.leftButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(0);
#else
            return false;
#endif
        }

        /// <summary>
        /// 读取右键（排斥）是否按下。
        /// Returns true when the secondary (right) mouse button is held.
        /// 副ボタン（右）が押されているかを返します。
        /// </summary>
        private bool IsPushPressed()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null && Mouse.current.rightButton.isPressed;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.GetMouseButton(1);
#else
            return false;
#endif
        }

        /// <summary>
        /// 读取当前鼠标屏幕坐标。
        /// Returns the current mouse position in screen space.
        /// 現在のマウス位置をスクリーン座標で返します。
        /// </summary>
        private Vector2 GetMouseScreenPosition()
        {
#if ENABLE_INPUT_SYSTEM
            return Mouse.current != null
                ? Mouse.current.position.ReadValue()
                : Vector2.zero;
#elif ENABLE_LEGACY_INPUT_MANAGER
            return Input.mousePosition;
#else
            return Vector2.zero;
#endif
        }

        // ── 核心逻辑 ────────────────────────────────────────────────────────

        public override bool TryGetField(out Liquid2DForceFieldData data)
        {
            data = default;

            bool pull = enablePull && IsPullPressed();
            bool push = enablePush && IsPushPressed();
            if (!pull && !push) return false;

            var cam = interactionCamera ? interactionCamera : Camera.main;
            if (!cam) return false;

            // 将屏幕坐标转换为世界坐标（z 轴使用相机近截面距离）。
            // Convert screen position to world space (z uses the camera near clip distance).
            // スクリーン座標をワールド座標に変換します（z はカメラのニアクリップ面）。
            Vector2 screenPos = GetMouseScreenPosition();
            Vector3 screenPoint = new Vector3(screenPos.x, screenPos.y, cam.nearClipPlane);
            Vector3 world = cam.ScreenToWorldPoint(screenPoint);

            // 左键吸引取正强度，右键排斥取负强度（与 Strength 的符号约定一致）。
            // LMB attracts with positive strength, RMB repels with negative strength (matching the Strength sign convention).
            // 左ボタンは正の強度で引力、右ボタンは負の強度で斥力。
            float s = Mathf.Abs(Strength) * (push ? -1f : 1f);

            data = new Liquid2DForceFieldData
            {
                Center = new float2(world.x, world.y),
                Radius = Radius,
                Strength = s,
                VelocityDamping = VelocityDamping,
            };
            ApplyAdvanced(ref data);
            return true;
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;

            bool pull = enablePull && IsPullPressed();
            bool push = enablePush && IsPushPressed();
            if (!pull && !push) return;

            var cam = interactionCamera ? interactionCamera : Camera.main;
            if (!cam) return;

            Vector2 screenPos = GetMouseScreenPosition();
            Vector3 screenPoint = new Vector3(screenPos.x, screenPos.y, cam.nearClipPlane);
            Vector3 world = cam.ScreenToWorldPoint(screenPoint);
            world.z = 0f;

            Gizmos.color = pull ? Color.green : Color.red;
            Gizmos.DrawWireSphere(world, Radius);
        }
#endif
    }
}
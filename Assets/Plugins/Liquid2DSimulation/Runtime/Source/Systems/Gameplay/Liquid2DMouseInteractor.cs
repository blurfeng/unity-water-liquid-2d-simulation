using Fs.Liquid2D.Localization;
using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 鼠标流体交互器：作为 <see cref="Liquid2DForceFieldSource"/> 的使用范例 / 正式玩法的输入驱动。每帧判断鼠标按键，
    /// 按住左键时把范围内粒子吸向鼠标、按住右键时推开。鼠标位置经相机
    /// 转为世界坐标作为力场中心；未按下任何键时本帧不施力（被注册表跳过）。
    /// Mouse fluid interactor: a usage example of <see cref="Liquid2DForceFieldSource"/> / input driver for real gameplay.
    /// Each frame it reads mouse buttons — hold LMB to pull particles toward the cursor, hold RMB to push them away.
    /// The cursor is converted to world space via the camera as the
    /// field center; when no button is held it applies no force this frame (skipped by the registry).
    /// マウス流体インタラクター：<see cref="Liquid2DForceFieldSource"/> の使用例 / 本番ゲームプレイの入力ドライバー。
    /// 左ボタンで粒子をカーソルへ引き寄せ、右ボタンで押し出します。
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

        public override bool TryGetField(out Liquid2DForceFieldData data)
        {
            data = default;

            bool pull = enablePull && Input.GetMouseButton(0);
            bool push = enablePush && Input.GetMouseButton(1);
            if (!pull && !push) return false;

            var cam = interactionCamera ? interactionCamera : Camera.main;
            if (!cam) return false;

            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);

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

            bool pull = enablePull && Input.GetMouseButton(0);
            bool push = enablePush && Input.GetMouseButton(1);
            if (!pull && !push) return;

            var cam = interactionCamera ? interactionCamera : Camera.main;
            if (!cam) return;

            Vector3 world = cam.ScreenToWorldPoint(Input.mousePosition);
            world.z = 0f;
            Gizmos.color = pull ? Color.green : Color.red;
            Gizmos.DrawWireSphere(world, Radius);
        }
#endif
    }
}

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
        public override void OnInspectorGUI()
        {
            DrawDefaultInspector();

            var bridge = (Liquid2DRigidbodyBridge)target;

            // 必需：流体碰撞器。 // Required: fluid collider. // 必須：流体コライダー。
            var fluidCollider = bridge.GetComponentInChildren<Liquid2DCollider>(true);
            if (!fluidCollider)
            {
                EditorGUILayout.HelpBox(
                    "缺少 Liquid2DCollider（自身或子物体）：流体无法推动此物体。请添加一个 Liquid2D Box/Circle/Capsule/Polygon Collider。\n" +
                    "Missing Liquid2DCollider (self or children): the fluid cannot push this body. Add a Liquid2D collider.",
                    MessageType.Error);
            }

            // 推荐：Unity 碰撞器。 // Recommended: Unity collider. // 推奨：Unity コライダー。
            var unityCollider = bridge.GetComponentInChildren<Collider2D>(true);
            if (!unityCollider)
            {
                EditorGUILayout.HelpBox(
                    "未找到 Unity Collider2D：物体不会与普通场景碰撞体（地面/墙/其它刚体）交互，会直接穿过。需要落地/挡墙时请添加。\n" +
                    "No Unity Collider2D: this body won't collide with normal scene colliders (ground/walls) and will pass through. Add one if needed.",
                    MessageType.Info);
            }

            // 体积 / 密度信息。 // Volume / density info. // 体積/密度情報。
            var rb = bridge.GetComponent<Rigidbody2D>();
            float volume = bridge.ComputeAutoVolume();
            float mass = rb ? rb.mass : 0f;
            float density = volume > 1e-6f ? mass / volume : 0f;
            EditorGUILayout.HelpBox(
                $"自动体积(2D面积) Volume ≈ {volume:F3} | 质量 Mass = {mass:F3} | 物体密度 Density ≈ {density:F3}\n" +
                "物体密度 < 流体密度(描述器 Density) → 上浮；> → 下沉。Body density below the fluid's (descriptor Density) floats; above sinks.",
                MessageType.None);
        }
    }
}

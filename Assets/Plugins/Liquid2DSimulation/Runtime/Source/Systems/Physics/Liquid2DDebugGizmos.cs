using Fs.Liquid2D.Localization;
using Unity.Collections;
using Unity.Mathematics;
using UnityEngine;
#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
#endif

namespace Fs.Liquid2D
{
    /// <summary>
    /// 编辑器调试可视化：把每个流体粒子的物理半径与渲染可视尺寸画到 Scene 视图，方便调试 radius 与 Sprite 融合尺寸的比例。
    /// 纯数据架构下粒子没有 GameObject，故由本组件从 <see cref="Liquid2DSimulation"/> 的 SoA 读数据绘制 gizmo。仅运行时（Play）有数据。
    /// Editor debug visualization: draws each fluid particle's physics radius and render visual size into the Scene view,
    /// to help tune the ratio between radius and the sprite fusion size. Since particles have no GameObject in the pure-data
    /// architecture, this component reads the <see cref="Liquid2DSimulation"/> SoA and draws gizmos. Data exists only at runtime (Play).
    /// エディタ用デバッグ可視化：各流体粒子の物理半径と描画サイズを Scene ビューに描画し、radius と Sprite 融合サイズの比率調整を補助します。
    /// 純データアーキテクチャでは粒子に GameObject が無いため、本コンポーネントが <see cref="Liquid2DSimulation"/> の SoA を読み描画します。データは実行時（Play）のみ。
    /// </summary>
    [AddComponentMenu("Liquid2D/Liquid2D Debug Gizmos")]
    public class Liquid2DDebugGizmos : MonoBehaviour
    {
        [LocalizationTooltip(
             "绘制粒子的物理半径（实际碰撞/邻居半径）。",
             "Draw the particle's physics radius (actual collision/neighbor radius).",
             "粒子の物理半径（実際の衝突/近傍半径）を描画します。")]
        public bool drawPhysicsRadius = true;

        [LocalizationTooltip(
             "物理半径圆的颜色。",
             "Color of the physics-radius circle.",
             "物理半径円の色。")]
        public Color physicsColor = new Color(0.2f, 1f, 0.4f, 0.9f);

        [LocalizationTooltip(
             "绘制渲染可视尺寸（radius × renderScale，即 Sprite 融合用大小）。",
             "Draw the render visual size (radius × renderScale, i.e. the sprite fusion size).",
             "描画サイズ（radius × renderScale、Sprite 融合サイズ）を描画します。")]
        public bool drawRenderSize = true;

        [LocalizationTooltip(
             "渲染尺寸圆的颜色。",
             "Color of the render-size circle.",
             "描画サイズ円の色。")]
        public Color renderColor = new Color(1f, 0.6f, 0.1f, 0.4f);

        [Min(0f), LocalizationTooltip(
             "最多绘制的粒子数（0 表示不限）。粒子很多时限制以保持 Scene 视图流畅。",
             "Max particles to draw (0 = unlimited). Cap this when there are many particles to keep the Scene view responsive.",
             "描画する最大粒子数（0 は無制限）。粒子が多い場合は Scene ビューの応答性のため制限します。")]
        public int maxDraw = 2000;

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!Application.isPlaying) return;
            if (!drawPhysicsRadius && !drawRenderSize) return;
            if (!Liquid2DSimulation.TryGetRenderData(out var store, out NativeArray<int> active, out int activeCount,
                    out IReadOnlyList<Liquid2DParticleDescriptor> descriptors))
                return;

            int limit = maxDraw > 0 ? math.min(activeCount, maxDraw) : activeCount;
            var forward = Vector3.forward; // Scene 视图 2D 平面法线。 // 2D plane normal. // 2D 平面法線。

            for (int i = 0; i < limit; i++)
            {
                int slot = active[i];
                float2 p = store.positions[slot];
                float r = store.radii[slot];
                var center = new Vector3(p.x, p.y, 0f);

                if (drawPhysicsRadius)
                {
                    Handles.color = physicsColor;
                    Handles.DrawWireDisc(center, forward, r);
                }

                if (drawRenderSize)
                {
                    // 可视直径 = radius × 2 × renderScale ⇒ 可视半径 = radius × renderScale。
                    // Visual diameter = radius × 2 × renderScale ⇒ visual radius = radius × renderScale.
                    // 可視直径 = radius × 2 × renderScale ⇒ 可視半径 = radius × renderScale。
                    float scale = 1f;
                    int t = store.typeId[slot];
                    if (descriptors != null && t >= 0 && t < descriptors.Count && descriptors[t] != null)
                        scale = descriptors[t].renderScale;
                    Handles.color = renderColor;
                    Handles.DrawWireDisc(center, forward, r * scale);
                }
            }
        }
#endif
    }
}

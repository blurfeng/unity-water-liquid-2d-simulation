using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Fs.Liquid2D.Volumes
{
    [CustomEditor(typeof(Liquid2DVolume))]
    public class Liquid2DVolumeEditor : VolumeComponentEditor
    {
        public override void OnEnable()
        {
            if (!target)
                return;
        
            base.OnEnable();
        }
        
        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            // 可以自动渲染 List<Liquid2DVolumeData>，不需要手动写了。
            // 但是 overrideState 的勾选框和 List 的折叠箭头会重叠在一起，影响美观。有空再说吧。
            
            // var comp = (Liquid2DVolume)target;
            //
            // if (Liquid2DLayerUtil.IsHaveLayer)
            // {
            //     #region Liquid2D Layer Mask
            //
            //     EditorGUILayout.BeginHorizontal();
            //     GUILayout.Space(13f);
            //
            //     // 勾选框，控制 overrideState。
            //     comp.liquid2DLayerMask.overrideState = 
            //         EditorGUILayout.Toggle(comp.liquid2DLayerMask.overrideState, GUILayout.Width(15f));
            //
            //     EditorGUI.BeginDisabledGroup(!comp.liquid2DLayerMask.overrideState);
            //     // 2D流体遮罩层。只会渲染选中层的2D流体。
            //     int mask = (int)comp.liquid2DLayerMask.value;
            //     mask = EditorGUILayout.MaskField("Liquid2D Layer Mask", mask, Liquid2DLayerUtil.Liquid2DLayerNames);
            //     comp.liquid2DLayerMask.value = unchecked((uint)mask);
            //     EditorGUI.EndDisabledGroup();
            //
            //     EditorGUILayout.EndHorizontal();
            //
            //     #endregion
            // }
            //
            // if (GUI.changed)
            // {
            //     EditorUtility.SetDirty(comp);
            // }
        }
    }
}
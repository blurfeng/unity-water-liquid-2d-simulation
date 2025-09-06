using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;

namespace Volumes
{
    [CustomEditor(typeof(Liquid2d))]
    public class Liquid2dVolumeEditor : VolumeComponentEditor
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

            var comp = (Liquid2d)target;
        
            if (RenderingLayerMaskUtil.IsHaveLayer)
            {
                EditorGUILayout.BeginHorizontal();
                GUILayout.Space(13f);
            
                // 勾选框，控制 overrideState。
                comp.renderingLayerMask.overrideState = 
                    EditorGUILayout.Toggle(comp.renderingLayerMask.overrideState, GUILayout.Width(15f));
            
                EditorGUI.BeginDisabledGroup(!comp.renderingLayerMask.overrideState);
                // MaskField。
                int mask = (int)comp.renderingLayerMask.value;
                mask = EditorGUILayout.MaskField("Rendering Layer Mask", mask, RenderingLayerMaskUtil.LayerNames);
                comp.renderingLayerMask.value = unchecked((uint)mask);
                EditorGUI.EndDisabledGroup();
            
                EditorGUILayout.EndHorizontal();
            }
        
            if (GUI.changed)
            {
                EditorUtility.SetDirty(comp);
            }
        }
    }
}
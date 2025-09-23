using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Linq;

namespace Plugins.Liquid2DSimulation.Samples.Editor
{
[InitializeOnLoad]
    public static class AutoAddRendererDataOnSceneOpen
    {
        static string[] searchPaths = new string[]{
            "Assets/Samples/Liquid 2D Simulation",
            "Assets/Plugins/Liquid2DSimulation/Samples"
        };
        
        static AutoAddRendererDataOnSceneOpen()
        {
            EditorSceneManager.sceneOpened += OnSceneOpened;
        }

        private static void OnSceneOpened(UnityEngine.SceneManagement.Scene scene, OpenSceneMode mode)
        {
            var scenePath = scene.path.Replace("\\", "/");
            if (!scenePath.Contains("/Samples~/", System.StringComparison.OrdinalIgnoreCase) &&
                !scenePath.Contains("/Samples/", System.StringComparison.OrdinalIgnoreCase))
                return;

            // 查找 Samples 目录下的 Liquid2DRenderer2D.asset。 // Find Liquid2DRenderer2D.asset in Samples directory. // SamplesディレクトリでLiquid2DRenderer2D.assetを検索。
            var guids = AssetDatabase.FindAssets("Liquid2DRenderer2D t:ScriptableRendererData", searchPaths);
            string assetPath = guids.Select(AssetDatabase.GUIDToAssetPath)
                                    .FirstOrDefault(p => p.EndsWith("Liquid2DRenderer2D.asset"));
            if (string.IsNullOrEmpty(assetPath)) return;

            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(assetPath);
            if (rendererData == null) return;

            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (urpAsset == null) return;

            var so = new SerializedObject(urpAsset);
            var list = so.FindProperty("m_RendererDataList");
            int rendererIndex = -1;
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == rendererData)
                {
                    rendererIndex = i;
                    break;
                }
            }
            if (rendererIndex == -1)
            {
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = rendererData;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(urpAsset);
                AssetDatabase.SaveAssets();
                rendererIndex = list.arraySize - 1;
                Debug.Log("已自动将 Renderer Data 添加到 URP Asset: " + assetPath);
            }

            // 设置所有相机使用新 Renderer。 // Set all cameras to use new Renderer. // すべてのカメラで新しいRendererを使用するよう設定。
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var urpCam = cam.GetComponent<UniversalAdditionalCameraData>();
                if (urpCam != null)
                {
                    urpCam.SetRenderer(rendererIndex);
                    Debug.Log("已自动将相机 " + cam.name + " 的 Renderer 设置为 Liquid2DRenderer2D");
                }
            }
        }
    }
}
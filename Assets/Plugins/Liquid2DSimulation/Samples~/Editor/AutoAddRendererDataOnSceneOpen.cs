using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using System.Linq;

namespace Fs.Liquid2D.Samples.Editor
{
    /// <summary>
    /// 自动在打开 Sample 场景时，将 Liquid2DRenderer2D 添加到 URP Renderer 列表中，并设置场景中的相机使用该 Renderer。
    /// Automatically add Liquid2DRenderer2D to URP Renderer list and set cameras in the scene to use it when opening Sample scenes.
    /// サンプルシーンを開くときに、Liquid2DRenderer2DをURPレンダラーリストに自動的に追加し、シーン内のカメラがそれを使用するように設定します。
    /// </summary>
    [InitializeOnLoad]
    public static class AutoAddRendererDataOnSceneOpen
    {
        private static readonly string[] _searchPaths = new string[]{
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
            var guids = AssetDatabase.FindAssets("Liquid2DRenderer2D t:ScriptableRendererData", _searchPaths);
            string assetPath = guids.Select(AssetDatabase.GUIDToAssetPath)
                                    .FirstOrDefault(p => p.EndsWith("Liquid2DRenderer2D.asset"));
            if (string.IsNullOrEmpty(assetPath)) return;

            var rendererData = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(assetPath);
            if (!rendererData) return;

            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            if (!urpAsset) return;

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
                Debug.Log($"Auto added Liquid2DRenderer2D to URP Renderer List at index {rendererIndex}.");
            }

            // 设置所有相机使用新 Renderer。 // Set all cameras to use new Renderer. // すべてのカメラで新しいRendererを使用するよう設定。
            foreach (var cam in Object.FindObjectsByType<Camera>(FindObjectsSortMode.None))
            {
                var urpCam = cam.GetComponent<UniversalAdditionalCameraData>();
                if (urpCam)
                {
                    urpCam.SetRenderer(rendererIndex);
                    Debug.Log($"Auto set Camera '{cam.name}' to use Liquid2DRenderer2D.");
                }
            }
        }
    }
}
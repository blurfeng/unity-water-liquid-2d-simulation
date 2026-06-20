using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fs.Liquid2D.Editor
{
    /// <summary>
    /// Liquid2D 编辑器通用工具：集中处理对当前项目 URP 资源（Pipeline Asset / RendererData / Feature）的查找与修改，
    /// 供自定义 Inspector 与 Sample 自动化脚本共用，避免重复实现。
    /// Liquid2D editor utilities: centralizes access to the project's URP assets (Pipeline Asset / RendererData /
    /// Feature), shared by custom inspectors and sample automation scripts to avoid duplication.
    /// Liquid2D エディタ共通ユーティリティ：プロジェクトの URP アセット（Pipeline Asset / RendererData / Feature）への
    /// アクセスを集約し、カスタム Inspector とサンプル自動化スクリプトで共用します。
    /// </summary>
    public static class Liquid2DEditorUtility
    {
        /// <summary>
        /// 获取当前生效的 URP 渲染管线资源；非 URP 或未配置时返回 null。
        /// Get the currently active URP pipeline asset; returns null when not using URP or none is configured.
        /// 現在有効な URP パイプラインアセットを取得します。URP 未使用/未設定の場合は null。
        /// </summary>
        public static UniversalRenderPipelineAsset GetCurrentUrpAsset()
        {
            return GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset
                   ?? GraphicsSettings.defaultRenderPipeline as UniversalRenderPipelineAsset;
        }

        /// <summary>
        /// 读取 URP 资源的 RendererData 列表（通过 SerializedObject 读取 m_RendererDataList，兼容不同 URP 版本）。
        /// Read the URP asset's RendererData list (via SerializedObject on m_RendererDataList, compatible across URP versions).
        /// URP アセットの RendererData 一覧を取得します（m_RendererDataList を SerializedObject 経由で読み取り）。
        /// </summary>
        public static List<ScriptableRendererData> GetRendererDataList(UniversalRenderPipelineAsset urpAsset)
        {
            var result = new List<ScriptableRendererData>();
            if (!urpAsset) return result;

            var so = new SerializedObject(urpAsset);
            var list = so.FindProperty("m_RendererDataList");
            if (list == null) return result;

            for (int i = 0; i < list.arraySize; i++)
            {
                var data = list.GetArrayElementAtIndex(i).objectReferenceValue as ScriptableRendererData;
                if (data) result.Add(data);
            }

            return result;
        }

        /// <summary>
        /// 在当前 URP 资源的所有 RendererData 中，查找名称标签与 <paramref name="nameTag"/> 相同的 <see cref="Liquid2DFeature"/>，
        /// 并返回其所在的 RendererData 资源。
        /// Searches all RendererData of the current URP asset for a <see cref="Liquid2DFeature"/> whose name tag equals
        /// <paramref name="nameTag"/>, returning the owning RendererData asset.
        /// 現在の URP アセットの全 RendererData から、名前タグが <paramref name="nameTag"/> と一致する
        /// <see cref="Liquid2DFeature"/> を探し、その RendererData を返します。
        /// </summary>
        public static bool TryFindFeatureRendererDataByNameTag(
            string nameTag, out ScriptableRendererData rendererData, out Liquid2DFeature feature)
        {
            rendererData = null;
            feature = null;
            if (string.IsNullOrEmpty(nameTag)) return false;

            var urpAsset = GetCurrentUrpAsset();
            if (!urpAsset) return false;

            foreach (var data in GetRendererDataList(urpAsset))
            {
                if (!data) continue;
                foreach (var f in data.rendererFeatures)
                {
                    if (f is Liquid2DFeature liquidFeature &&
                        string.Equals(liquidFeature.NameTag, nameTag, System.StringComparison.Ordinal))
                    {
                        rendererData = data;
                        feature = liquidFeature;
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>
        /// 收集当前 URP 资源中所有 <see cref="Liquid2DFeature"/> 的名称标签（去重、去空）。供「一键修复」下拉使用。
        /// Collects all <see cref="Liquid2DFeature"/> name tags in the current URP asset (distinct, non-empty). Used by quick-fix dropdowns.
        /// 現在の URP アセット内の全 <see cref="Liquid2DFeature"/> の名前タグを収集します（重複・空を除外）。
        /// </summary>
        public static List<string> GetAllFeatureNameTags()
        {
            var result = new List<string>();
            var urpAsset = GetCurrentUrpAsset();
            if (!urpAsset) return result;

            foreach (var data in GetRendererDataList(urpAsset))
            {
                if (!data) continue;
                foreach (var f in data.rendererFeatures)
                {
                    if (f is Liquid2DFeature liquidFeature &&
                        !string.IsNullOrEmpty(liquidFeature.NameTag) &&
                        !result.Contains(liquidFeature.NameTag))
                    {
                        result.Add(liquidFeature.NameTag);
                    }
                }
            }

            return result;
        }

        /// <summary>
        /// 若指定 RendererData 尚未加入当前 URP 资源的 RendererData 列表，则添加并保存；返回其在列表中的索引（-1 表示失败）。
        /// Adds the given RendererData to the current URP asset's RendererData list if missing, then saves; returns its
        /// index in the list (-1 on failure).
        /// 指定の RendererData が URP アセットの一覧に無ければ追加・保存し、そのインデックスを返します（失敗時 -1）。
        /// </summary>
        public static int EnsureRendererDataInUrp(ScriptableRendererData rendererData)
        {
            if (!rendererData) return -1;

            var urpAsset = GetCurrentUrpAsset();
            if (!urpAsset) return -1;

            var so = new SerializedObject(urpAsset);
            var list = so.FindProperty("m_RendererDataList");
            if (list == null) return -1;

            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == rendererData)
                    return i;
            }

            list.InsertArrayElementAtIndex(list.arraySize);
            list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = rendererData;
            so.ApplyModifiedProperties();
            EditorUtility.SetDirty(urpAsset);
            AssetDatabase.SaveAssets();

            return list.arraySize - 1;
        }
    }
}

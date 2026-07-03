#if !UNITY_6000_0_OR_NEWER
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace Fs.Liquid2D.Editor
{
    /// <summary>
    /// Fs.Liquid2D.RenderingLayerMask（Unity 2022.3 兼容垫片）的 Inspector 绘制器。
    /// 从 URP Global Settings 读取已配置的渲染层名称，绘制成与 Unity 6 内置一致的勾选式遮罩下拉。
    /// 逻辑复刻自 URP 内部的 EditorUtils.DrawRenderingLayerMask（该方法为 internal 无法直接调用），仅用公有 API。
    /// U6 不编译本文件（引擎内置的 RenderingLayerMask 结构体自带绘制器）。
    ///
    /// Inspector drawer for Fs.Liquid2D.RenderingLayerMask (the Unity 2022.3 compatibility shim).
    /// Reads the configured rendering-layer names from URP Global Settings and draws the same checkbox-style mask
    /// popup as Unity 6's built-in one. Logic mirrors URP's internal EditorUtils.DrawRenderingLayerMask (that method
    /// is internal, so it cannot be called directly) using only public API. Not compiled on U6 (the engine's built-in
    /// RenderingLayerMask struct has its own drawer).
    ///
    /// Fs.Liquid2D.RenderingLayerMask（Unity 2022.3 互換シム）用の Inspector ドロワー。
    /// URP Global Settings から設定済みのレンダリングレイヤー名を読み取り、Unity 6 の組み込みと同じチェックボックス式
    /// マスクポップアップを描画します。ロジックは URP 内部の EditorUtils.DrawRenderingLayerMask（internal のため直接
    /// 呼び出せない）を公開 API のみで再現しています。U6 ではコンパイルされません（組み込み構造体が独自ドロワーを持つ）。
    /// </summary>
    [CustomPropertyDrawer(typeof(Fs.Liquid2D.RenderingLayerMask))]
    public class RenderingLayerMaskDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty bitsProp = property.FindPropertyRelative("_bits");
            if (bitsProp == null)
            {
                // 兜底：结构体布局异常时退回默认绘制，避免抛异常。
                // Fallback: if the struct layout is unexpected, draw the default field instead of throwing.
                // フォールバック：構造体レイアウトが想定外の場合、例外を投げずデフォルト描画に退避。
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            int mask = unchecked((int)bitsProp.uintValue);
            string[] names = GetRenderingLayerMaskNames(mask);

            EditorGUI.BeginProperty(position, label, property);
            EditorGUI.BeginChangeCheck();
            mask = EditorGUI.MaskField(position, label, mask, names);
            if (EditorGUI.EndChangeCheck())
                bitsProp.uintValue = unchecked((uint)mask);
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// 读取 URP Global Settings 的渲染层名称，并按 URP 原生行为补足未定义位（"Unused Layer N"）。
        /// Reads URP Global Settings rendering-layer names, padding undefined bits ("Unused Layer N") like URP does.
        /// URP Global Settings のレンダリングレイヤー名を読み取り、URP 標準に倣って未定義ビットを補います。
        /// </summary>
        private static string[] GetRenderingLayerMaskNames(int mask)
        {
            // URP 14 的 UniversalRenderPipelineGlobalSettings 类是 internal（跨程序集不可访问），
            // 因此改从当前生效的 URP 资产取名称——UniversalRenderPipelineAsset.renderingLayerMaskNames 是 public，
            // 其实现内部转发到 Global Settings，效果一致。
            // URP 14's UniversalRenderPipelineGlobalSettings class is internal (inaccessible across assemblies),
            // so we read the names from the active URP asset instead — UniversalRenderPipelineAsset.renderingLayerMaskNames
            // is public and forwards to Global Settings internally, giving the same result.
            // URP 14 の UniversalRenderPipelineGlobalSettings クラスは internal（アセンブリ跨ぎでアクセス不可）のため、
            // 有効な URP アセットから名前を取得します（UniversalRenderPipelineAsset.renderingLayerMaskNames は public で
            // 内部的に Global Settings へ転送され、結果は同じ）。
            var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
            string[] names = urpAsset ? urpAsset.renderingLayerMaskNames : null;
            if (names == null || names.Length == 0)
                names = new[] { "Default" };

            // 若掩码用到的最高位超过已定义名称数量，补 "Unused Layer N"，与 URP 一致。
            // If the mask uses bits beyond the defined names, pad with "Unused Layer N", matching URP.
            // マスクが定義済み名称数を超えるビットを使う場合、URP に合わせて "Unused Layer N" を補います。
            int maskCount = (int)Mathf.Log(mask, 2) + 1;
            if (mask > 0 && names.Length < maskCount && maskCount <= 32)
            {
                var padded = new string[maskCount];
                for (int i = 0; i < maskCount; ++i)
                    padded[i] = i < names.Length ? names[i] : $"Unused Layer {i}";
                names = padded;
            }

            return names;
        }
    }
}
#endif

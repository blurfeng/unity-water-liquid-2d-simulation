using UnityEditor;
using UnityEngine;

namespace Fs.Liquid2D.Editor
{
    /// <summary>
    /// <see cref="Liquid2DParticleDescriptor"/> 的自定义 Inspector：高亮校验 NameTag/Sprite/Material 等关键配置，
    /// 提供「定位 RendererData」「一键修复 NameTag」等便捷操作与颜色/贴图预览。
    /// Custom inspector for <see cref="Liquid2DParticleDescriptor"/>: highlights validation of key settings
    /// (NameTag/Sprite/Material) and offers conveniences like "locate RendererData", "quick-fix NameTag", and previews.
    /// <see cref="Liquid2DParticleDescriptor"/> のカスタム Inspector：主要設定（NameTag/Sprite/Material）の検証を
    /// ハイライトし、「RendererData を表示」「NameTag のワンクリック修復」やプレビューを提供します。
    /// </summary>
    [CustomEditor(typeof(Liquid2DParticleDescriptor))]
    [CanEditMultipleObjects]
    public class Liquid2DParticleDescriptorEditor : UnityEditor.Editor
    {
        private static readonly Color _colorOk = new Color(0.55f, 0.9f, 0.55f);
        private static readonly Color _colorWarn = new Color(0.95f, 0.85f, 0.4f);
        private static readonly Color _colorError = new Color(0.95f, 0.5f, 0.5f);

        // private SerializedProperty _radius;
        private SerializedProperty _renderScale;
        private SerializedProperty _defaultLifetime;
        private SerializedProperty _renderSettings;
        private SerializedProperty _sprite;
        private SerializedProperty _material;
        private SerializedProperty _color;
        private SerializedProperty _nameTag;

        private void OnEnable()
        {
            // _radius = serializedObject.FindProperty("Radius");
            _renderScale = serializedObject.FindProperty("RenderScale");
            _defaultLifetime = serializedObject.FindProperty("DefaultLifetime");
            _renderSettings = serializedObject.FindProperty("RenderSettings");
            if (_renderSettings != null)
            {
                _sprite = _renderSettings.FindPropertyRelative("Sprite");
                _material = _renderSettings.FindPropertyRelative("Material");
                _color = _renderSettings.FindPropertyRelative("Color");
                _nameTag = _renderSettings.FindPropertyRelative("NameTag");
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // RenderSettings 可能为 null（资源刚创建）。 // RenderSettings may be null on a freshly created asset.
            if (_renderSettings == null || _sprite == null)
            {
                EditorGUILayout.HelpBox(
                    L("渲染设置 (RenderSettings) 缺失，无法渲染。",
                        "RenderSettings is missing; nothing can be rendered.",
                        "RenderSettings がありません。描画できません。"),
                    MessageType.Error);
                DrawDefaultInspector();
                return;
            }

            DrawSummaryBox();
            EditorGUILayout.Space();

            DrawNameTagRow();
            DrawSpriteRow();
            DrawMaterialRow();
            DrawColorRow();

            EditorGUILayout.Space();

            // 其余字段走默认绘制，避免重复手绘。 // Remaining fields use default drawing.
            DrawPropertiesExcluding(serializedObject, "m_Script", "RenderSettings");
            DrawScaleAndLifetimeHints();

            serializedObject.ApplyModifiedProperties();
        }

        // ---------------------------------------------------------------------------------------------------------

        private void DrawSummaryBox()
        {
            bool spriteOk = _sprite.objectReferenceValue;
            bool materialOk = _material.objectReferenceValue;
            string tag = _nameTag.stringValue;
            bool tagEmpty = string.IsNullOrEmpty(tag);
            bool tagMatched = !tagEmpty &&
                Liquid2DEditorUtility.TryFindFeatureRendererDataByNameTag(tag, out _, out _);

            if (!spriteOk || !materialOk)
            {
                EditorGUILayout.HelpBox(
                    L("配置不完整：Sprite / Material 为渲染必需项，缺失将无法渲染。",
                        "Incomplete: Sprite / Material are required for rendering; missing them disables rendering.",
                        "未完成：Sprite / Material は描画に必須です。欠けると描画されません。"),
                    MessageType.Error);
            }
            else if (!tagEmpty && !tagMatched)
            {
                EditorGUILayout.HelpBox(
                    L("NameTag 在当前 URP 中找不到匹配的 Liquid2DFeature，流体将不会被渲染。",
                        "NameTag has no matching Liquid2DFeature in the current URP; the fluid will not be rendered.",
                        "現在の URP に一致する Liquid2DFeature が無いため、流体は描画されません。"),
                    MessageType.Warning);
            }
            else if (tagEmpty)
            {
                EditorGUILayout.HelpBox(
                    L("NameTag 为空：将被所有 Liquid2DFeature 渲染。如需配合 Volume 控制，请设置唯一标签。",
                        "Empty NameTag: rendered by every Liquid2DFeature. Set a unique tag if you need Volume control.",
                        "NameTag が空：すべての Liquid2DFeature で描画されます。Volume 制御には一意のタグを設定してください。"),
                    MessageType.Info);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    L("配置有效：渲染必需项齐备，NameTag 已匹配到 Feature。",
                        "Valid: required render fields present and NameTag matches a Feature.",
                        "有効：必須項目が揃い、NameTag が Feature に一致しています。"),
                    MessageType.Info);
            }
        }

        private void DrawNameTagRow()
        {
            string tag = _nameTag.stringValue;
            bool tagEmpty = string.IsNullOrEmpty(tag);
            bool matched = Liquid2DEditorUtility.TryFindFeatureRendererDataByNameTag(
                tag, out var locatedData, out _);

            Color statusColor = tagEmpty ? _colorWarn : (matched ? _colorOk : _colorError);

            EditorGUILayout.BeginHorizontal();
            {
                Color prev = GUI.backgroundColor;
                GUI.backgroundColor = statusColor;
                EditorGUILayout.PropertyField(_nameTag, new GUIContent(
                    L("名称标签 NameTag", "NameTag", "名前タグ NameTag"),
                    _nameTag.tooltip));
                GUI.backgroundColor = prev;

                using (new EditorGUI.DisabledScope(!matched || !locatedData))
                {
                    if (GUILayout.Button(new GUIContent(
                            L("定位 RendererData", "Locate RendererData", "RendererData を表示"),
                            L("在 Project 中选中并高亮对应的 RendererData 资源。",
                                "Select and ping the matching RendererData asset in the Project window.",
                                "対応する RendererData アセットを Project で選択・ハイライトします。")),
                            GUILayout.Width(140f)) && locatedData)
                    {
                        Selection.activeObject = locatedData;
                        EditorGUIUtility.PingObject(locatedData);
                    }
                }
            }
            EditorGUILayout.EndHorizontal();

            // 常驻显示当前 URP 中所有可用的 NameTag，方便直接选用。 // Always show all available NameTags in the current URP for direct selection.
            var tags = Liquid2DEditorUtility.GetAllFeatureNameTags();
            if (tags.Count > 0)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();
                {
                    int current = tags.IndexOf(tag);
                    int picked = EditorGUILayout.Popup(new GUIContent(
                            L("可用列表", "Available", "利用可能"),
                            L("当前 URP 中所有 Liquid2DFeature 的 NameTag，选择即写入上方字段。",
                                "All Liquid2DFeature NameTags in the current URP; selecting one writes it into the field above.",
                                "現在の URP の全 Liquid2DFeature の NameTag。選択すると上のフィールドに反映されます。")),
                        current, tags.ToArray());
                    if (picked >= 0 && picked != current)
                    {
                        _nameTag.stringValue = tags[picked];
                    }
                }
                EditorGUILayout.EndHorizontal();
                EditorGUI.indentLevel--;
            }
        }

        private void DrawSpriteRow()
        {
            DrawRequiredObjectRow(_sprite,
                L("贴图 Sprite", "Sprite", "スプライト Sprite"));
        }

        private void DrawMaterialRow()
        {
            DrawRequiredObjectRow(_material,
                L("材质 Material", "Material", "マテリアル Material"));
        }

        private void DrawRequiredObjectRow(SerializedProperty prop, string label)
        {
            bool ok = prop.objectReferenceValue;
            Color prev = GUI.backgroundColor;
            GUI.backgroundColor = ok ? prev : _colorError;
            EditorGUILayout.PropertyField(prop, new GUIContent(label, prop.tooltip));
            GUI.backgroundColor = prev;

            if (!ok)
            {
                EditorGUILayout.HelpBox(
                    L($"{label} 为渲染必需项。", $"{label} is required for rendering.", $"{label} は描画に必須です。"),
                    MessageType.Error);
            }
        }

        private void DrawColorRow()
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.PropertyField(_color, new GUIContent(
                    L("颜色 Color", "Color", "カラー Color"), _color.tooltip));

                // 颜色预览块（HDR 颜色钳制到可显示范围）。 // Color preview swatch (HDR clamped for display).
                Rect r = GUILayoutUtility.GetRect(40f, EditorGUIUtility.singleLineHeight,
                    GUILayout.Width(40f));
                Color c = _color.colorValue;
                c.a = 1f;
                EditorGUI.DrawRect(r, c);

                // 透明覆盖一个带 tooltip 的标签，鼠标悬停时说明该颜色的含义。 // Overlay a transparent tooltip label so hovering explains the color's meaning.
                GUI.Label(r, new GUIContent(string.Empty,
                    L("流体粒子的渲染颜色（HDR）。预览块固定为不透明以便观察色相，实际透明度由 Color 的 Alpha 决定。",
                        "The fluid particle's render color (HDR). The swatch is forced opaque to show the hue; actual transparency comes from the Color alpha.",
                        "流体パーティクルの描画カラー（HDR）。プレビューは色相確認のため不透明固定で、実際の透明度は Color のアルファで決まります。")));
            }
            EditorGUILayout.EndHorizontal();

            // Sprite 缩略图预览。 // Sprite thumbnail preview.
            var sprite = _sprite.objectReferenceValue as Sprite;
            if (sprite)
            {
                Texture2D preview = AssetPreview.GetAssetPreview(sprite);
                if (preview)
                {
                    Rect r = GUILayoutUtility.GetRect(64f, 64f, GUILayout.Width(64f), GUILayout.Height(64f));
                    GUI.DrawTexture(r, preview, ScaleMode.ScaleToFit);
                }
            }
        }

        private void DrawScaleAndLifetimeHints()
        {
            if (_renderScale != null)
            {
                float scale = _renderScale.floatValue;
                if (scale < 1f || scale > 8f)
                {
                    EditorGUILayout.HelpBox(
                        L("RenderScale 建议取 1~8：metaball 融合需要远大于物理半径的可视 blob。",
                            "RenderScale recommended 1~8: metaball fusion needs visual blobs much larger than the physics radius.",
                            "RenderScale は 1~8 推奨：メタボール融合には物理半径より大きな可視 blob が必要です。"),
                        MessageType.Info);
                }
            }

            if (_defaultLifetime != null && Mathf.Approximately(_defaultLifetime.floatValue, 0f))
            {
                EditorGUILayout.HelpBox(
                    L("DefaultLifetime = 0：寿命无限（不会自动消亡）。",
                        "DefaultLifetime = 0: infinite lifetime (particles never auto-expire).",
                        "DefaultLifetime = 0：寿命は無限（自動消滅しません）。"),
                    MessageType.None);
            }
        }

        // ---------------------------------------------------------------------------------------------------------

        /// <summary>
        /// 按系统语言选择文案（中/英/日）。 // Pick text by system language (zh/en/ja).
        /// </summary>
        private static string L(string zh, string en, string ja)
        {
            switch (Application.systemLanguage)
            {
                case SystemLanguage.Chinese:
                case SystemLanguage.ChineseSimplified:
                case SystemLanguage.ChineseTraditional:
                    return zh;
                case SystemLanguage.Japanese:
                    return ja;
                default:
                    return en;
            }
        }
    }
}

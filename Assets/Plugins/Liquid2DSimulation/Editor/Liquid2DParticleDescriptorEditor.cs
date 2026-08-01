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
        private SerializedProperty _colorMode;
        private SerializedProperty _colorGradient;
        private SerializedProperty _gradientSource;
        private SerializedProperty _gradientSpeedMin;
        private SerializedProperty _gradientSpeedMax;
        private SerializedProperty _gradientFoamStart;
        private SerializedProperty _gradientFoamEnd;
        private SerializedProperty _gradientSmoothing;

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
                _colorMode = _renderSettings.FindPropertyRelative("ColorMode");
                _colorGradient = _renderSettings.FindPropertyRelative("ColorGradient");
                _gradientSource = _renderSettings.FindPropertyRelative("GradientSource");
                _gradientSpeedMin = _renderSettings.FindPropertyRelative("GradientSpeedMin");
                _gradientSpeedMax = _renderSettings.FindPropertyRelative("GradientSpeedMax");
                _gradientFoamStart = _renderSettings.FindPropertyRelative("GradientFoamStart");
                _gradientFoamEnd = _renderSettings.FindPropertyRelative("GradientFoamEnd");
                _gradientSmoothing = _renderSettings.FindPropertyRelative("GradientSmoothing");
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
            DrawSpritePreview();
            DrawColorModeRows();

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
                    "NameTag",
                    _nameTag.tooltip));
                GUI.backgroundColor = prev;

                using (new EditorGUI.DisabledScope(!matched || !locatedData))
                {
                    if (GUILayout.Button(new GUIContent("Locate RendererData",
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
                            "Available",
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
            DrawRequiredObjectRow(_sprite, "Sprite");
        }

        private void DrawMaterialRow()
        {
            DrawRequiredObjectRow(_material, "Material");
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

        // 绘制 Color 字段 + 色相预览块。仅 Simple（简单）颜色模式使用此颜色，故由 DrawColorModeRows 在该模式下调用。
        // Draw the Color field + hue swatch. Only Simple color mode uses this color, so DrawColorModeRows calls it in that mode.
        // Color フィールド + 色相プレビューを描画。Simple モードのみ使用。
        private void DrawColorField()
        {
            EditorGUILayout.BeginHorizontal();
            {
                EditorGUILayout.PropertyField(_color, new GUIContent("Color", _color.tooltip));

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
        }

        // Sprite 缩略图预览（与颜色模式无关，始终显示于 Sprite/Material 之后）。 // Sprite thumbnail preview (mode-independent; always shown after Sprite/Material). // Sprite サムネイル。
        private void DrawSpritePreview()
        {
            var sprite = _sprite.objectReferenceValue as Sprite;
            if (!sprite) return;
            Texture2D preview = AssetPreview.GetAssetPreview(sprite);
            if (preview)
            {
                Rect r = GUILayoutUtility.GetRect(64f, 64f, GUILayout.Width(64f), GUILayout.Height(64f));
                GUI.DrawTexture(r, preview, ScaleMode.ScaleToFit);
            }
        }

        /// <summary>
        /// 绘制颜色模式及其（仅 Gradient 时的）渐变子配置。RenderSettings 在本编辑器中手绘并被排除于默认绘制之外，
        /// 故新增字段须在此显式绘制。 // Draw the color mode and (Gradient-only) gradient sub-config. RenderSettings is
        /// hand-drawn here and excluded from default drawing, so new fields must be drawn explicitly. // カラーモードを描画。
        /// </summary>
        private void DrawColorModeRows()
        {
            if (_colorMode == null)
            {
                // 极端情况下（无 ColorMode 属性）仍需绘制 Color，避免其被完全隐藏。 // Fallback: still draw Color if ColorMode property is missing. // フォールバック。
                DrawColorField();
                return;
            }

            EditorGUILayout.PropertyField(_colorMode, new GUIContent("Color Mode", _colorMode.tooltip));

            EditorGUI.indentLevel++;

            // Simple（简单）模式：Color 生效，显示于此；Gradient 模式：Color 被忽略，改显示渐变配置。
            // Simple mode: Color applies, shown here; Gradient mode: Color is ignored, gradient config shown instead.
            // Simple：Color を表示；Gradient：Color を無視し渐変設定を表示。
            if (_colorMode.enumValueIndex != (int)EParticleColorMode.Gradient)
            {
                DrawColorField();
                EditorGUI.indentLevel--;
                return;
            }

            if (_colorGradient != null)
                EditorGUILayout.PropertyField(_colorGradient, new GUIContent("Color Gradient", _colorGradient.tooltip));
            if (_gradientSource != null)
                EditorGUILayout.PropertyField(_gradientSource, new GUIContent("Gradient Source", _gradientSource.tooltip));

            // 按子模式只显示相关参数：Speed→Speed Max；Foam→Foam Start/End；FoamWithSpeed→两者都要。
            // Show only the relevant params per sub-mode: Speed→Speed Max; Foam→Foam Start/End; FoamWithSpeed→both.
            // サブモード別に関連パラメータのみ表示：Speed→Speed Max、Foam→Foam Start/End、FoamWithSpeed→両方。
            int src = _gradientSource != null ? _gradientSource.enumValueIndex : (int)EGradientColorSource.Speed;
            bool usesFoam = src == (int)EGradientColorSource.Foam || src == (int)EGradientColorSource.FoamWithSpeed;
            bool usesSpeed = src == (int)EGradientColorSource.Speed || src == (int)EGradientColorSource.FoamWithSpeed;

            if (usesFoam)
            {
                if (_gradientFoamStart != null)
                    EditorGUILayout.PropertyField(_gradientFoamStart, new GUIContent("Foam Start", _gradientFoamStart.tooltip));
                if (_gradientFoamEnd != null)
                    EditorGUILayout.PropertyField(_gradientFoamEnd, new GUIContent("Foam End", _gradientFoamEnd.tooltip));

                // FoamEnd 应小于 FoamStart（否则映射退化）。 // FoamEnd should be less than FoamStart (else the mapping degenerates). // FoamEnd < FoamStart。
                if (_gradientFoamStart != null && _gradientFoamEnd != null
                    && _gradientFoamEnd.floatValue >= _gradientFoamStart.floatValue)
                {
                    EditorGUILayout.HelpBox(
                        L("Foam End 应小于 Foam Start，否则泡沫映射无效。",
                            "Foam End should be less than Foam Start, otherwise the foam mapping is invalid.",
                            "Foam End は Foam Start より小さくしてください。さもないと泡マッピングが無効です。"),
                        MessageType.Warning);
                }
            }

            if (usesSpeed && _gradientSpeedMax != null)
            {
                // 速度归一化下限（缓慢移动不出色/泡）。 // Speed lower bound (slow motion produces no color/foam). // 速度下限。
                if (_gradientSpeedMin != null)
                    EditorGUILayout.PropertyField(_gradientSpeedMin, new GUIContent("Speed Min", _gradientSpeedMin.tooltip));

                // FoamWithSpeed 模式下 Speed Max 语义为「泡沫达到满强度所需的速度」。 // In FoamWithSpeed mode, Speed Max is "the speed at which foam reaches full strength". // FoamWithSpeed では「泡が最大になる速度」。
                EditorGUILayout.PropertyField(_gradientSpeedMax, new GUIContent("Speed Max", _gradientSpeedMax.tooltip));

                // Speed Min 应小于 Speed Max（否则速度重映射退化）。 // Speed Min should be less than Speed Max. // Speed Min < Speed Max。
                if (_gradientSpeedMin != null && _gradientSpeedMin.floatValue >= _gradientSpeedMax.floatValue)
                {
                    EditorGUILayout.HelpBox(
                        L("Speed Min 应小于 Speed Max，否则速度重映射无效（几乎所有速度都被归为 0 或 1）。",
                            "Speed Min should be less than Speed Max, otherwise the speed remap is invalid (almost all speeds collapse to 0 or 1).",
                            "Speed Min は Speed Max より小さくしてください。さもないと速度リマップが無効になります。"),
                        MessageType.Warning);
                }

                // 必须大于 0：0 或负值会让速度归一化溢出（t 恒饱和为 1），所有粒子都采样到渐变末端（最大速度色）。
                // 自动纠正为 1 使配置立即有效（含旧资产遗留的 0）。
                // Must be > 0: 0 or negative overflows the speed normalization (t saturates to 1), so every particle samples
                // the gradient's end (max-speed color). Auto-correct to 1 so the config is immediately valid (covers the
                // legacy 0 left in old assets). // 0 以下は不可、1 に自動補正。
                if (_gradientSpeedMax.floatValue <= 0f)
                {
                    _gradientSpeedMax.floatValue = 1f;
                    EditorGUILayout.HelpBox(
                        L("Speed Max 必须大于 0，否则所有粒子都会采样到渐变末端（最大速度色）。已自动设为 1，请按流体实际速度上限调整。",
                            "Speed Max must be greater than 0, otherwise every particle samples the gradient's end (max-speed color). Reset to 1; tune it to your fluid's actual peak speed.",
                            "Speed Max は 0 より大きくしてください。さもないと全粒子がグラデーション末端（最大速度色）を採取します。1 にリセットしました。流体の実際の最大速度に調整してください。"),
                        MessageType.Warning);
                }
                else if (_gradientSpeedMax.floatValue < 1f)
                {
                    EditorGUILayout.HelpBox(
                        L("Speed Max 小于 1：速度略大即达渐变末端，可能大部分粒子都采样到最大速度色。建议按流体实际速度上限设置（如 5~20）。",
                            "Speed Max below 1: even small speeds reach the gradient end, so most particles may sample the max-speed color. Set it near your fluid's actual peak speed (e.g. 5~20).",
                            "Speed Max が 1 未満：わずかな速度でグラデーション末端に達し、多くの粒子が最大速度色になります。流体の実際の最大速度（例 5~20）に設定してください。"),
                        MessageType.Info);
                }
            }

            // 渐变时间平滑（所有渐变子模式通用，逐流体独立）。 // Gradient temporal smoothing (all gradient sub-modes; per-fluid). // 全渐変モード共通。
            if (_gradientSmoothing != null)
                EditorGUILayout.PropertyField(_gradientSmoothing, new GUIContent("Gradient Smoothing", _gradientSmoothing.tooltip));

            EditorGUI.indentLevel--;
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
                    L("DefaultLifetime = 0：存活时间无限（不会自动消亡）。",
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

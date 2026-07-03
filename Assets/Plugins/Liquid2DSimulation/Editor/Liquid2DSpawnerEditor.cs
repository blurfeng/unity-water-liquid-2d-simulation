using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace Fs.Liquid2D.Editor
{
    /// <summary>
    /// <see cref="Liquid2DSpawner"/> 的自定义 Inspector：提供路径点可重排列表、Scene 视图拖拽手柄、
    /// 路径统计、运行时状态面板等编辑便利功能。
    /// Custom inspector for <see cref="Liquid2DSpawner"/>: reorderable waypoint list, Scene-view drag handles,
    /// path statistics, runtime status panel, and editing convenience tools.
    /// <see cref="Liquid2DSpawner"/> のカスタム Inspector：並べ替え可能なウェイポイントリスト、Scene ビューのドラッグハンドル、
    /// パス統計、実行時ステータスパネルなどの編集補助機能を提供します。
    /// </summary>
    [CustomEditor(typeof(Liquid2DSpawner))]
    [CanEditMultipleObjects]
    public class Liquid2DSpawnerEditor : UnityEditor.Editor
    {
        private static readonly Color _colorOk   = new Color(0.55f, 0.9f, 0.55f);
        private static readonly Color _colorWarn  = new Color(0.95f, 0.85f, 0.4f);
        private static readonly Color _colorInfo  = new Color(0.5f, 0.8f, 1f, 0.9f);

        // Cached SerializedProperties — Movement section (others drawn by DrawPropertiesExcluding)
        private SerializedProperty _moveEnabled;
        private SerializedProperty _moveOnAwake;
        private SerializedProperty _moveMode;
        private SerializedProperty _moveSpeed;
        private SerializedProperty _waypoints;

        private ReorderableList _waypointList;

        // ── Lifecycle ─────────────────────────────────────────────────────────────

        private void OnEnable()
        {
            _moveEnabled = serializedObject.FindProperty("moveEnabled");
            _moveOnAwake = serializedObject.FindProperty("moveOnAwake");
            _moveMode    = serializedObject.FindProperty("moveMode");
            _moveSpeed   = serializedObject.FindProperty("moveSpeed");
            _waypoints   = serializedObject.FindProperty("waypoints");

            BuildWaypointList();
        }

        // ── Inspector ─────────────────────────────────────────────────────────────

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Draw every property except the Movement section (which we draw custom below).
            DrawPropertiesExcluding(serializedObject,
                "moveEnabled", "moveOnAwake", "moveMode", "moveSpeed", "waypoints");

            GUILayout.Space(4);
            DrawMovementSection();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawMovementSection()
        {
            DrawSectionSeparator("Movement");

            EditorGUILayout.PropertyField(_moveEnabled,
                new GUIContent(L("启用路径移动", "Enable Path Movement", "パス移動を有効にする"),
                               L("启用后 Spawner 将沿路径点序列移动（与喷射独立）。",
                                 "The Spawner will move along the waypoint sequence, independent of spawning.",
                                 "Spawner はウェイポイント列に沿って移動します（噴射とは独立）。")));

            bool enabled = _moveEnabled.boolValue;
            using (new EditorGUI.DisabledScope(!enabled))
            {
                EditorGUILayout.PropertyField(_moveOnAwake,
                    new GUIContent(L("启动时自动移动", "Move On Awake", "起動時に移動")));
                EditorGUILayout.PropertyField(_moveMode,
                    new GUIContent(L("移动模式", "Move Mode", "移動モード")));
                EditorGUILayout.PropertyField(_moveSpeed,
                    new GUIContent(L("移动速度 (单位/秒)", "Move Speed (units/s)", "移動速度（単位/秒）")));

                GUILayout.Space(4);

                if (enabled)
                    DrawWarnings();

                _waypointList.DoLayoutList();

                if (_waypoints.arraySize >= 2)
                    DrawPathStats();

                if (Application.isPlaying && enabled)
                    DrawRuntimeStatus();
            }
        }

        // ── Warnings ─────────────────────────────────────────────────────────────

        private void DrawWarnings()
        {
            int count = _waypoints.arraySize;
            if (count == 0)
            {
                using (new BgColorScope(_colorWarn))
                    EditorGUILayout.HelpBox(
                        L("未配置路径点。移动不会发生。",
                          "No waypoints configured — movement will not occur.",
                          "ウェイポイントが設定されていません。移動は発生しません。"),
                        MessageType.Warning);
            }
            else if (count == 1)
            {
                using (new BgColorScope(_colorInfo))
                    EditorGUILayout.HelpBox(
                        L("只有 1 个路径点：Spawner 将瞬移到该点。Once 模式下完成后停止，其他模式将一直停留。",
                          "Single waypoint: the Spawner teleports to it. Stops in Once mode; stays in others.",
                          "ウェイポイントが1つ：Spawner はそこにテレポートします。Once モードで完了、他は停留。"),
                        MessageType.Info);
            }
        }

        // ── Path Statistics ───────────────────────────────────────────────────────

        private void DrawPathStats()
        {
            int count = _waypoints.arraySize;
            float onewayLen = 0f;
            for (int i = 0; i < count - 1; i++)
            {
                Vector2 a = _waypoints.GetArrayElementAtIndex(i).FindPropertyRelative("Position").vector2Value;
                Vector2 b = _waypoints.GetArrayElementAtIndex(i + 1).FindPropertyRelative("Position").vector2Value;
                onewayLen += Vector2.Distance(a, b);
            }

            // Cycle length depends on mode (enumValueIndex: 0=Once, 1=Loop, 2=PingPong)
            int modeIdx = _moveMode.enumValueIndex;
            float cycleLen = onewayLen;
            if (modeIdx == 1) // Loop: add return leg
            {
                Vector2 first = _waypoints.GetArrayElementAtIndex(0).FindPropertyRelative("Position").vector2Value;
                Vector2 last  = _waypoints.GetArrayElementAtIndex(count - 1).FindPropertyRelative("Position").vector2Value;
                cycleLen = onewayLen + Vector2.Distance(last, first);
            }
            else if (modeIdx == 2) // PingPong: twice the one-way
            {
                cycleLen = onewayLen * 2f;
            }

            float speed = _moveSpeed.floatValue;
            float cycleTime = speed > 0.0001f ? cycleLen / speed : 0f;

            string modeLabel = modeIdx == 0
                ? L("单次", "one-way", "一方向")
                : modeIdx == 1
                    ? L("每循环", "per loop", "ループごと")
                    : L("每往复", "per cycle", "往復ごと");

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.LabelField(
                    L($"路径长度: {onewayLen:F2} 单位  |  耗时 ({modeLabel}): ~{cycleTime:F1} 秒",
                      $"Path length: {onewayLen:F2} units  |  Time ({modeLabel}): ~{cycleTime:F1} s",
                      $"パス長: {onewayLen:F2} 単位  |  所要時間 ({modeLabel}): ~{cycleTime:F1} 秒"),
                    EditorStyles.miniLabel);
            }
        }

        // ── Runtime Status Panel ──────────────────────────────────────────────────

        private void DrawRuntimeStatus()
        {
            GUILayout.Space(4);

            // Colored header bar
            Rect headerRect = EditorGUILayout.GetControlRect(false, EditorGUIUtility.singleLineHeight + 2f);
            EditorGUI.DrawRect(headerRect, new Color(0.2f, 0.4f, 0.8f, 0.18f));
            EditorGUI.LabelField(headerRect,
                L("  ▶ 运行状态", "  ▶ Runtime Status", "  ▶ 実行状態"),
                EditorStyles.boldLabel);

            var spawner = (Liquid2DSpawner)target;

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.Toggle(
                    L("移动中", "Is Moving", "移動中"), spawner.IsMoving);

                if (spawner.IsMoveComplete)
                {
                    EditorGUILayout.LabelField(
                        L("状态", "Status", "状態"),
                        L("✓ 已完成 (Once)", "✓ Completed (Once)", "✓ 完了 (Once)"));
                }
                else if (spawner.IsMoving)
                {
                    int idx = spawner.CurrentWaypointIndex;
                    EditorGUILayout.LabelField(
                        L("当前目标路径点", "Target Waypoint", "目標ウェイポイント"),
                        idx.ToString());

                    if (idx < _waypoints.arraySize)
                    {
                        Vector2 localPos = _waypoints.GetArrayElementAtIndex(idx)
                            .FindPropertyRelative("Position").vector2Value;
                        Vector3 worldTarget = LocalToWorld(spawner, localPos);
                        float dist = Vector2.Distance(spawner.transform.position, worldTarget);
                        EditorGUILayout.LabelField(
                            L("距下一路径点", "Distance to Next", "次のウェイポイントまで"),
                            $"{dist:F2}");
                    }
                }
            }

            Repaint(); // Keep refreshing in play mode
        }

        // ── Waypoint ReorderableList ──────────────────────────────────────────────

        private void BuildWaypointList()
        {
            _waypointList = new ReorderableList(serializedObject, _waypoints,
                draggable: true, displayHeader: true, displayAddButton: true, displayRemoveButton: true)
                {
                    drawHeaderCallback = DrawWaypointListHeader,
                    drawElementCallback = DrawWaypointElement,
                    elementHeight = EditorGUIUtility.singleLineHeight + 6f,
                    onAddCallback = OnAddWaypoint
                };
        }

        private void DrawWaypointListHeader(Rect rect)
        {
            // Left: title
            float btnW = 120f;
            EditorGUI.LabelField(new Rect(rect.x, rect.y, rect.width - btnW - 4f, rect.height),
                L($"路径点列表 ({_waypoints.arraySize} 个，父局部坐标)",
                  $"Waypoints ({_waypoints.arraySize}, parent-local)",
                  $"ウェイポイント ({_waypoints.arraySize} 個、親ローカル)"));

            // Right: "Add at Current Position" button
            bool multiEdit = targets.Length > 1;
            using (new EditorGUI.DisabledScope(multiEdit))
            {
                if (GUI.Button(
                    new Rect(rect.xMax - btnW, rect.y + 1f, btnW, rect.height - 2f),
                    L("+ 在此处添加", "+ Add at Current", "+ 現在地点で追加"),
                    EditorStyles.miniButton))
                {
                    AddWaypointAtCurrent();
                }
            }
        }

        private void DrawWaypointElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            var element  = _waypoints.GetArrayElementAtIndex(index);
            var posProp  = element.FindPropertyRelative("Position");
            var waitProp = element.FindPropertyRelative("WaitTime");

            float y = rect.y + 3f;
            float h = EditorGUIUtility.singleLineHeight;
            float x = rect.x;
            float w = rect.width;

            // Index badge
            const float idxW = 22f;
            EditorGUI.LabelField(new Rect(x, y, idxW, h), index.ToString(), EditorStyles.centeredGreyMiniLabel);
            x += idxW + 2f;
            w -= idxW + 2f;

            // "← Set Here" button (rightmost)
            bool multiEdit = targets.Length > 1;
            float setW = 76f;
            Rect setRect = new Rect(x + w - setW, y, setW, h);
            w -= setW + 4f;

            // Wait label + field
            const float waitLabelW = 28f;
            const float waitFieldW = 44f;
            Rect waitLabelRect = new Rect(x + w - waitLabelW - waitFieldW - 4f, y, waitLabelW, h);
            Rect waitFieldRect = new Rect(x + w - waitFieldW, y, waitFieldW, h);
            w -= waitLabelW + waitFieldW + 8f;

            // Position field (fills remaining space)
            EditorGUI.PropertyField(new Rect(x, y, w, h), posProp, GUIContent.none);

            // Wait label + field
            EditorGUI.LabelField(waitLabelRect, L("等待", "Wait", "待機"), EditorStyles.miniLabel);
            EditorGUI.PropertyField(waitFieldRect, waitProp, GUIContent.none);

            // "Set Here" button
            using (new EditorGUI.DisabledScope(multiEdit))
            {
                if (GUI.Button(setRect, L("← 设为此处", "← Set Here", "← ここに"), EditorStyles.miniButton))
                {
                    posProp.vector2Value = ((Liquid2DSpawner)target).transform.localPosition;
                }
            }
        }

        private void OnAddWaypoint(ReorderableList list)
        {
            AddWaypointAtCurrent();
            list.index = _waypoints.arraySize - 1;
        }

        private void AddWaypointAtCurrent()
        {
            var spawner = (Liquid2DSpawner)target;
            int newIdx = _waypoints.arraySize;
            _waypoints.arraySize = newIdx + 1;
            var newElem = _waypoints.GetArrayElementAtIndex(newIdx);
            newElem.FindPropertyRelative("Position").vector2Value = spawner.transform.localPosition;
            newElem.FindPropertyRelative("WaitTime").floatValue = 0f;
        }

        // ── Scene View Handles ────────────────────────────────────────────────────

        private void OnSceneGUI()
        {
            var spawner = (Liquid2DSpawner)target;
            // Use the public property — serializedObject must NOT be accessed inside OnSceneGUI.
            if (!spawner.MoveEnabled) return;

            // Read directly from the target; Liquid2DWaypoint is a class so items are mutable.
            var waypointData = spawner.Waypoints;
            int count = waypointData.Count;
            if (count == 0) return;

            int selectedIdx = _waypointList.index;

            for (int i = 0; i < count; i++)
            {
                Vector3 worldPos = LocalToWorld(spawner, waypointData[i].Position);

                // Handle color: white = selected in list; green / red / yellow by position
                if (i == selectedIdx)
                    Handles.color = Color.white;
                else if (i == 0)
                    Handles.color = new Color(0.3f, 1f, 0.3f, 0.9f);
                else if (i == count - 1)
                    Handles.color = new Color(1f, 0.3f, 0.3f, 0.9f);
                else
                    Handles.color = new Color(1f, 0.9f, 0.3f, 0.9f);

                float size = HandleUtility.GetHandleSize(worldPos) * 0.12f;

                EditorGUI.BeginChangeCheck();
                Vector3 newWorldPos = Handles.FreeMoveHandle(
                    worldPos, size, Vector3.zero, Handles.SphereHandleCap);

                if (EditorGUI.EndChangeCheck())
                {
                    // Record undo snapshot then write directly — no serializedObject in OnSceneGUI.
                    Undo.RecordObject(target, "Move Waypoint");
                    waypointData[i].Position = WorldToLocal(spawner, newWorldPos);
                    EditorUtility.SetDirty(target);
                    _waypointList.index = i;
                }

                Handles.color = Color.white;
                Handles.Label(worldPos + Vector3.up * (size + 0.08f), i.ToString());
            }
        }

        // ── Coordinate Helpers ────────────────────────────────────────────────────

        private static Vector3 LocalToWorld(Liquid2DSpawner spawner, Vector2 localPos)
        {
            Vector3 world = spawner.transform.parent != null
                ? spawner.transform.parent.TransformPoint(new Vector3(localPos.x, localPos.y, 0f))
                : new Vector3(localPos.x, localPos.y, 0f);
            world.z = spawner.transform.position.z; // keep handle in the same Z plane as the spawner
            return world;
        }

        private static Vector2 WorldToLocal(Liquid2DSpawner spawner, Vector3 worldPos)
        {
            if (spawner.transform.parent != null)
            {
                Vector3 local = spawner.transform.parent.InverseTransformPoint(worldPos);
                return new Vector2(local.x, local.y);
            }
            return new Vector2(worldPos.x, worldPos.y);
        }

        // ── UI Helpers ────────────────────────────────────────────────────────────

        private static void DrawSectionSeparator(string title)
        {
            GUILayout.Space(4);
            Rect lineRect = EditorGUILayout.GetControlRect(false, 1f);
            EditorGUI.DrawRect(lineRect, new Color(0.5f, 0.5f, 0.5f, 0.35f));
            GUILayout.Space(3);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        private static string L(string zh, string en, string ja)
        {
            try
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
                        return string.IsNullOrEmpty(en) ? zh : en;
                }
            }
            catch
            {
                return string.IsNullOrEmpty(en) ? zh : en;
            }
        }

        // GUI.backgroundColor scope helper
        private readonly struct BgColorScope : System.IDisposable
        {
            private readonly Color _prev;
            public BgColorScope(Color color) { _prev = GUI.backgroundColor; GUI.backgroundColor = color; }
            public void Dispose() => GUI.backgroundColor = _prev;
        }
    }
}

using System.Text;
using Fs.Liquid2D;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// DevSceneParticle 测试用 UI。运行时用代码生成「监控面板 + 控制按钮 + 调参滑条」，
/// 复用场景中 HandlerUI 下已有的 Canvas（不依赖手摆控件）。仅供开发调试，非发行内容。
/// </summary>
public class HandlerUI : MonoBehaviour
{
    [Header("Refs")]
    [SerializeField] private Liquid2DSpawner spawner;

    [Header("Monitor")]
    [SerializeField] private float refreshInterval = 0.25f; // 监控面板刷新间隔（秒）。

    private Font _font;
    private Text _monitorText;

    // 各状态按钮的标签，状态切换时刷新文本。
    private Text _spawnerBtnLabel;
    private Text _modeBtnLabel;
    private Text _readbackBtnLabel;
    private Text _pauseBtnLabel;
    private Text _slowmoBtnLabel;
    private Text _fpsCapBtnLabel;

    private bool _paused;
    private bool _slowmo;

    // 最大帧率限制档位：索引 0 为 Default（沿用进入场景时的默认设置，含 vSync），-1 为 Unlimited（关 vSync 真正不限帧），其余为固定上限值。
    private const int _fpsCapDefaultIndex = 0;
    private static readonly int[] _fpsCapValues = { 0, -1, 30, 60, 120, 144 }; // 索引 0 仅占位（Default 走还原逻辑），-1 = Unlimited。
    private int _fpsCapIndex;

    // 进入场景时的默认帧率设置，供 Default 档位还原（vSync 可能为开启状态）。
    private int _defaultVSyncCount;
    private int _defaultTargetFrameRate;

    // FPS 平滑（用未缩放时间，暂停/慢放时仍准确）。
    private float _fpsAccum;
    private int _fpsFrames;
    private float _refreshTimer;
    private float _displayFps;
    private float _displayMs;

    private readonly StringBuilder _sb = new StringBuilder(256);

    private void Start()
    {
        _font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");

        // 记录进入场景时的默认帧率设置，并把档位初始化为 Default —— 即沿用这套默认模式，不主动改动引擎状态。
        _defaultVSyncCount = QualitySettings.vSyncCount;
        _defaultTargetFrameRate = Application.targetFrameRate;
        _fpsCapIndex = _fpsCapDefaultIndex;

        EnsureEventSystem();

        var canvas = EnsureCanvas();
        ClearChildren(canvas.transform); // 清掉场景里旧的手摆按钮等子节点。

        BuildMonitorPanel(canvas.transform);
        BuildControlPanel(canvas.transform);

        RefreshButtonLabels();
        UpdateMonitorText();
    }

    private void OnDestroy()
    {
        // 退出时还原时间缩放，避免停在暂停/慢放状态。
        Time.timeScale = 1f;
    }

    private void Update()
    {
        _fpsAccum += Time.unscaledDeltaTime;
        _fpsFrames++;
        _refreshTimer += Time.unscaledDeltaTime;

        if (_refreshTimer >= refreshInterval)
        {
            float avg = _fpsAccum / Mathf.Max(1, _fpsFrames);
            _displayMs = avg * 1000f;
            _displayFps = avg > 0f ? 1f / avg : 0f;
            _fpsAccum = 0f;
            _fpsFrames = 0;
            _refreshTimer = 0f;
            UpdateMonitorText();
        }
    }

    #region Monitor 监控面板

    private void BuildMonitorPanel(Transform parent)
    {
        var panel = CreatePanel(parent, new Vector2(0f, 1f), new Vector2(0f, -10f), 240f);
        _monitorText = CreateText(panel, "", 16, TextAnchor.UpperLeft);
    }

    private void UpdateMonitorText()
    {
        if (!_monitorText) return;

        int aliveCount = 0;
        if (Liquid2DSimulation.HasInstance &&
            Liquid2DSimulation.TryGetRenderData(out _, out _, out int activeCount, out _))
        {
            aliveCount = activeCount;
        }

        _sb.Clear();
        _sb.Append("FPS   : ").Append(_displayFps.ToString("0")).Append('\n');
        _sb.Append("Frame : ").Append(_displayMs.ToString("0.0")).Append(" ms\n");
        _sb.Append("Alive : ").Append(aliveCount).Append('\n');
        _sb.Append("Solver: ").Append(Liquid2DSimulation.Mode).Append('\n');
        _sb.Append("Readbk: ").Append(Liquid2DSimulation.GpuReadbackToStore ? "ON" : "OFF").Append('\n');
        _sb.Append("FpsCap: ").Append(FpsCapText()).Append('\n');
        _sb.Append("Scale : ").Append(Time.timeScale.ToString("0.00"));
        _monitorText.text = _sb.ToString();
    }

    #endregion

    #region Control 控制面板

    private void BuildControlPanel(Transform parent)
    {
        var panel = CreatePanel(parent, new Vector2(1f, 1f), new Vector2(-10f, -10f), 220f);

        _spawnerBtnLabel = CreateButton(panel, "Spawner", OnClickSpawnerSwitch);
        CreateButton(panel, "Spawn One", OnClickSpawnOne);
        CreateButton(panel, "Clear All", OnClickClearAll);
        _pauseBtnLabel = CreateButton(panel, "Pause", OnClickPause);
        _slowmoBtnLabel = CreateButton(panel, "Slow-Mo", OnClickSlowMo);
        _modeBtnLabel = CreateButton(panel, "Mode", OnClickMode);
        _readbackBtnLabel = CreateButton(panel, "Readback", OnClickReadback);
        _fpsCapBtnLabel = CreateButton(panel, "FpsCap", OnClickFpsCap);

        CreateSlider(panel, "Flow x", 0f, 3f, 1f, v => { if (spawner) spawner.SetFlowRateFactor(v); });
        CreateSlider(panel, "Eject x", 0f, 3f, 1f, v => { if (spawner) spawner.SetEjectForceFactor(v); });
    }

    private void OnClickSpawnerSwitch()
    {
        if (!spawner) return;
        spawner.enabled = !spawner.enabled;
        RefreshButtonLabels();
    }

    private void OnClickSpawnOne()
    {
        if (spawner) spawner.SpawnOne();
    }

    private void OnClickClearAll()
    {
        if (Liquid2DSimulation.HasInstance) Liquid2DSimulation.Instance.ClearAll();
    }

    private void OnClickPause()
    {
        _paused = !_paused;
        Time.timeScale = _paused ? 0f : (_slowmo ? 0.25f : 1f);
        RefreshButtonLabels();
    }

    private void OnClickSlowMo()
    {
        _slowmo = !_slowmo;
        if (!_paused) Time.timeScale = _slowmo ? 0.25f : 1f;
        RefreshButtonLabels();
    }

    private void OnClickMode()
    {
        Liquid2DSimulation.Mode = Liquid2DSimulation.Mode == Liquid2DSimulationMode.Gpu
            ? Liquid2DSimulationMode.Cpu
            : Liquid2DSimulationMode.Gpu;
        RefreshButtonLabels();
    }

    private void OnClickReadback()
    {
        Liquid2DSimulation.GpuReadbackToStore = !Liquid2DSimulation.GpuReadbackToStore;
        RefreshButtonLabels();
    }

    private void OnClickFpsCap()
    {
        _fpsCapIndex = (_fpsCapIndex + 1) % _fpsCapValues.Length;
        ApplyFpsCap();
        RefreshButtonLabels();
    }

    // 应用当前档位的最大帧率限制。
    // Default 档位还原进入场景时的默认设置；其余档位关闭 vSync 后再设上限（否则 vSync 开启时 targetFrameRate 会被忽略）。
    private void ApplyFpsCap()
    {
        if (_fpsCapIndex == _fpsCapDefaultIndex)
        {
            QualitySettings.vSyncCount = _defaultVSyncCount;
            Application.targetFrameRate = _defaultTargetFrameRate;
            return;
        }

        QualitySettings.vSyncCount = 0;
        Application.targetFrameRate = _fpsCapValues[_fpsCapIndex];
    }

    private string FpsCapText()
    {
        if (_fpsCapIndex == _fpsCapDefaultIndex)
        {
            // Default 档位标注其实际含义，便于一眼看出默认模式。
            if (_defaultVSyncCount > 0) return "Default(VSync)";
            return _defaultTargetFrameRate <= 0 ? "Default(Unlimited)" : "Default(" + _defaultTargetFrameRate + ")";
        }

        int cap = _fpsCapValues[_fpsCapIndex];
        return cap <= 0 ? "Unlimited" : cap.ToString();
    }

    private void RefreshButtonLabels()
    {
        if (_spawnerBtnLabel) _spawnerBtnLabel.text = (spawner && spawner.enabled) ? "Spawner: ON" : "Spawner: OFF";
        if (_pauseBtnLabel) _pauseBtnLabel.text = _paused ? "Resume" : "Pause";
        if (_slowmoBtnLabel) _slowmoBtnLabel.text = _slowmo ? "Slow-Mo: ON" : "Slow-Mo: OFF";
        if (_modeBtnLabel) _modeBtnLabel.text = "Mode: " + Liquid2DSimulation.Mode;
        if (_readbackBtnLabel) _readbackBtnLabel.text = Liquid2DSimulation.GpuReadbackToStore ? "Readback: ON" : "Readback: OFF";
        if (_fpsCapBtnLabel) _fpsCapBtnLabel.text = "FpsCap: " + FpsCapText();
    }

    #endregion

    #region UI builders UI 构建辅助

    private Canvas EnsureCanvas()
    {
        var canvas = GetComponentInChildren<Canvas>(true);
        if (canvas) return canvas;

        var go = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
        go.transform.SetParent(transform, false);
        go.layer = LayerMask.NameToLayer("UI");
        canvas = go.GetComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        var scaler = go.GetComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(800f, 600f);
        return canvas;
    }

    private void EnsureEventSystem()
    {
        if (FindFirstObjectByType<EventSystem>()) return;
        new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
    }

    private static void ClearChildren(Transform t)
    {
        for (int i = t.childCount - 1; i >= 0; i--)
            Destroy(t.GetChild(i).gameObject);
    }

    // 创建一个带半透明背景、竖向自动布局、随内容撑高的面板。anchoredPos 以 pivot 为基准的偏移。
    private RectTransform CreatePanel(Transform parent, Vector2 cornerAnchor, Vector2 anchoredPos, float width)
    {
        var go = new GameObject("Panel", typeof(RectTransform), typeof(Image),
            typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        rt.anchorMin = cornerAnchor;
        rt.anchorMax = cornerAnchor;
        rt.pivot = cornerAnchor;
        rt.anchoredPosition = anchoredPos;
        rt.sizeDelta = new Vector2(width, 0f);

        go.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.5f);

        var vlg = go.GetComponent<VerticalLayoutGroup>();
        vlg.padding = new RectOffset(10, 10, 10, 10);
        vlg.spacing = 6f;
        vlg.childControlWidth = true;
        vlg.childControlHeight = true;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;

        go.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        return rt;
    }

    private Text CreateText(Transform parent, string text, int fontSize, TextAnchor align)
    {
        var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var t = go.GetComponent<Text>();
        t.font = _font;
        t.fontSize = fontSize;
        t.text = text;
        t.color = Color.white;
        t.alignment = align;
        t.horizontalOverflow = HorizontalWrapMode.Overflow;
        t.verticalOverflow = VerticalWrapMode.Overflow;
        t.supportRichText = false;
        return t;
    }

    // 创建按钮，返回其标签 Text（便于外部刷新状态文案）。
    private Text CreateButton(Transform parent, string label, UnityAction onClick)
    {
        var go = new GameObject("Button", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        go.transform.SetParent(parent, false);
        go.GetComponent<Image>().color = new Color(0.20f, 0.45f, 0.85f, 1f);
        go.GetComponent<LayoutElement>().minHeight = 32f;
        go.GetComponent<Button>().onClick.AddListener(onClick);

        var labelText = CreateText(go.transform, label, 16, TextAnchor.MiddleCenter);
        StretchFull(labelText.rectTransform);
        return labelText;
    }

    private Slider CreateSlider(Transform parent, string label, float min, float max, float value, UnityAction<float> onChanged)
    {
        var row = new GameObject("SliderRow", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(LayoutElement));
        row.transform.SetParent(parent, false);
        var rlg = row.GetComponent<VerticalLayoutGroup>();
        rlg.spacing = 2f;
        rlg.childControlWidth = true;
        rlg.childControlHeight = true;
        rlg.childForceExpandWidth = true;
        rlg.childForceExpandHeight = false;
        row.GetComponent<LayoutElement>().minHeight = 44f;

        var valueLabel = CreateText(row.transform, $"{label}: {value:0.00}", 14, TextAnchor.MiddleLeft);
        valueLabel.gameObject.AddComponent<LayoutElement>().minHeight = 18f;

        var sgo = new GameObject("Slider", typeof(RectTransform), typeof(Slider), typeof(LayoutElement));
        sgo.transform.SetParent(row.transform, false);
        sgo.GetComponent<LayoutElement>().minHeight = 18f;
        var slider = sgo.GetComponent<Slider>();

        var bg = CreateImage("Background", sgo.transform, new Color(0.15f, 0.15f, 0.15f, 1f));
        StretchFull(bg.rectTransform);

        var fillArea = CreateRect("Fill Area", sgo.transform);
        StretchFull(fillArea);
        fillArea.offsetMin = new Vector2(5f, 0f);
        fillArea.offsetMax = new Vector2(-5f, 0f);
        var fill = CreateImage("Fill", fillArea, new Color(0.2f, 0.6f, 1f, 1f));
        fill.rectTransform.sizeDelta = new Vector2(10f, 0f);

        var handleArea = CreateRect("Handle Slide Area", sgo.transform);
        StretchFull(handleArea);
        handleArea.offsetMin = new Vector2(5f, 0f);
        handleArea.offsetMax = new Vector2(-5f, 0f);
        var handle = CreateImage("Handle", handleArea, Color.white);
        handle.rectTransform.sizeDelta = new Vector2(14f, 0f);

        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;
        slider.minValue = min;
        slider.maxValue = max;
        slider.value = value;
        slider.onValueChanged.AddListener(v =>
        {
            valueLabel.text = $"{label}: {v:0.00}";
            onChanged?.Invoke(v);
        });
        return slider;
    }

    private Image CreateImage(string name, Transform parent, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image));
        go.transform.SetParent(parent, false);
        var img = go.GetComponent<Image>();
        img.color = color;
        return img;
    }

    private static RectTransform CreateRect(string name, Transform parent)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = go.GetComponent<RectTransform>();
        rt.SetParent(parent, false);
        return rt;
    }

    private static void StretchFull(RectTransform rt)
    {
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    #endregion
}

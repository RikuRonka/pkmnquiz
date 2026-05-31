using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

public class MainMenuController : MonoBehaviour
{
    private const float SettingsRightPadding = 24f;
    private const float SettingsRowHeight = 28f;
    private const float SettingsRowWidth = 430f;
    private const float SettingsFontSize = 20f;
    private const float SettingsToggleSize = 20f;
    private const float MusicDropdownGap = 12f;
    private const float QuizButtonRestoreInterval = 0.5f;
    private static bool menuChromeVisible = true;

    public Button fullQuizBtn;
    private float nextQuizButtonRestoreTime;

    void Awake()
    {
        MultiplayerMenuPanel.EnsureInScene();
        SingleplayerScoreboardPanel.EnsureInScene();
        SingleplayerProgressResetPanel.EnsureInScene();
        ConfigureSettingsControls();

        bool fullscreen = PlayerPrefs.GetInt("fullscreen", 1) == 1;
        Screen.fullScreen = fullscreen;
        Screen.fullScreenMode = fullscreen
            ? FullScreenMode.FullScreenWindow
            : FullScreenMode.Windowed;
        if (fullQuizBtn)
        {
            fullQuizBtn.onClick.RemoveListener(PlayFullQuiz);
            if (fullQuizBtn.onClick.GetPersistentEventCount() == 0)
                fullQuizBtn.onClick.AddListener(PlayFullQuiz);
        }

        RestoreMainMenuButtonInteractivity();
    }

    void Start()
    {
        RestoreMainMenuButtonInteractivity();
    }

    void Update()
    {
        ApplyMenuChromeVisibility();

        if (Time.unscaledTime < nextQuizButtonRestoreTime)
            return;

        nextQuizButtonRestoreTime = Time.unscaledTime + QuizButtonRestoreInterval;
        RestoreQuizButtonInteractivity();
    }

    private void ConfigureSettingsControls()
    {
        ConfigureMusicControls();
        var parent = FindSettingsParent();

        ConfigureToggleRow(
            GameObject.Find("fullscreenToggle")?.transform as RectTransform,
            "Fullscreen",
            -62f
        );
        ConfigureToggleRow(
            EnsurePauseOnFocusLossToggle(parent),
            "Pause when app loses focus",
            -94f
        );

        ConfigureToggleRow(EnsureAutofillToggle(parent), "Enable quiz autofill button", -126f);
    }

    private static RectTransform FindSettingsParent()
    {
        var fullscreenParent = GameObject.Find("fullscreenToggle")?.transform.parent as RectTransform;
        if (fullscreenParent)
            return fullscreenParent;

        var musicParent = GameObject.Find("musicToggle")?.transform.parent as RectTransform;
        if (musicParent)
            return musicParent;

        var canvas = FindFirstObjectByType<Canvas>();
        return canvas ? canvas.transform as RectTransform : null;
    }

    private static void ConfigureMusicControls()
    {
        var musicRow = GameObject.Find("musicToggle")?.transform as RectTransform;
        if (musicRow)
        {
            musicRow.anchorMin = musicRow.anchorMax = new Vector2(1f, 1f);
            musicRow.pivot = new Vector2(1f, 1f);
            musicRow.anchoredPosition = new Vector2(-SettingsRightPadding, -24f);
            musicRow.sizeDelta = new Vector2(430f, SettingsRowHeight);
            musicRow.localScale = Vector3.one;

            var toggle = musicRow.GetComponent<Toggle>();
            ConfigureToggleBox(toggle, positionToggle: false);
        }

        var slider = GameObject.Find("musicSlider")?.transform as RectTransform;
        if (slider)
        {
            slider.anchorMin = slider.anchorMax = new Vector2(1f, 1f);
            slider.pivot = new Vector2(1f, 0.5f);
            slider.anchoredPosition = new Vector2(-250f, -38f);
            slider.sizeDelta = new Vector2(150f, 16f);
            slider.localScale = Vector3.one;
        }

        var dropdown = GameObject.Find("musicDropDown")?.transform as RectTransform;
        if (dropdown)
        {
            dropdown.anchorMin = dropdown.anchorMax = new Vector2(0f, 0.5f);
            dropdown.pivot = new Vector2(1f, 0.5f);
            dropdown.anchoredPosition = new Vector2(-MusicDropdownGap, 0f);
            dropdown.sizeDelta = new Vector2(250f, 42f);
            dropdown.localScale = new Vector3(0.76f, 0.76f, 1f);

            foreach (var text in dropdown.GetComponentsInChildren<TMP_Text>(true))
                text.fontSize = Mathf.Min(text.fontSize, 18f);
        }
    }

    private static void ConfigureToggleRow(RectTransform row, string label, float topOffset)
    {
        if (!row)
            return;

        row.anchorMin = row.anchorMax = new Vector2(1f, 1f);
        row.pivot = new Vector2(1f, 1f);
        row.anchoredPosition = new Vector2(-SettingsRightPadding, topOffset);
        row.sizeDelta = new Vector2(SettingsRowWidth, SettingsRowHeight);
        row.localScale = Vector3.one;

        var toggle = row.GetComponentInChildren<Toggle>(true);
        ConfigureToggleBox(toggle);

        TMP_Text labelText = null;
        foreach (var text in row.GetComponentsInChildren<TMP_Text>(true))
        {
            if (toggle && text.transform.IsChildOf(toggle.transform))
                continue;

            labelText = text;
            break;
        }

        if (!labelText)
            return;

        labelText.text = label;
        labelText.fontSize = SettingsFontSize;
        labelText.enableAutoSizing = false;
        labelText.alignment = TextAlignmentOptions.MidlineLeft;
        labelText.color = Color.white;
        labelText.raycastTarget = false;

        var labelRt = labelText.transform as RectTransform;
        if (!labelRt)
            return;

        labelRt.anchorMin = Vector2.zero;
        labelRt.anchorMax = Vector2.one;
        labelRt.pivot = new Vector2(0f, 0.5f);
        labelRt.offsetMin = new Vector2(32f, 0f);
        labelRt.offsetMax = Vector2.zero;
        labelRt.localScale = Vector3.one;
    }

    private static void ConfigureToggleBox(Toggle toggle, bool positionToggle = true)
    {
        if (!toggle)
            return;

        var toggleRt = toggle.transform as RectTransform;
        if (positionToggle && toggleRt)
        {
            toggleRt.anchorMin = toggleRt.anchorMax = new Vector2(0f, 0.5f);
            toggleRt.pivot = new Vector2(0f, 0.5f);
            toggleRt.anchoredPosition = Vector2.zero;
            toggleRt.sizeDelta = new Vector2(SettingsToggleSize, SettingsToggleSize);
            toggleRt.localScale = Vector3.one;
        }

        if (positionToggle)
            ConfigureGraphicRect(toggle.targetGraphic);
        else
            ConfigureStandaloneToggleBackground(toggle.targetGraphic);

        ConfigureGraphicRect(toggle.graphic);
    }

    private static RectTransform EnsurePauseOnFocusLossToggle(RectTransform parent)
    {
        var row = GameObject.Find("pauseGameifUnfocused")?.transform as RectTransform;
        if (!row && parent)
        {
            var rowGo = new GameObject("pauseGameifUnfocused", typeof(RectTransform));
            rowGo.layer = parent.gameObject.layer;
            row = rowGo.GetComponent<RectTransform>();
            row.SetParent(parent, false);

            var labelGo = new GameObject(
                "pauseGameAppUnfocusedToggleText",
                typeof(RectTransform),
                typeof(CanvasRenderer),
                typeof(TextMeshProUGUI)
            );
            labelGo.layer = rowGo.layer;
            labelGo.GetComponent<RectTransform>().SetParent(row, false);
            CopyLabelStyle(labelGo.GetComponent<TextMeshProUGUI>());
        }

        if (!row)
            return null;

        var toggle = row.GetComponentInChildren<Toggle>(true);
        if (!toggle)
            toggle = CreateToggle(row, "pauseGameAppUnfocusedToggle");

        if (toggle && !toggle.GetComponent<PauseOnFocusLossSetting>())
            toggle.gameObject.AddComponent<PauseOnFocusLossSetting>();

        return row;
    }

    private static void ConfigureStandaloneToggleBackground(Graphic graphic)
    {
        if (!graphic || graphic.transform is not RectTransform rt)
            return;

        rt.anchorMin = rt.anchorMax = new Vector2(0f, 0.5f);
        rt.pivot = new Vector2(0f, 0.5f);
        rt.anchoredPosition = Vector2.zero;
        rt.sizeDelta = new Vector2(SettingsToggleSize, SettingsToggleSize);
        rt.localScale = Vector3.one;
    }

    private static void ConfigureGraphicRect(Graphic graphic)
    {
        if (!graphic || graphic.transform is not RectTransform rt)
            return;

        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        rt.localScale = Vector3.one;
    }

    private static RectTransform EnsureAutofillToggle(RectTransform parent)
    {
        var existing = GameObject.Find("autofillToggle")?.transform as RectTransform;
        if (existing)
        {
            if (parent && existing.parent != parent)
                existing.SetParent(parent, false);

            return existing;
        }

        if (!parent)
            return null;

        var row = new GameObject("autofillToggle", typeof(RectTransform));
        row.layer = parent.gameObject.layer;
        var rowRt = row.GetComponent<RectTransform>();
        rowRt.SetParent(parent, false);

        var toggle = CreateToggle(rowRt, "autofillToggleToggle");
        toggle.SetIsOnWithoutNotify(PlayerPrefs.GetInt(QuizManager.KEY_ENABLE_AUTOFILL_BUTTON, 0) == 1);
        toggle.onValueChanged.AddListener(on =>
        {
            PlayerPrefs.SetInt(QuizManager.KEY_ENABLE_AUTOFILL_BUTTON, on ? 1 : 0);
            PlayerPrefs.Save();
        });

        var labelGo = new GameObject(
            "autofillToggleText",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(TextMeshProUGUI)
        );
        labelGo.layer = row.layer;
        var labelRt = labelGo.GetComponent<RectTransform>();
        labelRt.SetParent(rowRt, false);
        var label = labelGo.GetComponent<TextMeshProUGUI>();
        CopyLabelStyle(label);

        return rowRt;
    }

    private static Toggle CreateToggle(RectTransform parent, string name)
    {
        var toggleGo = new GameObject(name, typeof(RectTransform), typeof(Toggle));
        toggleGo.layer = parent.gameObject.layer;
        var toggleRt = toggleGo.GetComponent<RectTransform>();
        toggleRt.SetParent(parent, false);

        var backgroundGo = new GameObject(
            "Background",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        backgroundGo.layer = parent.gameObject.layer;
        var backgroundRt = backgroundGo.GetComponent<RectTransform>();
        backgroundRt.SetParent(toggleRt, false);
        var backgroundImage = backgroundGo.GetComponent<Image>();

        var checkGo = new GameObject(
            "Checkmark",
            typeof(RectTransform),
            typeof(CanvasRenderer),
            typeof(Image)
        );
        checkGo.layer = parent.gameObject.layer;
        var checkRt = checkGo.GetComponent<RectTransform>();
        checkRt.SetParent(backgroundRt, false);
        var checkImage = checkGo.GetComponent<Image>();

        CopyToggleSprites(backgroundImage, checkImage);

        var toggle = toggleGo.GetComponent<Toggle>();
        toggle.targetGraphic = backgroundImage;
        toggle.graphic = checkImage;
        return toggle;
    }

    private static void CopyToggleSprites(Image backgroundImage, Image checkImage)
    {
        var sourceToggle =
            GameObject.Find("fullscreenToggleToggle")?.GetComponent<Toggle>()
            ?? GameObject.Find("musicToggle")?.GetComponent<Toggle>();

        if (sourceToggle?.targetGraphic is Image sourceBackground)
        {
            backgroundImage.sprite = sourceBackground.sprite;
            backgroundImage.type = sourceBackground.type;
            backgroundImage.color = sourceBackground.color;
        }
        else
        {
            backgroundImage.color = Color.white;
        }

        if (sourceToggle?.graphic is Image sourceCheck)
        {
            checkImage.sprite = sourceCheck.sprite;
            checkImage.type = sourceCheck.type;
            checkImage.color = sourceCheck.color;
        }
        else
        {
            checkImage.color = Color.black;
        }
    }

    private static void CopyLabelStyle(TextMeshProUGUI label)
    {
        var source = GameObject.Find("fullscreenToggleText")?.GetComponent<TMP_Text>();
        if (source)
        {
            label.font = source.font;
            label.fontMaterial = source.fontMaterial;
        }

        label.text = "Enable quiz autofill button";
        label.color = Color.white;
        label.fontSize = SettingsFontSize;
        label.alignment = TextAlignmentOptions.MidlineLeft;
        label.raycastTarget = false;
    }

    private void RestoreMainMenuButtonInteractivity()
    {
        RestoreQuizButtonInteractivity();
        RestoreUtilityButtonInteractivity();
        ApplyMenuChromeVisibility();
    }

    private void RestoreQuizButtonInteractivity()
    {
        bool canChooseQuiz = !QuizNetworkRuntime.IsMultiplayerClientOnly;

        if (fullQuizBtn)
            SetButtonInteractable(fullQuizBtn, canChooseQuiz);

        foreach (
            var button in FindObjectsByType<Button>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            if (IsMenuQuizButton(button))
                SetButtonInteractable(button, canChooseQuiz);
        }

        foreach (
            var typeButton in FindObjectsByType<TypeFilterButton>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            if (typeButton && typeButton.TryGetComponent(out Button button))
                SetButtonInteractable(button, canChooseQuiz);
        }
    }

    private static void RestoreUtilityButtonInteractivity()
    {
        SetButtonInteractable(GameObject.Find("PatchNotesButton")?.GetComponent<Button>(), true);
        SetButtonInteractable(GameObject.Find("UpdateButton")?.GetComponent<Button>(), true);
    }

    public static void SetMenuChromeVisible(bool visible)
    {
        menuChromeVisible = visible;

        foreach (
            var controller in FindObjectsByType<MainMenuController>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            controller.ApplyMenuChromeVisibility();
        }
    }

    private void ApplyMenuChromeVisibility()
    {
        SetActiveIfFound("musicToggle", menuChromeVisible);
        SetActiveIfFound("musicSlider", menuChromeVisible);
        SetActiveIfFound("musicDropDown", menuChromeVisible);
        SetActiveIfFound("fullscreenToggle", menuChromeVisible);
        SetActiveIfFound("pauseGameifUnfocused", menuChromeVisible);
        SetActiveIfFound("autofillToggle", menuChromeVisible);
    }

    private static void SetActiveIfFound(string objectName, bool active)
    {
        foreach (
            var transform in FindObjectsByType<Transform>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None
            )
        )
        {
            if (!transform || transform.name != objectName)
                continue;

            var go = transform.gameObject;
            if (go.activeSelf != active)
                go.SetActive(active);
        }
    }

    private static bool IsMenuQuizButton(Button button)
    {
        if (!button)
            return false;

        int listenerCount = button.onClick.GetPersistentEventCount();
        for (int i = 0; i < listenerCount; i++)
        {
            if (!IsMenuQuizButtonMethod(button.onClick.GetPersistentMethodName(i)))
                continue;

            var target = button.onClick.GetPersistentTarget(i);
            if (target is MenuRouter || target is MainMenuController)
                return true;
        }

        return false;
    }

    private static bool IsMenuQuizButtonMethod(string methodName)
    {
        return methodName == "PlayFullQuiz"
            || methodName == "PlayGenQuiz"
            || methodName == "PlayTypeQuiz"
            || methodName == "PlayMegaEvolutionsQuiz"
            || methodName == "PlayGen"
            || methodName == "PlayType";
    }

    private static void SetButtonInteractable(Button button, bool interactable)
    {
        if (!button)
            return;

        button.interactable = interactable;
        if (button.TryGetComponent<CanvasGroup>(out var group))
        {
            group.interactable = interactable;
            group.blocksRaycasts = interactable;
            if (interactable && group.alpha <= 0.01f)
                group.alpha = 1f;
        }

        if (button.TryGetComponent<UiButtonHover>(out var hover))
            hover.RefreshDisabledVisual();
    }

    public static async void PlayFullQuiz()
    {
        if (await QuizNetworkRuntime.TryHandleMenuQuizSelectionAsync(0))
            return;

        QuizNetworkRuntime.Shutdown();
        GameSettings.Generation = 0;
        GameSettings.TypeFilter = null;
        GameSettings.ArmQuizLaunch();
        SceneManager.LoadScene("Quiz");
    }

    public static void PlayGen(int gen)
    {
        PlayGenAsync(gen);
    }

    private static async void PlayGenAsync(int gen)
    {
        if (await QuizNetworkRuntime.TryHandleMenuQuizSelectionAsync(gen))
            return;

        QuizNetworkRuntime.Shutdown();
        GameSettings.Generation = gen;
        GameSettings.TypeFilter = null;
        GameSettings.ArmQuizLaunch();
        SceneManager.LoadScene("Quiz");
    }

    public static void PlayType(string typeName)
    {
        PlayTypeAsync(typeName);
    }

    private static async void PlayTypeAsync(string typeName)
    {
        if (await QuizNetworkRuntime.TryHandleMenuQuizSelectionAsync(0, typeName))
            return;

        QuizNetworkRuntime.Shutdown();
        GameSettings.Generation = null;
        GameSettings.TypeFilter = new[] { typeName };
        GameSettings.ArmQuizLaunch();
        SceneManager.LoadScene("Quiz");
    }

    public static void Quit()
    {
        Application.Quit();
    }
}

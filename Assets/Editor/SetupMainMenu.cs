using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Editor script — CyberBoss/Setup Main Menu.
///
/// Idempotent: destroys any existing MainMenuPanel before rebuilding.
/// Requires HUDCanvas to already exist (run CyberBoss/Setup HUD first).
///
/// Hierarchy produced (added as the last child of HUDCanvas so it renders on
/// top of the health bars / cooldown panel; stays permanently active — see
/// MainMenuScreen for why — visibility is driven by its CanvasGroup):
///   HUDCanvas
///   └── MainMenuPanel      (full-screen stretch, always active)
///       ├── Background         (semi-transparent black — same as GameOverPanel)
///       ├── TitleText          (TMP 72pt cyan — "CYBERBOSS")
///       ├── SubtitleText       (TMP 24pt magenta — tagline)
///       ├── ControlKeysText    (TMP 22pt cyan, right-aligned key column)
///       ├── ControlActionsText (TMP 22pt, left-aligned action column)
///       └── HintText           (TMP 24pt — start / resume hint, swapped at runtime)
///
/// Styled to match SetupHUD's CreateGameOverPanel so the opening/pause screen
/// and the death/victory screens read as one visual family. Shown at scene
/// start (pausing the game) and reopenable at any time via M (see
/// MainMenuScreen.Update()).
/// </summary>
public static class SetupMainMenu
{
    private const string HudConfigAssetPath = "Assets/ScriptableObjects/HUDConfig.asset";
    private const string CanvasObjectName    = "HUDCanvas";
    private const string PanelObjectName     = "MainMenuPanel";

    [MenuItem("CyberBoss/Setup Main Menu")]
    public static void Execute()
    {
        var canvasGo = GameObject.Find(CanvasObjectName);
        if (canvasGo == null)
        {
            Debug.LogError("[SetupMainMenu] HUDCanvas not found. Run CyberBoss/Setup HUD first.");
            return;
        }

        RemoveExistingPanel(canvasGo.transform);

        var config = AssetDatabase.LoadAssetAtPath<HUDConfig>(HudConfigAssetPath);
        if (config == null)
        {
            Debug.LogError("[SetupMainMenu] HUDConfig asset not found. Run CyberBoss/Setup HUD first.");
            return;
        }

        GameObject panel = CreateMainMenuPanel(canvasGo.transform);
        panel.transform.SetAsLastSibling(); // render on top of health bars / cooldown panel

        var menuScreen = panel.AddComponent<MainMenuScreen>();

        // MainMenuScreen.Awake() always forces the panel visible at the start of
        // Play — this saved/edit-time default only matters for the Editor: without
        // it, the panel's opaque backdrop (CanvasGroup defaults to alpha=1 when
        // added) renders in the Scene View at all times, since the GameObject
        // itself must stay permanently active for the M-key pause listener to
        // keep receiving Update() calls (see MainMenuScreen's class doc).
        var canvasGroup = panel.GetComponent<CanvasGroup>();
        canvasGroup.alpha          = 0f;
        canvasGroup.interactable   = false;
        canvasGroup.blocksRaycasts = false;

        TMP_Text titleText         = panel.transform.Find("TitleText").GetComponent<TMP_Text>();
        TMP_Text subtitleText      = panel.transform.Find("SubtitleText").GetComponent<TMP_Text>();
        TMP_Text controlKeysText   = panel.transform.Find("ControlKeysText").GetComponent<TMP_Text>();
        TMP_Text controlActionsText = panel.transform.Find("ControlActionsText").GetComponent<TMP_Text>();
        TMP_Text hintText          = panel.transform.Find("HintText").GetComponent<TMP_Text>();
        var bossController         = Object.FindAnyObjectByType<BossController>();
        var gameOverScreen         = canvasGo.GetComponentInChildren<GameOverScreen>(includeInactive: true);

        WireMainMenuScreen(menuScreen, titleText, subtitleText, controlKeysText, controlActionsText,
            hintText, config, bossController, gameOverScreen);

        EditorUtility.SetDirty(canvasGo);
        EditorSceneManager.MarkAllScenesDirty();
        AssetDatabase.SaveAssets();

        Debug.Log("[SetupMainMenu] MainMenuPanel created. Shows at scene start and pauses the game " +
            "(Time.timeScale = 0) until ENTER is pressed. Press M any time after that to reopen it as a pause menu.");
    }

    // ------------------------------------------------------------------
    // Idempotency
    // ------------------------------------------------------------------

    private static void RemoveExistingPanel(Transform canvasTransform)
    {
        var existing = canvasTransform.Find(PanelObjectName);
        if (existing == null) return;
        Undo.DestroyObjectImmediate(existing.gameObject);
        Debug.Log("[SetupMainMenu] Removed existing MainMenuPanel — rebuilding from scratch.");
    }

    // ------------------------------------------------------------------
    // Panel construction
    // ------------------------------------------------------------------

    private static GameObject CreateMainMenuPanel(Transform parent)
    {
        var panel = CreateRectObject(parent, PanelObjectName);
        var rt    = (RectTransform)panel.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Full-screen semi-transparent backdrop — matches GameOverPanel exactly.
        var bg   = CreateRectObject(panel.transform, "Background");
        var bgRT = (RectTransform)bg.transform;
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        bg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);

        // Title — large, bold, centred, 190px above screen centre.
        var titleGo  = CreateRectObject(panel.transform, "TitleText");
        SetHorizontalStretch((RectTransform)titleGo.transform, yOffset: 190f, height: 110f);
        var titleTMP = titleGo.AddComponent<TextMeshProUGUI>();
        titleTMP.text      = "CYBERBOSS"; // default; overwritten at runtime
        titleTMP.fontSize  = 72f;
        titleTMP.fontStyle = FontStyles.Bold;
        titleTMP.alignment = TextAlignmentOptions.Center;
        titleTMP.color     = new Color(0.10f, 0.90f, 1.00f);

        // Subtitle — tagline, 140px above centre.
        var subGo  = CreateRectObject(panel.transform, "SubtitleText");
        SetHorizontalStretch((RectTransform)subGo.transform, yOffset: 140f, height: 36f);
        var subTMP = subGo.AddComponent<TextMeshProUGUI>();
        subTMP.text      = "RL-TRAINED CYBERPUNK BOSS FIGHT";
        subTMP.fontSize  = 22f;
        subTMP.fontStyle = FontStyles.Bold;
        subTMP.alignment = TextAlignmentOptions.Center;
        subTMP.color     = new Color(0.85f, 0.10f, 0.75f);

        // Controls list — two separate columns (keys, actions) rather than one
        // text block with inline tags. TMP's <pos> tag was unreliable for this
        // purpose (columns didn't line up); two independently right/left
        // pivoted RectTransforms guarantee pixel alignment regardless of how
        // wide each key label is. Mirrors the KeyLabel/SkillNameLabel split
        // SetupHUD already uses for the cooldown slots.
        var keysGo  = CreateRectObject(panel.transform, "ControlKeysText");
        var keysRT  = (RectTransform)keysGo.transform;
        keysRT.anchorMin        = new Vector2(0.5f, 0.5f);
        keysRT.anchorMax        = new Vector2(0.5f, 0.5f);
        keysRT.pivot            = new Vector2(1f, 0.5f);   // right edge pivot
        keysRT.anchoredPosition = new Vector2(-10f, -20f); // right edge 10px left of centre
        keysRT.sizeDelta        = new Vector2(220f, 300f);
        var keysTMP = keysGo.AddComponent<TextMeshProUGUI>();
        keysTMP.text        = "WASD\nSHIFT\nSPACE\nQ\nR\nE\nF\nLEFT CLICK\nM"; // default; overwritten at runtime
        keysTMP.fontSize    = 20f;
        keysTMP.fontStyle   = FontStyles.Bold;
        keysTMP.alignment   = TextAlignmentOptions.TopRight;
        keysTMP.lineSpacing = 6f;
        keysTMP.color       = new Color(0.10f, 0.90f, 1.00f);

        var actionsGo  = CreateRectObject(panel.transform, "ControlActionsText");
        var actionsRT  = (RectTransform)actionsGo.transform;
        actionsRT.anchorMin        = new Vector2(0.5f, 0.5f);
        actionsRT.anchorMax        = new Vector2(0.5f, 0.5f);
        actionsRT.pivot            = new Vector2(0f, 0.5f);  // left edge pivot
        actionsRT.anchoredPosition = new Vector2(10f, -20f); // left edge 10px right of centre
        actionsRT.sizeDelta        = new Vector2(320f, 300f);
        var actionsTMP = actionsGo.AddComponent<TextMeshProUGUI>();
        actionsTMP.text        = "Move\nRun\nDash\nParry\nRanged Blast\nBurst Strike\nBarrier\nAttack\nPause";
        actionsTMP.fontSize    = 20f;
        actionsTMP.alignment   = TextAlignmentOptions.TopLeft;
        actionsTMP.lineSpacing = 6f;
        actionsTMP.color       = new Color(0.85f, 0.85f, 0.85f);

        // Start hint — bottom, matches GameOverPanel's HintText styling.
        var hintGo  = CreateRectObject(panel.transform, "HintText");
        SetHorizontalStretch((RectTransform)hintGo.transform, yOffset: -240f, height: 44f);
        var hintTMP = hintGo.AddComponent<TextMeshProUGUI>();
        hintTMP.text      = "[ PRESS ENTER TO START ]";
        hintTMP.fontSize  = 24f;
        hintTMP.alignment = TextAlignmentOptions.Center;
        hintTMP.color     = new Color(0.85f, 0.85f, 0.85f);

        return panel;
    }

    // ------------------------------------------------------------------
    // Serialized field wiring
    // ------------------------------------------------------------------

    private static void WireMainMenuScreen(
        MainMenuScreen screen,
        TMP_Text titleText, TMP_Text subtitleText,
        TMP_Text controlKeysText, TMP_Text controlActionsText, TMP_Text hintText,
        HUDConfig config, BossController bossController, GameOverScreen gameOverScreen)
    {
        var so = new SerializedObject(screen);
        AssignField(so, "_titleText",          titleText);
        AssignField(so, "_subtitleText",       subtitleText);
        AssignField(so, "_controlKeysText",    controlKeysText);
        AssignField(so, "_controlActionsText", controlActionsText);
        AssignField(so, "_hintText",           hintText);
        AssignField(so, "_config",             config);
        AssignField(so, "_bossController",     bossController);
        AssignField(so, "_gameOverScreen",     gameOverScreen);
        so.ApplyModifiedProperties();
    }

    // ------------------------------------------------------------------
    // Layout helpers — mirrors SetupHUD's helpers exactly
    // ------------------------------------------------------------------

    private static GameObject CreateRectObject(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);
        return go;
    }

    private static void SetHorizontalStretch(RectTransform rt, float yOffset, float height)
    {
        rt.anchorMin        = new Vector2(0f, 0.5f);
        rt.anchorMax        = new Vector2(1f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, yOffset);
        rt.sizeDelta         = new Vector2(0f, height);
    }

    private static void AssignField(SerializedObject so, string fieldName, Object value)
    {
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogError($"[SetupMainMenu] SerializedProperty '{fieldName}' not found on " +
                $"'{so.targetObject.GetType().Name}'. Check the field name matches exactly.");
            return;
        }
        prop.objectReferenceValue = value;
    }
}

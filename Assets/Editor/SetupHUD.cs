using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using TMPro;
using UnityEngine.InputSystem.UI;

/// <summary>
/// Editor script — CyberBoss/Setup HUD.
///
/// Idempotent: destroys any existing HUDCanvas before rebuilding. Run any time
/// you want to reset the HUD to a clean state.
///
/// Hierarchy produced:
///   HUDCanvas
///   ├── PlayerHealthBar  (top-left, 620×36, neon green)
///   │   ├── NameLabel    (TMP, "PLAYER", top 12px)
///   │   └── BarArea      (bottom 20px, full width)
///   │       ├── Background
///   │       ├── Fill         (Horizontal fill — wired to HUDController._playerHealthFill)
///   │       └── SegmentsOverlay
///   │           └── Div_1 … Div_9  (dark 2px dividers at 10% intervals)
///   ├── BossHealthBar    (top-right, 620×36, neon red, mirrored)
///   │   └── [same structure as PlayerHealthBar]
///   ├── CooldownPanel    (bottom-centre, 430×118)
///   │   ├── [Image on root = dim neon border]
///   │   ├── InnerBackground
///   │   └── SkillSlot_Dash / _Parry / _RangedBlast / _BurstStrike / _Barrier  (78×106 each)
///   │       ├── [Image on root = slot border wired to CooldownUI._slotBorders[i]]
///   │       ├── Background
///   │       ├── KeyLabel      (TMP "SPACE"/"Q"/"R"/"E"/"F", top of slot)
///   │       ├── Fill          (Radial360 — wired to CooldownUI._skillFills[i])
///   │       └── SkillNameLabel (TMP "DASH"/…/"BARRIER", bottom of slot)
///   └── GameOverPanel    (full-screen stretch, starts inactive)
///       ├── Background   (semi-transparent black)
///       ├── MessageText  (TMP 72pt — wired to GameOverScreen._messageText)
///       ├── SubtitleText (TMP 28pt — wired to GameOverScreen._subtitleText)
///       └── HintText     (TMP 24pt — wired to GameOverScreen._hintText)
///
/// PREREQUISITE: Window > TextMeshPro > Import TMP Essential Resources
///               must be run once before entering Play mode.
/// </summary>
public static class SetupHUD
{
    private const string HudConfigAssetPath = "Assets/ScriptableObjects/HUDConfig.asset";
    private const string CanvasObjectName   = "HUDCanvas";

    // Labels indexed by PlayerSkills.SkillIndex constants (0=Dash … 4=Barrier).
    private static readonly string[] SkillKeys  = { "SPACE", "Q", "R", "E", "F" };
    private static readonly string[] SkillNames = { "DASH", "PARRY", "BLAST", "STRIKE", "BARRIER" };
    private static readonly string[] SlotIds    = {
        "SkillSlot_Dash", "SkillSlot_Parry", "SkillSlot_RangedBlast",
        "SkillSlot_BurstStrike", "SkillSlot_Barrier"
    };

    // ------------------------------------------------------------------
    // Menu entry point
    // ------------------------------------------------------------------

    [MenuItem("CyberBoss/Setup HUD")]
    public static void Execute()
    {
        RemoveExistingCanvas(); // Idempotent — safe to re-run at any time.

        EnsureScriptableObjectsFolder();
        HUDConfig config = GetOrCreateHudConfig();
        EnsureEventSystem();

        // Build hierarchy top-down.
        GameObject canvas          = CreateCanvas();
        GameObject playerHealthBar = CreateHealthBar(canvas.transform, "PlayerHealthBar",
            "PLAYER", config.PlayerBarColor, isLeftAnchored: true);
        GameObject bossHealthBar   = CreateHealthBar(canvas.transform, "BossHealthBar",
            "BOSS", config.BossBarColor, isLeftAnchored: false);
        GameObject cooldownPanel   = CreateCooldownPanel(canvas.transform, config);
        GameObject gameOverPanel   = CreateGameOverPanel(canvas.transform);

        // Add MonoBehaviours.
        var hudController  = canvas.AddComponent<HUDController>();
        var cooldownUI     = cooldownPanel.AddComponent<CooldownUI>();
        var gameOverScreen = gameOverPanel.AddComponent<GameOverScreen>();

        // Resolve Image and TMP_Text references before wiring.
        Image    playerFill   = playerHealthBar.transform.Find("BarArea/Fill").GetComponent<Image>();
        Image    bossFill     = bossHealthBar.transform.Find("BarArea/Fill").GetComponent<Image>();
        Image[]  skillFills   = ResolveSlotChildImages(cooldownPanel.transform, childName: "Fill");
        Image[]  slotBorders  = ResolveSlotRootImages(cooldownPanel.transform);
        TMP_Text messageText  = gameOverPanel.transform.Find("MessageText").GetComponent<TMP_Text>();
        TMP_Text subtitleText = gameOverPanel.transform.Find("SubtitleText").GetComponent<TMP_Text>();
        TMP_Text hintText     = gameOverPanel.transform.Find("HintText").GetComponent<TMP_Text>();

        // Wire serialized (private) fields via SerializedObject.
        WireHudController(hudController, playerFill, bossFill, cooldownUI, gameOverScreen);
        WireCooldownUI(cooldownUI, skillFills, slotBorders, config);
        WireGameOverScreen(gameOverScreen, messageText, subtitleText, hintText, config);

        // Panel starts inactive — activated at runtime by GameOverScreen.Show().
        gameOverPanel.SetActive(false);

        EditorUtility.SetDirty(canvas);
        EditorSceneManager.MarkAllScenesDirty();
        AssetDatabase.SaveAssets();

        Debug.Log("[SetupHUD] HUDCanvas rebuilt with cyberpunk visual style.\n" +
            "PREREQUISITE: Window > TextMeshPro > Import TMP Essential Resources");
    }

    // ------------------------------------------------------------------
    // Idempotency
    // ------------------------------------------------------------------

    private static void RemoveExistingCanvas()
    {
        var existing = GameObject.Find(CanvasObjectName);
        if (existing == null) return;
        Undo.DestroyObjectImmediate(existing);
        Debug.Log("[SetupHUD] Removed existing HUDCanvas — rebuilding from scratch.");
    }

    // ------------------------------------------------------------------
    // ScriptableObject folder / HUDConfig asset
    // ------------------------------------------------------------------

    private static void EnsureScriptableObjectsFolder()
    {
        if (!AssetDatabase.IsValidFolder("Assets/ScriptableObjects"))
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
    }

    private static HUDConfig GetOrCreateHudConfig()
    {
        var existing = AssetDatabase.LoadAssetAtPath<HUDConfig>(HudConfigAssetPath);
        if (existing != null)
        {
            Debug.Log("[SetupHUD] Using existing HUDConfig asset.");
            return existing;
        }

        // Default colors/strings are defined in HUDConfig field initializers;
        // CreateInstance picks them up automatically.
        var config = ScriptableObject.CreateInstance<HUDConfig>();
        AssetDatabase.CreateAsset(config, HudConfigAssetPath);
        EditorUtility.SetDirty(config);
        Debug.Log($"[SetupHUD] Created HUDConfig asset at '{HudConfigAssetPath}'.");
        return config;
    }

    // ------------------------------------------------------------------
    // EventSystem
    // ------------------------------------------------------------------

    private static void EnsureEventSystem()
    {
        if (Object.FindAnyObjectByType<EventSystem>() != null) return;

        var go = new GameObject("EventSystem");
        go.AddComponent<EventSystem>();
        // New Input System requires InputSystemUIInputModule, not StandaloneInputModule.
        go.AddComponent<InputSystemUIInputModule>();
        Undo.RegisterCreatedObjectUndo(go, "Add EventSystem");
        Debug.Log("[SetupHUD] Created EventSystem with InputSystemUIInputModule.");
    }

    // ------------------------------------------------------------------
    // Canvas
    // ------------------------------------------------------------------

    private static GameObject CreateCanvas()
    {
        var go = new GameObject(CanvasObjectName, typeof(RectTransform));
        Undo.RegisterCreatedObjectUndo(go, "Create HUD Canvas");

        var canvas = go.AddComponent<Canvas>();
        canvas.renderMode   = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 10; // renders above the 3D scene

        var scaler = go.AddComponent<CanvasScaler>();
        scaler.uiScaleMode         = CanvasScaler.ScaleMode.ScaleWithScreenSize;
        scaler.referenceResolution = new Vector2(1920f, 1080f);
        scaler.matchWidthOrHeight  = 0.5f;

        go.AddComponent<GraphicRaycaster>();
        return go;
    }

    // ------------------------------------------------------------------
    // Health bars  (near-full-width, 10-segment, character name label)
    // ------------------------------------------------------------------

    private static GameObject CreateHealthBar(
        Transform parent, string groupName, string characterLabel,
        Color barColor, bool isLeftAnchored)
    {
        var group = CreateRectObject(parent, groupName);
        var rt    = (RectTransform)group.transform;

        // 620px per bar — compact enough not to obstruct the play area.
        // Player: x=20..640. Boss: x=1280..1900. 640px clear in the centre.
        if (isLeftAnchored)
        {
            rt.anchorMin        = new Vector2(0f, 1f);
            rt.anchorMax        = new Vector2(0f, 1f);
            rt.pivot            = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(20f, -16f);
        }
        else
        {
            rt.anchorMin        = new Vector2(1f, 1f);
            rt.anchorMax        = new Vector2(1f, 1f);
            rt.pivot            = new Vector2(1f, 1f);
            rt.anchoredPosition = new Vector2(-20f, -16f);
        }
        rt.sizeDelta = new Vector2(620f, 36f);

        // Character name label — top 12px of the group.
        var labelGo  = CreateRectObject(group.transform, "NameLabel");
        var labelRT  = (RectTransform)labelGo.transform;
        labelRT.anchorMin        = new Vector2(0f, 1f);
        labelRT.anchorMax        = new Vector2(1f, 1f);
        labelRT.pivot            = new Vector2(0.5f, 1f);
        labelRT.anchoredPosition = Vector2.zero;
        labelRT.sizeDelta        = new Vector2(0f, 12f);
        var labelTMP = labelGo.AddComponent<TextMeshProUGUI>();
        labelTMP.text      = characterLabel;
        labelTMP.fontSize  = 11f;
        labelTMP.fontStyle = FontStyles.Bold;
        labelTMP.alignment = isLeftAnchored
            ? TextAlignmentOptions.Left
            : TextAlignmentOptions.Right;
        labelTMP.color = barColor;

        // Bar area — bottom 20px of the group (4px visual gap below the label).
        var barArea   = CreateRectObject(group.transform, "BarArea");
        var barAreaRT = (RectTransform)barArea.transform;
        barAreaRT.anchorMin        = new Vector2(0f, 0f);
        barAreaRT.anchorMax        = new Vector2(1f, 0f);
        barAreaRT.pivot            = new Vector2(0.5f, 0f);
        barAreaRT.anchoredPosition = Vector2.zero;
        barAreaRT.sizeDelta        = new Vector2(0f, 20f);

        CreateStretchedImage(barArea.transform, "Background",
            new Color(0.04f, 0.04f, 0.04f, 0.92f));

        var fill      = CreateStretchedImage(barArea.transform, "Fill", barColor);
        fill.type       = Image.Type.Filled;
        fill.fillMethod = Image.FillMethod.Horizontal;
        fill.fillAmount = 1f;

        // 9 dark dividers at 10% intervals produce the 10-segment battery look.
        CreateSegmentDividers(barArea.transform);

        return group;
    }

    private static void CreateSegmentDividers(Transform barArea)
    {
        var overlay   = CreateRectObject(barArea, "SegmentsOverlay");
        var overlayRT = (RectTransform)overlay.transform;
        overlayRT.anchorMin = Vector2.zero;
        overlayRT.anchorMax = Vector2.one;
        overlayRT.offsetMin = Vector2.zero;
        overlayRT.offsetMax = Vector2.zero;

        // Dark dividers rendered above the fill — always visible regardless of health level.
        var dividerColor = new Color(0.02f, 0.02f, 0.04f, 0.80f);
        for (int i = 1; i <= 9; i++)
        {
            var div   = CreateRectObject(overlay.transform, $"Div_{i}");
            var divRT = (RectTransform)div.transform;
            float xPct   = i / 10f;
            // Anchor at xPct of parent width; offsetMin/Max extend 1px each side = 2px wide.
            divRT.anchorMin = new Vector2(xPct, 0f);
            divRT.anchorMax = new Vector2(xPct, 1f);
            divRT.pivot     = new Vector2(0.5f, 0.5f);
            divRT.offsetMin = new Vector2(-1f, 0f);
            divRT.offsetMax = new Vector2(1f,  0f);
            div.AddComponent<Image>().color = dividerColor;
        }
    }

    // ------------------------------------------------------------------
    // Cooldown panel  (dark strip, 5 slots with key labels + radial fills)
    // ------------------------------------------------------------------

    private static GameObject CreateCooldownPanel(Transform parent, HUDConfig config)
    {
        // Slot: 78×106, gap: 6px. Total: 5×78 + 4×6 = 414px. Panel: 430px → 8px side padding.
        var panel = CreateRectObject(parent, "CooldownPanel");
        var rt    = (RectTransform)panel.transform;
        rt.anchorMin        = new Vector2(0.5f, 0f);
        rt.anchorMax        = new Vector2(0.5f, 0f);
        rt.pivot            = new Vector2(0.5f, 0f);
        rt.anchoredPosition = new Vector2(0f, 20f);
        rt.sizeDelta        = new Vector2(430f, 118f);

        // Panel border Image lives on the root — a dim neon outline around the strip.
        // CooldownUI is also added to this root in Execute().
        panel.AddComponent<Image>().color = new Color(0.10f, 0.90f, 1.00f, 0.30f);

        // Inner dark background, 2px inset from the border.
        var innerBg   = CreateRectObject(panel.transform, "InnerBackground");
        var innerBgRT = (RectTransform)innerBg.transform;
        innerBgRT.anchorMin = Vector2.zero;
        innerBgRT.anchorMax = Vector2.one;
        innerBgRT.offsetMin = new Vector2(2f,  2f);
        innerBgRT.offsetMax = new Vector2(-2f, -2f);
        innerBg.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.06f, 0.94f);

        // x-centres for the 5 slots relative to the panel's horizontal midpoint.
        // Panel pivot is (0.5, 0), so x=0 is the horizontal centre of the 430px strip.
        float[] xCentres = { -168f, -84f, 0f, 84f, 168f };

        for (int i = 0; i < SlotIds.Length; i++)
            CreateSkillSlot(panel.transform, SlotIds[i], SkillKeys[i], SkillNames[i],
                xCentres[i], config);

        return panel;
    }

    private static void CreateSkillSlot(
        Transform parent, string slotId, string keyLabel, string skillName,
        float xCentre, HUDConfig config)
    {
        var slot   = CreateRectObject(parent, slotId);
        var slotRT = (RectTransform)slot.transform;
        slotRT.anchorMin        = new Vector2(0.5f, 0.5f);
        slotRT.anchorMax        = new Vector2(0.5f, 0.5f);
        slotRT.pivot            = new Vector2(0.5f, 0.5f);
        slotRT.anchoredPosition = new Vector2(xCentre, 0f);
        slotRT.sizeDelta        = new Vector2(78f, 106f);

        // Border Image on the slot root — CooldownUI switches this between
        // SlotBorderReadyColor (neon glow) and SlotBorderDepletedColor (dim grey).
        slot.AddComponent<Image>().color = config.SlotBorderReadyColor;

        // Inner dark background, 2px inset from the slot border.
        var bg   = CreateRectObject(slot.transform, "Background");
        var bgRT = (RectTransform)bg.transform;
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = new Vector2(2f,  2f);
        bgRT.offsetMax = new Vector2(-2f, -2f);
        bg.AddComponent<Image>().color = new Color(0.04f, 0.04f, 0.08f, 0.96f);

        // Key binding label — top of slot.
        // Slot local y range: -53 (bottom) … +53 (top).
        // anchoredPosition=(0,-6): pivot at y=47, bottom edge at y=29.
        var keyGo  = CreateRectObject(slot.transform, "KeyLabel");
        var keyRT  = (RectTransform)keyGo.transform;
        keyRT.anchorMin        = new Vector2(0f, 1f);
        keyRT.anchorMax        = new Vector2(1f, 1f);
        keyRT.pivot            = new Vector2(0.5f, 1f);
        keyRT.anchoredPosition = new Vector2(0f, -6f);
        keyRT.sizeDelta        = new Vector2(0f, 18f);
        var keyTMP = keyGo.AddComponent<TextMeshProUGUI>();
        keyTMP.text      = keyLabel;
        keyTMP.fontSize  = 11f;
        keyTMP.fontStyle = FontStyles.Bold;
        keyTMP.alignment = TextAlignmentOptions.Center;
        keyTMP.color     = new Color(0.70f, 0.70f, 0.70f);

        // Radial360 fill — centred in the slot. Occupies y=-31..+31 (62px).
        // 2px gap to key label bottom at y=29.
        var fillGo  = CreateRectObject(slot.transform, "Fill");
        var fillRT  = (RectTransform)fillGo.transform;
        fillRT.anchorMin        = new Vector2(0.5f, 0.5f);
        fillRT.anchorMax        = new Vector2(0.5f, 0.5f);
        fillRT.pivot            = new Vector2(0.5f, 0.5f);
        fillRT.anchoredPosition = Vector2.zero;
        fillRT.sizeDelta        = new Vector2(62f, 62f);
        var fillImg           = fillGo.AddComponent<Image>();
        fillImg.type          = Image.Type.Filled;
        fillImg.fillMethod    = Image.FillMethod.Radial360;
        fillImg.fillClockwise = true;
        fillImg.fillOrigin    = (int)Image.Origin360.Top;
        fillImg.fillAmount    = 1f;
        fillImg.color         = config.CooldownReadyColor;

        // Skill name label — bottom of slot.
        // anchoredPosition=(0,+6): pivot at y=-47, top edge at y=-31.
        var nameGo  = CreateRectObject(slot.transform, "SkillNameLabel");
        var nameRT  = (RectTransform)nameGo.transform;
        nameRT.anchorMin        = new Vector2(0f, 0f);
        nameRT.anchorMax        = new Vector2(1f, 0f);
        nameRT.pivot            = new Vector2(0.5f, 0f);
        nameRT.anchoredPosition = new Vector2(0f, 6f);
        nameRT.sizeDelta        = new Vector2(0f, 16f);
        var nameTMP = nameGo.AddComponent<TextMeshProUGUI>();
        nameTMP.text      = skillName;
        nameTMP.fontSize  = 10f;
        nameTMP.fontStyle = FontStyles.Bold;
        nameTMP.alignment = TextAlignmentOptions.Center;
        nameTMP.color     = new Color(0.55f, 0.55f, 0.55f);
    }

    // ------------------------------------------------------------------
    // Game over panel  (death / victory overlay)
    // ------------------------------------------------------------------

    private static GameObject CreateGameOverPanel(Transform parent)
    {
        var panel = CreateRectObject(parent, "GameOverPanel");
        var rt    = (RectTransform)panel.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;

        // Full-screen semi-transparent backdrop.
        var bg   = CreateRectObject(panel.transform, "Background");
        var bgRT = (RectTransform)bg.transform;
        bgRT.anchorMin = Vector2.zero;
        bgRT.anchorMax = Vector2.one;
        bgRT.offsetMin = Vector2.zero;
        bgRT.offsetMax = Vector2.zero;
        bg.AddComponent<Image>().color = new Color(0f, 0f, 0f, 0.88f);

        // Main message — 80px above screen centre. Color overwritten by GameOverScreen.Show().
        var msgGo  = CreateRectObject(panel.transform, "MessageText");
        SetHorizontalStretch((RectTransform)msgGo.transform, yOffset: 80f, height: 110f);
        var msgTMP = msgGo.AddComponent<TextMeshProUGUI>();
        msgTMP.text      = "YOU DIED"; // default; overwritten at runtime
        msgTMP.fontSize  = 72f;
        msgTMP.fontStyle = FontStyles.Bold;
        msgTMP.alignment = TextAlignmentOptions.Center;
        msgTMP.color     = new Color(0.95f, 0.10f, 0.10f); // death color; updated at runtime

        // Subtitle — 10px above screen centre. Text and color overwritten at runtime.
        var subGo  = CreateRectObject(panel.transform, "SubtitleText");
        SetHorizontalStretch((RectTransform)subGo.transform, yOffset: 10f, height: 50f);
        var subTMP = subGo.AddComponent<TextMeshProUGUI>();
        subTMP.text      = "SYSTEM FAILURE"; // default; overwritten at runtime
        subTMP.fontSize  = 28f;
        subTMP.fontStyle = FontStyles.Bold;
        subTMP.alignment = TextAlignmentOptions.Center;
        subTMP.color     = new Color(0.60f, 0.60f, 0.60f);

        // Restart hint — 70px below screen centre.
        var hintGo  = CreateRectObject(panel.transform, "HintText");
        SetHorizontalStretch((RectTransform)hintGo.transform, yOffset: -70f, height: 44f);
        var hintTMP = hintGo.AddComponent<TextMeshProUGUI>();
        hintTMP.text      = "[ PRESS T TO RESTART ]"; // default; overwritten at runtime
        hintTMP.fontSize  = 24f;
        hintTMP.alignment = TextAlignmentOptions.Center;
        hintTMP.color     = new Color(0.85f, 0.85f, 0.85f);

        return panel;
    }

    // ------------------------------------------------------------------
    // Reference resolvers
    // ------------------------------------------------------------------

    /// <summary>Find the named child Image inside each skill slot.</summary>
    private static Image[] ResolveSlotChildImages(Transform cooldownPanel, string childName)
    {
        var images = new Image[SlotIds.Length];
        for (int i = 0; i < SlotIds.Length; i++)
        {
            var slotTransform = cooldownPanel.Find(SlotIds[i]);
            if (slotTransform == null)
            {
                Debug.LogError($"[SetupHUD] Slot '{SlotIds[i]}' not found under CooldownPanel.");
                continue;
            }

            var child = slotTransform.Find(childName);
            if (child == null)
            {
                Debug.LogError($"[SetupHUD] Child '{childName}' not found under '{SlotIds[i]}'.");
                continue;
            }

            images[i] = child.GetComponent<Image>();
        }
        return images;
    }

    /// <summary>
    /// Get the Image on each slot's root GameObject — the border Image that
    /// CooldownUI drives for the ready/depleted glow effect.
    /// </summary>
    private static Image[] ResolveSlotRootImages(Transform cooldownPanel)
    {
        var images = new Image[SlotIds.Length];
        for (int i = 0; i < SlotIds.Length; i++)
        {
            var slotTransform = cooldownPanel.Find(SlotIds[i]);
            if (slotTransform == null)
            {
                Debug.LogError($"[SetupHUD] Slot '{SlotIds[i]}' not found under CooldownPanel.");
                continue;
            }
            images[i] = slotTransform.GetComponent<Image>();
        }
        return images;
    }

    // ------------------------------------------------------------------
    // Serialized field wiring  (private fields require SerializedObject)
    // ------------------------------------------------------------------

    private static void WireHudController(
        HUDController controller,
        Image playerFill, Image bossFill,
        CooldownUI cooldownUI, GameOverScreen gameOverScreen)
    {
        var so = new SerializedObject(controller);
        AssignField(so, "_playerHealthFill", playerFill);
        AssignField(so, "_bossHealthFill",   bossFill);
        AssignField(so, "_cooldownUI",       cooldownUI);
        AssignField(so, "_gameOverScreen",   gameOverScreen);
        so.ApplyModifiedProperties();
    }

    private static void WireCooldownUI(
        CooldownUI cooldownUI, Image[] skillFills, Image[] slotBorders, HUDConfig config)
    {
        var so = new SerializedObject(cooldownUI);
        WireImageArray(so, "_skillFills",  skillFills);
        WireImageArray(so, "_slotBorders", slotBorders);
        AssignField(so, "_config", config);
        so.ApplyModifiedProperties();
    }

    private static void WireGameOverScreen(
        GameOverScreen screen,
        TMP_Text messageText, TMP_Text subtitleText, TMP_Text hintText,
        HUDConfig config)
    {
        var so = new SerializedObject(screen);
        AssignField(so, "_messageText",  messageText);
        AssignField(so, "_subtitleText", subtitleText);
        AssignField(so, "_hintText",     hintText);
        AssignField(so, "_config",       config);
        so.ApplyModifiedProperties();
    }

    // ------------------------------------------------------------------
    // Layout and creation helpers
    // ------------------------------------------------------------------

    private static GameObject CreateRectObject(Transform parent, string name)
    {
        var go = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);
        return go;
    }

    private static Image CreateStretchedImage(Transform parent, string name, Color color)
    {
        var go  = new GameObject(name, typeof(RectTransform));
        go.transform.SetParent(parent, worldPositionStays: false);
        var rt  = (RectTransform)go.transform;
        rt.anchorMin = Vector2.zero;
        rt.anchorMax = Vector2.one;
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
        var img = go.AddComponent<Image>();
        img.color = color;
        return img;
    }

    /// <summary>
    /// Anchor to the vertical midpoint of the parent with full horizontal stretch.
    /// yOffset: positive = above centre, negative = below centre.
    /// </summary>
    private static void SetHorizontalStretch(RectTransform rt, float yOffset, float height)
    {
        rt.anchorMin        = new Vector2(0f, 0.5f);
        rt.anchorMax        = new Vector2(1f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.anchoredPosition = new Vector2(0f, yOffset);
        rt.sizeDelta        = new Vector2(0f, height);
    }

    private static void WireImageArray(SerializedObject so, string fieldName, Image[] images)
    {
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogError($"[SetupHUD] SerializedProperty '{fieldName}' not found on " +
                $"'{so.targetObject.GetType().Name}'. Check the field name matches exactly.");
            return;
        }
        prop.arraySize = images.Length;
        for (int i = 0; i < images.Length; i++)
            prop.GetArrayElementAtIndex(i).objectReferenceValue = images[i];
    }

    private static void AssignField(SerializedObject so, string fieldName, Object value)
    {
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogError($"[SetupHUD] SerializedProperty '{fieldName}' not found on " +
                $"'{so.targetObject.GetType().Name}'. Check the field name matches exactly.");
            return;
        }
        prop.objectReferenceValue = value;
    }
}

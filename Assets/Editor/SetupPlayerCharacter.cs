using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine.InputSystem;

/// <summary>
/// One-shot setup for the player character. Run from the menu:
///   CyberBoss → Setup Player Character
///
/// Idempotent — safe to run multiple times. Actions performed:
///   1. Adds "Speed" float parameter to the Muryotaisu animator controller.
///   2. Creates Assets/ScriptableObjects/PlayerConfig.asset with default values.
///   3. Opens CyberArena scene if not already open.
///   4. Instantiates Muryotaisu prefab at origin (or reconfigures if already present).
///   5. Updates CharacterController to spec: center (0, 0.9, 0), height 1.8, radius 0.35.
///   6. Removes the legacy MuryotaisuController (uses UnityEngine.Input — incompatible
///      with the New Input System active in this project).
///   7. Adds PlayerInput component (SendMessages, Player action map).
///   8. Adds PlayerController with PlayerConfig assigned via SerializedObject.
/// </summary>
public static class SetupPlayerCharacter
{
    private const string PrefabPath       = "Assets/Mryotaisu/Prefabs/Muryotaisu.prefab";
    private const string ControllerPath   = "Assets/Mryotaisu/Animators/Muryotaisu.controller";
    private const string InputActionsPath = "Assets/InputSystem_Actions.inputactions";
    private const string ScenePath        = "Assets/Scenes/CyberArena.unity";
    private const string ConfigFolder     = "Assets/ScriptableObjects";
    private const string ConfigAssetPath  = "Assets/ScriptableObjects/PlayerConfig.asset";

    [MenuItem("CyberBoss/Setup Player Character")]
    public static void Execute()
    {
        AddSpeedParameterToAnimator();
        var config = CreateOrLoadPlayerConfig();
        EnsureCyberArenaSceneOpen();
        SetupPlayerInScene(config);
        SaveAll();
        Debug.Log("[CyberBoss] Player character setup complete. " +
                  "Press Play to verify isometric movement.");
    }

    // ── Animator ──────────────────────────────────────────────────────────

    static void AddSpeedParameterToAnimator()
    {
        var controller =
            AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);

        if (controller == null)
        {
            Debug.LogError(
                $"[CyberBoss] Animator controller not found at '{ControllerPath}'.");
            return;
        }

        foreach (var param in controller.parameters)
        {
            if (param.name == "Speed") return; // Already present — nothing to do.
        }

        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        EditorUtility.SetDirty(controller);
        Debug.Log("[CyberBoss] Added 'Speed' float parameter to Muryotaisu animator.");
    }

    // ── PlayerConfig ScriptableObject ─────────────────────────────────────

    static PlayerConfig CreateOrLoadPlayerConfig()
    {
        var existing = AssetDatabase.LoadAssetAtPath<PlayerConfig>(ConfigAssetPath);
        if (existing != null) return existing;

        if (!AssetDatabase.IsValidFolder(ConfigFolder))
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");

        var config = ScriptableObject.CreateInstance<PlayerConfig>();
        AssetDatabase.CreateAsset(config, ConfigAssetPath);
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();

        Debug.Log($"[CyberBoss] Created PlayerConfig at '{ConfigAssetPath}'.");
        return config;
    }

    // ── Scene ─────────────────────────────────────────────────────────────

    static void EnsureCyberArenaSceneOpen()
    {
        for (int i = 0; i < EditorSceneManager.sceneCount; i++)
        {
            if (EditorSceneManager.GetSceneAt(i).path == ScenePath) return;
        }
        EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
    }

    static void SetupPlayerInScene(PlayerConfig config)
    {
        var playerGO = FindOrInstantiatePlayer();

        ConfigureCharacterController(playerGO);
        RemoveLegacyController(playerGO);
        ConfigurePlayerInput(playerGO);
        ConfigurePlayerController(playerGO, config);

        playerGO.tag = "Player";
    }

    static GameObject FindOrInstantiatePlayer()
    {
        var existing = GameObject.Find("Muryotaisu");
        if (existing != null)
        {
            Debug.Log("[CyberBoss] Found existing Muryotaisu in scene — reconfiguring.");
            return existing;
        }

        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
            throw new System.InvalidOperationException(
                $"[CyberBoss] Player prefab not found at '{PrefabPath}'.");

        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.transform.position = Vector3.zero;
        Debug.Log("[CyberBoss] Instantiated Muryotaisu prefab at origin (0, 0, 0).");
        return instance;
    }

    static void ConfigureCharacterController(GameObject go)
    {
        var cc = go.GetComponent<CharacterController>();
        if (cc == null) cc = go.AddComponent<CharacterController>();

        cc.center = new Vector3(0f, 0.9f, 0f);
        cc.height = 1.8f;
        cc.radius = 0.35f;
    }

    /// Removes MuryotaisuController from the instance. That script calls
    /// UnityEngine.Input directly, which throws at runtime when the New Input
    /// System package is active (active input handling = Input System only).
    static void RemoveLegacyController(GameObject go)
    {
        foreach (var mb in go.GetComponents<MonoBehaviour>())
        {
            if (mb == null) continue;
            if (mb.GetType().FullName != "Muryotaisu.MuryotaisuController") continue;

            Object.DestroyImmediate(mb);
            Debug.Log("[CyberBoss] Removed legacy MuryotaisuController component.");
            return;
        }
    }

    static void ConfigurePlayerInput(GameObject go)
    {
        var pi = go.GetComponent<PlayerInput>() ?? go.AddComponent<PlayerInput>();

        var inputAsset =
            AssetDatabase.LoadAssetAtPath<InputActionAsset>(InputActionsPath);

        if (inputAsset == null)
        {
            Debug.LogError(
                $"[CyberBoss] InputActionAsset not found at '{InputActionsPath}'.");
            return;
        }

        pi.actions              = inputAsset;
        pi.defaultActionMap     = "Player";
        pi.notificationBehavior = PlayerNotifications.SendMessages;
    }

    static void ConfigurePlayerController(GameObject go, PlayerConfig config)
    {
        var pc = go.GetComponent<PlayerController>()
              ?? go.AddComponent<PlayerController>();

        // SerializedObject is required to write to a private [SerializeField] field.
        var so   = new SerializedObject(pc);
        var prop = so.FindProperty("_config");
        prop.objectReferenceValue = config;
        so.ApplyModifiedProperties();
    }

    // ── Save ──────────────────────────────────────────────────────────────

    static void SaveAll()
    {
        EditorSceneManager.MarkAllScenesDirty();
        EditorSceneManager.SaveOpenScenes();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }
}

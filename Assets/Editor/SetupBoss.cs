using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditorInternal;

/// <summary>
/// One-shot editor setup for Boss and Player Combat wiring,
/// and boss skill config/prefab creation.
///
/// Menu items (all idempotent — safe to run multiple times):
///   CyberBoss/Setup Boss          — spawns the CyberSoldier boss in the scene
///   CyberBoss/Setup Player Combat — adds HealthSystem and DamageHandler to the player
///   CyberBoss/Setup Boss Skills   — creates the five per-skill SO configs, the orange
///                                   boss projectile prefab, and wires them to BossSkills
///
/// Component-order contract (BossController.cs requirement):
///   BossController must appear before HealthSystem in the component list so that
///   GetComponent&lt;IDamageable&gt;() resolves to BossController, not HealthSystem.
///   This ensures shield-phase absorption in BossController.TakeDamage()
///   is reached before health is modified directly.
///   ComponentUtility.MoveComponentUp is used after adding all three components
///   to enforce this ordering.
///
/// HitboxManager damage deviation from spec:
///   The spec requests setting _damagesPlayer=true and _damage=10 on HitboxManager
///   via SerializedObject. Those fields do not exist on HitboxManager — damage is
///   supplied per-swing at runtime via HitboxManager.Activate(float, GameObject),
///   called by each BossSkill.Execute(). This is intentional: damage per skill varies
///   Damage per skill varies. There is no serialized default damage.
/// </summary>
public static class SetupBoss
{
    private const string BossAssetPath   = "Assets/MyAssets/CyberSoldier/CyberSoldier.fbx";
    private const string BossConfigPath  = "Assets/ScriptableObjects/BossConfig.asset";
    private const string SOFolder        = "Assets/ScriptableObjects";
    private const string PrefabFolder    = "Assets/Prefabs";
    private const string BossTag         = "Boss";
    private const string PlayerName      = "Muryotaisu";

    // ------------------------------------------------------------------
    // Menu items
    // ------------------------------------------------------------------

    [MenuItem("CyberBoss/Setup Boss")]
    public static void SetupBossInScene()
    {
        RemoveExistingBoss();

        var bossGo = InstantiateBoss();
        if (bossGo == null) return;

        EnsureTagExists(BossTag);
        bossGo.tag = BossTag;

        var config = GetOrCreateBossConfig();
        AddAndWireComponents(bossGo, config);
        AddBossHitbox(bossGo);
        PositionBoss(bossGo);

        EditorUtility.SetDirty(bossGo);
        EditorSceneManager.MarkAllScenesDirty();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log("[SetupBoss] Boss setup complete. " +
                  "Boss 'CyberSoldier' spawned at (5, 0, 0) facing origin.");
    }

    [MenuItem("CyberBoss/Setup Player Combat")]
    public static void SetupPlayerCombat()
    {
        var playerGo = GameObject.Find(PlayerName);
        if (playerGo == null)
        {
            Debug.LogError($"[SetupBoss] '{PlayerName}' not found in scene. " +
                           "Run 'CyberBoss/Setup Player Character' first.");
            return;
        }

        EnsureHealthSystem(playerGo, maxHealth: 100f);
        EnsureDamageHandler(playerGo);

        EditorUtility.SetDirty(playerGo);
        EditorSceneManager.MarkAllScenesDirty();
        AssetDatabase.SaveAssets();

        Debug.Log($"[SetupBoss] Player combat setup complete on '{PlayerName}'.");
    }

    // ------------------------------------------------------------------
    // Setup Boss — steps
    // ------------------------------------------------------------------

    private static void RemoveExistingBoss()
    {
        // Search by name first, then by tag so stale objects are never left in the scene
        // regardless of what they were renamed to between Setup runs.
        var byName = GameObject.Find("Boss");
        if (byName != null)
        {
            Object.DestroyImmediate(byName);
            Debug.Log("[SetupBoss] Destroyed existing 'Boss' GameObject (found by name).");
        }

        // FindGameObjectsWithTag throws if the tag is unregistered, so guard it.
        try
        {
            foreach (var go in GameObject.FindGameObjectsWithTag(BossTag))
            {
                Debug.Log($"[SetupBoss] Destroyed stale boss '{go.name}' (found by tag).");
                Object.DestroyImmediate(go);
            }
        }
        catch (UnityEngine.UnityException)
        {
            // Tag not registered yet — safe to ignore; EnsureTagExists runs next.
        }
    }

    private static GameObject InstantiateBoss()
    {
        var bossPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BossAssetPath);
        if (bossPrefab == null)
        {
            Debug.LogError(
                $"[SetupBoss] CyberSoldier.fbx not found at '{BossAssetPath}'. Aborting. " +
                "Verify the asset is imported at that path.");
            return null;
        }

        var bossGo = (GameObject)PrefabUtility.InstantiatePrefab(bossPrefab);
        bossGo.name = "Boss";
        Debug.Log("[SetupBoss] Instantiated CyberSoldier as 'Boss'.");
        return bossGo;
    }

    private static void AddAndWireComponents(GameObject bossGo, BossConfig config)
    {
        // Add in dependency order first: HealthSystem, BossSkills, then BossController.
        // ComponentUtility.MoveComponentUp is called after to satisfy the ordering
        // contract in BossController.cs: BossController must precede HealthSystem so
        // GetComponent<IDamageable>() returns BossController (shield-phase gate).
        bossGo.AddComponent<HealthSystem>();
        bossGo.AddComponent<BossSkills>();
        var bossController = bossGo.AddComponent<BossController>();

        // Move BossController above BossSkills, then above HealthSystem.
        // Two moves required: BossSkills → HealthSystem → BossController leads.
        ComponentUtility.MoveComponentUp(bossController);
        ComponentUtility.MoveComponentUp(bossController);

        // BossConfig goes to BossController only — BossSkills uses per-skill
        // configs created by CyberBoss/Setup Boss Skills.
        AssignConfig(bossController, config, "_config");

        Debug.Log("[SetupBoss] Added HealthSystem, BossSkills, BossController. " +
                  "BossController moved to top of IDamageable component list. " +
                  "Run 'CyberBoss/Setup Boss Skills' to assign per-skill configs.");
    }

    private static void AddBossHitbox(GameObject bossGo)
    {
        var hitboxGo = new GameObject("BossHitbox");
        hitboxGo.transform.SetParent(bossGo.transform, worldPositionStays: false);

        // CapsuleCollider is required by [RequireComponent] on HitboxManager.
        // Configure before adding HitboxManager to satisfy the requirement.
        var capsule   = hitboxGo.AddComponent<CapsuleCollider>();
        capsule.isTrigger = true;
        capsule.height    = 2f;
        capsule.radius    = 0.4f;

        // HitboxManager.Activate(float damage, GameObject owner) is called per swing
        // by each BossSkill. There are no serialized _damagesPlayer or _damage fields
        // to set — see class-level doc comment for the rationale.
        hitboxGo.AddComponent<HitboxManager>();

        Debug.Log("[SetupBoss] Created BossHitbox child (CapsuleCollider trigger, HitboxManager).");
    }

    private static void PositionBoss(GameObject bossGo)
    {
        bossGo.transform.position   = new Vector3(2f, 0f, 2f);
        bossGo.transform.rotation   = Quaternion.Euler(0f, -120f, 0f);
        bossGo.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);

        Debug.Log(
            $"[SetupBoss] Boss transform set — " +
            $"pos {bossGo.transform.position}, " +
            $"rot {bossGo.transform.rotation.eulerAngles}, " +
            $"scale {bossGo.transform.localScale}");
    }

    // ------------------------------------------------------------------
    // Setup Player Combat — steps
    // ------------------------------------------------------------------

    private static void EnsureHealthSystem(GameObject go, float maxHealth)
    {
        var healthSystem = go.GetComponent<HealthSystem>();
        if (healthSystem == null)
        {
            healthSystem = go.AddComponent<HealthSystem>();
            Debug.Log($"[SetupBoss] Added HealthSystem to '{go.name}'.");
        }

        var so = new SerializedObject(healthSystem);
        so.FindProperty("_maxHealth").floatValue = maxHealth;
        so.ApplyModifiedProperties();
    }

    private static void EnsureDamageHandler(GameObject go)
    {
        if (go.GetComponent<DamageHandler>() != null) return;

        go.AddComponent<DamageHandler>();
        Debug.Log($"[SetupBoss] Added DamageHandler to '{go.name}'.");
    }

    // ------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------

    private static BossConfig GetOrCreateBossConfig()
    {
        var existing = AssetDatabase.LoadAssetAtPath<BossConfig>(BossConfigPath);
        if (existing != null)
        {
            Debug.Log("[SetupBoss] BossConfig.asset already exists — reusing.");
            return existing;
        }

        if (!AssetDatabase.IsValidFolder(SOFolder))
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");

        var config = ScriptableObject.CreateInstance<BossConfig>();
        AssetDatabase.CreateAsset(config, BossConfigPath);
        EditorUtility.SetDirty(config);
        Debug.Log($"[SetupBoss] Created BossConfig at '{BossConfigPath}'.");
        return config;
    }

    private static void AssignConfig(Object component, BossConfig config, string fieldName)
    {
        var so   = new SerializedObject(component);
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogError(
                $"[SetupBoss] SerializedProperty '{fieldName}' not found on " +
                $"'{component.GetType().Name}'. Check field name matches exactly.");
            return;
        }

        prop.objectReferenceValue = config;
        so.ApplyModifiedProperties();
    }

    // ------------------------------------------------------------------
    // Setup RL Boss
    // ------------------------------------------------------------------

    [MenuItem("CyberBoss/Setup RL Boss")]
    public static void SetupRLBoss()
    {
        var bossGo = GameObject.FindWithTag(BossTag);
        if (bossGo == null)
        {
            Debug.LogError(
                "[SetupBoss] No GameObject tagged 'Boss' found. " +
                "Run 'CyberBoss/Setup Boss' first.");
            return;
        }

        // Add SentisInferenceManager if missing.
        var sentis = bossGo.GetComponent<SentisInferenceManager>();
        if (sentis == null)
        {
            sentis = bossGo.AddComponent<SentisInferenceManager>();
            Debug.Log("[SetupBoss] Added SentisInferenceManager to Boss.");
        }

        // Load BossBrain.onnx and assign to SentisInferenceManager._onnxModel.
        const string onnxPath = "Assets/ML/Models/BossBrain.onnx";
        var onnxAsset = AssetDatabase.LoadAssetAtPath<Unity.InferenceEngine.ModelAsset>(onnxPath);
        if (onnxAsset == null)
        {
            Debug.LogError(
                $"[SetupBoss] BossBrain.onnx not found at '{onnxPath}'. " +
                "Train and export the model first: python Assets/ML/Training/cyberboss_env.py. " +
                "Aborting setup — re-run this command once the model exists so it doesn't " +
                "silently report success on a boss that isn't actually wired to run RL inference.");
            return;
        }

        {
            var so = new SerializedObject(sentis);
            so.FindProperty("_onnxModel").objectReferenceValue = onnxAsset;
            so.ApplyModifiedProperties();
            Debug.Log("[SetupBoss] Assigned BossBrain.onnx to SentisInferenceManager.");
        }

        // Add BossRLAgent if missing.
        var rlAgent = bossGo.GetComponent<BossRLAgent>();
        if (rlAgent == null)
        {
            rlAgent = bossGo.AddComponent<BossRLAgent>();
            Debug.Log("[SetupBoss] Added BossRLAgent to Boss.");
        }

        // Wire SentisInferenceManager into BossRLAgent._sentisManager.
        {
            var so = new SerializedObject(rlAgent);
            so.FindProperty("_sentisManager").objectReferenceValue = sentis;
            so.ApplyModifiedProperties();
            Debug.Log("[SetupBoss] Wired SentisInferenceManager into BossRLAgent.");
        }

        // Wire BossRLAgent into BossController._bossRLAgent.
        var bossController = bossGo.GetComponent<BossController>();
        if (bossController != null)
        {
            var so = new SerializedObject(bossController);
            so.FindProperty("_bossRLAgent").objectReferenceValue = rlAgent;
            so.ApplyModifiedProperties();
            Debug.Log("[SetupBoss] Wired BossRLAgent into BossController.");
        }

        // Wire BossController into GameOverScreen._bossController.
        var gameOverScreen = Object.FindAnyObjectByType<GameOverScreen>();
        if (gameOverScreen != null && bossController != null)
        {
            var so = new SerializedObject(gameOverScreen);
            so.FindProperty("_bossController").objectReferenceValue = bossController;
            so.ApplyModifiedProperties();
            Debug.Log("[SetupBoss] Wired BossController into GameOverScreen.");
        }
        else if (gameOverScreen == null)
        {
            Debug.LogWarning(
                "[SetupBoss] GameOverScreen not found in scene — wire _bossController manually.");
        }

        EditorUtility.SetDirty(bossGo);
        EditorSceneManager.MarkAllScenesDirty();
        AssetDatabase.SaveAssets();

        Debug.Log(
            "[SetupBoss] RL Boss setup complete. " +
            "To activate: select Boss → BossRLAgent → tick 'Use RL Policy'.");
    }

    // ------------------------------------------------------------------
    // Setup Boss Skills
    // ------------------------------------------------------------------

    [MenuItem("CyberBoss/Setup Boss Skills")]
    public static void SetupBossSkillsInScene()
    {
        var bossGo = GameObject.FindWithTag(BossTag);
        if (bossGo == null)
        {
            Debug.LogError(
                "[SetupBoss] No GameObject tagged 'Boss' found. " +
                "Run 'CyberBoss/Setup Boss' first.");
            return;
        }

        var bossSkills = bossGo.GetComponent<BossSkills>();
        if (bossSkills == null)
        {
            Debug.LogError(
                "[SetupBoss] BossSkills component not found on Boss. " +
                "Run 'CyberBoss/Setup Boss' first.");
            return;
        }

        if (!AssetDatabase.IsValidFolder(SOFolder))
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");

        if (!AssetDatabase.IsValidFolder(PrefabFolder))
            AssetDatabase.CreateFolder("Assets", "Prefabs");

        var chargeConfig      = GetOrCreateSkillConfig<BossChargeConfig>(
            "BossSkillConfig_Charge");
        var burstConfig       = GetOrCreateSkillConfig<BossProjectileBurstConfig>(
            "BossSkillConfig_ProjectileBurst");
        var aoeSlamConfig     = GetOrCreateSkillConfig<BossAoESlamConfig>(
            "BossSkillConfig_AoeSlam");
        var shieldConfig      = GetOrCreateSkillConfig<BossShieldPhaseConfig>(
            "BossSkillConfig_ShieldPhase");
        var teleportConfig    = GetOrCreateSkillConfig<BossTeleportStrikeConfig>(
            "BossSkillConfig_TeleportStrike");

        var projectilePrefab  = GetOrCreateBossProjectilePrefab();

        var shieldReactiveConfig = GetOrCreateSkillConfig<BossShieldReactiveTriggerConfig>(
            "BossSkillConfig_ShieldReactiveTrigger");

        var shieldReactiveTrigger = bossGo.GetComponent<BossShieldReactiveTrigger>();
        if (shieldReactiveTrigger == null)
            shieldReactiveTrigger = bossGo.AddComponent<BossShieldReactiveTrigger>();

        var so = new SerializedObject(bossSkills);
        so.FindProperty("_chargeConfig").objectReferenceValue         = chargeConfig;
        so.FindProperty("_projectileBurstConfig").objectReferenceValue = burstConfig;
        so.FindProperty("_aoeSlamConfig").objectReferenceValue        = aoeSlamConfig;
        so.FindProperty("_shieldPhaseConfig").objectReferenceValue    = shieldConfig;
        so.FindProperty("_teleportStrikeConfig").objectReferenceValue = teleportConfig;
        so.FindProperty("_bossProjectilePrefab").objectReferenceValue = projectilePrefab;
        so.ApplyModifiedProperties();

        var triggerSo = new SerializedObject(shieldReactiveTrigger);
        triggerSo.FindProperty("_config").objectReferenceValue = shieldReactiveConfig;
        triggerSo.ApplyModifiedProperties();

        EditorUtility.SetDirty(bossSkills);
        EditorUtility.SetDirty(shieldReactiveTrigger);
        EditorSceneManager.MarkAllScenesDirty();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log(
            "[SetupBoss] Boss skill setup complete. " +
            "Five per-skill configs created in Assets/ScriptableObjects/. " +
            "Orange boss projectile prefab created in Assets/Prefabs/. " +
            "All assigned to BossSkills. " +
            "BossShieldReactiveTrigger added and its config assigned — ShieldPhase " +
            "now fires reactively on player attacks instead of being turn-selected. " +
            "Set TargetLayerMask on BossSkillConfig_AoeSlam and " +
            "BossSkillConfig_TeleportStrike to your Player layer.");
    }

    private static T GetOrCreateSkillConfig<T>(string assetName) where T : ScriptableObject
    {
        string path     = $"{SOFolder}/{assetName}.asset";
        var    existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            Debug.Log($"[SetupBoss] {assetName}.asset already exists — reusing.");
            return existing;
        }

        var config = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(config, path);
        EditorUtility.SetDirty(config);
        Debug.Log($"[SetupBoss] Created {assetName}.asset at '{path}'.");
        return config;
    }

    private static GameObject GetOrCreateBossProjectilePrefab()
    {
        const string prefabPath = PrefabFolder + "/BossProjectile.prefab";
        const string matPath    = PrefabFolder + "/Mat_Projectile_BossOrange.mat";

        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
        if (existing != null)
        {
            Debug.Log("[SetupBoss] BossProjectile.prefab already exists — reusing.");
            return existing;
        }

        // Create the orange material.
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        if (mat.shader == null || mat.shader.name == "Hidden/InternalErrorShader")
        {
            // Fallback — URP may not be active in this project configuration.
            mat = new Material(Shader.Find("Standard"));
            Debug.LogWarning(
                "[SetupBoss] URP Lit shader not found — falling back to Standard. " +
                "Re-run Setup Boss Skills after switching to URP.");
        }

        mat.SetColor("_BaseColor", new Color(1f, 0.35f, 0f)); // Orange
        mat.color = new Color(1f, 0.35f, 0f);                  // Standard shader compat
        AssetDatabase.CreateAsset(mat, matPath);

        // Build the prefab source in the scene then save it as an asset.
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "BossProjectile";

        // Scale to a small projectile shape — matches the player's cyan capsule size.
        go.transform.localScale = new Vector3(0.25f, 0.25f, 0.5f);

        // Set trigger collider.
        go.GetComponent<Collider>().isTrigger = true;

        // Apply orange material.
        go.GetComponent<Renderer>().sharedMaterial = mat;

        // Add behaviour that gives it velocity and handles hit callbacks.
        go.AddComponent<ProjectileBehavior>();

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabPath);
        Object.DestroyImmediate(go);

        Debug.Log(
            $"[SetupBoss] Created orange BossProjectile prefab at '{prefabPath}'.");
        return prefab;
    }

    // ------------------------------------------------------------------
    // Shared helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Add the tag to ProjectSettings/TagManager if it does not already exist.
    /// Unity throws on gameObject.tag assignment if the tag is not registered.
    /// </summary>
    private static void EnsureTagExists(string tag)
    {
        var tagManagerAssets = AssetDatabase.LoadAllAssetsAtPath(
            "ProjectSettings/TagManager.asset");

        if (tagManagerAssets == null || tagManagerAssets.Length == 0)
        {
            Debug.LogWarning(
                "[SetupBoss] Could not load TagManager.asset. " +
                $"Add the '{tag}' tag manually via Edit > Project Settings > Tags.");
            return;
        }

        var tagManager = new SerializedObject(tagManagerAssets[0]);
        var tagsProp   = tagManager.FindProperty("tags");

        for (int i = 0; i < tagsProp.arraySize; i++)
        {
            if (tagsProp.GetArrayElementAtIndex(i).stringValue == tag)
                return; // Already registered.
        }

        int newIndex = tagsProp.arraySize;
        tagsProp.InsertArrayElementAtIndex(newIndex);
        tagsProp.GetArrayElementAtIndex(newIndex).stringValue = tag;
        tagManager.ApplyModifiedProperties();
        Debug.Log($"[SetupBoss] Registered tag '{tag}' in TagManager.");
    }
}

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// One-shot editor setup for Player Skills.
/// Run via: CyberBoss / Setup Player Skills
///
/// What this does:
///   1. Creates 5 SkillConfig ScriptableObject assets in Assets/ScriptableObjects/
///   2. Creates a cyan capsule projectile prefab in Assets/Prefabs/
///   3. Adds the 5 Animator trigger parameters to the player's AnimatorController
///   4. Adds PlayerSkills to the player GameObject and assigns all references
/// </summary>
public class SetupPlayerSkills
{
    const string PlayerName   = "Muryotaisu";
    const string SOPath       = "Assets/ScriptableObjects";
    const string PrefabPath   = "Assets/Prefabs";
    const string ControllerSearchFolder = "Assets/Mryotaisu";

    [MenuItem("CyberBoss/Setup Player Skills")]
    public static void Execute()
    {
        EnsureFolders();
        CreateSkillConfigs(out var dashCfg, out var parryCfg, out var blastCfg,
                           out var strikeCfg, out var barrierCfg);
        var projectilePrefab = CreateProjectilePrefab();
        AddAnimatorTriggers();
        WirePlayerSkills(dashCfg, parryCfg, blastCfg, strikeCfg, barrierCfg, projectilePrefab);

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[SetupPlayerSkills] Setup complete.");
    }

    // ── 1. Folders ─────────────────────────────────────────────────────────

    static void EnsureFolders()
    {
        if (!AssetDatabase.IsValidFolder(SOPath))
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
        if (!AssetDatabase.IsValidFolder(PrefabPath))
            AssetDatabase.CreateFolder("Assets", "Prefabs");
    }

    // ── 2. ScriptableObject assets ────────────���────────────────────────────

    static void CreateSkillConfigs(
        out DashSkillConfig        dash,
        out ParrySkillConfig       parry,
        out RangedBlastSkillConfig blast,
        out BurstStrikeSkillConfig strike,
        out BarrierSkillConfig     barrier)
    {
        dash    = GetOrCreate<DashSkillConfig>       ("SkillConfig_Dash");
        parry   = GetOrCreate<ParrySkillConfig>      ("SkillConfig_Parry");
        blast   = GetOrCreate<RangedBlastSkillConfig>("SkillConfig_RangedBlast");
        strike  = GetOrCreate<BurstStrikeSkillConfig>("SkillConfig_BurstStrike");
        barrier = GetOrCreate<BarrierSkillConfig>    ("SkillConfig_Barrier");
    }

    static T GetOrCreate<T>(string assetName) where T : ScriptableObject
    {
        string path = $"{SOPath}/{assetName}.asset";
        var    existing = AssetDatabase.LoadAssetAtPath<T>(path);
        if (existing != null)
        {
            Debug.Log($"[SetupPlayerSkills] {assetName}.asset already exists — skipping.");
            return existing;
        }
        var asset = ScriptableObject.CreateInstance<T>();
        AssetDatabase.CreateAsset(asset, path);
        Debug.Log($"[SetupPlayerSkills] Created {path}");
        return asset;
    }

    // ── 3. Projectile prefab (cyan capsule, trigger collider) ──────────────

    static GameObject CreateProjectilePrefab()
    {
        string prefabAssetPath = $"{PrefabPath}/Projectile_RangedBlast.prefab";

        // Return existing prefab rather than overwriting inspector assignments.
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(prefabAssetPath);
        if (existing != null)
        {
            Debug.Log("[SetupPlayerSkills] Projectile prefab already exists — skipping.");
            return existing;
        }

        // Build hierarchy in scene then save as prefab.
        var go = GameObject.CreatePrimitive(PrimitiveType.Capsule);
        go.name = "Projectile_RangedBlast";
        go.transform.localScale = new Vector3(0.2f, 0.3f, 0.2f);

        // Cyan emissive material
        var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        mat.SetColor("_BaseColor",     new Color(0f, 1f, 1f, 1f));
        mat.SetColor("_EmissionColor", new Color(0f, 3f, 3f, 1f));
        mat.EnableKeyword("_EMISSION");
        AssetDatabase.CreateAsset(mat, $"{PrefabPath}/Mat_Projectile_Cyan.mat");
        go.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // Replace the sphere collider (added by CreatePrimitive) with capsule trigger.
        Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
        var col = go.AddComponent<CapsuleCollider>();
        col.isTrigger = true;
        col.height    = 0.6f;
        col.radius    = 0.1f;

        // Rigidbody: kinematic so physics doesn't fight with our manual movement.
        var rb = go.AddComponent<Rigidbody>();
        rb.isKinematic    = true;
        rb.useGravity     = false;

        go.AddComponent<ProjectileBehavior>();

        go.SetActive(false); // Pool starts with inactive objects.

        var prefab = PrefabUtility.SaveAsPrefabAsset(go, prefabAssetPath);
        Object.DestroyImmediate(go); // Remove temp scene object.

        Debug.Log($"[SetupPlayerSkills] Created {prefabAssetPath}");
        return prefab;
    }

    // ── 4. Add Animator trigger parameters ────────���────────────────────────

    static readonly string[] TriggerNames =
    {
        "DashTrigger", "ParryTrigger", "BlastTrigger", "StrikeTrigger", "BarrierTrigger"
    };

    static void AddAnimatorTriggers()
    {
        // Find the animator controller asset inside the Mryotaisu import folder.
        string[] guids = AssetDatabase.FindAssets("t:AnimatorController", new[] { ControllerSearchFolder });
        if (guids.Length == 0)
        {
            Debug.LogError("[SetupPlayerSkills] No AnimatorController found under " + ControllerSearchFolder);
            return;
        }

        string controllerPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
        if (controller == null)
        {
            Debug.LogError("[SetupPlayerSkills] Could not load AnimatorController at " + controllerPath);
            return;
        }

        foreach (string triggerName in TriggerNames)
        {
            if (HasParameter(controller, triggerName))
            {
                Debug.Log($"[SetupPlayerSkills] {triggerName} already exists — skipping.");
                continue;
            }
            controller.AddParameter(triggerName, AnimatorControllerParameterType.Trigger);
            Debug.Log($"[SetupPlayerSkills] Added Animator trigger: {triggerName}");
        }

        EditorUtility.SetDirty(controller);
    }

    static bool HasParameter(AnimatorController controller, string paramName)
    {
        foreach (var p in controller.parameters)
            if (p.name == paramName) return true;
        return false;
    }

    // ── 5. Add and wire PlayerSkills component ─────���───────────────────────

    static void WirePlayerSkills(
        DashSkillConfig        dashCfg,
        ParrySkillConfig       parryCfg,
        RangedBlastSkillConfig blastCfg,
        BurstStrikeSkillConfig strikeCfg,
        BarrierSkillConfig     barrierCfg,
        GameObject             projectilePrefab)
    {
        var playerGo = GameObject.Find(PlayerName);
        if (playerGo == null)
        {
            Debug.LogError($"[SetupPlayerSkills] '{PlayerName}' not found in scene. " +
                           "Run 'CyberBoss/Setup Player Character' first.");
            return;
        }

        // Add or retrieve PlayerSkills component.
        var skills = playerGo.GetComponent<PlayerSkills>();
        if (skills == null)
            skills = playerGo.AddComponent<PlayerSkills>();

        // Assign serialized fields via SerializedObject so Unity records the change.
        var so = new SerializedObject(skills);
        so.FindProperty("_dashConfig")        .objectReferenceValue = dashCfg;
        so.FindProperty("_parryConfig")       .objectReferenceValue = parryCfg;
        so.FindProperty("_rangedBlastConfig") .objectReferenceValue = blastCfg;
        so.FindProperty("_burstStrikeConfig") .objectReferenceValue = strikeCfg;
        so.FindProperty("_barrierConfig")     .objectReferenceValue = barrierCfg;
        so.FindProperty("_projectilePrefab")  .objectReferenceValue = projectilePrefab;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(playerGo);
        Debug.Log($"[SetupPlayerSkills] PlayerSkills wired on '{PlayerName}'.");
    }
}

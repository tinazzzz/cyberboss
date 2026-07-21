using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Adds PlayerCombatEffects to the player GameObject and wires its config
/// references (_parryConfig / _barrierConfig — needed to source the Parry
/// spark ring's white-gold recolor and the Barrier shield's 3 end-state
/// profiles). Run once — idempotent (skips if component/fields already set).
/// </summary>
public static class SetupPlayerCombatEffects
{
    private const string SOFolder = "Assets/ScriptableObjects";

    [MenuItem("CyberBoss/Setup Player Combat Effects")]
    public static void Run()
    {
        var player = GameObject.FindWithTag("Player");
        if (player == null)
        {
            Debug.LogError("[SetupPlayerCombatEffects] No GameObject tagged 'Player' found. " +
                "Tag the player and re-run.");
            return;
        }

        var effects = player.GetComponent<PlayerCombatEffects>();
        if (effects == null)
        {
            effects = player.AddComponent<PlayerCombatEffects>();
            Debug.Log($"[SetupPlayerCombatEffects] Added PlayerCombatEffects to '{player.name}'.");
        }
        else
        {
            Debug.Log($"[SetupPlayerCombatEffects] PlayerCombatEffects already present on '{player.name}'.");
        }

        WireConfigs(effects);

        EditorUtility.SetDirty(player);
        var scene = SceneManager.GetActiveScene();
        UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

        Debug.Log("[SetupPlayerCombatEffects] Done. Save the scene (Ctrl+S) to persist.");
    }

    /// <summary>
    /// Check-before-set: only assigns a config reference if it isn't already
    /// wired, so re-running never clobbers a manual reassignment in the Inspector.
    /// </summary>
    private static void WireConfigs(PlayerCombatEffects effects)
    {
        var so = new SerializedObject(effects);

        var parryProp = so.FindProperty("_parryConfig");
        if (parryProp.objectReferenceValue == null)
        {
            var parryConfig = AssetDatabase.LoadAssetAtPath<ParrySkillConfig>(
                $"{SOFolder}/SkillConfig_Parry.asset");
            if (parryConfig != null)
                parryProp.objectReferenceValue = parryConfig;
            else
                Debug.LogWarning("[SetupPlayerCombatEffects] SkillConfig_Parry.asset not found — " +
                    "run CyberBoss/Setup Player Skills first, then re-run this command.");
        }

        var barrierProp = so.FindProperty("_barrierConfig");
        if (barrierProp.objectReferenceValue == null)
        {
            var barrierConfig = AssetDatabase.LoadAssetAtPath<BarrierSkillConfig>(
                $"{SOFolder}/SkillConfig_Barrier.asset");
            if (barrierConfig != null)
                barrierProp.objectReferenceValue = barrierConfig;
            else
                Debug.LogWarning("[SetupPlayerCombatEffects] SkillConfig_Barrier.asset not found — " +
                    "run CyberBoss/Setup Player Skills first, then re-run this command.");
        }

        so.ApplyModifiedProperties();
    }
}

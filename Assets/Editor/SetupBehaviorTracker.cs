using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>
/// One-shot editor setup for the Player Behavior Tracker.
/// Run via: CyberBoss / Setup Behavior Tracker
///
/// Actions (all idempotent — safe to run multiple times):
///   1. Finds the Player GameObject by tag "Player"
///   2. Adds PlayerBehaviorTracker if not present
///   3. Creates BehaviorTrackerConfig in Assets/ScriptableObjects/ if not present
///   4. Assigns the config to the tracker component via SerializedObject
///
/// Prerequisite: Run 'CyberBoss/Setup Player Character' first — that script tags
/// the player GameObject 'Player', which this tool requires to find it.
/// </summary>
public static class SetupBehaviorTracker
{
    private const string SOPath    = "Assets/ScriptableObjects";
    private const string AssetName = "BehaviorTrackerConfig";

    [MenuItem("CyberBoss/Setup Behavior Tracker")]
    public static void Execute()
    {
        var playerGo = GameObject.FindWithTag("Player");
        if (playerGo == null)
        {
            Debug.LogError(
                "[SetupBehaviorTracker] No GameObject tagged 'Player' found in scene. " +
                "Run 'CyberBoss/Setup Player Character' first — it tags the player.");
            return;
        }

        EnsureFolder();
        var config = GetOrCreateConfig();
        WireTracker(playerGo, config);

        EditorSceneManager.MarkAllScenesDirty();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log($"[SetupBehaviorTracker] Setup complete on '{playerGo.name}'.");
    }

    private static void EnsureFolder()
    {
        if (!AssetDatabase.IsValidFolder(SOPath))
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");
    }

    private static BehaviorTrackerConfig GetOrCreateConfig()
    {
        string assetPath = $"{SOPath}/{AssetName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<BehaviorTrackerConfig>(assetPath);
        if (existing != null)
        {
            Debug.Log($"[SetupBehaviorTracker] {AssetName}.asset already exists — reusing.");
            return existing;
        }

        var config = ScriptableObject.CreateInstance<BehaviorTrackerConfig>();
        AssetDatabase.CreateAsset(config, assetPath);
        Debug.Log($"[SetupBehaviorTracker] Created {assetPath}.");
        return config;
    }

    private static void WireTracker(GameObject playerGo, BehaviorTrackerConfig config)
    {
        var tracker = playerGo.GetComponent<PlayerBehaviorTracker>();
        if (tracker == null)
        {
            tracker = playerGo.AddComponent<PlayerBehaviorTracker>();
            Debug.Log(
                $"[SetupBehaviorTracker] Added PlayerBehaviorTracker to '{playerGo.name}'.");
        }

        var so = new SerializedObject(tracker);
        so.FindProperty("_config").objectReferenceValue = config;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(playerGo);
        Debug.Log(
            $"[SetupBehaviorTracker] BehaviorTrackerConfig assigned on '{playerGo.name}'.");
    }
}

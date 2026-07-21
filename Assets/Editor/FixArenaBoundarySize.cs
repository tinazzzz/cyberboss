using UnityEditor;
using UnityEngine;

public class FixArenaBoundarySize
{
    [MenuItem("CyberBoss/Fix Arena Boundary Size")]
    public static void Execute()
    {
        const string path = "Assets/ScriptableObjects/BehaviorTrackerConfig.asset";
        var config = AssetDatabase.LoadAssetAtPath<BehaviorTrackerConfig>(path);
        if (config == null) { Debug.LogError("[FixArenaBoundarySize] BehaviorTrackerConfig not found."); return; }

        var so = new SerializedObject(config);
        // Outermost grid line is at ±7 — 4 squares × 3.5 units each.
        so.FindProperty("_arenaRadius").floatValue = 7f;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(config);
        AssetDatabase.SaveAssets();
        Debug.Log("[FixArenaBoundarySize] ArenaRadius 10 → 7 (4×4 grid squares). Saved.");
    }
}

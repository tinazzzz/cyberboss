using UnityEngine;
using UnityEditor;

public class FixDashDistance
{
    [MenuItem("CyberBoss/Fix Dash Config")]
    public static void Execute()
    {
        var cfg = AssetDatabase.LoadAssetAtPath<DashSkillConfig>(
            "Assets/ScriptableObjects/SkillConfig_Dash.asset");
        if (cfg == null) { Debug.LogError("[FixDash] SkillConfig_Dash.asset not found."); return; }

        var so = new SerializedObject(cfg);
        so.FindProperty("_dashDistance").floatValue = 3.0f;
        so.FindProperty("_cooldown").floatValue = 0.5f;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(cfg);
        AssetDatabase.SaveAssets();
        Debug.Log("[FixDash] DashDistance=3.0, Cooldown=0.5s");
    }
}

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class FixArenaBoundary
{
    [MenuItem("CyberBoss/Fix Arena Boundary")]
    public static void Execute()
    {
        const string configPath = "Assets/ScriptableObjects/BehaviorTrackerConfig.asset";
        var config = AssetDatabase.LoadAssetAtPath<BehaviorTrackerConfig>(configPath);
        if (config == null)
        {
            Debug.LogError($"[FixArenaBoundary] BehaviorTrackerConfig not found at '{configPath}'.");
            return;
        }

        int fixed_ = 0;

        // ── PlayerController ─────────────────────────────────────────────
        var playerGo = GameObject.Find("Muryotaisu");
        if (playerGo != null)
        {
            var pc = playerGo.GetComponent<PlayerController>();
            if (pc != null)
            {
                var so = new SerializedObject(pc);
                so.FindProperty("_trackerConfig").objectReferenceValue = config;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(playerGo);
                Debug.Log("[FixArenaBoundary] PlayerController._trackerConfig → BehaviorTrackerConfig.");
                fixed_++;
            }
            else Debug.LogWarning("[FixArenaBoundary] PlayerController not found on Muryotaisu.");
        }
        else Debug.LogError("[FixArenaBoundary] Muryotaisu not found.");

        // ── BossController ───────────────────────────────────────────────
        var bossGo = GameObject.Find("Boss");
        if (bossGo != null)
        {
            var bc = bossGo.GetComponent<BossController>();
            if (bc != null)
            {
                var so = new SerializedObject(bc);
                so.FindProperty("_trackerConfig").objectReferenceValue = config;
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(bossGo);
                Debug.Log("[FixArenaBoundary] BossController._trackerConfig → BehaviorTrackerConfig.");
                fixed_++;
            }
            else Debug.LogWarning("[FixArenaBoundary] BossController not found on Boss.");
        }
        else Debug.LogError("[FixArenaBoundary] Boss not found.");

        if (fixed_ > 0)
        {
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"[FixArenaBoundary] Done — {fixed_} component(s) rewired. Scene saved.");
        }
    }
}

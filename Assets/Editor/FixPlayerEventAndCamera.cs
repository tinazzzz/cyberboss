using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using UnityEditor.SceneManagement;

public class FixPlayerEventAndCamera
{
    [MenuItem("CyberBoss/Fix Player Hit Event + Camera Distance")]
    public static void Execute()
    {
        // ── 1. Add AnimationEventReceiver to Muryotaisu ──────────────────
        var playerGo = GameObject.Find("Muryotaisu");
        if (playerGo != null)
        {
            if (playerGo.GetComponent<AnimationEventReceiver>() == null)
            {
                playerGo.AddComponent<AnimationEventReceiver>();
                EditorUtility.SetDirty(playerGo);
                Debug.Log("[Fix] Added AnimationEventReceiver to Muryotaisu — Hit event silenced.");
            }
            else
            {
                Debug.Log("[Fix] AnimationEventReceiver already on Muryotaisu.");
            }
        }
        else Debug.LogError("[Fix] Muryotaisu not found.");

        // ── 2. Pull camera further back (scale offset ~1.35x) ────────────
        // Original: (-4.81, 7.33, -7.59) — distance ~11.6 units
        // New:      (-6.50, 9.90, -10.24) — distance ~15.7 units, same angle
        var vcamGo = GameObject.Find("CM_IsometricCamera");
        if (vcamGo != null)
        {
            var follow = vcamGo.GetComponent<CinemachineFollow>();
            if (follow != null)
            {
                follow.FollowOffset = new Vector3(-6.5f, 9.9f, -10.24f);
                EditorUtility.SetDirty(vcamGo);
                Debug.Log("[Fix] Camera offset updated to (-6.5, 9.9, -10.24).");
            }
            else Debug.LogError("[Fix] CinemachineFollow not found on CM_IsometricCamera.");
        }
        else Debug.LogError("[Fix] CM_IsometricCamera not found.");

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[Fix] Done. Scene saved.");
    }
}

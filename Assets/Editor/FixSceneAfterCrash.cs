using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using UnityEditor.SceneManagement;

/// One-shot repair for the three regressions caused by closing Unity without saving:
///   1. CM_IsometricCamera.Follow lost its Muryotaisu reference → camera static
///   2. AnimationEventReceiver missing from Boss → FootR/FootL console spam
///   3. Duplicate AudioListener on the orphan "Camera" GameObject
///   4. Apply Root Motion disabled on both Animators (fights CharacterController/BossController)
public class FixSceneAfterCrash
{
    [MenuItem("CyberBoss/Fix Scene After Crash")]
    public static void Execute()
    {
        var playerGo = GameObject.Find("Muryotaisu");
        var bossGo   = GameObject.Find("Boss");
        var vcamGo   = GameObject.Find("CM_IsometricCamera");

        // ── 1. Camera Follow ──────────────────────────────────────────────
        if (vcamGo != null && playerGo != null)
        {
            var vcam = vcamGo.GetComponent<CinemachineCamera>();
            if (vcam != null)
            {
                vcam.Follow = playerGo.transform;
                EditorUtility.SetDirty(vcamGo);
                Debug.Log("[FixScene] CM_IsometricCamera.Follow → Muryotaisu.");
            }
            else Debug.LogWarning("[FixScene] CinemachineCamera not found on CM_IsometricCamera.");
        }
        else Debug.LogWarning("[FixScene] CM_IsometricCamera or Muryotaisu not found in scene.");

        // ── 2. AnimationEventReceiver on Boss ─────────────────────────────
        if (bossGo != null)
        {
            if (bossGo.GetComponent<AnimationEventReceiver>() == null)
            {
                bossGo.AddComponent<AnimationEventReceiver>();
                EditorUtility.SetDirty(bossGo);
                Debug.Log("[FixScene] Added AnimationEventReceiver to Boss — FootR/FootL silenced.");
            }
            else Debug.Log("[FixScene] AnimationEventReceiver already present on Boss.");

            var bossAnim = bossGo.GetComponent<Animator>();
            if (bossAnim != null && bossAnim.applyRootMotion)
            {
                bossAnim.applyRootMotion = false;
                EditorUtility.SetDirty(bossGo);
                Debug.Log("[FixScene] Disabled Apply Root Motion on Boss Animator.");
            }
        }
        else Debug.LogWarning("[FixScene] Boss GameObject not found.");

        // ── 3. Apply Root Motion off on Player (fights CharacterController) ──
        if (playerGo != null)
        {
            var playerAnim = playerGo.GetComponent<Animator>();
            if (playerAnim != null && playerAnim.applyRootMotion)
            {
                playerAnim.applyRootMotion = false;
                EditorUtility.SetDirty(playerGo);
                Debug.Log("[FixScene] Disabled Apply Root Motion on Muryotaisu Animator.");
            }
        }

        // ── 4. Remove duplicate AudioListener ────────────────────────────
        var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);
        int removed = 0;
        foreach (var al in listeners)
        {
            if (!al.gameObject.CompareTag("MainCamera"))
            {
                Debug.Log($"[FixScene] Removing AudioListener from '{al.gameObject.name}'.");
                Undo.DestroyObjectImmediate(al);
                removed++;
            }
        }
        if (removed == 0) Debug.Log("[FixScene] No duplicate AudioListeners found.");

        // ── Save ──────────────────────────────────────────────────────────
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[FixScene] Scene saved. All crash regressions repaired.");
    }
}

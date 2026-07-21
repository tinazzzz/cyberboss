using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;
using Unity.Cinemachine.TargetTracking;
using UnityEditor.SceneManagement;

/// Restores the isometric follow camera lost when Unity closed without saving.
/// CinemachineCamera alone never moves — it needs a CinemachineFollow body component
/// to physically track the player. Without it, setting .Follow is a no-op at runtime.
///
/// Camera values (approved isometric position):
///   Position offset: (-4.806611, 7.33482265, -7.588046) from player
///   Rotation: (31.13, 36.53, 0)
///   BindingMode: WorldSpace (camera stays at world-space offset from player)
///   PositionDamping: (0, 0, 0) — instant follow, no lag
public class FixCameraFollow
{
    [MenuItem("CyberBoss/Fix Camera Follow")]
    public static void Execute()
    {
        var vcamGo   = GameObject.Find("CM_IsometricCamera");
        var playerGo = GameObject.Find("Muryotaisu");

        if (vcamGo == null)   { Debug.LogError("[FixCameraFollow] CM_IsometricCamera not found."); return; }
        if (playerGo == null) { Debug.LogError("[FixCameraFollow] Muryotaisu not found."); return; }

        // Set Follow target on CinemachineCamera
        var vcam = vcamGo.GetComponent<CinemachineCamera>();
        if (vcam == null) { Debug.LogError("[FixCameraFollow] CinemachineCamera missing on CM_IsometricCamera."); return; }
        vcam.Follow = playerGo.transform;

        // Remove any stale body components before adding fresh ones
        var oldFollow   = vcamGo.GetComponent<CinemachineFollow>();
        var oldComposer = vcamGo.GetComponent<CinemachinePositionComposer>();
        if (oldFollow   != null) Object.DestroyImmediate(oldFollow);
        if (oldComposer != null) Object.DestroyImmediate(oldComposer);

        // Add CinemachineFollow with world-space fixed offset from player.
        // WorldSpace binding: camera = player.position + FollowOffset regardless of player rotation.
        // Zero damping: camera snaps to offset instantly each frame.
        var follow = vcamGo.AddComponent<CinemachineFollow>();
        follow.FollowOffset = new Vector3(-4.806611f, 7.33482265f, -7.588046f);

        var tracker = follow.TrackerSettings;
        tracker.BindingMode    = BindingMode.WorldSpace;
        tracker.PositionDamping = Vector3.zero;
        follow.TrackerSettings = tracker;

        // Restore approved isometric rotation
        vcamGo.transform.rotation = Quaternion.Euler(31.13f, 36.53f, 0f);

        EditorUtility.SetDirty(vcamGo);

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log("[FixCameraFollow] Done — CinemachineFollow added. " +
                  "World-space offset (-4.8, 7.3, -7.6), rotation (31, 36, 0). " +
                  "Camera will now follow Muryotaisu.");
    }
}

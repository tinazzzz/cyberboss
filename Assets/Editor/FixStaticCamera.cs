using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Fixes the static isometric camera and missing AudioListener.
///
/// Root causes found via scene inspection:
///   1. "Camera" GameObject has Camera component at depth 0.
///      "Main Camera" (CinemachineBrain) is at depth -1.
///      Unity renders higher-depth cameras on top, so the "Camera" GO's
///      static view paints over everything Cinemachine outputs.
///      Fix: disable the "Camera" GameObject (preserves it for inspection).
///
///   2. No AudioListener exists anywhere in the scene.
///      Fix: add one to Main Camera.
///
/// Everything else is correct in the scene:
///   - CM_IsometricCamera: Priority 10, TrackingTarget = Muryotaisu root transform
///   - CinemachineFollow: BindingMode=WorldSpace, Offset=(-4.8, 7.3, -7.6), Damping=0
///   - CinemachineBrain ChannelMask=-1 correctly receives OutputChannel=1
public static class FixStaticCamera
{
    [MenuItem("CyberBoss/Fix Static Camera (Depth + AudioListener)")]
    public static void Execute()
    {
        bool anyChange = false;

        anyChange |= DisableRogueCamera();
        anyChange |= EnsureAudioListener();

        if (!anyChange)
        {
            Debug.Log("[FixStaticCamera] Nothing to change — scene already correct.");
            return;
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[FixStaticCamera] Scene saved.");
    }

    // Disables the stray "Camera" GameObject whose Camera component at depth 0
    // renders on top of the Cinemachine-controlled Main Camera (depth -1),
    // producing the static view the user sees.
    private static bool DisableRogueCamera()
    {
        var rogueGo = GameObject.Find("Camera");
        if (rogueGo == null)
        {
            Debug.Log("[FixStaticCamera] 'Camera' GameObject not found — already removed or renamed.");
            return false;
        }

        var cam = rogueGo.GetComponent<Camera>();
        if (cam == null)
        {
            Debug.Log("[FixStaticCamera] 'Camera' GameObject has no Camera component — no depth conflict.");
            return false;
        }

        if (!rogueGo.activeSelf)
        {
            Debug.Log("[FixStaticCamera] 'Camera' GameObject already inactive — no change needed.");
            return false;
        }

        rogueGo.SetActive(false);
        EditorUtility.SetDirty(rogueGo);

        Debug.Log("[FixStaticCamera] Disabled 'Camera' GameObject. " +
                  "It had Camera depth=0 rendering over Main Camera depth=-1. " +
                  "Object is preserved inactive for inspection — delete manually if not needed.");
        return true;
    }

    // Adds an AudioListener to Main Camera if none exists in the scene.
    // Unity requires exactly one AudioListener; it belongs on the Cinemachine
    // output camera (Main Camera tagged MainCamera) so audio positioning
    // matches the rendered viewpoint.
    private static bool EnsureAudioListener()
    {
        // Check for existing AudioListener anywhere in the scene first.
        var existing = Object.FindAnyObjectByType<AudioListener>();
        if (existing != null)
        {
            Debug.Log($"[FixStaticCamera] AudioListener already exists on '{existing.gameObject.name}' — no change.");
            return false;
        }

        var mainCamGo = GameObject.FindWithTag("MainCamera");
        if (mainCamGo == null)
        {
            Debug.LogError("[FixStaticCamera] No GameObject tagged 'MainCamera' found. " +
                           "AudioListener not added — add it manually.");
            return false;
        }

        mainCamGo.AddComponent<AudioListener>();
        EditorUtility.SetDirty(mainCamGo);

        Debug.Log($"[FixStaticCamera] Added AudioListener to '{mainCamGo.name}' (tagged MainCamera).");
        return true;
    }
}

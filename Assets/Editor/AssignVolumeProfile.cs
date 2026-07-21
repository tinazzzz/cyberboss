using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering;

/// Assigns CyberArenaVolumeProfile.asset to the PostProcessVolume in the scene.
/// Without this, no post-processing effects (bloom, tonemapping, etc.) are active.
public class AssignVolumeProfile
{
    [MenuItem("CyberBoss/Assign Volume Profile")]
    public static void Execute()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
            "Assets/Settings/CyberArenaVolumeProfile.asset");

        if (profile == null)
        {
            Debug.LogError("[CyberBoss] VolumeProfile not found at Assets/Settings/CyberArenaVolumeProfile.asset");
            return;
        }

        var volumeGo = GameObject.Find("PostProcessVolume");
        if (volumeGo == null)
        {
            Debug.LogError("[CyberBoss] PostProcessVolume GameObject not found in scene.");
            return;
        }

        var volume = volumeGo.GetComponent<Volume>();
        if (volume == null)
        {
            Debug.LogError("[CyberBoss] No Volume component on PostProcessVolume.");
            return;
        }

        volume.sharedProfile = profile;
        volume.isGlobal      = true;
        volume.weight        = 1f;
        EditorUtility.SetDirty(volumeGo);

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");

        Debug.Log($"[CyberBoss] Volume profile '{profile.name}' assigned to PostProcessVolume. Post-processing now active.");
    }
}

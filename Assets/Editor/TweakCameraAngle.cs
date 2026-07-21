using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

public class TweakCameraAngle
{
    [MenuItem("CyberBoss/Tweak Camera Angle")]
    public static void Execute()
    {
        var vcamGo = GameObject.Find("CM_IsometricCamera");
        if (vcamGo == null) { Debug.LogError("[TweakCamera] CM_IsometricCamera not found."); return; }

        // Was (31.13, 36.53, 0) — reduce X pitch to look more toward horizon
        vcamGo.transform.rotation = Quaternion.Euler(25f, 36.53f, 0f);

        EditorUtility.SetDirty(vcamGo);
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[TweakCamera] Pitch: 22° → 25°. Scene saved.");
    }
}

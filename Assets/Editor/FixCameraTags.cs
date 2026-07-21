using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Restores correct camera tags.
/// Camera.main requires MainCamera tag + an actual Camera component.
/// CM_IsometricCamera has no Camera component so must stay Untagged.
public class FixCameraTags
{
    [MenuItem("CyberBoss/Fix Camera Tags")]
    public static void Execute()
    {
        var mainCam = GameObject.Find("Main Camera");
        var vcamGo  = GameObject.Find("CM_IsometricCamera");

        if (mainCam != null)
        {
            mainCam.tag = "MainCamera";
            EditorUtility.SetDirty(mainCam);
            Debug.Log("[FixCameraTags] Main Camera → tagged MainCamera.");
        }

        if (vcamGo != null)
        {
            vcamGo.tag = "Untagged";
            EditorUtility.SetDirty(vcamGo);
            Debug.Log("[FixCameraTags] CM_IsometricCamera → tagged Untagged.");
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[FixCameraTags] Done. Camera.main will now resolve correctly.");
    }
}

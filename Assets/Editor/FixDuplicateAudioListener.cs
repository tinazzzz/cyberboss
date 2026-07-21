using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Removes every AudioListener that is NOT on the MainCamera-tagged GameObject,
/// leaving exactly one listener in the scene.
public class FixDuplicateAudioListener
{
    [MenuItem("CyberBoss/Fix Duplicate Audio Listener")]
    public static void Execute()
    {
        var listeners = Object.FindObjectsByType<AudioListener>(FindObjectsInactive.Exclude);

        if (listeners.Length <= 1)
        {
            Debug.Log($"[CyberBoss] AudioListener check: {listeners.Length} found — nothing to fix.");
            return;
        }

        int removed = 0;
        foreach (var listener in listeners)
        {
            bool isOnMainCamera = listener.gameObject.CompareTag("MainCamera");
            if (!isOnMainCamera)
            {
                Debug.Log($"[CyberBoss] Removing AudioListener from '{listener.gameObject.name}' " +
                          $"(tag: '{listener.gameObject.tag}').");
                Undo.DestroyObjectImmediate(listener);
                removed++;
            }
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);

        Debug.Log($"[CyberBoss] Removed {removed} duplicate AudioListener(s). " +
                  "Exactly one AudioListener remains on Main Camera.");
    }
}

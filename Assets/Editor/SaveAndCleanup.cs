using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public class SaveAndCleanup
{
    public static void Execute()
    {
        var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
        bool saved = EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        if (!saved)
        {
            Debug.LogError("[CyberBoss] Failed to save scene to Assets/Scenes/CyberArena.unity");
            return;
        }

        // Remove the spurious root-level duplicate that save_scene MCP creates
        if (AssetDatabase.AssetPathExists("Assets/CyberArena.unity"))
        {
            AssetDatabase.DeleteAsset("Assets/CyberArena.unity");
            Debug.Log("[CyberBoss] Deleted duplicate Assets/CyberArena.unity");
        }

        AssetDatabase.Refresh();
        Debug.Log("[CyberBoss] Scene saved cleanly to Assets/Scenes/CyberArena.unity");
    }
}

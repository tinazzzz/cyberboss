using UnityEditor;

public class MoveSceneToScenes
{
    public static void Execute()
    {
        // Delete the empty placeholder at Assets/Scenes/CyberArena.unity (created by create_scene),
        // then move the populated root-level copy into the correct location.
        AssetDatabase.DeleteAsset("Assets/Scenes/CyberArena.unity");
        string error = AssetDatabase.MoveAsset("Assets/CyberArena.unity", "Assets/Scenes/CyberArena.unity");
        if (string.IsNullOrEmpty(error))
            UnityEngine.Debug.Log("[CyberBoss] Scene moved to Assets/Scenes/CyberArena.unity.");
        else
            UnityEngine.Debug.LogError("[CyberBoss] Move failed: " + error);
    }
}

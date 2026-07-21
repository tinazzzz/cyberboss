using UnityEngine;
using UnityEditor;

public class AlignSceneViewToCamera
{
    public static void Execute()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) { Debug.LogError("No active SceneView"); return; }

        // Match CM_IsometricCamera: position (18,20,-18), rotation Euler(35,45,0)
        sv.pivot    = new Vector3(0f, 2f, 0f);     // look-at point (arena centre, slightly above ground)
        sv.rotation = Quaternion.Euler(35f, 45f, 0f);
        sv.size     = 20f;                          // wide enough to see full arena + buildings
        sv.orthographic = false;
        sv.Repaint();
        Debug.Log("[CyberBoss] Scene View aligned to isometric camera.");
    }
}

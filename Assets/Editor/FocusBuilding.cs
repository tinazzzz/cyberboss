using UnityEngine;
using UnityEditor;

public class FocusBuilding
{
    [MenuItem("CyberBoss/Focus Scene View on FBX Building")]
    public static void Execute()
    {
        var sv = SceneView.lastActiveSceneView;
        if (sv == null) return;

        // Look at building from the SE-above direction matching the game camera
        // Building center (-7.6, 3.2, 2.65) — look from SE-above
        var target = new Vector3(-7.6f, 3.2f, 2.65f);
        var camPos = new Vector3(-7.6f + 6f, 3.2f + 8f, 2.65f - 6f);
        sv.pivot    = target;
        sv.rotation = Quaternion.LookRotation(target - camPos, Vector3.up);
        sv.size     = 5f;
        sv.orthographic = false;
        sv.Repaint();
    }
}

using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;

/// Fixes the CM_IsometricCamera pointing direction.
/// Previous bug: Euler(48, +45, 0) from position (20,28,-20) projects forward to (38,0,-2)
/// which is completely off the arena. Using LookAt to compute the correct rotation.
public class FixCameraAngle
{
    [MenuItem("CyberBoss/Fix Camera Angle")]
    public static void Execute()
    {
        // Camera sits south-east-above the arena and looks toward the north-west center.
        // LookAt from this position gives Euler ≈ (45, 315, 0) — Y must be negative 45.
        var camPos = new Vector3(18f, 24f, -18f);
        var lookAt = new Vector3(0f, 0f, 0f);   // arena centre

        var cmGo = GameObject.Find("CM_IsometricCamera");
        if (cmGo == null) { Debug.LogError("[CyberBoss] CM_IsometricCamera not found."); return; }

        cmGo.transform.position = camPos;
        cmGo.transform.rotation = Quaternion.LookRotation(lookAt - camPos, Vector3.up);

        var vcam = cmGo.GetComponent<CinemachineCamera>();
        if (vcam != null)
        {
            vcam.Lens.FieldOfView = 40f;
            vcam.Priority         = 10;
        }

        // Update the Main Camera to match immediately (Cinemachine does this at runtime)
        var mainCam = Camera.main;
        if (mainCam != null)
        {
            mainCam.transform.position = camPos;
            mainCam.transform.rotation = cmGo.transform.rotation;
        }

        // Align scene view to match the game camera exactly
        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            // Scene view: rotation = what direction you look FROM the pivot.
            // To see the same view as the game camera (at camPos looking at origin),
            // the scene view camera should sit at camPos relative to pivot.
            // sv.rotation = the inverse of the game camera's facing, effectively:
            // looking FROM camPos direction = opposite of game camera forward.
            // Simplest: just use the same rotation — SceneView handles framing from pivot.
            sv.pivot      = new Vector3(0f, 1f, 0f);
            sv.rotation   = cmGo.transform.rotation;
            sv.size       = 22f;
            sv.orthographic = false;
            sv.Repaint();
        }

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");

        var euler = cmGo.transform.rotation.eulerAngles;
        Debug.Log($"[CyberBoss] Camera fixed. Position: {camPos}, Euler: {euler}. " +
                  $"Now looking from SE-above toward arena centre.");
    }
}

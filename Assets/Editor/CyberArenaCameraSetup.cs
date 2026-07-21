using UnityEngine;
using UnityEditor;
using Unity.Cinemachine;

public class CyberArenaCameraSetup
{
    public static void Execute()
    {
        // Configure the existing Main Camera for URP
        var mainCam = GameObject.Find("Main Camera");
        if (mainCam == null)
        {
            mainCam = new GameObject("Main Camera");
            mainCam.AddComponent<Camera>();
            mainCam.tag = "MainCamera";
        }
        var cam = mainCam.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = new Color(0.02f, 0.02f, 0.04f, 1f);

        // CinemachineBrain is required in Cinemachine 3.x — it is the bridge that lets
        // CinemachineCamera instances drive the Unity Camera.  Without it the virtual
        // camera has no effect at runtime.
        if (mainCam.GetComponent<CinemachineBrain>() == null)
            mainCam.AddComponent<CinemachineBrain>();

        // Position the main camera away from the action (Cinemachine drives it)
        mainCam.transform.position = new Vector3(0, 20, -15);
        mainCam.transform.rotation = Quaternion.Euler(50f, 0f, 0f);

        // Create CinemachineCamera (Cinemachine 3.x uses CinemachineCamera)
        var vcamGo = new GameObject("CM_IsometricCamera");
        var vcam = vcamGo.AddComponent<CinemachineCamera>();

        // Isometric angle: 45° yaw, ~54.7° pitch (true isometric) — use 55° for visual clarity
        vcamGo.transform.position = new Vector3(0, 18, -14);
        vcamGo.transform.rotation = Quaternion.Euler(52f, 0f, 0f);

        // Lens settings — perspective gives better depth cues for a boss fight
        vcam.Lens.FieldOfView = 40f;
        vcam.Lens.NearClipPlane = 0.3f;
        vcam.Lens.FarClipPlane = 100f;

        // Priority so it drives the main camera
        vcam.Priority = 10;

        // No Follow/LookAt — fixed isometric position; player centering handled by PlayerController
        vcam.Follow = null;
        vcam.LookAt = null;

        Debug.Log("[CyberBoss] Cinemachine isometric camera created at CM_IsometricCamera.");

        EditorUtility.SetDirty(vcamGo);
    }
}

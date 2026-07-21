using UnityEngine;
using UnityEditor;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Diagnoses the most common causes of a black game view in URP.
/// Checks: URP renderer post-processing flag, camera HDR, volume profile assignment.
/// Also fixes any issues it finds and reports results.
public class DiagnoseGameView
{
    [MenuItem("CyberBoss/Diagnose & Fix Game View")]
    public static void Execute()
    {
        bool anyFix = false;

        // ── 1. URP Renderer Asset ─────────────────────────────────────────
        // URP requires Post Processing to be enabled on the renderer asset itself,
        // not just on the camera. This is the #1 silent cause of a black game view.
        var urpAsset = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        if (urpAsset == null)
        {
            Debug.LogError("[Diag] No URP asset assigned in Graphics Settings!");
        }
        else
        {
            Debug.Log($"[Diag] URP asset: {urpAsset.name}, HDR: {urpAsset.supportsHDR}");

            // The renderer list is internal; access via serialized object
            var so = new SerializedObject(urpAsset);
            so.Update();

            // Check HDR support on asset level
            if (!urpAsset.supportsHDR)
            {
                Debug.LogWarning("[Diag] URP asset HDR is OFF — enabling it.");
                urpAsset.supportsHDR = true;
                EditorUtility.SetDirty(urpAsset);
                anyFix = true;
            }
        }

        // ── 2. Main Camera ────────────────────────────────────────────────
        var mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("[Diag] No Main Camera in scene!");
        }
        else
        {
            Debug.Log($"[Diag] Main Camera: HDR={mainCam.allowHDR}, " +
                      $"ClearFlags={mainCam.clearFlags}, BG={mainCam.backgroundColor}");

            var urpData = mainCam.GetComponent<UniversalAdditionalCameraData>();
            if (urpData == null)
            {
                Debug.LogError("[Diag] Main Camera missing UniversalAdditionalCameraData!");
            }
            else
            {
                Debug.Log($"[Diag] URP Camera: renderPostProcessing={urpData.renderPostProcessing}, " +
                          $"antialiasing={urpData.antialiasing}");

                if (!urpData.renderPostProcessing)
                {
                    Debug.LogWarning("[Diag] renderPostProcessing is OFF — enabling it.");
                    urpData.renderPostProcessing = true;
                    EditorUtility.SetDirty(mainCam.gameObject);
                    anyFix = true;
                }
            }

            if (!mainCam.allowHDR)
            {
                Debug.LogWarning("[Diag] Camera HDR is OFF — enabling it.");
                mainCam.allowHDR = true;
                EditorUtility.SetDirty(mainCam.gameObject);
                anyFix = true;
            }

            // CinemachineBrain check
            var brain = mainCam.GetComponent<Unity.Cinemachine.CinemachineBrain>();
            Debug.Log($"[Diag] CinemachineBrain present: {brain != null}");
        }

        // ── 3. PostProcessVolume ──────────────────────────────────────────
        var volGo = GameObject.Find("PostProcessVolume");
        if (volGo == null)
        {
            Debug.LogError("[Diag] PostProcessVolume not found in scene!");
        }
        else
        {
            var vol = volGo.GetComponent<Volume>();
            if (vol == null)
            {
                Debug.LogError("[Diag] PostProcessVolume has no Volume component!");
            }
            else
            {
                bool hasProfile = vol.sharedProfile != null;
                Debug.Log($"[Diag] Volume: isGlobal={vol.isGlobal}, weight={vol.weight}, " +
                          $"profile={( hasProfile ? vol.sharedProfile.name : "NULL")}");

                if (!hasProfile)
                {
                    var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
                        "Assets/Settings/CyberArenaVolumeProfile.asset");
                    if (profile != null)
                    {
                        Debug.LogWarning("[Diag] Profile was null — assigning CyberArenaVolumeProfile.");
                        vol.sharedProfile = profile;
                        vol.isGlobal      = true;
                        vol.weight        = 1f;
                        EditorUtility.SetDirty(volGo);
                        anyFix = true;
                    }
                    else
                    {
                        Debug.LogError("[Diag] CyberArenaVolumeProfile.asset not found!");
                    }
                }
            }
        }

        // ── 4. CM_IsometricCamera ─────────────────────────────────────────
        var cmGo = GameObject.Find("CM_IsometricCamera");
        if (cmGo == null)
        {
            Debug.LogError("[Diag] CM_IsometricCamera not found!");
        }
        else
        {
            var euler = cmGo.transform.eulerAngles;
            Debug.Log($"[Diag] CM_IsometricCamera: pos={cmGo.transform.position}, euler={euler}");

            // Correct rotation check: Y should be ~315 (not 45)
            float yRot = euler.y;
            if (yRot > 0f && yRot < 180f)
            {
                Debug.LogWarning($"[Diag] Y rotation is {yRot}° — should be ~315°. Fixing.");
                var camPos = new Vector3(18f, 24f, -18f);
                cmGo.transform.position = camPos;
                cmGo.transform.rotation = Quaternion.LookRotation(Vector3.zero - camPos, Vector3.up);
                EditorUtility.SetDirty(cmGo);
                anyFix = true;
            }
        }

        // ── Save if any fixes applied ─────────────────────────────────────
        if (anyFix)
        {
            AssetDatabase.SaveAssets();
            var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
            Debug.Log("[Diag] Fixes applied and scene saved.");
        }
        else
        {
            Debug.Log("[Diag] No issues found — all settings look correct.");
        }
    }
}

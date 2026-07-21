using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using Unity.Cinemachine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// Fixes black game view and boosts lighting / ambience.
/// 1. Verifies CinemachineBrain on Main Camera and CM camera position
/// 2. Raises ambient light so the scene is never pitch black
/// 3. Boosts directional light, point light intensities, and emissive HDR values
/// 4. Ensures URP HDR and post-process are on
public class FixCameraAndLighting
{
    [MenuItem("CyberBoss/Fix Camera and Lighting")]
    public static void Execute()
    {
        FixCamera();
        FixAmbient();
        FixDirectionalLight();
        BoostPointLights();
        BoostEmissivePalette();
        FixPostProcess();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("[CyberBoss] Camera and lighting fixed.");
    }

    // ── Camera ────────────────────────────────────────────────────────────

    static void FixCamera()
    {
        // Ensure Main Camera has CinemachineBrain
        var mainCam = Camera.main;
        if (mainCam == null)
        {
            Debug.LogError("[CyberBoss] No Main Camera found.");
            return;
        }

        if (mainCam.GetComponent<CinemachineBrain>() == null)
            mainCam.gameObject.AddComponent<CinemachineBrain>();

        // Ensure HDR on camera
        mainCam.allowHDR = true;

        // Ensure the URP camera data has post-processing on
        var urpData = mainCam.gameObject.GetComponent<UniversalAdditionalCameraData>();
        if (urpData != null)
            urpData.renderPostProcessing = true;

        // CM_IsometricCamera: position well outside the scene, looking diagonally down
        var cmGo = GameObject.Find("CM_IsometricCamera");
        if (cmGo != null)
        {
            cmGo.transform.position = new Vector3(20f, 28f, -20f);
            cmGo.transform.rotation = Quaternion.Euler(48f, 45f, 0f);

            var vcam = cmGo.GetComponent<CinemachineCamera>();
            if (vcam != null)
            {
                vcam.Lens.FieldOfView = 40f;
                vcam.Priority         = 10;
            }
        }

        // Scene view — align to match
        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.pivot       = new Vector3(0f, 2f, 0f);
            sv.rotation    = Quaternion.Euler(48f, 45f, 0f);
            sv.size        = 28f;
            sv.orthographic = false;
            sv.Repaint();
        }

        Debug.Log("[CyberBoss] Camera: CinemachineBrain present, HDR on, position set.");
    }

    // ── Ambient ───────────────────────────────────────────────────────────

    static void FixAmbient()
    {
        // Flat ambient colour — dark purple-blue so shadows have colour not pure black
        RenderSettings.ambientMode  = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.04f, 0.02f, 0.10f);   // dim purple

        // Exponential fog for atmospheric depth
        RenderSettings.fog          = true;
        RenderSettings.fogMode      = FogMode.Exponential;
        RenderSettings.fogColor     = new Color(0.04f, 0.02f, 0.12f);   // purple-black
        RenderSettings.fogDensity   = 0.018f;                            // subtle haze

        Debug.Log("[CyberBoss] Ambient: flat purple, fog on.");
    }

    // ── Directional light ─────────────────────────────────────────────────

    static void FixDirectionalLight()
    {
        var dir = GameObject.Find("Directional Light");
        if (dir == null) return;
        var lt = dir.GetComponent<Light>();
        if (lt == null) return;

        lt.color     = new Color(0.55f, 0.65f, 0.90f);  // cool blue-white
        lt.intensity = 0.45f;                             // was 0.25 — just enough to reveal geometry
        lt.shadows   = LightShadows.Soft;
        Debug.Log("[CyberBoss] Directional light boosted.");
    }

    // ── Point lights ──────────────────────────────────────────────────────

    static void BoostPointLights()
    {
        // Specs: (object path or name, colour, intensity, range)
        var specs = new (string name, Color col, float intensity, float range)[]
        {
            // Back corners — cast large pool on floor
            ("ArenaLights/LNE",   new Color(0.4f, 0f,   1.0f), 6f,  22f),  // purple
            ("ArenaLights/LNW",   new Color(0f,   0.9f, 1.0f), 6f,  22f),  // cyan
            ("ArenaLights/LE",    new Color(1f,   0.5f, 0f),   5f,  18f),  // amber
            ("ArenaLights/LW",    new Color(1f,   0f,   0.6f), 5f,  18f),  // pink
            // Mid-arena fills — warm the centre of the floor
            ("ArenaLights/LMid1", new Color(0.1f, 0.4f, 1.0f), 4f,  14f),  // blue
            ("ArenaLights/LMid2", new Color(1f,   0f,   0.8f), 4f,  14f),  // magenta
            // Ground vent up-lights
            ("ArenaLights/LVent1",new Color(0f,   1f,   0.8f), 3f,   8f),  // teal
            ("ArenaLights/LVent2",new Color(0.6f, 0f,   1.0f), 3f,   8f),  // violet
            // Legacy Lights/ — reused from earlier build passes
            ("NeonCyan",          new Color(0f,   1f,   1.0f), 4f,  16f),
            ("NeonMagenta",       new Color(1f,   0f,   0.8f), 4f,  16f),
            ("NeonPurple",        new Color(0.6f, 0f,   1.0f), 3.5f,14f),
            ("NeonBlue",          new Color(0f,   0.4f, 1.0f), 3.5f,14f),
            ("NeonPink1",         new Color(1f,   0.1f, 0.6f), 3f,  12f),
            ("NeonPink2",         new Color(1f,   0.1f, 0.6f), 3f,  12f),
            ("CityLightNorth",    new Color(0.5f, 0f,   1.0f), 3f,  10f),
            ("CityLightSouth",    new Color(0f,   0.8f, 1.0f), 3f,  10f),
        };

        foreach (var (name, col, intensity, range) in specs)
        {
            var go = GameObject.Find(name)
                  ?? GameObject.Find("Lights/" + name.Split('/')[^1]);
            if (go == null) continue;
            var lt = go.GetComponent<Light>();
            if (lt == null) continue;
            lt.color     = col;
            lt.intensity = intensity;
            lt.range     = range;
            lt.shadows   = LightShadows.None;  // WebGL: no per-pixel shadows on point lights
        }

        Debug.Log("[CyberBoss] Point lights boosted.");
    }

    // ── Emissive palette ──────────────────────────────────────────────────
    // HDR emissive values must exceed the Bloom threshold (0.6) by enough
    // to produce visible halos. Previous values: 4–4.8. New: 6–8.

    static void BoostEmissivePalette()
    {
        var boosts = new (string id, Color hdr)[]
        {
            ("PA_ECyan",   new Color(0f,    6f,   6f)),
            ("PA_EPink",   new Color(7f,    0f,   4.8f)),
            ("PA_EPurple", new Color(3.6f,  0f,   7.2f)),
            ("PA_EBlue",   new Color(0f,    1.8f, 7.2f)),
            ("PA_EAmber",  new Color(6f,    3.6f, 0f)),
            ("PA_EGreen",  new Color(0f,    6f,   2.4f)),
            ("PA_EWhite",  new Color(5f,    5f,   5f)),
            ("PA_FLine",   new Color(0.8f,  0f,   3.2f)),
        };

        foreach (var (id, hdr) in boosts)
        {
            string path = $"Assets/Materials/{id}.mat";
            var m = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (m == null) continue;
            m.SetColor("_EmissionColor", hdr);
            m.EnableKeyword("_EMISSION");
            m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(m);
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[CyberBoss] Emissive palette boosted for bloom.");
    }

    // ── Post-process ──────────────────────────────────────────────────────

    static void FixPostProcess()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(
            "Assets/Settings/CyberArenaVolumeProfile.asset");
        if (profile == null)
        {
            Debug.LogWarning("[CyberBoss] VolumeProfile not found — post-process skipped.");
            return;
        }

        // Bloom — push intensity up so emissive glows are clearly visible
        if (profile.TryGet<Bloom>(out var bloom))
        {
            bloom.active    = true;
            bloom.intensity.Override(4.5f);     // was 3.2
            bloom.threshold.Override(0.5f);     // lower threshold catches more surfaces
            bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(1f, 0.75f, 0.95f));  // slight warm pink tint
        }

        // Chromatic Aberration
        if (profile.TryGet<ChromaticAberration>(out var ca))
        {
            ca.active = true;
            ca.intensity.Override(0.4f);
        }

        // Color Adjustments — slight warm offset for neon atmosphere
        if (profile.TryGet<ColorAdjustments>(out var cag))
        {
            cag.active          = true;
            cag.postExposure.Override(0.3f);   // slightly brighter overall
            cag.contrast.Override(20f);
            cag.saturation.Override(40f);
            cag.colorFilter.Override(new Color(0.96f, 0.88f, 1f));  // faint violet tint
        }

        // Vignette — dark edges for cinematic framing
        if (profile.TryGet<Vignette>(out var vig))
        {
            vig.active = true;
            vig.intensity.Override(0.5f);
            vig.smoothness.Override(0.35f);
        }

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
        Debug.Log("[CyberBoss] Post-process profile updated.");
    }
}

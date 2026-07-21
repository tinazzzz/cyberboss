using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

/// <summary>
/// Upgrades scene visuals to match the cyberpunk reference: wet reflective floor,
/// saturated neon palette, aggressive bloom, and atmospheric fog.
/// </summary>
public class SceneVisualOverhaul
{
    public static void Execute()
    {
        UpgradeMaterials();
        CreateEmissiveMaterials();
        UpgradePostProcessing();
        SetupFog();
        Debug.Log("[CyberBoss] Visual overhaul complete.");
    }

    static void UpgradeMaterials()
    {
        // --- Floor: wet reflective surface ---
        var floor = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ArenaFloor.mat");
        if (floor != null)
        {
            floor.SetColor("_BaseColor", new Color(0.01f, 0.01f, 0.02f, 1f)); // near-black
            floor.SetFloat("_Metallic", 0.9f);
            floor.SetFloat("_Smoothness", 0.95f);
            EditorUtility.SetDirty(floor);
        }

        // --- Walls: dark concrete with slight sheen ---
        var wall = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/ArenaWall.mat");
        if (wall != null)
        {
            wall.SetColor("_BaseColor", new Color(0.03f, 0.03f, 0.05f, 1f));
            wall.SetFloat("_Metallic", 0.2f);
            wall.SetFloat("_Smoothness", 0.3f);
            EditorUtility.SetDirty(wall);
        }

        AssetDatabase.SaveAssets();
    }

    static void CreateEmissiveMaterials()
    {
        CreateEmissive("NeonPink",   new Color(0.02f, 0.01f, 0.03f), new Color(6f,  0f,   2f,   1f));
        CreateEmissive("NeonCyan",   new Color(0.01f, 0.02f, 0.03f), new Color(0f,  4f,   3.5f, 1f));
        CreateEmissive("NeonPurple", new Color(0.02f, 0.01f, 0.04f), new Color(3f,  0f,   6f,   1f));
        CreateEmissive("NeonBlue",   new Color(0.01f, 0.01f, 0.03f), new Color(0f,  1f,   5f,   1f));
        CreateEmissive("NeonAmber",  new Color(0.03f, 0.01f, 0.01f), new Color(5f,  1.5f, 0f,   1f));
        CreateEmissive("BuildingFacade", new Color(0.02f, 0.02f, 0.04f), new Color(0.4f, 0.2f, 1.2f, 1f));
        AssetDatabase.SaveAssets();
    }

    static void CreateEmissive(string name, Color baseColor, Color emissionHDR)
    {
        string path = $"Assets/Materials/{name}.mat";
        var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            AssetDatabase.CreateAsset(mat, path);
        }
        mat.SetColor("_BaseColor", baseColor);
        mat.SetFloat("_Metallic", 0.0f);
        mat.SetFloat("_Smoothness", 0.5f);
        mat.SetColor("_EmissionColor", emissionHDR);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
    }

    static void UpgradePostProcessing()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/CyberArenaVolumeProfile.asset");
        if (profile == null) { Debug.LogError("[CyberBoss] VolumeProfile not found"); return; }

        // Bloom — heavy neon glow matching reference
        if (profile.TryGet<Bloom>(out var bloom))
        {
            bloom.intensity.Override(3.2f);
            bloom.threshold.Override(0.6f);
            bloom.scatter.Override(0.75f);
            bloom.tint.Override(new Color(1f, 0.5f, 0.9f, 1f)); // pink bloom tint
        }

        // Chromatic Aberration — stronger lens fringe
        if (profile.TryGet<ChromaticAberration>(out var ca))
            ca.intensity.Override(0.45f);

        // Color Adjustments — deep shadows, saturated neons
        if (profile.TryGet<ColorAdjustments>(out var colorAdj))
        {
            colorAdj.postExposure.Override(0.2f);
            colorAdj.contrast.Override(25f);
            colorAdj.colorFilter.Override(new Color(0.75f, 0.7f, 1f, 1f)); // purple-blue filter
            colorAdj.saturation.Override(35f);
        }

        // Vignette — heavier for cinematic darkness at edges
        if (profile.TryGet<Vignette>(out var vignette))
        {
            vignette.intensity.Override(0.55f);
            vignette.smoothness.Override(0.3f);
        }

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();
    }

    static void SetupFog()
    {
        // Exponential fog — deep blue-purple atmospheric haze
        RenderSettings.fog = true;
        RenderSettings.fogMode = FogMode.Exponential;
        RenderSettings.fogColor = new Color(0.04f, 0.02f, 0.12f, 1f); // deep purple
        RenderSettings.fogDensity = 0.04f;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.02f, 0.01f, 0.06f); // near-black purple ambient
    }
}

using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Fixes H1: properly writes all post-process components as sub-assets to the
/// VolumeProfile asset on disk.
/// Fixes H2: raises AdditionalLightsPerObjectLimit to 8 in both URP assets and
/// removes redundant arena point lights to stay within WebGL budget.
public class FixPostProcessAndLights
{
    const string ProfilePath  = "Assets/Settings/CyberArenaVolumeProfile.asset";
    const string PcAssetPath  = "Assets/Settings/PC_RPAsset.asset";
    const string MobAssetPath = "Assets/Settings/Mobile_RPAsset.asset";

    [MenuItem("CyberBoss/Fix H1+H2: Post-Process & Light Budget")]
    public static void Execute()
    {
        FixVolumeProfile();
        FixLightPerObjectLimit();
        TrimExcessLights();

        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        Debug.Log("[FixPP] H1 + H2 resolved.");
    }

    // ── H1: Rebuild volume profile components as proper sub-assets ─────────
    static void FixVolumeProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null) { Debug.LogError("[FixPP] VolumeProfile not found."); return; }

        // Remove any stale sub-assets from previous attempts
        foreach (var sub in AssetDatabase.LoadAllAssetsAtPath(ProfilePath))
            if (sub != profile && sub is VolumeComponent)
                AssetDatabase.RemoveObjectFromAsset(sub);

        profile.components.Clear();

        // ── Bloom ───────────────────────────────────────────────────────────
        var bloom = ScriptableObject.CreateInstance<Bloom>();
        bloom.name = "Bloom";
        bloom.active = true;
        bloom.threshold.Override(0.5f);
        bloom.intensity.Override(1.8f);
        bloom.scatter.Override(0.65f);
        bloom.tint.Override(new Color(0.8f, 0.9f, 1.0f));   // cool cyan tint
        bloom.highQualityFiltering.Override(false);          // WebGL safe
        Persist(bloom, profile);

        // ── Chromatic Aberration ────────────────────────────────────────────
        var ca = ScriptableObject.CreateInstance<ChromaticAberration>();
        ca.name = "ChromaticAberration";
        ca.active = true;
        ca.intensity.Override(0.22f);
        Persist(ca, profile);

        // ── Vignette ────────────────────────────────────────────────────────
        var vignette = ScriptableObject.CreateInstance<Vignette>();
        vignette.name = "Vignette";
        vignette.active = true;
        vignette.color.Override(new Color(0.03f, 0f, 0.1f));  // deep purple edge
        vignette.intensity.Override(0.4f);
        vignette.smoothness.Override(0.4f);
        Persist(vignette, profile);

        // ── Color Adjustments ───────────────────────────────────────────────
        var ca2 = ScriptableObject.CreateInstance<ColorAdjustments>();
        ca2.name = "ColorAdjustments";
        ca2.active = true;
        ca2.postExposure.Override(0.3f);
        ca2.contrast.Override(15f);
        ca2.colorFilter.Override(new Color(0.9f, 0.92f, 1.0f));  // slightly cool
        ca2.saturation.Override(20f);
        Persist(ca2, profile);

        // ── Tonemapping ─────────────────────────────────────────────────────
        var tone = ScriptableObject.CreateInstance<Tonemapping>();
        tone.name = "Tonemapping";
        tone.active = true;
        tone.mode.Override(TonemappingMode.ACES);
        Persist(tone, profile);

        EditorUtility.SetDirty(profile);
        Debug.Log("[FixPP] H1: VolumeProfile rebuilt with 5 components as proper sub-assets.");
    }

    static void Persist(VolumeComponent component, VolumeProfile profile)
    {
        profile.components.Add(component);
        AssetDatabase.AddObjectToAsset(component, profile);
    }

    // ── H2a: Raise per-object light limit ──────────────────────────────────
    static void FixLightPerObjectLimit()
    {
        foreach (var path in new[] { PcAssetPath, MobAssetPath })
        {
            var asset = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>(path);
            if (asset == null) { Debug.LogWarning($"[FixPP] URP asset not found: {path}"); continue; }

            // Raise additional lights per-object limit from 4 → 8
            // (max meaningful for WebGL Forward without killing GPU performance)
            asset.maxAdditionalLightsCount = 8;
            EditorUtility.SetDirty(asset);
            Debug.Log($"[FixPP] H2: {path} → maxAdditionalLightsCount = 8");
        }
    }

    // ── H2b: Trim arena light count from 23 down to ~12 ──────────────────
    // Keep: ArenaLights group (8 targeted neon lights in the corners/edges).
    // Remove: stale residual lights created by earlier build iterations
    // (NeonCyan, NeonMagenta, NeonPurple, NeonBlue, NeonPink1, NeonPink2,
    //  CityLightNorth, CityLightSouth, and any duplicates).
    static void TrimExcessLights()
    {
        string[] staleNames = {
            "NeonCyan", "NeonMagenta", "NeonPurple", "NeonBlue",
            "NeonPink1", "NeonPink2", "CityLightNorth", "CityLightSouth",
        };

        int removed = 0;
        foreach (var name in staleNames)
        {
            // FindObjectsByType checks all roots; some may be children
            var go = GameObject.Find(name);
            if (go != null)
            {
                Object.DestroyImmediate(go);
                removed++;
                Debug.Log($"[FixPP] Removed stale light: {name}");
            }
        }

        // Also remove the window point lights we added to buildings — they were
        // added on top of the 23 and further exceed the budget.
        // (Removed to stay within light budget.)
        RemoveChildGroup("tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74", "CyanWindowLights");
        RemoveChildGroup("neon storefront 3d model", "WarmStorefrontLights");

        Debug.Log($"[FixPP] H2: removed {removed} stale residual lights + building window lights.");
    }

    static void RemoveChildGroup(string parentName, string groupName)
    {
        var parent = GameObject.Find(parentName);
        if (parent == null) return;
        var group = parent.transform.Find(groupName);
        if (group != null) { Object.DestroyImmediate(group.gameObject); Debug.Log($"[FixPP] Removed {parentName}/{groupName}"); }
    }
}

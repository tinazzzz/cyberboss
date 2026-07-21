using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

public class CyberArenaPostProcessSetup
{
    public static void Execute()
    {
        // Create the Volume Profile asset
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        AssetDatabase.CreateAsset(profile, "Assets/Settings/CyberArenaVolumeProfile.asset");

        // Bloom — bright neon glow; WebGL-safe
        var bloom = profile.Add<Bloom>(overrides: true);
        bloom.active = true;
        bloom.intensity.Override(1.8f);
        bloom.threshold.Override(0.85f);
        bloom.scatter.Override(0.6f);
        bloom.tint.Override(new Color(0.8f, 0.6f, 1f, 1f)); // slight purple tint

        // Chromatic Aberration — cyberpunk lens distortion
        var ca = profile.Add<ChromaticAberration>(overrides: true);
        ca.active = true;
        ca.intensity.Override(0.25f);

        // Color Adjustments — push cool/teal shadows, lift contrast
        var colorAdj = profile.Add<ColorAdjustments>(overrides: true);
        colorAdj.active = true;
        colorAdj.postExposure.Override(0.1f);
        colorAdj.contrast.Override(15f);
        colorAdj.colorFilter.Override(new Color(0.85f, 0.9f, 1f, 1f)); // cool blue filter
        colorAdj.saturation.Override(20f);

        // Tonemapping
        var tone = profile.Add<Tonemapping>(overrides: true);
        tone.active = true;
        tone.mode.Override(TonemappingMode.ACES);

        // Vignette — darkened edges for cinematic feel
        var vignette = profile.Add<Vignette>(overrides: true);
        vignette.active = true;
        vignette.color.Override(Color.black);
        vignette.intensity.Override(0.4f);
        vignette.smoothness.Override(0.4f);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        // Create Global Volume GameObject in scene
        var go = new GameObject("PostProcessVolume");
        var volume = go.AddComponent<Volume>();
        volume.isGlobal = true;
        volume.profile = profile;

        Debug.Log("[CyberBoss] Post-process volume created with bloom, chromatic aberration, color grading, tonemapping, vignette.");
    }
}

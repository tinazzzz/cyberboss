using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;

public class AddDepthOfField
{
    public static void Execute()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>("Assets/Settings/CyberArenaVolumeProfile.asset");
        if (profile == null)
        {
            Debug.LogError("[CyberBoss] Could not load CyberArenaVolumeProfile.asset");
            return;
        }

        // Subtle DoF — Bokeh mode is not WebGL-compatible (compute shader).
        // Gaussian mode uses fragment shaders only — safe for WebGL.
        var dof = profile.Add<DepthOfField>(overrides: true);
        dof.active = true;
        dof.mode.Override(DepthOfFieldMode.Gaussian);
        dof.gaussianStart.Override(18f);  // sharp from 0–18 units (covers the whole arena)
        dof.gaussianEnd.Override(30f);    // soft falloff past 30 units (beyond arena walls)
        dof.gaussianMaxRadius.Override(0.5f); // subtle blur — doesn't obscure gameplay

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        Debug.Log("[CyberBoss] Depth of Field (Gaussian, WebGL-safe) added to CyberArenaVolumeProfile.");
    }
}

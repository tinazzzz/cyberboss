using UnityEngine;
using UnityEditor;

/// Sets HDR cyan emission on CyanWindowGlow.mat — no asset creation, no reimport.
public class SetWindowGlowEmission
{
    [MenuItem("CyberBoss/Set Window Glow Emission")]
    public static void Execute()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/cyberpunk_building_1/Materials/CyanWindowGlow.mat");
        if (mat == null) { Debug.LogError("[WGlow] Material not found."); return; }

        mat.SetColor("_BaseColor", new Color(0f, 0.02f, 0.05f, 1f));
        mat.SetColor("_EmissionColor", new Color(0f, 6f, 6f));   // HDR cyan above bloom threshold
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log("[WGlow] Emission set on CyanWindowGlow.mat");
    }
}

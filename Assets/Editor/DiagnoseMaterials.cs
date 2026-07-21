using UnityEngine;
using UnityEditor;

public class DiagnoseMaterials
{
    [MenuItem("CyberBoss/Diagnose Materials")]
    public static void Execute()
    {
        Debug.Log("=== BUILDING 1 (FBX extracted mat) ===");
        var b1 = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/cyberpunk_building_1/Materials/tripo_mat_35ef6ea0.mat");
        if (b1 != null) DumpMat(b1);

        Debug.Log("=== BUILDING 2 (GLB original materials) ===");
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath("Assets/Models/neon storefront 3d model.glb"))
        {
            if (a is Material m) DumpMat(m);
        }

        Debug.Log("=== BUILDING 2 (our override mat) ===");
        var b2 = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/Models/Materials/neon_storefront_glow.mat");
        if (b2 != null) DumpMat(b2);
    }

    static void DumpMat(Material m)
    {
        Debug.Log($"--- {m.name} | shader: {m.shader.name} ---");

        // Common emission properties
        string[] emitProps = { "_EmissionColor", "_EmissiveFactor", "emissiveFactor",
                               "_EmissiveColor", "_EmissiveIntensity" };
        foreach (var p in emitProps)
            if (m.HasProperty(p)) Debug.Log($"  {p} = {m.GetColor(p)}");

        string[] emitTex = { "_EmissionMap", "_EmissiveTexture", "emissiveTexture" };
        foreach (var p in emitTex)
            if (m.HasProperty(p))
            {
                var t = m.GetTexture(p);
                Debug.Log($"  {p} = {(t != null ? t.name : "null")}");
            }

        // Roughness / smoothness
        string[] floatProps = { "_Smoothness", "_Roughness", "_Metallic",
                                "roughnessFactor", "metallicFactor",
                                "_RoughnessRemapMax", "_RoughnessRemapMin" };
        foreach (var p in floatProps)
            if (m.HasProperty(p)) Debug.Log($"  {p} = {m.GetFloat(p):F3}");

        // Base color
        string[] colorProps = { "_BaseColor", "_Color", "baseColorFactor" };
        foreach (var p in colorProps)
            if (m.HasProperty(p)) Debug.Log($"  {p} = {m.GetColor(p)}");

        // Keywords
        Debug.Log($"  keywords: {string.Join(", ", m.shaderKeywords)}");
    }
}

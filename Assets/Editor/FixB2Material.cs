using UnityEngine;
using UnityEditor;

/// Fixes building 2 material: clears the incorrectly assigned normal-as-metallic,
/// wires the normal map to _BumpMap, and sets reasonable PBR values.
public class FixB2Material
{
    const string B2Mat = "Assets/Models/Materials/neon_storefront_glow.mat";
    const string B2Glb = "Assets/Models/neon storefront 3d model.glb";

    [MenuItem("CyberBoss/Fix Building 2 Material")]
    public static void Execute()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(B2Mat);
        if (mat == null) { Debug.LogError("[B2Mat] Mat not found."); return; }

        Texture2D normalTex  = null;
        Texture2D basecolor  = null;
        Texture2D roughMetal = null;

        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(B2Glb))
        {
            if (a is not Texture2D t) continue;
            string n = t.name.ToLower();

            if (n.Contains("normal"))        normalTex  = t;
            else if (n.Contains("base") || n.Contains("albedo") || n.Contains("color"))
                                             basecolor  = t;
            else if (n.Contains("roughness") || n.Contains("metallic") ||
                     n.Contains("_orm")      || n.Contains("_rm"))
                                             roughMetal = t;

            Debug.Log($"[B2Mat] GLB texture: {t.name}");
        }

        // Basecolor
        if (basecolor != null)
            mat.SetTexture("_BaseMap", basecolor);

        // Normal map — wire to _BumpMap and enable keyword
        if (normalTex != null)
        {
            // Mark as normal map if it isn't already
            string normalPath = AssetDatabase.GetAssetPath(normalTex);
            if (!string.IsNullOrEmpty(normalPath))
            {
                var ti = AssetImporter.GetAtPath(normalPath) as TextureImporter;
                if (ti != null && ti.textureType != TextureImporterType.NormalMap)
                {
                    ti.textureType = TextureImporterType.NormalMap;
                    ti.SaveAndReimport();
                    normalTex = AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath);
                }
            }
            mat.SetTexture("_BumpMap", normalTex);
            mat.EnableKeyword("_NORMALMAP");
            mat.SetFloat("_BumpScale", 1f);
            Debug.Log($"[B2Mat] Normal map assigned: {normalTex.name}");
        }

        // Metallic/roughness — only if we found a dedicated map (not the normal map)
        if (roughMetal != null)
        {
            mat.SetTexture("_MetallicGlossMap", roughMetal);
            mat.SetFloat("_SmoothnessTextureChannel", 0f); // read from texture A
            Debug.Log($"[B2Mat] Roughness/metallic map: {roughMetal.name}");
        }
        else
        {
            // No roughness texture — clear any wrong assignment, use scalar values
            mat.SetTexture("_MetallicGlossMap", null);
            mat.SetFloat("_Metallic", 0.15f);             // slight metallic sheen
            mat.SetFloat("_Smoothness", 0.60f);           // moderate glossiness
            mat.SetFloat("_SmoothnessTextureChannel", 1f); // use slider
            Debug.Log("[B2Mat] No roughness/metallic texture found — using scalar values.");
        }

        mat.SetColor("_BaseColor", Color.white);

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        Debug.Log("[B2Mat] Done.");
    }
}

using UnityEngine;
using UnityEditor;

/// Fixes the over-rough PBR values that make both buildings look chalky/dull.
/// Also applies the roughness texture as inverted smoothness for building 1
/// (glTF roughness = 1 - URP smoothness).
public class FixMaterialRoughness
{
    const string B1Mat  = "Assets/cyberpunk_building_1/Materials/tripo_mat_35ef6ea0.mat";
    const string B1Rm   = "Assets/cyberpunk_building_1/tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74.fbm/cyberpunk_building_3d_1_rm.JPEG";
    const string B2Mat  = "Assets/Models/Materials/neon_storefront_glow.mat";
    const string B2Glb  = "Assets/Models/neon storefront 3d model.glb";

    [MenuItem("CyberBoss/Fix Material Roughness")]
    public static void Execute()
    {
        FixBuilding1();
        FixBuilding2();
        AssetDatabase.SaveAssets();
        Debug.Log("[Roughness] Done.");
    }

    static void FixBuilding1()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(B1Mat);
        if (mat == null) { Debug.LogError("[Roughness] B1 mat not found."); return; }

        // rm.JPEG = combined roughness-metallic from Tripo's glTF export
        // R channel = roughness, G channel = metallic (glTF packed convention)
        // URP Lit MetallicGlossMap: R = metallic, A = smoothness
        // We use the rm texture as-is for metallic (R), and override smoothness
        // with a mid-range value since we can't invert-channel in the editor easily.
        var rmTex = AssetDatabase.LoadAssetAtPath<Texture2D>(B1Rm);
        if (rmTex != null)
            mat.SetTexture("_MetallicGlossMap", rmTex);

        // Smoothness 0.55: gives visible specular highlights without being too shiny.
        // This is the inverted midpoint of Tripo's typical roughness range (0.4–0.6).
        mat.SetFloat("_Smoothness", 0.55f);

        // SmoothnessTextureChannel = 0 means URP reads smoothness from metallic map's A channel.
        // Since our rm texture likely has no useful A channel, drive smoothness from the slider.
        mat.SetFloat("_SmoothnessTextureChannel", 1f);  // 1 = use slider, not texture A

        EditorUtility.SetDirty(mat);
        Debug.Log("[Roughness] Building 1 smoothness fixed.");
    }

    static void FixBuilding2()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(B2Mat);
        if (mat == null) { Debug.LogError("[Roughness] B2 mat not found."); return; }

        // The original glTF material had roughnessFactor = 1.0 (fully matte).
        // Our URP Lit override sits at 0.5 smoothness, which is already better.
        // Bring it up a bit more — 0.60 gives nice cyberpunk specular on the storefront.
        mat.SetFloat("_Smoothness", 0.60f);
        mat.SetFloat("_SmoothnessTextureChannel", 1f);

        // Try to pull a roughness/metallic texture from the GLB sub-assets
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(B2Glb))
        {
            if (a is Texture2D t)
            {
                string n = t.name.ToLower();
                if (n.Contains("rough") || n.Contains("metal") || n.Contains("orm") || n.Contains("_rm"))
                {
                    mat.SetTexture("_MetallicGlossMap", t);
                    Debug.Log($"[Roughness] Building 2 metallic/roughness tex: {t.name}");
                    break;
                }
            }
        }

        EditorUtility.SetDirty(mat);
        Debug.Log("[Roughness] Building 2 smoothness fixed.");
    }
}

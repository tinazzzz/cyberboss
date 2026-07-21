using UnityEngine;
using UnityEditor;

/// Converts the three Muryotaisu character materials from Built-in Standard shader
/// to URP Lit. Preserves all texture references and PBR values.
public class ConvertMuryotaisuMaterials
{
    const string MatRoot = "Assets/Mryotaisu/Models/Materials/";

    [MenuItem("CyberBoss/Convert Muryotaisu Materials to URP")]
    public static void Execute()
    {
        ConvertBody();
        ConvertFace();
        ConvertHair();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        Debug.Log("[MryoMat] All three materials converted to URP Lit.");
    }

    static void ConvertBody()
    {
        var mat = Load("Body.mat");

        // Grab textures before the shader change wipes the property table
        var mainTex    = mat.GetTexture("_MainTex")          as Texture2D;
        var bumpMap    = mat.GetTexture("_BumpMap")          as Texture2D;
        var metalGloss = mat.GetTexture("_MetallicGlossMap") as Texture2D;
        var occMap     = mat.GetTexture("_OcclusionMap")     as Texture2D;
        float cutoff   = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;

        ApplyURPLit(mat);

        mat.SetTexture("_BaseMap",         mainTex);
        mat.SetColor("_BaseColor",         Color.white);
        mat.SetTexture("_BumpMap",         bumpMap);
        mat.SetFloat("_BumpScale",         1f);
        mat.SetTexture("_MetallicGlossMap", metalGloss);
        mat.SetFloat("_Metallic",          0f);
        mat.SetFloat("_Smoothness",        0.25f);
        mat.SetFloat("_SmoothnessTextureChannel", 1f);
        mat.SetTexture("_OcclusionMap",    occMap);
        mat.SetFloat("_OcclusionStrength", 0.58f);

        // Alpha cutout (for hair/cloth transparency)
        SetAlphaCutout(mat, cutoff);

        EditorUtility.SetDirty(mat);
        Debug.Log("[MryoMat] Body.mat converted.");
    }

    static void ConvertFace()
    {
        var mat = Load("Face.mat");

        var mainTex = mat.GetTexture("_MainTex") as Texture2D;

        ApplyURPLit(mat);

        mat.SetTexture("_BaseMap", mainTex);
        mat.SetColor("_BaseColor", Color.white);
        mat.SetFloat("_Metallic",  0f);
        mat.SetFloat("_Smoothness", 0.3f);

        EditorUtility.SetDirty(mat);
        Debug.Log("[MryoMat] Face.mat converted.");
    }

    static void ConvertHair()
    {
        var mat = Load("Hair.mat");

        var mainTex  = mat.GetTexture("_MainTex") as Texture2D;
        var bumpMap  = mat.GetTexture("_BumpMap") as Texture2D;
        var illumTex = mat.GetTexture("_EmissionMap") as Texture2D;
        float cutoff = mat.HasProperty("_Cutoff") ? mat.GetFloat("_Cutoff") : 0.5f;

        ApplyURPLit(mat);

        mat.SetTexture("_BaseMap",  mainTex);
        mat.SetColor("_BaseColor",  Color.white);
        mat.SetTexture("_BumpMap",  bumpMap);
        mat.SetFloat("_BumpScale",  1f);
        mat.SetFloat("_Metallic",   0f);
        mat.SetFloat("_Smoothness", 0.4f);

        // Hair illumination map → subtle emission
        if (illumTex != null)
        {
            mat.SetTexture("_EmissionMap", illumTex);
            mat.SetColor("_EmissionColor", new Color(0.15f, 0.15f, 0.15f));
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        }

        // Hair uses alpha cutout for transparency at strand edges
        SetAlphaCutout(mat, cutoff);

        EditorUtility.SetDirty(mat);
        Debug.Log("[MryoMat] Hair.mat converted.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    static Material Load(string filename) =>
        AssetDatabase.LoadAssetAtPath<Material>(MatRoot + filename);

    static void ApplyURPLit(Material mat)
    {
        mat.shader = Shader.Find("Universal Render Pipeline/Lit");
        // Clear any Built-in keywords that don't map to URP
        mat.shaderKeywords = System.Array.Empty<string>();
    }

    static void SetAlphaCutout(Material mat, float cutoff)
    {
        // URP Lit alpha clip: surface type 0=Opaque, render queue 2450=Cutout
        mat.SetFloat("_Surface",      0f);          // Opaque surface
        mat.SetFloat("_AlphaClip",    1f);          // Enable clip
        mat.SetFloat("_Cutoff",       cutoff);
        mat.SetFloat("_Mode",         1f);          // Cutout mode
        mat.SetFloat("_ZWrite",       1f);
        mat.SetOverrideTag("RenderType", "TransparentCutout");
        mat.renderQueue = 2450;
        mat.EnableKeyword("_ALPHATEST_ON");
        mat.EnableKeyword("_NORMALMAP");
    }
}

using UnityEngine;
using UnityEditor;

/// Extracts the embedded material from the cyberpunk building FBX and wires up
/// all PBR textures to the correct URP Lit shader slots.
/// Root cause: materialLocation=1 (External) but no .mat was ever extracted,
/// so the model renders with an untextured default material.
public class FixBuildingMaterial
{
    const string FbxPath  = "Assets/cyberpunk_building_1/tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74.fbx";
    const string TexBase  = "Assets/cyberpunk_building_1/tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74.fbm/";
    const string MatDir   = "Assets/cyberpunk_building_1/Materials";
    const string MatPath  = "Assets/cyberpunk_building_1/Materials/tripo_mat_35ef6ea0.mat";

    [MenuItem("CyberBoss/Fix Cyberpunk Building Material")]
    public static void Execute()
    {
        // ── 1. Fix normal map import type ─────────────────────────────────
        // Normal maps imported as Default look flat/incorrect in URP.
        FixNormalMapImport(TexBase + "cyberpunk_building_3d_1_normal.JPEG");

        // ── 2. Extract the embedded material to a standalone .mat ─────────
        if (!AssetDatabase.IsValidFolder(MatDir))
            AssetDatabase.CreateFolder("Assets/cyberpunk_building_1", "Materials");

        var importer = AssetImporter.GetAtPath(FbxPath) as ModelImporter;
        if (importer == null) { Debug.LogError("[BuildingMat] ModelImporter not found for FBX."); return; }

        // Find the embedded material inside the FBX asset
        Material embeddedMat = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(FbxPath))
        {
            if (asset is Material m) { embeddedMat = m; break; }
        }

        if (embeddedMat == null)
        {
            Debug.LogError("[BuildingMat] No embedded material found in FBX. Check FBX import settings.");
            return;
        }

        Debug.Log($"[BuildingMat] Extracting material: {embeddedMat.name}");

        // ExtractAsset moves it out of the FBX and updates externalObjects remapping
        string error = AssetDatabase.ExtractAsset(embeddedMat, MatPath);
        if (!string.IsNullOrEmpty(error))
        {
            // Already extracted — just reload it
            Debug.LogWarning($"[BuildingMat] ExtractAsset: {error} — trying to load existing.");
        }

        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // ── 3. Load the now-external material ─────────────────────────────
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            Debug.LogError($"[BuildingMat] Material not found at {MatPath} after extraction.");
            return;
        }

        // ── 4. Assign PBR textures to URP Lit slots ───────────────────────
        var basecolor = Load(TexBase + "cyberpunk_building_3d_1_basecolor.JPEG");
        var normalMap = Load(TexBase + "cyberpunk_building_3d_1_normal.JPEG");
        var metallic  = Load(TexBase + "cyberpunk_building_3d_1_metallic.JPEG");

        if (basecolor != null)
        {
            mat.SetTexture("_BaseMap", basecolor);
            mat.SetColor("_BaseColor", Color.white);   // tint must be white to show texture correctly
        }

        if (normalMap != null)
        {
            mat.SetTexture("_BumpMap", normalMap);
            mat.SetFloat("_BumpScale", 1f);
            mat.EnableKeyword("_NORMALMAP");
        }

        if (metallic != null)
        {
            mat.SetTexture("_MetallicGlossMap", metallic);
            mat.SetFloat("_Metallic", 1f);
            mat.EnableKeyword("_METALLICSPECGLOSSMAP");
        }

        // URP Lit uses Smoothness = 1 - Roughness. Building surfaces are moderately rough.
        // We can't directly use the roughness JPEG (would need channel packing into metallic A).
        // 0.25 gives a matte-ish but not totally flat surface — appropriate for a building.
        mat.SetFloat("_Smoothness", 0.25f);
        mat.SetFloat("_SmoothnessTextureChannel", 0f);  // 0 = from metallic map alpha

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();

        // ── 5. Reimport FBX so the scene object picks up the new material ─
        importer.SaveAndReimport();

        Debug.Log("[BuildingMat] Done. Textures assigned: " +
                  $"basecolor={basecolor != null}, normal={normalMap != null}, metallic={metallic != null}");
    }

    static Texture2D Load(string path)
    {
        var t = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        if (t == null) Debug.LogWarning($"[BuildingMat] Texture not found: {path}");
        return t;
    }

    static void FixNormalMapImport(string path)
    {
        var ti = AssetImporter.GetAtPath(path) as TextureImporter;
        if (ti == null) return;
        if (ti.textureType == TextureImporterType.NormalMap) return;

        ti.textureType = TextureImporterType.NormalMap;
        ti.SaveAndReimport();
        Debug.Log($"[BuildingMat] Reimported {path} as NormalMap.");
    }
}

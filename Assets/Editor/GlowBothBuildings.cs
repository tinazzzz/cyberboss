using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.Rendering.Universal;

/// Makes both 3D building models glow by enabling emission on their materials.
/// Strategy: use each building's own basecolor texture as the emission map.
/// Bright/cyan window areas in the basecolor will bloom; dark walls will not.
/// Also removes the previous floating-quad approach from building 1.
public class GlowBothBuildings
{
    [MenuItem("CyberBoss/Glow Both Buildings")]
    public static void Execute()
    {
        // ── 1. Clean up old floating-quad approach ─────────────────────────
        var tripoGo = GameObject.Find("tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74");
        if (tripoGo != null)
        {
            var oldGlows = tripoGo.transform.Find("WindowGlows");
            if (oldGlows != null)
            {
                Object.DestroyImmediate(oldGlows.gameObject);
                Debug.Log("[Glow] Removed old WindowGlows quads from building 1.");
            }
        }

        // ── 2. Building 1 — URP Lit extracted material ────────────────────
        GlowBuilding1();

        // ── 3. Building 2 — GLB embedded material ─────────────────────────
        GlowBuilding2();

        // ── 4. Save ────────────────────────────────────────────────────────
        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        Debug.Log("[Glow] Both buildings updated.");
    }

    // ── Building 1 ─────────────────────────────────────────────────────────
    // Material is already a standalone .mat (URP Lit). Just enable emission.
    static void GlowBuilding1()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(
            "Assets/cyberpunk_building_1/Materials/tripo_mat_35ef6ea0.mat");
        if (mat == null) { Debug.LogError("[Glow] tripo_mat_35ef6ea0.mat not found."); return; }

        // Use the basecolor as the emission map — cyan windows will glow,
        // dark concrete areas will not.
        var basecolor = mat.GetTexture("_BaseMap") as Texture2D;
        if (basecolor != null)
            mat.SetTexture("_EmissionMap", basecolor);

        // HDR cyan tint — multiplies the emission map, boosting the cyan windows
        mat.SetColor("_EmissionColor", new Color(0f, 3f, 3f));
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        EditorUtility.SetDirty(mat);
        Debug.Log("[Glow] Building 1 emission enabled (basecolor as emission map).");
    }

    // ── Building 2 ─────────────────────────────────────────────────────────
    // Material is embedded in the GLB. Extract it, switch to URP Lit, enable emission.
    static void GlowBuilding2()
    {
        const string glbPath = "Assets/Models/neon storefront 3d model.glb";
        const string matOut  = "Assets/Models/Materials/neon_storefront_glow.mat";

        // Ensure output folder exists
        if (!AssetDatabase.IsValidFolder("Assets/Models/Materials"))
            AssetDatabase.CreateFolder("Assets/Models", "Materials");

        // Load all sub-assets from the GLB — find the material and textures
        var allAssets  = AssetDatabase.LoadAllAssetsAtPath(glbPath);
        Material srcMat   = null;
        Texture2D basecolor = null;

        foreach (var a in allAssets)
        {
            if (a is Material m && srcMat == null)    srcMat    = m;
            if (a is Texture2D t)
            {
                // Basecolor texture is usually named "baseColorTexture" or similar
                string n = t.name.ToLower();
                if (n.Contains("base") || n.Contains("albedo") || n.Contains("color") || n.Contains("diffuse"))
                    basecolor = t;
            }
        }

        // If no basecolor found by name, just grab the first texture
        if (basecolor == null)
        {
            foreach (var a in allAssets)
                if (a is Texture2D t) { basecolor = t; break; }
        }

        // Load or create the new URP Lit material
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matOut);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = "neon_storefront_glow";
            AssetDatabase.CreateAsset(mat, matOut);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            mat = AssetDatabase.LoadAssetAtPath<Material>(matOut);
        }

        // Copy key properties from the source GLB material if available
        if (srcMat != null)
        {
            mat.color = srcMat.color;
            // Try to copy over basecolor texture from the source
            var srcBase = srcMat.GetTexture("baseColorTexture") as Texture2D
                       ?? srcMat.GetTexture("_BaseMap")        as Texture2D
                       ?? srcMat.GetTexture("_MainTex")        as Texture2D;
            if (srcBase != null) basecolor = srcBase;
        }

        // Assign textures to URP Lit slots
        if (basecolor != null)
        {
            mat.SetTexture("_BaseMap", basecolor);
            mat.SetTexture("_EmissionMap", basecolor);
            Debug.Log($"[Glow] Building 2 basecolor: {basecolor.name}");
        }
        else
        {
            // No texture found — use a solid cyan emission tint
            mat.SetColor("_BaseColor", new Color(0.1f, 0.1f, 0.15f));
            Debug.LogWarning("[Glow] Building 2: no basecolor texture found, using solid cyan.");
        }

        mat.SetColor("_BaseColor",    Color.white);
        mat.SetColor("_EmissionColor", new Color(0f, 3f, 3f));   // HDR cyan
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

        EditorUtility.SetDirty(mat);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        // Apply the new material to the scene object
        var storefrontGo = GameObject.Find("neon storefront 3d model");
        if (storefrontGo != null)
        {
            var r = storefrontGo.GetComponent<MeshRenderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;
                EditorUtility.SetDirty(storefrontGo);
                Debug.Log("[Glow] Building 2 material assigned.");
            }
        }
        else
        {
            Debug.LogWarning("[Glow] 'neon storefront 3d model' not found in scene.");
        }
    }
}

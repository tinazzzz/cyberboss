using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;

/// Selective window glow using per-building emission mask textures.
/// Uses a RenderTexture readback to sample pixels without requiring isReadable,
/// then writes a black/white mask (white = window area) and applies it as
/// _EmissionMap so only those pixels exceed the bloom threshold.
public class FixWindowGlowMasks
{
    // Building 1 paths
    const string B1Mat  = "Assets/cyberpunk_building_1/Materials/tripo_mat_35ef6ea0.mat";
    const string B1Tex  = "Assets/cyberpunk_building_1/tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74.fbm/cyberpunk_building_3d_1_basecolor.JPEG";
    const string B1Mask = "Assets/cyberpunk_building_1/Materials/window_mask_cyan.png";

    // Building 2 paths
    const string B2Mat  = "Assets/Models/Materials/neon_storefront_glow.mat";
    const string B2Glb  = "Assets/Models/neon storefront 3d model.glb";
    const string B2Mask = "Assets/Models/Materials/window_mask_warm.png";

    [MenuItem("CyberBoss/Fix Window Glow (Selective Masks)")]
    public static void Execute()
    {
        // ── Building 1: cyan window glow ──────────────────────────────────
        var b1Src = AssetDatabase.LoadAssetAtPath<Texture2D>(B1Tex);
        if (b1Src == null) { Debug.LogError("[WMask] Building 1 basecolor not found."); }
        else
        {
            // Select cyan pixels: high G+B relative to R
            var mask = CreateMask(b1Src, B1Mask, c =>
                Mathf.Clamp01((c.g + c.b - c.r * 1.8f - 0.15f) * 3f));
            if (mask != null)
            {
                ApplyEmission(B1Mat, mask, new Color(0f, 4f, 4f));   // HDR cyan
                Debug.Log("[WMask] Building 1 cyan mask applied.");
            }
        }

        // ── Building 2: yellow-orange window glow ─────────────────────────
        Texture2D b2Src = null;
        foreach (var a in AssetDatabase.LoadAllAssetsAtPath(B2Glb))
            if (a is Texture2D t && t.name.ToLower().Contains("base")) { b2Src = t; break; }

        if (b2Src == null)
        {
            // Fallback: grab any texture from the GLB
            foreach (var a in AssetDatabase.LoadAllAssetsAtPath(B2Glb))
                if (a is Texture2D t) { b2Src = t; break; }
        }

        if (b2Src == null) { Debug.LogError("[WMask] Building 2 basecolor not found."); }
        else
        {
            // Select warm/bright pixels: high overall brightness, warm tone (r+g > b)
            var mask = CreateMask(b2Src, B2Mask, c =>
            {
                float warmth   = Mathf.Clamp01(c.r * 0.5f + c.g * 0.4f - c.b * 0.9f);
                float bright   = (c.r + c.g + c.b) / 3f;
                return Mathf.Clamp01((warmth + bright - 0.65f) * 4f);
            });
            if (mask != null)
            {
                ApplyEmission(B2Mat, mask, new Color(4f, 1.8f, 0f));  // HDR amber-orange
                Debug.Log("[WMask] Building 2 warm mask applied.");
            }
        }

        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        Debug.Log("[WMask] Done.");
    }

    // ── Helpers ────────────────────────────────────────────────────────────

    /// Reads a texture via RenderTexture (no isReadable needed), applies the
    /// selector per pixel, writes a PNG mask, returns the imported asset.
    static Texture2D CreateMask(Texture2D src, string savePath,
                                 System.Func<Color, float> selector)
    {
        // Blit source into a readable RenderTexture
        var rt = RenderTexture.GetTemporary(
            src.width, src.height, 0,
            RenderTextureFormat.ARGB32, RenderTextureReadWrite.Default);
        Graphics.Blit(src, rt);

        var prev = RenderTexture.active;
        RenderTexture.active = rt;
        var copy = new Texture2D(src.width, src.height, TextureFormat.RGBA32, false);
        copy.ReadPixels(new Rect(0, 0, src.width, src.height), 0, 0);
        copy.Apply();
        RenderTexture.active = prev;
        RenderTexture.ReleaseTemporary(rt);

        // Downsample to 512 max to keep asset size small
        int w = Mathf.Min(src.width, 512);
        int h = Mathf.Min(src.height, 512);
        float xs = (float)src.width  / w;
        float ys = (float)src.height / h;

        var mask = new Texture2D(w, h, TextureFormat.RGB24, false);
        var pixels = new Color[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            Color c = copy.GetPixel(
                Mathf.RoundToInt(x * xs),
                Mathf.RoundToInt(y * ys));
            float v = selector(c);
            pixels[y * w + x] = new Color(v, v, v, 1f);
        }
        mask.SetPixels(pixels);
        mask.Apply();
        Object.DestroyImmediate(copy);

        // Save to disk
        string fullPath = Path.Combine(
            Application.dataPath, savePath.Replace("Assets/", ""));
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath));
        File.WriteAllBytes(fullPath, mask.EncodeToPNG());
        Object.DestroyImmediate(mask);

        AssetDatabase.Refresh();
        return AssetDatabase.LoadAssetAtPath<Texture2D>(savePath);
    }

    static void ApplyEmission(string matPath, Texture2D mask, Color hdrColor)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { Debug.LogError($"[WMask] Mat not found: {matPath}"); return; }

        mat.SetTexture("_EmissionMap", mask);
        mat.SetColor("_EmissionColor", hdrColor);
        mat.EnableKeyword("_EMISSION");
        mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(mat);
    }
}

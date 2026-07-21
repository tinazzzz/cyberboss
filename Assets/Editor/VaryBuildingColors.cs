using UnityEngine;
using UnityEditor;

/// Gives each background building a distinct neon-tinted emissive material
/// so the skyline reads as a varied cyberpunk cityscape rather than a flat purple block.
public class VaryBuildingColors
{
    public static void Execute()
    {
        // (building path, base color, HDR emission)
        var defs = new (string path, Color baseCol, Color emission)[]
        {
            ("CityBackground/BldgN1",  Hex(0x05020D), HDR(1.0f, 0.0f, 0.6f, 4f)),   // hot pink
            ("CityBackground/BldgN2",  Hex(0x020510), HDR(0.0f, 0.5f, 1.0f, 3f)),   // electric blue
            ("CityBackground/BldgN3",  Hex(0x03010A), HDR(0.6f, 0.0f, 1.0f, 4f)),   // purple
            ("CityBackground/BldgS1",  Hex(0x020A05), HDR(0.0f, 1.0f, 0.8f, 3f)),   // cyan-green
            ("CityBackground/BldgS2",  Hex(0x080209), HDR(1.0f, 0.1f, 0.8f, 4f)),   // magenta
            ("CityBackground/BldgE1",  Hex(0x030109), HDR(0.5f, 0.0f, 1.0f, 3f)),   // purple
            ("CityBackground/BldgE2",  Hex(0x050200), HDR(1.0f, 0.3f, 0.0f, 3f)),   // amber
            ("CityBackground/BldgW1",  Hex(0x020308), HDR(0.0f, 0.4f, 1.0f, 4f)),   // blue
            ("CityBackground/BldgW2",  Hex(0x06010A), HDR(0.9f, 0.0f, 0.5f, 3f)),   // pink
        };

        foreach (var (path, baseCol, emission) in defs)
        {
            var go = GameObject.Find(path.Replace("/", "/"));
            if (go == null) { Debug.LogWarning($"[CyberBoss] Not found: {path}"); continue; }

            var renderer = go.GetComponent<Renderer>();
            if (renderer == null) continue;

            // Create a unique material per building (don't share)
            string matName = go.name + "_Mat";
            string matPath = $"Assets/Materials/{matName}.mat";
            var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
            if (mat == null)
            {
                mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
                AssetDatabase.CreateAsset(mat, matPath);
            }
            mat.SetColor("_BaseColor", baseCol);
            mat.SetFloat("_Metallic", 0.1f);
            mat.SetFloat("_Smoothness", 0.4f);
            mat.SetColor("_EmissionColor", emission);
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(mat);
            renderer.sharedMaterial = mat;
        }

        AssetDatabase.SaveAssets();
        Debug.Log("[CyberBoss] Building colors varied.");
    }

    static Color Hex(int rgb) =>
        new Color(((rgb >> 16) & 0xFF) / 255f, ((rgb >> 8) & 0xFF) / 255f, (rgb & 0xFF) / 255f);

    static Color HDR(float r, float g, float b, float intensity) =>
        new Color(r * intensity, g * intensity, b * intensity, 1f);
}

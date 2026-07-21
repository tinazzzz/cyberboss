using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Replaces full-building emission with targeted point lights placed inside each
/// building at window heights. This creates the "lit from within" effect without
/// making the entire surface glow.
public class FixWindowPointLights
{
    const string B1Mat = "Assets/cyberpunk_building_1/Materials/tripo_mat_35ef6ea0.mat";
    const string B2Mat = "Assets/Models/Materials/neon_storefront_glow.mat";

    [MenuItem("CyberBoss/Fix Window Point Lights")]
    public static void Execute()
    {
        // ── 1. Remove full-building emission from both materials ───────────
        RemoveEmission(B1Mat);
        RemoveEmission(B2Mat);

        // ── 2. Building 1: cyan window lights ─────────────────────────────
        // Bounds X:5.51–8.47  Y:0.05–6.45  Z:0.04–3.48
        // Camera at (18,24,-18) sees the -Z face (z=0.04) and +X face (x=8.47).
        // Place lights just inside those two visible faces at window floors.
        SetupLights(
            "tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74",
            "CyanWindowLights",
            new Color(0f, 1f, 1f),          // pure cyan
            intensity: 1.8f,
            range:     4.0f,
            positions: new[]
            {
                new Vector3(6.99f, 5.0f, 0.6f),   // upper floor, facing -Z
                new Vector3(6.99f, 3.4f, 0.6f),   // mid floor, facing -Z
                new Vector3(6.99f, 1.8f, 0.6f),   // lower floor, facing -Z
            });

        // ── 3. Building 2: yellow-orange storefront lights ─────────────────
        // Bounds X:1.84–8.08  Y:0.38–6.22  Z:-6.93–0.89
        // Center (4.96, 3.3, -3.02). The +Z face (z=0.89) faces the arena.
        // Place warm lights just inside the storefront windows (+Z face).
        SetupLights(
            "neon storefront 3d model",
            "WarmStorefrontLights",
            new Color(1f, 0.55f, 0f),       // amber-orange
            intensity: 2.0f,
            range:     5.0f,
            positions: new[]
            {
                new Vector3(3.5f, 2.0f, 0.3f),    // left storefront window
                new Vector3(6.5f, 2.0f, 0.3f),    // right storefront window
                new Vector3(4.96f, 3.8f, 0.3f),   // upper signage glow
            });

        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        Debug.Log("[WPL] Window point lights applied to both buildings.");
    }

    static void RemoveEmission(string matPath)
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(matPath);
        if (mat == null) { Debug.LogWarning($"[WPL] Material not found: {matPath}"); return; }

        mat.SetTexture("_EmissionMap", null);
        mat.SetColor("_EmissionColor", Color.black);
        mat.DisableKeyword("_EMISSION");
        EditorUtility.SetDirty(mat);
        Debug.Log($"[WPL] Emission removed: {matPath}");
    }

    static void SetupLights(string goName, string groupName, Color color,
                             float intensity, float range, Vector3[] positions)
    {
        var go = GameObject.Find(goName);
        if (go == null) { Debug.LogError($"[WPL] GameObject not found: {goName}"); return; }

        // Remove any existing light group on this object
        var old = go.transform.Find(groupName);
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var group = new GameObject(groupName);
        group.transform.SetParent(go.transform, worldPositionStays: false);
        group.transform.localPosition = Vector3.zero;
        group.transform.localRotation = Quaternion.identity;
        group.transform.localScale    = Vector3.one;

        foreach (var pos in positions)
        {
            var lightGo = new GameObject("WinLight");
            lightGo.transform.SetParent(group.transform, worldPositionStays: false);
            lightGo.transform.localPosition = pos;

            var lt       = lightGo.AddComponent<Light>();
            lt.type      = LightType.Point;
            lt.color     = color;
            lt.intensity = intensity;
            lt.range     = range;
            lt.shadows   = LightShadows.None;   // WebGL budget
        }

        EditorUtility.SetDirty(go);
        Debug.Log($"[WPL] {positions.Length} lights added to {goName}/{groupName}");
    }
}

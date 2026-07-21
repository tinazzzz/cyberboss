using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Fixes the cyberpunk_building_1 (tripo_convert_35ef6ea0) import which was
/// reading dark and dull instead of matching the sample_component_1 reference.
/// Two separate causes, both fixed here:
///  1. tripo_mat_35ef6ea0 has _Metallic = 1 with no reflection probe in the
///     scene, so its actually-vivid purple/mint albedo texture got almost no
///     diffuse response — metallic surfaces are lit almost entirely by
///     environment reflections, which this scene doesn't provide.
///  2. The 3 WinLight point lights under CyanWindowLights were authored with
///     local offsets sized for a ~1-unit building, but the building root has
///     an 8.5x scale + 270 deg X rotation baked in from the Tripo3D import.
///     Those offsets got amplified/rotated and landed the lights ~70 units
///     from the building, so they lit nothing. WindowGlows' quads avoided
///     this because their local offsets were authored small enough to survive
///     the same parent transform sanely.
public class FixTripoBuildingLighting
{
    const string BuildingName = "tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74";
    const string MaterialPath = "Assets/cyberpunk_building_1/Materials/tripo_mat_35ef6ea0.mat";

    [MenuItem("CyberBoss/Fix Tripo Building Lighting")]
    public static void Execute()
    {
        FixMaterial();
        FixMisplacedWindowLights();

        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        Debug.Log("[FixTripoBuilding] Done.");
    }

    static void FixMaterial()
    {
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
        if (mat == null) { Debug.LogError("[FixTripoBuilding] Material not found."); return; }
        mat.SetFloat("_Metallic", 0.2f);
        EditorUtility.SetDirty(mat);
        Debug.Log("[FixTripoBuilding] tripo_mat_35ef6ea0: _Metallic 1.0 -> 0.2.");
    }

    static void FixMisplacedWindowLights()
    {
        var building = GameObject.Find(BuildingName);
        if (building == null) { Debug.LogError("[FixTripoBuilding] Building not found."); return; }

        var windowGlows = building.transform.Find("WindowGlows");
        var cyanLights = building.transform.Find("CyanWindowLights");
        if (windowGlows == null || cyanLights == null)
        {
            Debug.LogError("[FixTripoBuilding] Missing WindowGlows or CyanWindowLights group.");
            return;
        }

        var targets = new[] { "Win_Top_L", "Win_Top_R", "Win_Band" };
        var lights = cyanLights.GetComponentsInChildren<Light>();

        for (int i = 0; i < lights.Length && i < targets.Length; i++)
        {
            var windowQuad = windowGlows.Find(targets[i]);
            if (windowQuad == null) { Debug.LogWarning($"[FixTripoBuilding] Window quad '{targets[i]}' not found."); continue; }

            // Matches the offset already used by WindowGlows/CyanWindowLight,
            // which sits ~0.5 back from its window quad's world Z and reads
            // correctly in-scene.
            lights[i].transform.position = windowQuad.position + new Vector3(0f, 0f, -0.5f);
            lights[i].intensity = 3.0f;
            lights[i].range = 5f;
            EditorUtility.SetDirty(lights[i]);
            Debug.Log($"[FixTripoBuilding] Repositioned light onto '{targets[i]}' at {lights[i].transform.position}.");
        }

        var existing = windowGlows.Find("CyanWindowLight");
        if (existing != null)
        {
            var light = existing.GetComponent<Light>();
            light.intensity = 2.8f;
            EditorUtility.SetDirty(light);
        }
    }
}

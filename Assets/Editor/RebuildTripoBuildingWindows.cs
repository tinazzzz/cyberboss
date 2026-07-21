using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// The tripo_convert_35ef6ea0 building's WindowGlows quads (Win_Top_L/R,
/// Win_Mid_L/R, Win_Band) were authored with local offsets/scale sized as if
/// the building had no parent transform, but the building root actually has
/// an 8.5x scale + 270 deg X rotation baked in from the Tripo3D import. That
/// warps small local offsets into huge, rotated world-space displacements —
/// the quads landed at world X ~7.7-13.6, nowhere near the actual mesh
/// (world X ~-1.26 to 2.66). They were floating, disconnected, off to the
/// side, coincidentally overlapping unrelated geometry in some views.
///
/// Rather than reverse-engineer sane local offsets against that warped parent
/// space, this parents the window group under a plain anchor placed at the
/// building's world center with identity rotation/scale, so the grid below
/// can be authored in ordinary world-relative units.
public class RebuildTripoBuildingWindows
{
    const string BuildingName = "tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74";
    static readonly Vector3 BuildingWorldCenter = new Vector3(0.7f, 4.08f, 9.5f);

    [MenuItem("CyberBoss/Rebuild Tripo Building Windows")]
    public static void Execute()
    {
        var building = GameObject.Find(BuildingName);
        if (building == null) { Debug.LogError("[RebuildWindows] Building not found."); return; }

        var windowGlows = building.transform.Find("WindowGlows");
        var cyanLightsGroup = building.transform.Find("CyanWindowLights");
        if (windowGlows == null) { Debug.LogError("[RebuildWindows] WindowGlows not found."); return; }

        // Anchor at the building's real world center, identity rotation/scale,
        // so children below can use plain world-relative offsets.
        var anchorGO = GameObject.Find("TripoBuilding1_WindowAnchor");
        if (anchorGO == null) anchorGO = new GameObject("TripoBuilding1_WindowAnchor");
        anchorGO.transform.position = BuildingWorldCenter;
        anchorGO.transform.rotation = Quaternion.identity;
        anchorGO.transform.localScale = Vector3.one;
        anchorGO.transform.SetParent(building.transform.parent, true);

        windowGlows.SetParent(anchorGO.transform, false);
        var faceRotation = Quaternion.Euler(0f, 180f, 0f); // faces -Z, the arena-facing side

        PlaceQuad(windowGlows, "Win_Top_L", new Vector3(-0.9f, 2.0f, -2.35f), faceRotation, new Vector2(1.3f, 1.6f));
        PlaceQuad(windowGlows, "Win_Top_R", new Vector3(0.9f, 2.0f, -2.35f), faceRotation, new Vector2(1.3f, 1.6f));
        PlaceQuad(windowGlows, "Win_Mid_L", new Vector3(-0.9f, -0.2f, -2.35f), faceRotation, new Vector2(1.3f, 1.6f));
        PlaceQuad(windowGlows, "Win_Mid_R", new Vector3(0.9f, -0.2f, -2.35f), faceRotation, new Vector2(1.3f, 1.6f));
        PlaceQuad(windowGlows, "Win_Band", new Vector3(0f, -2.3f, -2.35f), faceRotation, new Vector2(3.2f, 1.0f));

        // Lights: reuse the 3 misplaced WinLight point lights + the one
        // correctly-authored CyanWindowLight, now aimed at the real windows.
        var winLights = cyanLightsGroup != null ? cyanLightsGroup.GetComponentsInChildren<Light>() : new Light[0];
        var existingLight = windowGlows.Find("CyanWindowLight")?.GetComponent<Light>();

        PlaceLight(winLights.Length > 0 ? winLights[0] : null, windowGlows.Find("Win_Top_L"));
        PlaceLight(winLights.Length > 1 ? winLights[1] : null, windowGlows.Find("Win_Top_R"));
        PlaceLight(winLights.Length > 2 ? winLights[2] : null, windowGlows.Find("Win_Mid_R"));
        PlaceLight(existingLight, windowGlows.Find("Win_Mid_L"));

        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        Debug.Log("[RebuildWindows] Done.");
    }

    static void PlaceQuad(Transform parent, string name, Vector3 localPos, Quaternion worldRot, Vector2 size)
    {
        var t = parent.Find(name);
        if (t == null) { Debug.LogWarning($"[RebuildWindows] '{name}' not found."); return; }

        t.localPosition = localPos;
        t.rotation = worldRot;
        t.localScale = new Vector3(size.x, size.y, 0.05f);
        EditorUtility.SetDirty(t.gameObject);
    }

    static void PlaceLight(Light light, Transform windowQuad)
    {
        if (light == null || windowQuad == null) return;
        light.transform.position = windowQuad.position + windowQuad.forward * -0.6f;
        light.intensity = 3.0f;
        light.range = 5f;
        EditorUtility.SetDirty(light);
    }
}

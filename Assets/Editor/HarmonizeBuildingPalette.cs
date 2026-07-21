using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Harmonizes the 8 procedural background buildings (Buildings/Tower*,
/// Bld*) each mixed 2 unrelated neon colors (e.g. TowerNE = purple + cyan,
/// BldE1 = amber + purple, BldW2 = pink + purple) across 7 glow surfaces
/// each (2 bands, rim, 2 pillars), pulling from a 7-color material set
/// (cyan/pink/purple/blue/amber/green/white). With 8 buildings each showing
/// 2 of those colors, the skyline read as a scattered rainbow rather than a
/// deliberate cyberpunk palette.
///
/// Replaces it with a single 2-tone scheme split by side of the arena:
/// buildings east of center get one unified purple accent across all their
/// glow surfaces, buildings west of center get one unified cyan accent.
/// Same split applied to the paired ArenaLights point lights that sit next
/// to those buildings, so the light color matches the building it lights
/// instead of clashing with it.
public class HarmonizeBuildingPalette
{
    [MenuItem("CyberBoss/Harmonize Building Palette")]
    public static void Execute()
    {
        var purple = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PA_EPurple.mat");
        var cyan = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PA_ECyan.mat");
        if (purple == null || cyan == null) { Debug.LogError("[Harmonize] Palette materials not found."); return; }

        var eastBuildings = new[] { "TowerNE", "BldN1", "BldE1", "BldE2" };
        var westBuildings = new[] { "TowerNW", "BldN2", "BldW1", "BldW2" };

        foreach (var id in eastBuildings) RecolorBuilding(id, purple);
        foreach (var id in westBuildings) RecolorBuilding(id, cyan);

        RecolorLights();

        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        Debug.Log("[Harmonize] Done.");
    }

    static void RecolorBuilding(string id, Material accent)
    {
        var buildings = GameObject.Find("Buildings");
        if (buildings == null) { Debug.LogError("[Harmonize] Buildings root not found."); return; }

        string[] suffixes = { "_BA_F", "_BA_S", "_BB_F", "_BB_S", "_Rim", "_PL", "_PR" };
        foreach (var suf in suffixes)
        {
            var t = buildings.transform.Find(id + suf);
            if (t == null) { Debug.LogWarning($"[Harmonize] '{id}{suf}' not found."); continue; }
            var r = t.GetComponent<Renderer>();
            if (r == null) continue;
            r.sharedMaterial = accent;
            EditorUtility.SetDirty(r);
        }
    }

    static void RecolorLights()
    {
        var lights = GameObject.Find("ArenaLights");
        if (lights == null) { Debug.LogWarning("[Harmonize] ArenaLights not found."); return; }

        var purple = new Color(0.55f, 0f, 1f);
        var cyan = new Color(0f, 0.85f, 1f);

        SetLightColor(lights.transform, "LNE", purple);
        SetLightColor(lights.transform, "LE", purple);
        SetLightColor(lights.transform, "LMid1", purple);
        SetLightColor(lights.transform, "LNW", cyan);
        SetLightColor(lights.transform, "LW", cyan);
        SetLightColor(lights.transform, "LMid2", cyan);
    }

    static void SetLightColor(Transform parent, string name, Color color)
    {
        var t = parent.Find(name);
        if (t == null) { Debug.LogWarning($"[Harmonize] Light '{name}' not found."); return; }
        var l = t.GetComponent<Light>();
        if (l == null) return;
        l.color = color;
        EditorUtility.SetDirty(l);
    }
}

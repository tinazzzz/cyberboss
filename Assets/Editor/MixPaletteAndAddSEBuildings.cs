using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Follow-up to HarmonizeBuildingPalette: the strict left=cyan/right=purple
/// split read as too rigid a mirror. This interleaves the two colors across
/// the skyline instead (alternating along the back row and each side) so it
/// stays a disciplined 2-tone palette but doesn't look like two solid-color
/// halves.
///
/// Also extends the skyline into the south-east quadrant: the isometric
/// camera looks toward the NE corner, and the existing layout deliberately
/// keeps buildings out of the near-camera south region (BldE2 stops at
/// z = -1), which left the right side of the frame comparatively empty.
/// Adds two more buildings further south along the east wall, matching the
/// existing tall+short pairing pattern, well outside the centre combat area
/// so they don't interfere with gameplay or camera framing.
public class MixPaletteAndAddSEBuildings
{
    [MenuItem("CyberBoss/Mix Palette + Add SE Buildings")]
    public static void Execute()
    {
        var purple = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PA_EPurple.mat");
        var cyan = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PA_ECyan.mat");
        var bodyA = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PA_BodyA.mat");
        var bodyB = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/PA_BodyB.mat");
        if (purple == null || cyan == null || bodyA == null || bodyB == null)
        {
            Debug.LogError("[MixPalette] Palette materials not found.");
            return;
        }

        MixExistingBuildings(purple, cyan);
        AddSouthEastBuildings(bodyA, bodyB, purple, cyan);
        RetuneLights(purple, cyan);

        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        Debug.Log("[MixPalette] Done.");
    }

    // ── Interleave the existing 8 buildings' single accent color ───────────
    static void MixExistingBuildings(Material purple, Material cyan)
    {
        var assignment = new (string id, Material accent)[]
        {
            ("TowerNE", cyan),   // was purple
            ("TowerNW", purple), // was cyan
            ("BldN1",   purple), // unchanged
            ("BldN2",   cyan),   // unchanged
            ("BldE1",   cyan),   // was purple
            ("BldE2",   purple), // unchanged
            ("BldW1",   purple), // was cyan
            ("BldW2",   cyan),   // unchanged
        };

        var buildings = GameObject.Find("Buildings");
        if (buildings == null) { Debug.LogError("[MixPalette] Buildings root not found."); return; }

        string[] suffixes = { "_BA_F", "_BA_S", "_BB_F", "_BB_S", "_Rim", "_PL", "_PR" };
        foreach (var (id, accent) in assignment)
        {
            foreach (var suf in suffixes)
            {
                var t = buildings.transform.Find(id + suf);
                if (t == null) continue;
                var r = t.GetComponent<Renderer>();
                if (r == null) continue;
                r.sharedMaterial = accent;
                EditorUtility.SetDirty(r);
            }
        }
    }

    // ── New buildings filling the south-east gap ────────────────────────────
    static void AddSouthEastBuildings(Material bodyA, Material bodyB, Material purple, Material cyan)
    {
        var buildings = GameObject.Find("Buildings");
        if (buildings == null) return;

        if (buildings.transform.Find("BldSE1") == null)
            Bld(buildings, "BldSE1", new Vector2(13f, -7f), 6f, 12f, 6f, bodyA, cyan);
        if (buildings.transform.Find("BldSE2") == null)
            Bld(buildings, "BldSE2", new Vector2(17f, -3f), 5f, 7f, 5f, bodyB, purple);
    }

    // Mirrors RebuildArenaFinal.Bld()/HBand() so new buildings match the
    // existing family (body + 2 accent bands + rooftop rim + 2 edge pillars),
    // but with a single accent color per building instead of two.
    static void Bld(GameObject root, string id, Vector2 xz, float sx, float sy, float sz,
                    Material body, Material glow)
    {
        float hx = sx * 0.5f, hz = sz * 0.5f;

        Cube(root, id, new Vector3(xz.x, sy * 0.5f, xz.y), new Vector3(sx, sy, sz), body);

        float yA = sy * 0.28f;
        HBand(root, id + "_BA", xz, yA, sx, sz, hx, hz, glow);
        float yB = sy * 0.62f;
        HBand(root, id + "_BB", xz, yB, sx, sz, hx, hz, glow);

        Cube(root, id + "_Rim",
             new Vector3(xz.x, sy + 0.3f, xz.y),
             new Vector3(sx + 0.15f, 0.5f, sz + 0.15f), glow);

        Cube(root, id + "_PL",
             new Vector3(xz.x - hx + 0.1f, sy * 0.5f, xz.y - hz + 0.1f),
             new Vector3(0.14f, sy, 0.14f), glow);
        Cube(root, id + "_PR",
             new Vector3(xz.x + hx - 0.1f, sy * 0.5f, xz.y - hz + 0.1f),
             new Vector3(0.14f, sy, 0.14f), glow);
    }

    static void HBand(GameObject root, string id, Vector2 xz, float y, float sx, float sz,
                      float hx, float hz, Material mat)
    {
        Cube(root, id + "_F",
             new Vector3(xz.x, y, xz.y - hz - 0.06f),
             new Vector3(sx * 0.85f, 0.4f, 0.06f), mat);
        Cube(root, id + "_S",
             new Vector3(xz.x + hx + 0.06f, y, xz.y),
             new Vector3(0.06f, 0.4f, sz * 0.85f), mat);
    }

    static GameObject Cube(GameObject parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent.transform);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        return go;
    }

    // ── Match ArenaLights to the new per-building assignment ───────────────
    static void RetuneLights(Material purple, Material cyan)
    {
        var lights = GameObject.Find("ArenaLights");
        if (lights == null) return;

        var purpleCol = new Color(0.55f, 0f, 1f);
        var cyanCol = new Color(0f, 0.85f, 1f);

        SetLightColor(lights.transform, "LNE", cyanCol);    // now beside TowerNE (cyan)
        SetLightColor(lights.transform, "LNW", purpleCol);  // now beside TowerNW (purple)
        SetLightColor(lights.transform, "LE", cyanCol);     // beside BldE1 (cyan)
        SetLightColor(lights.transform, "LW", purpleCol);   // beside BldW1 (purple)
        SetLightColor(lights.transform, "LMid1", purpleCol); // beside BldN1 (purple)
        SetLightColor(lights.transform, "LMid2", cyanCol);   // beside BldN2 (cyan)

        // New fill light for the south-east addition.
        if (lights.transform.Find("LSE") == null)
        {
            var go = new GameObject("LSE");
            go.transform.SetParent(lights.transform);
            go.transform.position = new Vector3(13f, 5f, -7f);
            var lt = go.AddComponent<Light>();
            lt.type = LightType.Point;
            lt.color = cyanCol;
            lt.intensity = 4f;
            lt.range = 14f;
            lt.shadows = LightShadows.None;
        }
    }

    static void SetLightColor(Transform parent, string name, Color color)
    {
        var t = parent.Find(name);
        if (t == null) return;
        var l = t.GetComponent<Light>();
        if (l == null) return;
        l.color = color;
        EditorUtility.SetDirty(l);
    }
}

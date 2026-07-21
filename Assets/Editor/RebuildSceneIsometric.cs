using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Rebuilds CyberArena with proper isometric layout:
///  - Camera at 45 deg Y rotation (diagonal, not facing a flat wall)
///  - 3D buildings placed ON the arena floor along edges, center open for combat
///  - Cyberpunk props scattered inside the arena
public class RebuildSceneIsometric
{
    // Shared palette — assigned once in Execute, reused everywhere
    static Material _bodyA, _bodyB;
    static Material _emitCyan, _emitPink, _emitPurple, _emitBlue, _emitAmber, _emitGreen;
    static Material _floorLine;

    [MenuItem("CyberBoss/Rebuild Scene Isometric")]
    public static void Execute()
    {
        InitPalette();
        FixCamera();
        CleanOldGeometry();
        RebuildFloor();
        PlaceBuildings();
        PlaceProps();
        RepositionLights();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("[CyberBoss] Isometric scene rebuilt.");
    }

    // ─── Camera ──────────────────────────────────────────────────────────

    static void FixCamera()
    {
        // 45 deg Y — diagonal isometric (looks into corner, not straight at a wall).
        // 48 deg X pitch — steep enough to see open floor in foreground clearly.
        var cm = GameObject.Find("CM_IsometricCamera");
        if (cm != null)
        {
            cm.transform.position = new Vector3(20f, 28f, -20f);
            cm.transform.rotation = Quaternion.Euler(48f, 45f, 0f);
        }

        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.pivot = new Vector3(0f, 2f, 0f);
            sv.rotation = Quaternion.Euler(48f, 45f, 0f);
            sv.size = 26f;
            sv.orthographic = false;
            sv.Repaint();
        }
    }

    // ─── Cleanup ─────────────────────────────────────────────────────────

    static void CleanOldGeometry()
    {
        string[] targets = {
            "Buildings", "NeonTrim", "CityBackground", "FloorDetails", "ArenaProps",
            "Arena/WallNorth", "Arena/WallSouth", "Arena/WallEast", "Arena/WallWest"
        };
        foreach (var n in targets)
        {
            var go = GameObject.Find(n);
            if (go != null) Object.DestroyImmediate(go);
        }
    }

    // ─── Floor ───────────────────────────────────────────────────────────

    static void RebuildFloor()
    {
        var floor = GameObject.Find("Arena/ArenaFloor") ?? GameObject.Find("ArenaFloor");
        if (floor == null) return;
        var mat = Mat("ArenaFloor", C(0.03f, 0.03f, 0.06f), 0.95f, 0.98f);
        floor.GetComponent<Renderer>().sharedMaterial = mat;
    }

    // ─── Buildings ───────────────────────────────────────────────────────
    // Arena floor is 20x20 (-10 to +10 on X and Z).
    // Buildings are placed with their centres at the floor edges so they
    // straddle the perimeter — inner face at ~edge, body extending outward.
    // This means the open combat area in the centre stays ~12x12 clear.

    static void PlaceBuildings()
    {
        var root = new GameObject("Buildings");

        // ── Corner towers ─────────────────────────────────────────────────
        // Back corners (far from camera) = TALL skyline presence.
        // Front corners (nearest the camera) = SHORT so they don't block the arena view.
        Tower(root, "TowerNE",  new Vector3( 13f, 0f,  13f), new Vector3(9f, 24f, 9f), _bodyA, _emitPurple, _emitCyan);
        Tower(root, "TowerNW",  new Vector3(-13f, 0f,  13f), new Vector3(9f, 20f, 9f), _bodyB, _emitCyan,   _emitPink);
        Tower(root, "TowerSE",  new Vector3( 13f, 0f, -13f), new Vector3(7f,  7f, 7f), _bodyA, _emitPink,   _emitAmber);
        Tower(root, "TowerSW",  new Vector3(-13f, 0f, -13f), new Vector3(7f,  6f, 7f), _bodyB, _emitBlue,   _emitCyan);

        // ── Back edge (far — full height buildings visible above arena) ───
        Building(root, "BldN1",  new Vector3( 4f, 0f,  13f), new Vector3(6f, 15f, 6f), _bodyA, _emitPink);
        Building(root, "BldN2",  new Vector3(-4f, 0f,  13f), new Vector3(6f, 13f, 6f), _bodyB, _emitAmber);

        // ── Side edges (medium — visible left and right of frame) ─────────
        Building(root, "BldE1",  new Vector3( 13f, 0f,  4f), new Vector3(6f, 14f, 6f), _bodyA, _emitAmber);
        Building(root, "BldE2",  new Vector3( 13f, 0f, -2f), new Vector3(5f, 10f, 5f), _bodyB, _emitCyan);
        Building(root, "BldW1",  new Vector3(-13f, 0f,  4f), new Vector3(6f, 16f, 6f), _bodyA, _emitGreen);
        Building(root, "BldW2",  new Vector3(-13f, 0f, -2f), new Vector3(5f, 11f, 5f), _bodyB, _emitPink);

        // ── Front edge (near camera) — kept very short so arena floor is visible ──
        Building(root, "BldS1",  new Vector3( 4f, 0f, -13f), new Vector3(5f, 5f, 4f), _bodyA, _emitCyan);
        Building(root, "BldS2",  new Vector3(-4f, 0f, -13f), new Vector3(4f, 4f, 4f), _bodyB, _emitPurple);
    }

    // Creates a building: body cube + window strips on all 4 faces + rooftop rim + edge pillars
    static void Tower(GameObject parent, string id,
                      Vector3 basePos, Vector3 size,
                      Material body, Material glow, Material accent)
    {
        // Body
        var go = Cube(parent, id, basePos + Vector3.up * size.y * 0.5f, size, body);

        float hw = size.x * 0.5f, hd = size.z * 0.5f;
        int rows = Mathf.Max(2, Mathf.FloorToInt(size.y / 3.2f));

        for (int r = 0; r < rows; r++)
        {
            float y = basePos.y + 1.6f + r * 3.2f;
            var winMat = (r % 2 == 0) ? glow : accent;
            // North face strip
            Cube(parent, $"{id}_WN{r}", new Vector3(basePos.x, y, basePos.z + hd + 0.06f),
                 new Vector3(size.x * 0.82f, 0.45f, 0.06f), winMat);
            // East face strip
            Cube(parent, $"{id}_WE{r}", new Vector3(basePos.x + hw + 0.06f, y, basePos.z),
                 new Vector3(0.06f, 0.45f, size.z * 0.82f), winMat);
            // South face
            Cube(parent, $"{id}_WS{r}", new Vector3(basePos.x, y, basePos.z - hd - 0.06f),
                 new Vector3(size.x * 0.82f, 0.45f, 0.06f), winMat);
            // West face
            Cube(parent, $"{id}_WW{r}", new Vector3(basePos.x - hw - 0.06f, y, basePos.z),
                 new Vector3(0.06f, 0.45f, size.z * 0.82f), winMat);
        }

        // Rooftop rim
        Cube(parent, $"{id}_Rim",
             new Vector3(basePos.x, basePos.y + size.y + 0.3f, basePos.z),
             new Vector3(size.x + 0.15f, 0.5f, size.z + 0.15f), glow);

        // Vertical edge pillars (4 corners of the tower face the arena)
        float ph = size.y;
        Cube(parent, $"{id}_PL", new Vector3(basePos.x - hw + 0.1f, basePos.y + ph * 0.5f, basePos.z - hd + 0.1f),
             new Vector3(0.14f, ph, 0.14f), glow);
        Cube(parent, $"{id}_PR", new Vector3(basePos.x + hw - 0.1f, basePos.y + ph * 0.5f, basePos.z - hd + 0.1f),
             new Vector3(0.14f, ph, 0.14f), accent);
    }

    static void Building(GameObject parent, string id,
                         Vector3 basePos, Vector3 size,
                         Material body, Material glow)
    {
        Tower(parent, id, basePos, size, body, glow, _emitCyan);
    }

    // ─── Arena props ─────────────────────────────────────────────────────
    // Scattered near the edges of the open area (not in the fight centre)

    static void PlaceProps()
    {
        var root = new GameObject("ArenaProps");

        // Lamp posts — near the four quadrant edges of the open space
        LampPost(root, "Lamp1",  new Vector3( 6f, 0f,  5f), _emitCyan);
        LampPost(root, "Lamp2",  new Vector3(-6f, 0f,  5f), _emitPink);
        LampPost(root, "Lamp3",  new Vector3( 6f, 0f, -5f), _emitPurple);
        LampPost(root, "Lamp4",  new Vector3(-6f, 0f, -5f), _emitBlue);

        // Neon sign poles standing inside the arena
        SignPole(root, "Sign1",  new Vector3( 7.5f, 0f,  2f), _emitPink);
        SignPole(root, "Sign2",  new Vector3(-7.5f, 0f, -2f), _emitCyan);
        SignPole(root, "Sign3",  new Vector3( 2f, 0f,  7.5f), _emitAmber);

        // Barrel clusters pushed to the sides
        BarrelCluster(root, "Barrels1", new Vector3( 7.5f, 0f, 0f));
        BarrelCluster(root, "Barrels2", new Vector3(-7.5f, 0f, 0f));

        // Tech terminal near north wall
        Terminal(root, "Terminal1", new Vector3(0f, 0f, 7f));

        // Floor circuit lines
        FloorGrid(root);
    }

    static void LampPost(GameObject parent, string id, Vector3 base_, Material glow)
    {
        // Post (Cylinder, scaled to thin pole)
        var post = Cylinder(parent, id + "_Post",
            base_ + Vector3.up * 3.5f,
            new Vector3(0.09f, 3.5f, 0.09f),
            _bodyA);
        // Crossarm
        Cube(parent, id + "_Arm",
            base_ + new Vector3(0.35f, 7f, 0f),
            new Vector3(0.7f, 0.08f, 0.08f), _bodyA);
        // Glow orb
        Sphere(parent, id + "_Orb",
            base_ + new Vector3(0.7f, 7f, 0f),
            0.35f, glow);
    }

    static void SignPole(GameObject parent, string id, Vector3 base_, Material glow)
    {
        Cylinder(parent, id + "_Pole", base_ + Vector3.up * 4f, new Vector3(0.1f, 4f, 0.1f), _bodyA);
        // Sign board
        Cube(parent, id + "_Board", base_ + new Vector3(0f, 8.3f, 0f), new Vector3(2.2f, 0.9f, 0.1f), glow);
        // Small accent below the board
        Cube(parent, id + "_Accent", base_ + new Vector3(0f, 7.7f, 0f), new Vector3(2.2f, 0.12f, 0.12f), glow);
    }

    static void BarrelCluster(GameObject parent, string id, Vector3 center)
    {
        var offsets = new[] { Vector3.zero, new Vector3(0.65f, 0f, 0.3f), new Vector3(-0.4f, 0f, 0.55f) };
        var mats = new[] { _emitCyan, _emitAmber, _emitGreen };
        for (int i = 0; i < offsets.Length; i++)
            Cylinder(parent, $"{id}_B{i}",
                center + offsets[i] + Vector3.up * 0.55f,
                new Vector3(0.45f, 0.55f, 0.45f), mats[i]);
    }

    static void Terminal(GameObject parent, string id, Vector3 base_)
    {
        Cube(parent, id + "_Body", base_ + Vector3.up * 0.9f, new Vector3(1.2f, 1.8f, 0.65f), _bodyB);
        // Screen
        Cube(parent, id + "_Screen", base_ + new Vector3(0f, 1.3f, 0.35f), new Vector3(0.85f, 0.65f, 0.05f), _emitGreen);
        // Keyboard ledge
        Cube(parent, id + "_Kbd", base_ + new Vector3(0f, 0.35f, 0.38f), new Vector3(0.9f, 0.08f, 0.3f), _emitCyan);
    }

    static void FloorGrid(GameObject parent)
    {
        // Thin circuit board lines floating just above the floor
        for (int i = -2; i <= 2; i++)
        {
            Cube(parent, $"FL_H{i}", new Vector3(i * 3f, 0.01f, 0f), new Vector3(0.04f, 0.01f, 18f), _floorLine);
            Cube(parent, $"FL_V{i}", new Vector3(0f, 0.01f, i * 3f), new Vector3(18f, 0.01f, 0.04f), _floorLine);
        }
        // Crosshair diagonals at centre
        var diagObj = GameObject.CreatePrimitive(PrimitiveType.Cube);
        diagObj.name = "FL_DiagA";
        diagObj.transform.SetParent(parent.transform);
        diagObj.transform.position = new Vector3(0f, 0.01f, 0f);
        diagObj.transform.localScale = new Vector3(0.04f, 0.01f, 18f);
        diagObj.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
        diagObj.GetComponent<Renderer>().sharedMaterial = _floorLine;
        Object.DestroyImmediate(diagObj.GetComponent<BoxCollider>());

        var diagObj2 = GameObject.CreatePrimitive(PrimitiveType.Cube);
        diagObj2.name = "FL_DiagB";
        diagObj2.transform.SetParent(parent.transform);
        diagObj2.transform.position = new Vector3(0f, 0.01f, 0f);
        diagObj2.transform.localScale = new Vector3(0.04f, 0.01f, 18f);
        diagObj2.transform.rotation = Quaternion.Euler(0f, -45f, 0f);
        diagObj2.GetComponent<Renderer>().sharedMaterial = _floorLine;
        Object.DestroyImmediate(diagObj2.GetComponent<BoxCollider>());
    }

    // ─── Lights ──────────────────────────────────────────────────────────

    static void RepositionLights()
    {
        // Move existing point lights to near-building positions so they cast
        // coloured fills onto the open floor from the corners
        MoveLight("NeonCyan",        new Vector3(-7f, 8f,  9f));
        MoveLight("NeonMagenta",     new Vector3( 7f, 8f,  9f));
        MoveLight("NeonPurple",      new Vector3( 9f, 8f, -7f));
        MoveLight("NeonBlue",        new Vector3(-9f, 8f, -7f));
        MoveLight("NeonPink1",       new Vector3( 0f, 5f,  8f));
        MoveLight("NeonPink2",       new Vector3( 0f, 5f, -8f));
        MoveLight("CityLightNorth",  new Vector3( 0f, 4f,  5f));
        MoveLight("CityLightSouth",  new Vector3( 0f, 4f, -5f));
    }

    static void MoveLight(string name, Vector3 pos)
    {
        var go = GameObject.Find("Lights/" + name) ?? GameObject.Find(name);
        if (go != null) go.transform.position = pos;
    }

    // ─── Palette ─────────────────────────────────────────────────────────

    static void InitPalette()
    {
        _bodyA    = Mat("Bld_DarkA",   C(0.03f, 0.02f, 0.05f), 0.3f, 0.4f);
        _bodyB    = Mat("Bld_DarkB",   C(0.04f, 0.04f, 0.06f), 0.3f, 0.4f);
        _emitCyan   = Emit("E_Cyan",   C(0,0,0), new Color(0f,   4.0f, 4.0f));
        _emitPink   = Emit("E_Pink",   C(0,0,0), new Color(4.8f, 0f,   3.2f));
        _emitPurple = Emit("E_Purple", C(0,0,0), new Color(2.4f, 0f,   4.8f));
        _emitBlue   = Emit("E_Blue",   C(0,0,0), new Color(0f,   1.2f, 4.8f));
        _emitAmber  = Emit("E_Amber",  C(0,0,0), new Color(4.0f, 2.4f, 0f));
        _emitGreen  = Emit("E_Green",  C(0,0,0), new Color(0f,   4.0f, 1.2f));
        _floorLine  = Emit("E_FloorLine", C(0,0,0), new Color(0.5f, 0f, 2.0f));
    }

    // ─── Primitive helpers ────────────────────────────────────────────────

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

    static GameObject Cylinder(GameObject parent, string name, Vector3 pos, Vector3 scale, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = name;
        go.transform.SetParent(parent.transform);
        go.transform.position = pos;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
        return go;
    }

    static GameObject Sphere(GameObject parent, string name, Vector3 pos, float radius, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = name;
        go.transform.SetParent(parent.transform);
        go.transform.position = pos;
        go.transform.localScale = Vector3.one * radius * 2f;
        go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<SphereCollider>());
        return go;
    }

    // ─── Material helpers ─────────────────────────────────────────────────

    static Material Mat(string id, Color base_, float metallic, float smooth)
    {
        string p = $"Assets/Materials/{id}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
        m.SetColor("_BaseColor", base_);
        m.SetFloat("_Metallic", metallic);
        m.SetFloat("_Smoothness", smooth);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Emit(string id, Color base_, Color hdr)
    {
        string p = $"Assets/Materials/{id}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p);
        if (m == null) { m = new Material(Shader.Find("Universal Render Pipeline/Lit")); AssetDatabase.CreateAsset(m, p); }
        m.SetColor("_BaseColor", base_);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", 0.5f);
        m.SetColor("_EmissionColor", hdr);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Color C(float r, float g, float b) => new Color(r, g, b);
}

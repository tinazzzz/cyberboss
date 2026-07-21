using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Final arena rebuild.
/// Layout rules:
///   - NO buildings south of z=0 — the foreground (near camera) must stay clear
///   - Buildings only at back (z=+13) and sides (x=±13), graded tall→short front-to-back
///   - Variety props inside the arena: holographic columns, crates, vents, dumpsters, sign poles
///   - 8 coloured point lights for cyberpunk ambience
public class RebuildArenaFinal
{
    static Material _bodyA, _bodyB;
    static Material _eCyan, _ePink, _ePurple, _eBlue, _eAmber, _eGreen, _eWhite;
    static Material _floorMat, _floorLine;

    [MenuItem("CyberBoss/Rebuild Arena Final")]
    public static void Execute()
    {
        InitPalette();
        FixCamera();
        Cleanup();
        UpdateFloor();
        PlaceBuildings();
        PlaceProps();
        SetupLights();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        AssetDatabase.SaveAssets();
        Debug.Log("[CyberBoss] Arena final rebuild complete.");
    }

    // ── Camera ────────────────────────────────────────────────────────────

    static void FixCamera()
    {
        var cm = GameObject.Find("CM_IsometricCamera");
        if (cm != null)
        {
            cm.transform.position = new Vector3(20f, 28f, -20f);
            cm.transform.rotation = Quaternion.Euler(48f, 45f, 0f);
        }
        var sv = SceneView.lastActiveSceneView;
        if (sv != null)
        {
            sv.pivot       = new Vector3(0f, 2f, 0f);
            sv.rotation    = Quaternion.Euler(48f, 45f, 0f);
            sv.size        = 28f;
            sv.orthographic = false;
            sv.Repaint();
        }
    }

    // ── Cleanup ───────────────────────────────────────────────────────────

    static void Cleanup()
    {
        foreach (var name in new[] { "Buildings", "ArenaProps", "NeonTrim",
                                     "CityBackground", "FloorDetails",
                                     "Arena/WallNorth", "Arena/WallSouth",
                                     "Arena/WallEast",  "Arena/WallWest" })
        {
            var go = GameObject.Find(name);
            if (go != null) Object.DestroyImmediate(go);
        }
    }

    // ── Floor ─────────────────────────────────────────────────────────────

    static void UpdateFloor()
    {
        var floor = GameObject.Find("Arena/ArenaFloor") ?? GameObject.Find("ArenaFloor");
        if (floor == null) return;
        floor.GetComponent<Renderer>().sharedMaterial = _floorMat;
    }

    // ── Buildings ─────────────────────────────────────────────────────────
    // Only back and side buildings. Nothing south of z = -1.
    // Heights taper down toward the camera so the skyline is dramatic
    // in the back but doesn't obstruct the arena floor in the foreground.

    static void PlaceBuildings()
    {
        var root = new GameObject("Buildings");

        // Back corners — tallest, anchor the skyline
        Bld(root, "TowerNE",  V( 13f, 13f), 9f, 26f, 9f, _bodyA, _ePurple, _eCyan);
        Bld(root, "TowerNW",  V(-13f, 13f), 9f, 22f, 9f, _bodyB, _eCyan,   _ePink);

        // Back-centre — medium-tall, fills gap between corner towers
        Bld(root, "BldN1",   V(  4f, 13f), 6f, 16f, 6f, _bodyA, _ePink,   _eAmber);
        Bld(root, "BldN2",   V( -4f, 13f), 6f, 14f, 6f, _bodyB, _eAmber,  _eCyan);

        // East side — medium height, visible on right side of frame
        Bld(root, "BldE1",   V( 13f,  5f), 6f, 15f, 6f, _bodyA, _eAmber,  _ePurple);
        Bld(root, "BldE2",   V( 13f, -1f), 5f,  8f, 5f, _bodyB, _eCyan,   _ePink);

        // West side — medium height, visible on left side of frame
        Bld(root, "BldW1",   V(-13f,  5f), 6f, 17f, 6f, _bodyA, _eGreen,  _eCyan);
        Bld(root, "BldW2",   V(-13f, -1f), 5f,  7f, 5f, _bodyB, _ePink,   _ePurple);
    }

    // One building: body + 2 horizontal accent bands + rooftop rim + 2 edge glow pillars.
    // Minimal geometry to avoid script timeout.
    static void Bld(GameObject root, string id, Vector2 xz,
                    float sx, float sy, float sz,
                    Material body, Material glowA, Material glowB)
    {
        float hx = sx * 0.5f, hz = sz * 0.5f;

        // Body
        Cube(root, id, new Vector3(xz.x, sy * 0.5f, xz.y), new Vector3(sx, sy, sz), body);

        // Window band A (lower third)
        float yA = sy * 0.28f;
        HBand(root, id + "_BA", xz, yA, sx, sz, hx, hz, glowA);

        // Window band B (upper half)
        float yB = sy * 0.62f;
        HBand(root, id + "_BB", xz, yB, sx, sz, hx, hz, glowB);

        // Rooftop rim
        Cube(root, id + "_Rim",
             new Vector3(xz.x, sy + 0.3f, xz.y),
             new Vector3(sx + 0.15f, 0.5f, sz + 0.15f), glowA);

        // Vertical edge pillars on the arena-facing faces
        Cube(root, id + "_PL",
             new Vector3(xz.x - hx + 0.1f, sy * 0.5f, xz.y - hz + 0.1f),
             new Vector3(0.14f, sy, 0.14f), glowA);
        Cube(root, id + "_PR",
             new Vector3(xz.x + hx - 0.1f, sy * 0.5f, xz.y - hz + 0.1f),
             new Vector3(0.14f, sy, 0.14f), glowB);
    }

    // One horizontal accent band wrapping the front two faces of a building.
    static void HBand(GameObject root, string id, Vector2 xz,
                      float y, float sx, float sz, float hx, float hz,
                      Material mat)
    {
        // Front face (facing arena, -Z direction)
        Cube(root, id + "_F",
             new Vector3(xz.x, y, xz.y - hz - 0.06f),
             new Vector3(sx * 0.85f, 0.4f, 0.06f), mat);
        // Side face
        Cube(root, id + "_S",
             new Vector3(xz.x + hx + 0.06f, y, xz.y),
             new Vector3(0.06f, 0.4f, sz * 0.85f), mat);
    }

    // ── Props ─────────────────────────────────────────────────────────────
    // Placed inside the arena, away from the centre combat zone.
    // Nothing south of z = -5 (clear foreground for camera).

    static void PlaceProps()
    {
        var root = new GameObject("ArenaProps");

        // ── Holographic display columns (glowing cylinder + flat top panel) ──
        HoloColumn(root, "Holo1", new Vector3( 7f, 0f,  6f), _eCyan);
        HoloColumn(root, "Holo2", new Vector3(-7f, 0f,  6f), _ePurple);

        // ── Neon sign poles ───────────────────────────────────────────────
        SignPole(root, "Sign1", new Vector3( 8.5f, 0f,  2f),  _ePink);
        SignPole(root, "Sign2", new Vector3(-8.5f, 0f,  2f),  _eCyan);
        SignPole(root, "Sign3", new Vector3(  0f,  0f,  9f),  _eAmber);

        // ── Tech crate stacks ─────────────────────────────────────────────
        CrateStack(root, "Crates1", new Vector3( 8f, 0f, -1f));
        CrateStack(root, "Crates2", new Vector3(-8f, 0f, -1f));

        // ── Dumpsters ─────────────────────────────────────────────────────
        Dumpster(root, "Dump1", new Vector3( 7f, 0f, 8f),  _eGreen);
        Dumpster(root, "Dump2", new Vector3(-7f, 0f, 8f),  _ePink);

        // ── Ground vents / glow grates (flush with floor) ─────────────────
        GlowGrate(root, "Vent1", new Vector3( 5f, 0f,  3f), _eCyan);
        GlowGrate(root, "Vent2", new Vector3(-5f, 0f,  3f), _ePurple);
        GlowGrate(root, "Vent3", new Vector3( 0f, 0f,  6f), _ePink);
        GlowGrate(root, "Vent4", new Vector3( 0f, 0f, -3f), _eBlue);

        // ── Electricity / antenna posts ────────────────────────────────────
        AntennaPost(root, "Ant1", new Vector3( 9f, 0f, 9f),  _eCyan);
        AntennaPost(root, "Ant2", new Vector3(-9f, 0f, 9f),  _ePink);

        // ── Floor circuit lines ────────────────────────────────────────────
        FloorGrid(root);
    }

    static void HoloColumn(GameObject root, string id, Vector3 base_, Material glow)
    {
        // Pedestal
        Cube(root, id + "_Ped", base_ + V3(0, 0.3f, 0), new Vector3(0.8f, 0.6f, 0.8f), _bodyA);
        // Shaft
        Cylinder(root, id + "_Shaft", base_ + V3(0, 1.5f, 0), new Vector3(0.15f, 1.2f, 0.15f), _bodyB);
        // Hologram panel (thin, floating, emissive)
        Cube(root, id + "_Panel", base_ + V3(0, 3.2f, 0), new Vector3(1.4f, 1.8f, 0.04f), glow);
        // Base ring glow
        Cylinder(root, id + "_Ring", base_ + V3(0, 0.05f, 0), new Vector3(1.1f, 0.04f, 1.1f), glow);
    }

    static void SignPole(GameObject root, string id, Vector3 base_, Material glow)
    {
        Cylinder(root, id + "_Pole", base_ + V3(0, 3.5f, 0), new Vector3(0.1f, 3.5f, 0.1f), _bodyA);
        // Horizontal arm
        Cube(root, id + "_Arm",  base_ + V3(0.5f, 7f, 0),  new Vector3(1f, 0.08f, 0.08f), _bodyA);
        // Sign board
        Cube(root, id + "_Sign", base_ + V3(1f, 7.5f, 0),   new Vector3(1.8f, 0.85f, 0.08f), glow);
        // Trim strip below sign
        Cube(root, id + "_Trim", base_ + V3(1f, 7.02f, 0),  new Vector3(1.8f, 0.1f, 0.1f),  glow);
    }

    static void CrateStack(GameObject root, string id, Vector3 base_)
    {
        // Bottom crate (large)
        Cube(root, id + "_C1", base_ + V3(0, 0.45f, 0),       new Vector3(1.2f, 0.9f, 1f),  _bodyA);
        Cube(root, id + "_E1", base_ + V3(0, 0.9f, 0),        new Vector3(1.22f, 0.08f, 1.02f), _eCyan);
        // Second crate (medium, offset)
        Cube(root, id + "_C2", base_ + V3(0.3f, 1.55f, 0.1f), new Vector3(0.9f, 0.7f, 0.9f), _bodyB);
        Cube(root, id + "_E2", base_ + V3(0.3f, 1.9f, 0.1f),  new Vector3(0.92f, 0.08f, 0.92f), _eAmber);
        // Third crate (small)
        Cube(root, id + "_C3", base_ + V3(-0.2f, 2.3f, 0),    new Vector3(0.7f, 0.5f, 0.7f), _bodyA);
    }

    static void Dumpster(GameObject root, string id, Vector3 base_, Material accent)
    {
        // Body
        Cube(root, id + "_Body", base_ + V3(0, 0.55f, 0), new Vector3(1.8f, 1.1f, 0.9f), _bodyA);
        // Lid
        Cube(root, id + "_Lid",  base_ + V3(0, 1.15f, 0), new Vector3(1.82f, 0.15f, 0.92f), _bodyB);
        // Accent stripe
        Cube(root, id + "_Str",  base_ + V3(0, 0.55f, 0.47f), new Vector3(1.6f, 0.25f, 0.04f), accent);
        // Warning stripe
        Cube(root, id + "_Warn", base_ + V3(0, 0.85f, 0.47f), new Vector3(1.6f, 0.12f, 0.04f), _eAmber);
    }

    static void GlowGrate(GameObject root, string id, Vector3 base_, Material glow)
    {
        // Flat grate flush with floor
        Cube(root, id + "_Grate", base_ + V3(0, 0.008f, 0), new Vector3(1.2f, 0.01f, 0.8f), _bodyB);
        // Glowing slots
        Cube(root, id + "_S1", base_ + V3(-0.3f, 0.015f, 0), new Vector3(0.2f, 0.015f, 0.7f), glow);
        Cube(root, id + "_S2", base_ + V3( 0.0f, 0.015f, 0), new Vector3(0.2f, 0.015f, 0.7f), glow);
        Cube(root, id + "_S3", base_ + V3( 0.3f, 0.015f, 0), new Vector3(0.2f, 0.015f, 0.7f), glow);
        // Wisps of fog/light (thin vertical cube = "steam")
        Cube(root, id + "_Steam", base_ + V3(0, 0.3f, 0), new Vector3(0.6f, 0.6f, 0.6f), glow);
    }

    static void AntennaPost(GameObject root, string id, Vector3 base_, Material glow)
    {
        Cylinder(root, id + "_Post", base_ + V3(0, 4f, 0), new Vector3(0.08f, 4f, 0.08f), _bodyA);
        // Cross-arm
        Cube(root, id + "_Arm", base_ + V3(0, 8.1f, 0), new Vector3(1.2f, 0.06f, 0.06f), _bodyA);
        // Tip orb
        Sphere(root, id + "_Tip", base_ + V3(0, 8.4f, 0), 0.2f, glow);
        // Insulators
        Sphere(root, id + "_L", base_ + V3(-0.55f, 8.1f, 0), 0.12f, glow);
        Sphere(root, id + "_R", base_ + V3( 0.55f, 8.1f, 0), 0.12f, glow);
    }

    static void FloorGrid(GameObject root)
    {
        for (int i = -2; i <= 2; i++)
        {
            Cube(root, $"FL_H{i}", new Vector3(i * 3.5f, 0.012f, 0f), new Vector3(0.04f, 0.01f, 20f), _floorLine);
            Cube(root, $"FL_V{i}", new Vector3(0f, 0.012f, i * 3.5f), new Vector3(20f, 0.01f, 0.04f), _floorLine);
        }
        // Diagonal accents
        for (int i = -1; i <= 1; i += 2)
        {
            var d = Cube(root, $"FL_D{i}", Vector3.up * 0.012f, new Vector3(0.04f, 0.01f, 18f), _floorLine);
            d.transform.rotation = Quaternion.Euler(0f, i * 45f, 0f);
        }
    }

    // ── Lighting ──────────────────────────────────────────────────────────

    static void SetupLights()
    {
        // Remove old prop lights group if it exists
        var old = GameObject.Find("ArenaLights");
        if (old != null) Object.DestroyImmediate(old);

        var root = new GameObject("ArenaLights");

        // 4 corner fills — near back buildings, casting onto the floor
        AddLight(root, "LNE",  new Vector3( 8f, 7f, 8f),  new Color(0.4f, 0f, 1f),   4.5f, 18f);  // purple
        AddLight(root, "LNW",  new Vector3(-8f, 7f, 8f),  new Color(0f, 0.9f, 1f),   4.5f, 18f);  // cyan
        AddLight(root, "LE",   new Vector3( 9f, 5f, 0f),  new Color(1f, 0.5f, 0f),   3.5f, 14f);  // amber
        AddLight(root, "LW",   new Vector3(-9f, 5f, 0f),  new Color(1f, 0f, 0.6f),   3.5f, 14f);  // pink

        // 2 mid-arena fill lights — low, soft, coloured
        AddLight(root, "LMid1", new Vector3( 4f, 3f, 0f), new Color(0.1f, 0.4f, 1f), 2.5f, 12f); // blue
        AddLight(root, "LMid2", new Vector3(-4f, 3f, 0f), new Color(1f, 0f, 0.8f),   2.5f, 12f); // magenta

        // 2 ground vent upward accent lights — warm floor glow
        AddLight(root, "LVent1", new Vector3( 5f, 0.5f,  3f), new Color(0f, 1f, 0.8f), 1.5f, 6f); // teal
        AddLight(root, "LVent2", new Vector3(-5f, 0.5f,  3f), new Color(0.6f, 0f, 1f), 1.5f, 6f); // purple

        // Reposition existing Lights/ objects that were left from earlier builds
        MoveLight("NeonCyan",       new Vector3(-8f, 8f,  9f));
        MoveLight("NeonMagenta",    new Vector3( 8f, 8f,  9f));
        MoveLight("NeonPurple",     new Vector3( 9f, 8f, -5f));
        MoveLight("NeonBlue",       new Vector3(-9f, 8f, -5f));
        MoveLight("NeonPink1",      new Vector3( 0f, 6f,  8f));
        MoveLight("NeonPink2",      new Vector3( 0f, 6f, -4f));
        MoveLight("CityLightNorth", new Vector3( 0f, 4f,  5f));
        MoveLight("CityLightSouth", new Vector3( 0f, 4f, -2f));
    }

    static void AddLight(GameObject parent, string id, Vector3 pos,
                         Color col, float intensity, float range)
    {
        var go = new GameObject(id);
        go.transform.SetParent(parent.transform);
        go.transform.position = pos;
        var lt = go.AddComponent<Light>();
        lt.type      = LightType.Point;
        lt.color     = col;
        lt.intensity = intensity;
        lt.range     = range;
        lt.shadows   = LightShadows.None;   // WebGL: no per-pixel shadows on point lights
    }

    static void MoveLight(string name, Vector3 pos)
    {
        var go = GameObject.Find("Lights/" + name) ?? GameObject.Find(name);
        if (go != null) go.transform.position = pos;
    }

    // ── Palette ───────────────────────────────────────────────────────────

    static void InitPalette()
    {
        _bodyA    = Mat("PA_BodyA",  C(0.03f, 0.02f, 0.05f), 0.3f, 0.4f);
        _bodyB    = Mat("PA_BodyB",  C(0.05f, 0.04f, 0.07f), 0.3f, 0.4f);
        _eCyan    = Emit("PA_ECyan",   C(0,0,0), new Color(0f,    4.0f,  4.0f));
        _ePink    = Emit("PA_EPink",   C(0,0,0), new Color(4.8f,  0f,    3.2f));
        _ePurple  = Emit("PA_EPurple", C(0,0,0), new Color(2.4f,  0f,    4.8f));
        _eBlue    = Emit("PA_EBlue",   C(0,0,0), new Color(0f,    1.2f,  4.8f));
        _eAmber   = Emit("PA_EAmber",  C(0,0,0), new Color(4.0f,  2.4f,  0f));
        _eGreen   = Emit("PA_EGreen",  C(0,0,0), new Color(0f,    4.0f,  1.6f));
        _eWhite   = Emit("PA_EWhite",  C(0,0,0), new Color(3.0f,  3.0f,  3.0f));
        _floorMat = Mat("PA_Floor",  C(0.03f, 0.03f, 0.06f), 0.95f, 0.98f);
        _floorLine= Emit("PA_FLine",   C(0,0,0), new Color(0.5f,  0f,    2.0f));
    }

    // ── Primitive helpers ─────────────────────────────────────────────────

    static GameObject Cube(GameObject p, string n, Vector3 pos, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = n; go.transform.SetParent(p.transform);
        go.transform.position = pos; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<BoxCollider>());
        return go;
    }

    static void Cylinder(GameObject p, string n, Vector3 pos, Vector3 scale, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
        go.name = n; go.transform.SetParent(p.transform);
        go.transform.position = pos; go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<CapsuleCollider>());
    }

    static void Sphere(GameObject p, string n, Vector3 pos, float r, Material m)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        go.name = n; go.transform.SetParent(p.transform);
        go.transform.position = pos; go.transform.localScale = Vector3.one * r * 2f;
        go.GetComponent<Renderer>().sharedMaterial = m;
        Object.DestroyImmediate(go.GetComponent<SphereCollider>());
    }

    // ── Material helpers ──────────────────────────────────────────────────

    static Material Mat(string id, Color b, float met, float smo)
    {
        string p = $"Assets/Materials/{id}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p)
             ?? NewMat(p);
        m.SetColor("_BaseColor", b);
        m.SetFloat("_Metallic", met);
        m.SetFloat("_Smoothness", smo);
        EditorUtility.SetDirty(m); return m;
    }

    static Material Emit(string id, Color b, Color hdr)
    {
        string p = $"Assets/Materials/{id}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p) ?? NewMat(p);
        m.SetColor("_BaseColor", b);
        m.SetFloat("_Metallic", 0f);
        m.SetFloat("_Smoothness", 0.5f);
        m.SetColor("_EmissionColor", hdr);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m); return m;
    }

    static Material NewMat(string path)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    static Color C(float r, float g, float b) => new Color(r, g, b);
    static Vector3 V3(float x, float y, float z) => new Vector3(x, y, z);
    static Vector2 V(float x, float z)            => new Vector2(x, z);
}

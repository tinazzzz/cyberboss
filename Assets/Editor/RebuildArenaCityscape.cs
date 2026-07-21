using UnityEngine;
using UnityEditor;

/// Tears down the old arena-with-walls approach and replaces it with detailed
/// cyberpunk building facades that surround the fight floor on all four sides.
/// Buildings ARE the arena boundary — no separate wall geometry.
public class RebuildArenaCityscape
{
    // ── Materials ──────────────────────────────────────────────────────────
    static Material _mBody, _mCyanWin, _mAmberWin, _mPinkSign, _mCyanSign,
                    _mPurpleSign, _mTrimPink, _mTrimCyan, _mPillarPink,
                    _mPillarCyan, _mDark, _mFloorLine;

    // ── Entry point ────────────────────────────────────────────────────────
    public static void Execute()
    {
        // 1. Destroy old geometry
        Destroy("Arena/WallNorth"); Destroy("Arena/WallSouth");
        Destroy("Arena/WallEast");  Destroy("Arena/WallWest");
        Destroy("NeonTrim"); Destroy("CityBackground");

        // 2. Materialise
        _mBody      = Mat("BldBody",      C(0.02f,0.02f,0.04f), Color.black,         0.1f,0.2f);
        _mCyanWin   = Emit("WinCyan",     C(0.01f,0.02f,0.03f), H(0,4f,3.5f));
        _mAmberWin  = Emit("WinAmber",    C(0.03f,0.02f,0.01f), H(5f,2f,0));
        _mPinkSign  = Emit("SignPink",    C(0.03f,0.01f,0.03f), H(9f,0,3f));
        _mCyanSign  = Emit("SignCyan",    C(0.01f,0.03f,0.03f), H(0,6f,5f));
        _mPurpleSign= Emit("SignPurple",  C(0.02f,0.01f,0.04f), H(3f,0,9f));
        _mTrimPink  = Emit("TrimPink",    C(0.02f,0.01f,0.02f), H(3f,0,1.5f));
        _mTrimCyan  = Emit("TrimCyan",    C(0.01f,0.02f,0.02f), H(0,2f,2f));
        _mPillarPink= Emit("PillarPink",  C(0.02f,0.01f,0.02f), H(8f,0,4f));
        _mPillarCyan= Emit("PillarCyan",  C(0.01f,0.02f,0.03f), H(0,5f,4f));
        _mDark      = Mat("DarkMetal",    C(0.04f,0.04f,0.05f), Color.black, 0.6f,0.4f);
        _mFloorLine = Emit("FloorLine",   C(0.02f,0.02f,0.04f), H(0.4f,0,1.2f));
        AssetDatabase.SaveAssets();

        // 3. Build facades (buildings wrap tightly around the 20×20 floor)
        var root = new GameObject("Buildings");

        BuildNorth(root.transform);   // z = +10 … +13
        BuildSouth(root.transform);   // z = -10 … -13
        BuildEast(root.transform);    // x = +10 … +13
        BuildWest(root.transform);    // x = -10 … -13
        BuildCorners(root.transform); // 4 corner pillars

        // 4. Floor circuit lines
        AddFloorDetails();

        AssetDatabase.SaveAssets();
        Debug.Log("[CyberBoss] Cityscape rebuild complete.");
    }

    // ── North facade (most visible backdrop from isometric camera) ─────────
    static void BuildNorth(Transform p)
    {
        float fz = 11.5f;   // front face z (arena-side)
        float bz = 13.0f;   // body center z
        float h  = 12f;     // building height
        float w  = 36f;     // wide enough to fill frame

        // Main body
        Box("N_Body",   V(0, h*.5f, bz),        V(w, h, 3f),    _mBody, p);

        // Awning / street canopy at base
        Box("N_Awning", V(0, 1.1f, fz-.35f),    V(w, 0.2f, 0.7f), _mTrimPink, p);

        // Window rows (3 floors)
        Box("N_Win1",   V(0, 1.8f, fz+.02f),    V(w-3f, 0.55f, 0.06f), _mCyanWin,  p);
        Box("N_Win2",   V(0, 4.8f, fz+.02f),    V(w-3f, 0.55f, 0.06f), _mAmberWin, p);
        Box("N_Win3",   V(0, 7.8f, fz+.02f),    V(w-3f, 0.55f, 0.06f), _mCyanWin,  p);

        // Horizontal floor dividers
        Box("N_Div1",   V(0, 3.1f, bz),         V(w, 0.18f, 3.1f), _mTrimPink, p);
        Box("N_Div2",   V(0, 6.1f, bz),         V(w, 0.18f, 3.1f), _mTrimCyan, p);

        // Neon signs (flush with face, facing south into arena)
        Box("N_Sign1",  V( 8f, 5.0f, fz),       V(8f, 1.5f, 0.08f), _mPinkSign,   p);
        Box("N_Sign2",  V(-8f, 7.2f, fz),       V(9f, 1.1f, 0.08f), _mCyanSign,   p);
        Box("N_Board",  V(-1f, 9.8f, fz+.02f),  V(12f,2.8f, 0.08f), _mPurpleSign, p);
        Box("N_SmSign", V( 5f, 9.2f, fz),       V(3f, 0.7f, 0.08f), _mCyanSign,   p);

        // Vertical neon pillar accents
        Box("N_PilL",   V(-16f, h*.5f, fz+.05f),V(0.3f, h, 0.3f), _mPillarPink, p);
        Box("N_PilR",   V( 16f, h*.5f, fz+.05f),V(0.3f, h, 0.3f), _mPillarCyan, p);
        Box("N_PilM",   V(  0f, h*.5f, fz+.05f),V(0.2f, h, 0.2f), _mPillarPink, p);

        // AC units (small protrusions from facade)
        AcUnit("N_AC1", V(-5f, 3.5f, fz-.22f), p);
        AcUnit("N_AC2", V( 4f, 3.5f, fz-.22f), p);
        AcUnit("N_AC3", V(-2f, 6.5f, fz-.22f), p);
        AcUnit("N_AC4", V( 9f, 6.5f, fz-.22f), p);

        // Rooftop detail
        Box("N_Roof1",  V( 6f, h+.5f, bz-.2f), V(5f, 1.0f, 2.5f), _mDark, p);
        Box("N_Roof2",  V(-5f, h+.6f, bz),     V(3f, 1.2f, 2.5f), _mDark, p);
        Box("N_AntL",   V(-14f, h+1.5f, bz),   V(0.12f, 3f, 0.12f), _mDark, p);
        Box("N_AntR",   V( 14f, h+1.5f, bz),   V(0.12f, 3f, 0.12f), _mDark, p);
    }

    // ── South facade (behind camera, simpler — only partial visibility) ────
    static void BuildSouth(Transform p)
    {
        float fz = -11.5f; float bz = -13.0f; float h = 12f; float w = 36f;
        Box("S_Body",  V(0, h*.5f, bz),       V(w, h, 3f),   _mBody, p);
        Box("S_Awning",V(0, 1.1f, fz+.35f),   V(w, 0.2f, 0.7f), _mTrimCyan, p);
        Box("S_Win1",  V(0, 1.8f, fz-.02f),   V(w-3f, 0.55f, 0.06f), _mAmberWin, p);
        Box("S_Win2",  V(0, 4.8f, fz-.02f),   V(w-3f, 0.55f, 0.06f), _mCyanWin,  p);
        Box("S_Div1",  V(0, 3.1f, bz),        V(w, 0.18f, 3.1f), _mTrimCyan, p);
        Box("S_Sign1", V( 6f, 5.0f, fz),      V(7f, 1.3f, 0.08f), _mPinkSign, p);
        Box("S_Sign2", V(-7f, 7.0f, fz),      V(8f, 1.0f, 0.08f), _mCyanSign, p);
        Box("S_PilL",  V(-16f, h*.5f, fz-.05f),V(0.3f,h, 0.3f), _mPillarCyan, p);
        Box("S_PilR",  V( 16f, h*.5f, fz-.05f),V(0.3f,h, 0.3f), _mPillarPink, p);
    }

    // ── East facade ────────────────────────────────────────────────────────
    static void BuildEast(Transform p)
    {
        float fx = 11.5f; float bx = 13.0f; float h = 12f; float d = 36f;
        Box("E_Body",  V(bx,  h*.5f, 0),      V(3f, h, d),   _mBody, p);
        Box("E_Awning",V(fx-.35f, 1.1f, 0),   V(0.7f, 0.2f, d), _mTrimCyan, p);
        Box("E_Win1",  V(fx-.02f, 1.8f, 0),   V(0.06f, 0.55f, d-3f), _mCyanWin,  p);
        Box("E_Win2",  V(fx-.02f, 4.8f, 0),   V(0.06f, 0.55f, d-3f), _mAmberWin, p);
        Box("E_Win3",  V(fx-.02f, 7.8f, 0),   V(0.06f, 0.55f, d-3f), _mCyanWin,  p);
        Box("E_Div1",  V(bx, 3.1f, 0),        V(3.1f, 0.18f, d), _mTrimPink, p);
        Box("E_Div2",  V(bx, 6.1f, 0),        V(3.1f, 0.18f, d), _mTrimCyan, p);
        Box("E_Sign1", V(fx, 5.0f, -5f),      V(0.08f, 1.5f, 7f), _mPurpleSign, p);
        Box("E_Sign2", V(fx, 7.5f,  5f),      V(0.08f, 1.1f, 8f), _mPinkSign,   p);
        Box("E_Board", V(fx, 9.5f,  0f),      V(0.08f, 2.5f,10f), _mCyanSign,   p);
        Box("E_PilF",  V(fx+.05f, h*.5f,-16f),V(0.3f, h, 0.3f), _mPillarPink, p);
        Box("E_PilB",  V(fx+.05f, h*.5f, 16f),V(0.3f, h, 0.3f), _mPillarCyan, p);
        AcUnit("E_AC1", V(fx-.22f, 3.5f, -4f), p, true);
        AcUnit("E_AC2", V(fx-.22f, 3.5f,  4f), p, true);
        AcUnit("E_AC3", V(fx-.22f, 6.5f,  1f), p, true);
    }

    // ── West facade ────────────────────────────────────────────────────────
    static void BuildWest(Transform p)
    {
        float fx = -11.5f; float bx = -13.0f; float h = 12f; float d = 36f;
        Box("W_Body",  V(bx,  h*.5f, 0),      V(3f, h, d),   _mBody, p);
        Box("W_Awning",V(fx+.35f, 1.1f, 0),   V(0.7f, 0.2f, d), _mTrimPink, p);
        Box("W_Win1",  V(fx+.02f, 1.8f, 0),   V(0.06f, 0.55f, d-3f), _mAmberWin, p);
        Box("W_Win2",  V(fx+.02f, 4.8f, 0),   V(0.06f, 0.55f, d-3f), _mCyanWin,  p);
        Box("W_Win3",  V(fx+.02f, 7.8f, 0),   V(0.06f, 0.55f, d-3f), _mAmberWin, p);
        Box("W_Div1",  V(bx, 3.1f, 0),        V(3.1f, 0.18f, d), _mTrimCyan, p);
        Box("W_Div2",  V(bx, 6.1f, 0),        V(3.1f, 0.18f, d), _mTrimPink, p);
        Box("W_Sign1", V(fx, 5.0f,  6f),      V(0.08f, 1.5f, 8f), _mCyanSign,   p);
        Box("W_Sign2", V(fx, 7.5f, -5f),      V(0.08f, 1.0f, 7f), _mPurpleSign, p);
        Box("W_Board", V(fx, 9.5f,  1f),      V(0.08f, 2.5f, 9f), _mPinkSign,   p);
        Box("W_PilF",  V(fx-.05f, h*.5f,-16f),V(0.3f, h, 0.3f), _mPillarCyan, p);
        Box("W_PilB",  V(fx-.05f, h*.5f, 16f),V(0.3f, h, 0.3f), _mPillarPink, p);
        AcUnit("W_AC1", V(fx+.22f, 3.5f, -3f), p, true);
        AcUnit("W_AC2", V(fx+.22f, 6.5f,  5f), p, true);
    }

    // ── Corner pillar columns (fill gaps between N/S/E/W facades) ──────────
    static void BuildCorners(Transform p)
    {
        var corners = new[] {
            V( 13f, 6f,  13f), V(-13f, 6f,  13f),
            V( 13f, 6f, -13f), V(-13f, 6f, -13f)
        };
        var mats = new[] { _mPillarPink, _mPillarCyan, _mPillarCyan, _mPillarPink };
        for (int i = 0; i < 4; i++)
        {
            Box($"Corner{i}", corners[i], V(3f, 12f, 3f), _mBody, p);
            Box($"CornerPil{i}", new Vector3(corners[i].x, corners[i].y, corners[i].z),
                V(0.4f, 12f, 0.4f), mats[i], p);
        }
    }

    // ── Floor circuit / holographic lines ─────────────────────────────────
    static void AddFloorDetails()
    {
        var root = new GameObject("FloorDetails");
        // Center crosshair
        Box("FL_H", V(0, 0.01f, 0), V(18f, 0.02f, 0.06f), _mFloorLine, root.transform);
        Box("FL_V", V(0, 0.01f, 0), V(0.06f, 0.02f, 18f), _mFloorLine, root.transform);
        // Inner ring squares
        Box("FL_R1", V( 5f, 0.01f,  0), V(0.05f, 0.02f, 10f), _mFloorLine, root.transform);
        Box("FL_R2", V(-5f, 0.01f,  0), V(0.05f, 0.02f, 10f), _mFloorLine, root.transform);
        Box("FL_R3", V( 0,  0.01f,  5f), V(10f, 0.02f, 0.05f), _mFloorLine, root.transform);
        Box("FL_R4", V( 0,  0.01f, -5f), V(10f, 0.02f, 0.05f), _mFloorLine, root.transform);
        // Corner accent dots (small squares)
        float[] xs = { -7f, 7f, -7f, 7f }; float[] zs = { -7f, -7f, 7f, 7f };
        for (int i = 0; i < 4; i++)
            Box($"FL_Dot{i}", V(xs[i], 0.01f, zs[i]), V(0.3f, 0.02f, 0.3f), _mFloorLine, root.transform);
    }

    // ── AC unit helper (small box protrusion representing HVAC unit) ───────
    static void AcUnit(string name, Vector3 pos, Transform p, bool rotated = false)
    {
        var s = rotated ? V(0.5f, 0.6f, 0.8f) : V(0.8f, 0.6f, 0.5f);
        Box(name, pos, s, _mDark, p);
        // Small cyan vent strip on AC
        var vs = rotated ? V(0.08f, 0.1f, 0.6f) : V(0.6f, 0.1f, 0.08f);
        var vo = rotated ? V(0.3f, 0, 0) : V(0, 0, 0.3f);
        Box(name + "_Vent", pos + vo, vs, _mCyanWin, p);
    }

    // ── Scene destruction helper ───────────────────────────────────────────
    static void Destroy(string path)
    {
        var parts = path.Split('/');
        var root = GameObject.Find(parts[0]);
        if (root == null) return;
        if (parts.Length == 1) { Object.DestroyImmediate(root); return; }
        var t = root.transform.Find(parts[1]);
        if (t != null) Object.DestroyImmediate(t.gameObject);
    }

    // ── Primitive factory ──────────────────────────────────────────────────
    static void Box(string name, Vector3 pos, Vector3 scale, Material mat, Transform parent)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
        go.name = name;
        go.transform.SetParent(parent);
        go.transform.position = pos;
        go.transform.localScale = scale;
        if (mat != null) go.GetComponent<Renderer>().sharedMaterial = mat;
        Object.DestroyImmediate(go.GetComponent<BoxCollider>()); // no colliders on scenery
    }

    // ── Material factories ─────────────────────────────────────────────────
    static Material Mat(string id, Color base_, Color unused, float metal, float smooth)
    {
        string p = $"Assets/Materials/{id}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p)
                ?? NewMat(p);
        m.SetColor("_BaseColor", base_);
        m.SetFloat("_Metallic",  metal);
        m.SetFloat("_Smoothness",smooth);
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material Emit(string id, Color base_, Color hdr)
    {
        string p = $"Assets/Materials/{id}.mat";
        var m = AssetDatabase.LoadAssetAtPath<Material>(p)
                ?? NewMat(p);
        m.SetColor("_BaseColor",    base_);
        m.SetFloat("_Metallic",     0f);
        m.SetFloat("_Smoothness",   0.5f);
        m.SetColor("_EmissionColor",hdr);
        m.EnableKeyword("_EMISSION");
        m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        EditorUtility.SetDirty(m);
        return m;
    }

    static Material NewMat(string path)
    {
        var m = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        AssetDatabase.CreateAsset(m, path);
        return m;
    }

    // ── Shorthand helpers ──────────────────────────────────────────────────
    static Vector3 V(float x, float y, float z) => new Vector3(x, y, z);
    static Color   C(float r, float g, float b) => new Color(r, g, b);
    static Color   H(float r, float g, float b) => new Color(r, g, b); // HDR — values >1 OK
}

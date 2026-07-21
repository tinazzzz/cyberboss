using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Adds cyan neon window glow to the FBX building without touching its original material.
/// Approach:
///   1. New CyanWindowGlow.mat — emissive cyan, no albedo, URP Lit
///   2. Small quads placed just in front of each visible window
///   3. Soft cyan point light as a child to cast light outward
/// All objects are children of the building root so they move with it.
public class AddWindowGlow
{
    const string BuildingPath = "tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74";
    const string MatPath      = "Assets/cyberpunk_building_1/Materials/CyanWindowGlow.mat";

    [MenuItem("CyberBoss/Add Window Glow to FBX Building")]
    public static void Execute()
    {
        var buildingGo = GameObject.Find(BuildingPath);
        if (buildingGo == null)
        {
            Debug.LogError("[WindowGlow] Building not found in scene.");
            return;
        }

        // Remove any existing glow group so this is idempotent
        var existing = buildingGo.transform.Find("WindowGlows");
        if (existing != null) Object.DestroyImmediate(existing.gameObject);

        // ── 1. Create / load the emissive material ────────────────────────
        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null)
        {
            mat = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            mat.name = "CyanWindowGlow";

            // No albedo — pure emissive glow
            mat.SetColor("_BaseColor", new Color(0f, 0.05f, 0.1f, 1f));   // near-black base
            mat.SetColor("_EmissionColor", new Color(0f, 6f, 6f));          // HDR cyan — above bloom threshold
            mat.EnableKeyword("_EMISSION");
            mat.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;

            // Transparent-like surface so quads blend if they overlap geometry
            mat.SetFloat("_Surface", 0f);  // 0 = Opaque (fine since base is near-black)

            AssetDatabase.CreateAsset(mat, MatPath);
            AssetDatabase.SaveAssets();
            Debug.Log("[WindowGlow] Created CyanWindowGlow.mat");
        }

        // ── 2. Parent container ────────────────────────────────────────────
        var glowRoot = new GameObject("WindowGlows");
        glowRoot.transform.SetParent(buildingGo.transform, false);
        glowRoot.transform.localPosition = Vector3.zero;
        glowRoot.transform.localRotation = Quaternion.identity;
        glowRoot.transform.localScale    = Vector3.one;

        // ── 3. Window quad positions ──────────────────────────────────────
        // Building max-Z face is at z ≈ 4.37. Place quads at z = 4.40 (just proud of surface).
        // Positions estimated from the visual capture — windows on the +Z face.
        // (x, y, z) in world space, (width, height) in metres.
        var windows = new (string name, Vector3 pos, Vector2 size)[]
        {
            ("Win_Top_L",    new Vector3(-8.05f, 5.10f, 4.40f), new Vector2(0.38f, 0.65f)),
            ("Win_Top_R",    new Vector3(-7.22f, 5.10f, 4.40f), new Vector2(0.38f, 0.65f)),
            ("Win_Mid_Band", new Vector3(-7.60f, 4.25f, 4.40f), new Vector2(0.85f, 0.22f)),
            ("Win_Mid_L",    new Vector3(-8.05f, 3.50f, 4.40f), new Vector2(0.38f, 0.45f)),
            ("Win_Mid_R",    new Vector3(-7.22f, 3.50f, 4.40f), new Vector2(0.38f, 0.45f)),
        };

        foreach (var (name, pos, size) in windows)
            CreateWindowQuad(name, pos, size, glowRoot.transform, mat);

        // ── 4. Cyan point light ────────────────────────────────────────────
        var lightGo = new GameObject("CyanWindowLight");
        lightGo.transform.SetParent(glowRoot.transform, false);
        lightGo.transform.position = new Vector3(-7.60f, 3.80f, 5.20f);  // slightly in front of face

        var lt = lightGo.AddComponent<Light>();
        lt.type      = LightType.Point;
        lt.color     = new Color(0f, 1f, 1f);   // cyan
        lt.intensity = 1.8f;
        lt.range     = 5.5f;
        lt.shadows   = LightShadows.None;        // WebGL: no per-pixel shadows on point lights

        // ── 5. Save ────────────────────────────────────────────────────────
        EditorUtility.SetDirty(buildingGo);
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");

        Debug.Log("[WindowGlow] Done — 5 window quads + cyan point light added as children.");
    }

    static void CreateWindowQuad(string name, Vector3 worldPos, Vector2 size,
                                  Transform parent, Material mat)
    {
        var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
        go.name = name;

        // Face +Z (toward the camera looking from outside the arena)
        go.transform.SetParent(parent, worldPositionStays: true);
        go.transform.position = worldPos;
        go.transform.rotation = Quaternion.identity;          // Quad default faces +Z
        go.transform.localScale = new Vector3(size.x, size.y, 0.01f);

        go.GetComponent<MeshRenderer>().sharedMaterial = mat;

        // Remove physics collider — these are purely visual
        Object.DestroyImmediate(go.GetComponent<MeshCollider>());
    }
}

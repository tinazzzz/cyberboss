using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Adds window quad meshes + cyan point light as children of the FBX building.
/// Prerequisite: CyanWindowGlow.mat must already exist (created by SetWindowGlowEmission).
public class AddWindowGlowObjects
{
    const string BuildingPath = "tripo_convert_35ef6ea0-f767-4ce8-8d2d-6b27e4e81a74";
    const string MatPath      = "Assets/cyberpunk_building_1/Materials/CyanWindowGlow.mat";

    [MenuItem("CyberBoss/Add Window Glow Objects")]
    public static void Execute()
    {
        var buildingGo = GameObject.Find(BuildingPath);
        if (buildingGo == null) { Debug.LogError("[WGlow] Building not found."); return; }

        var mat = AssetDatabase.LoadAssetAtPath<Material>(MatPath);
        if (mat == null) { Debug.LogError("[WGlow] CyanWindowGlow.mat not found — run SetWindowGlowEmission first."); return; }

        // Idempotent: remove any previous glow group
        var old = buildingGo.transform.Find("WindowGlows");
        if (old != null) Object.DestroyImmediate(old.gameObject);

        var root = new GameObject("WindowGlows");
        root.transform.SetParent(buildingGo.transform, worldPositionStays: false);
        root.transform.localPosition = Vector3.zero;
        root.transform.localRotation = Quaternion.identity;
        root.transform.localScale    = Vector3.one;

        // Building world-space bounds: X 5.51→8.47 (centre 6.99), Y 0.05→6.45, Z 0.04→3.48
        // Game camera at (18,24,-18) sees the -Z face (z=0.04, facing toward camera).
        // Quads sit at z = 0.00 (just in front of z=0.04 surface), rotated 180° Y to face -Z.
        var faceRot = Quaternion.Euler(0f, 180f, 0f);
        var windows = new (string n, Vector3 pos, Vector2 sz)[]
        {
            ("Win_Top_L",  new Vector3(6.35f, 5.10f, 0.00f), new Vector2(0.38f, 0.65f)),
            ("Win_Top_R",  new Vector3(7.62f, 5.10f, 0.00f), new Vector2(0.38f, 0.65f)),
            ("Win_Band",   new Vector3(6.99f, 4.25f, 0.00f), new Vector2(0.85f, 0.20f)),
            ("Win_Mid_L",  new Vector3(6.35f, 3.50f, 0.00f), new Vector2(0.38f, 0.45f)),
            ("Win_Mid_R",  new Vector3(7.62f, 3.50f, 0.00f), new Vector2(0.38f, 0.45f)),
        };

        foreach (var (n, pos, sz) in windows)
        {
            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = n;
            Object.DestroyImmediate(quad.GetComponent<MeshCollider>());
            quad.GetComponent<MeshRenderer>().sharedMaterial = mat;
            quad.transform.SetParent(root.transform, worldPositionStays: true);
            quad.transform.position   = pos;
            quad.transform.rotation   = faceRot;   // face -Z toward the game camera
            quad.transform.localScale = new Vector3(sz.x, sz.y, 0.01f);
        }

        // Point light just in front of the -Z face, casting cyan glow toward camera
        var lightGo = new GameObject("CyanWindowLight");
        lightGo.transform.SetParent(root.transform, worldPositionStays: false);
        lightGo.transform.position = new Vector3(6.99f, 3.50f, -0.50f);

        var lt       = lightGo.AddComponent<Light>();
        lt.type      = LightType.Point;
        lt.color     = new Color(0f, 1f, 1f);
        lt.intensity = 1.8f;
        lt.range     = 5.5f;
        lt.shadows   = LightShadows.None;

        EditorUtility.SetDirty(buildingGo);
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");

        Debug.Log("[WGlow] 5 window quads + cyan point light added to building.");
    }
}

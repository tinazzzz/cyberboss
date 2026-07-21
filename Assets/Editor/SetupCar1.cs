using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires up car_1's FBX ("neon sports car") with its loose Color/Normal textures and
/// places it in the scene under CityBackground/. The FBX references "Color.jpg" and
/// "Normal.jpg" (confirmed via strings dump) but the delivered files on disk are
/// "Color.jpeg"/"Normal.jpeg" — an extension mismatch Unity's automatic FBX texture
/// search won't resolve — so this wires them explicitly.
///
/// Metallic/roughness maps exist on disk (neonsportscar3dmodel_metallic/roughness.jpeg)
/// but are intentionally NOT wired here: URP/Lit's Metallic Map slot reads smoothness
/// from that same texture's alpha channel by default, and a plain JPEG has no real
/// alpha data, so plugging it in blind risks a wrong (fully smooth/mirror-like) result
/// with no way to visually verify from an editor script. A flat Metallic/Smoothness
/// slider is set instead, matching this project's existing convention (e.g. ArenaFloor's
/// fixed metallic/smoothness values) — wire the maps by hand in the Inspector if you
/// want the extra fidelity once you can see the live result.
/// </summary>
public static class SetupCar1
{
    private const string FbxPath = "Assets/Models/car_1/source/neon+sports+car+3d+model.fbx";
    private const string TexturesFolder = "Assets/Models/car_1/textures";

    [MenuItem("CyberBoss/Setup Car 1 (Neon Sports Car)")]
    public static void Run()
    {
        if (!File.Exists(FbxPath))
        {
            Debug.LogError($"[SetupCar1] FBX not found at '{FbxPath}'.");
            return;
        }

        WireMaterials();
        PlaceInScene();

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[SetupCar1] Done. Save the scene (Ctrl+S) to persist.");
    }

    private static void WireMaterials()
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(FbxPath);
        var materials = new List<Material>();
        foreach (var asset in assets)
            if (asset is Material mat)
                materials.Add(mat);

        if (materials.Count == 0)
        {
            Debug.LogWarning("[SetupCar1] No materials found on the FBX — check the import settings.");
            return;
        }

        var baseMap = FindTexture("Color");
        var normalMap = FindTexture("Normal");

        if (baseMap == null)
            Debug.LogWarning("[SetupCar1] No 'Color' texture found in " + TexturesFolder);
        if (normalMap == null)
            Debug.LogWarning("[SetupCar1] No 'Normal' texture found in " + TexturesFolder);

        if (normalMap != null)
            SetAsNormalMap(normalMap);

        foreach (var mat in materials)
        {
            ConvertToUrpLit(mat);

            if (baseMap != null) mat.SetTexture("_BaseMap", baseMap);
            if (normalMap != null)
            {
                mat.SetTexture("_BumpMap", normalMap);
                mat.EnableKeyword("_NORMALMAP");
            }

            // Flat slider approximation — see class remarks re: unwired metallic/roughness maps.
            mat.SetFloat("_Metallic", 0.6f);
            mat.SetFloat("_Smoothness", 0.65f);

            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
    }

    private static Texture2D FindTexture(string filenameHint)
    {
        var guids = AssetDatabase.FindAssets("", new[] { TexturesFolder });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            var name = Path.GetFileNameWithoutExtension(path);
            if (name.Equals(filenameHint, System.StringComparison.OrdinalIgnoreCase))
                return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }
        return null;
    }

    private static void SetAsNormalMap(Texture2D tex)
    {
        var path = AssetDatabase.GetAssetPath(tex);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        if (importer == null) return;
        if (importer.textureType != TextureImporterType.NormalMap)
        {
            importer.textureType = TextureImporterType.NormalMap;
            importer.SaveAndReimport();
        }
    }

    private static void ConvertToUrpLit(Material mat)
    {
        if (mat.shader != null && mat.shader.name == "Universal Render Pipeline/Lit") return;
        var urpLit = Shader.Find("Universal Render Pipeline/Lit");
        if (urpLit != null) mat.shader = urpLit;
    }

    private const string InstanceName = "NeonSportsCar_Car1";

    private static void PlaceInScene()
    {
        // Matches the existing "neon storefront" model's established convention in this
        // scene: scene root (no CityBackground/Buildings grouping), placed close to the
        // arena rather than far out. Scale is intentionally left at the raw import value
        // (1) for this pass — the existing storefront needed a 7.822391x scale-up from
        // its native import size to look building-sized, so scale=1 may make this model
        // render far too small to notice. The bounds logged below tell us the real size
        // so the correct multiplier can be set precisely next pass instead of guessed.
        var instance = GameObject.Find(InstanceName);
        bool isNew = instance == null;

        if (isNew)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null)
            {
                Debug.LogError("[SetupCar1] Could not load the FBX as a GameObject.");
                return;
            }
            instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            instance.name = InstanceName;
        }

        instance.transform.SetParent(null, worldPositionStays: false);
        instance.transform.localPosition = new Vector3(6f, 0.5f, 4f);
        instance.transform.localRotation = Quaternion.Euler(0f, 30f, 0f);

        LogBounds(instance, "SetupCar1");

        Debug.Log($"[SetupCar1] {(isNew ? "Placed" : "Updated")} '{InstanceName}' at (6, 0.5, 4), scale 1, " +
            "at scene root. Check the bounds logged above — a car should read roughly 4-5 units long.");
    }

    private static void LogBounds(GameObject go, string tag)
    {
        var renderers = go.GetComponentsInChildren<Renderer>();
        if (renderers.Length == 0)
        {
            Debug.LogWarning($"[{tag}] No renderers found on '{go.name}' — cannot report bounds.");
            return;
        }
        var bounds = renderers[0].bounds;
        for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
        Debug.Log($"[{tag}] '{go.name}' world-space bounds size: {bounds.size} (center {bounds.center}).");
    }
}

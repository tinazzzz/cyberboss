using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires "Store Front.fbx" (building_2) up with its loose PBR textures and places it
/// in the scene under CityBackground/. The FBX's embedded material definitions
/// reference textures by filenames (verified via strings dump of the FBX) that don't
/// exactly match what's on disk in Assets/Models/building_2/textures/ (extension
/// mismatches, e.g. some duplicated .jpeg copies alongside the correct .jpg/.exr set
/// extracted from the delivered zip) — Unity's automatic FBX texture search will not
/// reliably resolve these, so this script wires BaseMap + Normal Map explicitly by
/// keyword-matching material names against texture filenames instead of relying on
/// Unity's heuristic search. Metallic/roughness maps are intentionally left
/// unwired (see class remarks) — set a flat Smoothness slider instead.
/// </summary>
public static class SetupBuilding2
{
    private const string FbxPath = "Assets/Models/building_2/Store Front.fbx";
    private const string TexturesFolder = "Assets/Models/building_2/textures";

    // (material-name keyword, diffuse texture filename substring, normal texture filename substring)
    private static readonly (string keyword, string diffuseHint, string normalHint)[] TextureSets =
    {
        ("concrete", "brushed_concrete_diff", "brushed_concrete_nor"),
        ("pattern",  "large_square_pattern_01_diff", "large_square_pattern_01_nor"),
        ("tile",     "long_white_tiles_diff", "long_white_tiles_nor"),
    };

    [MenuItem("CyberBoss/Setup Building 2 (Store Front)")]
    public static void Run()
    {
        if (!File.Exists(FbxPath))
        {
            Debug.LogError($"[SetupBuilding2] FBX not found at '{FbxPath}'.");
            return;
        }

        WireMaterials();
        PlaceInScene();

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[SetupBuilding2] Done. Save the scene (Ctrl+S) to persist.");
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
            Debug.LogWarning("[SetupBuilding2] No materials found on the FBX — check the import settings.");
            return;
        }

        var matched = new HashSet<Material>();

        foreach (var (keyword, diffuseHint, normalHint) in TextureSets)
        {
            Material mat = null;
            foreach (var m in materials)
            {
                if (m.name.ToLowerInvariant().Contains(keyword))
                {
                    mat = m;
                    break;
                }
            }

            if (mat == null)
            {
                Debug.LogWarning($"[SetupBuilding2] No material name matched keyword '{keyword}' — " +
                    $"available materials: {string.Join(", ", GetNames(materials))}. Wire '{diffuseHint}'/'{normalHint}' manually.");
                continue;
            }

            ConvertToUrpLit(mat);

            var diffuseTex = FindTexture(diffuseHint);
            if (diffuseTex != null)
                mat.SetTexture("_BaseMap", diffuseTex);
            else
                Debug.LogWarning($"[SetupBuilding2] No diffuse texture found matching '{diffuseHint}' for material '{mat.name}'.");

            var normalTex = FindTexture(normalHint);
            if (normalTex != null)
            {
                SetAsNormalMap(normalTex);
                mat.SetTexture("_BumpMap", normalTex);
                mat.EnableKeyword("_NORMALMAP");
            }
            else
            {
                Debug.LogWarning($"[SetupBuilding2] No normal texture found matching '{normalHint}' for material '{mat.name}'.");
            }

            // Flat slider approximation — see class remarks re: unwired roughness maps.
            mat.SetFloat("_Metallic", 0f);
            mat.SetFloat("_Smoothness", 0.35f);

            EditorUtility.SetDirty(mat);
            matched.Add(mat);
        }

        // Confirmed via the FBX's actual "On Demand Remap" material list (checked live
        // in the Inspector — Unity's importer metadata doesn't expose this otherwise):
        // there are 3 distinct "007.1"-based materials, not 1 — "007.1", "007.1 (emission)",
        // and "007.1 (glass)". Only "007.1 (glass)" is the window and should get the
        // warm-glow treatment (BaseMap + Emission from the same texture, above the bloom
        // threshold — same technique as SetWindowGlowEmission.cs elsewhere in this
        // project). "007.1 (emission)" is the sign and almost certainly already carries
        // its own correct emission color (blue) from the original FBX import — a plain
        // shader conversion preserves whatever was already serialized on it, so it must
        // NOT be touched beyond that. Plain "007.1" is the non-illuminated roof/trim
        // variant and should likewise stay untouched and dark.
        var windowTex = FindTexture("007.1");

        foreach (var m in materials)
        {
            if (matched.Contains(m)) continue;

            ConvertToUrpLit(m);

            if (m.name.Equals("007.1 (glass)", System.StringComparison.OrdinalIgnoreCase) && windowTex != null)
            {
                m.SetTexture("_BaseMap", windowTex);
                m.SetColor("_BaseColor", Color.white);
                m.SetTexture("_EmissionMap", windowTex);
                m.SetColor("_EmissionColor", new Color(6f, 3.5f, 1f)); // warm HDR orange/yellow
                m.EnableKeyword("_EMISSION");
                m.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
                Debug.Log($"[SetupBuilding2] Material '{m.name}' — wired '007.1' as BaseMap + warm HDR Emission.");
            }
            else
            {
                // "007.1" and "007.1 (emission)" (and anything else unrecognized) land
                // here — shader converted only, nothing else touched, so whatever
                // emission/base color the original FBX import already carried (e.g. the
                // sign's blue) survives untouched.
                Debug.Log($"[SetupBuilding2] Material '{m.name}' — shader converted to URP/Lit only, " +
                    "existing properties (base color / emission) left untouched.");
            }

            EditorUtility.SetDirty(m);
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
            if (name.ToLowerInvariant().Contains(filenameHint.ToLowerInvariant()))
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

    private static string[] GetNames(List<Material> mats)
    {
        var names = new string[mats.Count];
        for (int i = 0; i < mats.Count; i++) names[i] = mats[i].name;
        return names;
    }

    private const string InstanceName = "StoreFront_Building2";

    private static void PlaceInScene()
    {
        // The existing "neon storefront 3d model" sits at
        // scene root — {fileID: 0} parent, no "CityBackground"/"Buildings" grouping —
        // with a localScale of 7.822391 despite importing at scale 1, and sits close to
        // the arena at (8, 3.3, -1), not far out on the perimeter. Match that convention:
        // scene root, near-arena placement, and an explicit scale-up since these
        // Tripo/AI-generated model imports come in at a tiny normalized size.
        var instance = GameObject.Find(InstanceName);
        bool isNew = instance == null;

        if (isNew)
        {
            var fbx = AssetDatabase.LoadAssetAtPath<GameObject>(FbxPath);
            if (fbx == null)
            {
                Debug.LogError("[SetupBuilding2] Could not load the FBX as a GameObject.");
                return;
            }
            instance = (GameObject)PrefabUtility.InstantiatePrefab(fbx);
            instance.name = InstanceName;
        }

        // Only set the initial transform once — once placed, position/scale are the
        // user's to hand-tune in the Scene view, and re-running this command (e.g. to
        // pick up a material fix) must never stomp on that manual adjustment.
        if (isNew)
        {
            instance.transform.SetParent(null, worldPositionStays: false);
            instance.transform.localPosition = new Vector3(6f, 0f, -6f);
            instance.transform.localRotation = Quaternion.identity;
            instance.transform.localScale = Vector3.one;
        }

        LogBounds(instance, "SetupBuilding2");

        Debug.Log(isNew
            ? $"[SetupBuilding2] Placed '{InstanceName}' at (6, 0, -6), scale 1, at scene root."
            : $"[SetupBuilding2] '{InstanceName}' already exists — only materials were re-wired, " +
              "your manual position/scale adjustments were left untouched.");
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

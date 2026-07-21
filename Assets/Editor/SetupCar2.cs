using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Wires up car_2's glb (Tripo-generated) with its embedded textures and places it in
/// the scene under CityBackground/. Same pipeline as the two existing buildings (see
/// FixB2Material.cs) — the Tripo bridge importer already assigns URP/Lit-compatible
/// materials on import for .glb, so unlike car_1's loose-FBX case there is no shader
/// conversion needed here, just texture wiring.
///
/// car_2's metallic/roughness data arrived as two single-channel derived textures
/// (named "...@channels=B"/"...@channels=G" by the importer's channel-split of the
/// original packed metallicRoughness texture — per glTF spec B=metallic, G=roughness).
/// These are intentionally NOT wired into _MetallicGlossMap: URP reads smoothness from
/// that same texture's alpha channel, and a single-channel grayscale derived texture
/// has no meaningful alpha, so plugging it in blind risks a wrong result with no way to
/// verify visually from a script. A flat Metallic/Smoothness slider is used instead —
/// wire the maps by hand in the Inspector if you want the extra fidelity.
/// </summary>
public static class SetupCar2
{
    private const string GlbPath = "Assets/Models/car_2/source/tripo_pbr_model_d4ef354f-e817-45ab-b7dc-483e4c40ad50.glb";

    [MenuItem("CyberBoss/Setup Car 2 (Tripo PBR Car)")]
    public static void Run()
    {
        if (!File.Exists(GlbPath))
        {
            Debug.LogError($"[SetupCar2] glb not found at '{GlbPath}'.");
            return;
        }

        WireMaterials();
        PlaceInScene();

        var scene = SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        Debug.Log("[SetupCar2] Done. Save the scene (Ctrl+S) to persist.");
    }

    private static void WireMaterials()
    {
        var materials = new List<Material>();
        var textures = new List<Texture2D>();

        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(GlbPath))
        {
            if (asset is Material mat) materials.Add(mat);
            else if (asset is Texture2D tex) textures.Add(tex);
        }

        if (materials.Count == 0)
        {
            Debug.LogWarning("[SetupCar2] No materials found on the glb — check the import settings.");
            return;
        }

        Texture2D normalTex = null;
        Texture2D baseColorTex = null;
        var channelSplitTextures = new List<Texture2D>();

        foreach (var tex in textures)
        {
            Debug.Log($"[SetupCar2] glb texture: {tex.name}");

            var path = AssetDatabase.GetAssetPath(tex);
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;

            if (importer != null && importer.textureType == TextureImporterType.NormalMap)
            {
                normalTex = tex;
                continue;
            }
            if (tex.name.Contains("@channels="))
            {
                channelSplitTextures.Add(tex);
                continue;
            }
            // First remaining candidate (not normal, not a channel-split metallic/roughness
            // derivative) is treated as the base color texture.
            if (baseColorTex == null)
                baseColorTex = tex;
        }

        if (baseColorTex == null)
            Debug.LogWarning("[SetupCar2] Could not confidently identify a base color texture — check the log above and wire manually if needed.");
        if (normalTex == null)
            Debug.LogWarning("[SetupCar2] No texture was already flagged as a Normal Map by the importer — check manually.");
        if (channelSplitTextures.Count > 0)
            Debug.Log($"[SetupCar2] Found {channelSplitTextures.Count} channel-split metallic/roughness texture(s) — " +
                "not auto-wired (see class remarks); using flat Metallic/Smoothness sliders instead.");

        foreach (var mat in materials)
        {
            if (baseColorTex != null) mat.SetTexture("_BaseMap", baseColorTex);
            if (normalTex != null)
            {
                mat.SetTexture("_BumpMap", normalTex);
                mat.EnableKeyword("_NORMALMAP");
                mat.SetFloat("_BumpScale", 1f);
            }

            mat.SetTexture("_MetallicGlossMap", null);
            mat.SetFloat("_Metallic", 0.6f);
            mat.SetFloat("_Smoothness", 0.65f);
            mat.SetFloat("_SmoothnessTextureChannel", 1f); // use slider, not a texture channel

            EditorUtility.SetDirty(mat);
        }

        AssetDatabase.SaveAssets();
    }

    private const string InstanceName = "TripoCar_Car2";

    private static void PlaceInScene()
    {
        // Same convention as SetupCar1/SetupBuilding2 — scene root, close to the arena.
        // Scale left at 1 for this pass; see LogBounds output to determine the correct
        // multiplier rather than guessing.
        var instance = GameObject.Find(InstanceName);
        bool isNew = instance == null;

        if (isNew)
        {
            var glb = AssetDatabase.LoadAssetAtPath<GameObject>(GlbPath);
            if (glb == null)
            {
                Debug.LogError("[SetupCar2] Could not load the glb as a GameObject.");
                return;
            }
            instance = (GameObject)PrefabUtility.InstantiatePrefab(glb);
            instance.name = InstanceName;
        }

        instance.transform.SetParent(null, worldPositionStays: false);
        instance.transform.localPosition = new Vector3(-6f, 0.5f, 4f);
        instance.transform.localRotation = Quaternion.Euler(0f, -30f, 0f);

        LogBounds(instance, "SetupCar2");

        Debug.Log($"[SetupCar2] {(isNew ? "Placed" : "Updated")} '{InstanceName}' at (-6, 0.5, 4), scale 1, " +
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

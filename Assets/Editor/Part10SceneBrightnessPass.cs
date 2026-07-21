using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEditor;
using UnityEditor.SceneManagement;

/// Scene brightness pass: raises key/ambient light, eases off
/// contrast/vignette/chromatic aberration that were crushing midtones,
/// and adds two low fill lights over the fight area so characters pick up
/// neon rim color.
public class Part10SceneBrightnessPass
{
    const string ProfilePath = "Assets/Settings/CyberArenaVolumeProfile.asset";

    [MenuItem("CyberBoss/Scene Brightness Pass")]
    public static void Execute()
    {
        RaiseAmbient();
        RaiseKeyLight();
        EaseVolumeProfile();
        AddCharacterFillLights();

        AssetDatabase.SaveAssets();
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");
        Debug.Log("[Part10Brightness] Done.");
    }

    // Flat ambient was almost black, so anything outside a point-light's
    // radius (like the floor and the characters' shadowed sides) had
    // nothing filling it in. Trilight gives a colored sky/ground bounce
    // that reads as neon-lit fog instead of pure black.
    static void RaiseAmbient()
    {
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.12f, 0.08f, 0.22f);
        RenderSettings.ambientEquatorColor = new Color(0.16f, 0.12f, 0.24f);
        RenderSettings.ambientGroundColor = new Color(0.05f, 0.04f, 0.07f);
        RenderSettings.ambientIntensity = 1.5f;
    }

    static void RaiseKeyLight()
    {
        var go = GameObject.Find("Directional Light");
        if (go == null) { Debug.LogWarning("[Part10Brightness] Directional Light not found."); return; }
        var light = go.GetComponent<Light>();
        light.intensity = 0.85f;
        light.color = new Color(0.62f, 0.70f, 0.95f);
        EditorUtility.SetDirty(go);
    }

    static void EaseVolumeProfile()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null) { Debug.LogError("[Part10Brightness] VolumeProfile not found."); return; }

        if (profile.TryGet(out Vignette vignette))
        {
            vignette.intensity.Override(0.28f);
            vignette.smoothness.Override(0.45f);
        }

        if (profile.TryGet(out ColorAdjustments colorAdjustments))
        {
            colorAdjustments.postExposure.Override(0.9f);
            colorAdjustments.contrast.Override(8f);
        }

        if (profile.TryGet(out ChromaticAberration chromaticAberration))
        {
            chromaticAberration.intensity.Override(0.15f);
        }

        if (profile.TryGet(out Bloom bloom))
        {
            bloom.intensity.Override(2.0f);
        }

        EditorUtility.SetDirty(profile);
    }

    // Two soft point lights over the arena center so the player and boss
    // pick up cyan/magenta rim light regardless of where they are standing
    // mid-fight, matching the reference's neon-lit character silhouettes.
    static void AddCharacterFillLights()
    {
        CreateFillLight("CharacterFill_Cyan", new Vector3(1.5f, 2.4f, -1.5f), new Color(0.2f, 0.85f, 1.0f));
        CreateFillLight("CharacterFill_Magenta", new Vector3(-1.5f, 2.4f, 3.5f), new Color(1.0f, 0.15f, 0.75f));
    }

    static void CreateFillLight(string name, Vector3 position, Color color)
    {
        var arenaLights = GameObject.Find("ArenaLights");
        var existing = arenaLights != null ? arenaLights.transform.Find(name) : null;
        GameObject go = existing != null ? existing.gameObject : new GameObject(name, typeof(Light));

        go.transform.position = position;
        if (arenaLights != null) go.transform.SetParent(arenaLights.transform, true);

        var light = go.GetComponent<Light>();
        light.type = LightType.Point;
        light.color = color;
        light.intensity = 3.5f;
        light.range = 8f;
        light.shadows = LightShadows.None;

        EditorUtility.SetDirty(go);
    }
}

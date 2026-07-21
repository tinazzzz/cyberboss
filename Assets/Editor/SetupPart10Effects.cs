using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One-shot editor setup for expanded combat effects — impact sparks,
/// chromatic aberration pulse, and camera FOV punch.
///
/// Menu: CyberBoss / Setup Combat Effects
/// Safe to run multiple times (idempotent).
///
/// Actions performed:
///   1. Creates CombatEffectsConfig.asset in Assets/ScriptableObjects/ if absent.
///   2. Creates ImpactVolumeProfile.asset in Assets/Settings/ if absent.
///      The profile contains only ChromaticAberration at intensity 1.0.
///      CameraImpactEffects animates Volume.weight (0→target→0) at runtime;
///      the profile's CA value is never modified during play.
///   3. Creates/reuses 'ImpactEffectsVolume' GameObject (global Volume, priority 10,
///      weight 0, ImpactVolumeProfile assigned).
///   4. Finds the existing 'GamePolish' GameObject (created by SetupPart10Polish/SetupPolish)
///      and adds CombatVFXManager and CameraImpactEffects if absent.
///   5. Wires _config, _impactVolume, and _vcam on CameraImpactEffects via SerializedObject.
///   6. Wires _config on CombatVFXManager via SerializedObject.
///   7. Marks scene dirty and saves.
/// </summary>
public static class SetupPart10Effects
{
    private const string SOFolder            = "Assets/ScriptableObjects";
    private const string EffectsConfigAsset  = "CombatEffectsConfig";
    private const string SettingsFolder      = "Assets/Settings";
    private const string ImpactProfileAsset  = "ImpactVolumeProfile";
    private const string PolishGoName        = "GamePolish";
    private const string ImpactVolumeGoName  = "ImpactEffectsVolume";
    private const string VCamName            = "CM_IsometricCamera";

    [MenuItem("CyberBoss/Setup Combat Effects")]
    public static void Execute()
    {
        var effectsConfig   = GetOrCreateEffectsConfig();
        var impactProfile   = GetOrCreateImpactVolumeProfile();
        var impactVolume    = SetupImpactVolumeGameObject(impactProfile);
        var polishGo        = GetOrCreatePolishGameObject();

        SetupCombatVFXManager(polishGo, effectsConfig);
        SetupCameraImpactEffects(polishGo, effectsConfig, impactVolume);

        EditorSceneManager.MarkAllScenesDirty();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");

        Debug.Log("[SetupCombatEffects] Combat effects setup complete. " +
            "CombatVFXManager, CameraImpactEffects, and ImpactEffectsVolume wired. " +
            "Scene saved.");
    }

    // ------------------------------------------------------------------
    // 1. CombatEffectsConfig ScriptableObject
    // ------------------------------------------------------------------

    private static CombatEffectsConfig GetOrCreateEffectsConfig()
    {
        if (!AssetDatabase.IsValidFolder(SOFolder))
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");

        string path     = $"{SOFolder}/{EffectsConfigAsset}.asset";
        var    existing = AssetDatabase.LoadAssetAtPath<CombatEffectsConfig>(path);
        if (existing != null)
        {
            Debug.Log($"[SetupCombatEffects] {EffectsConfigAsset}.asset already exists — reusing.");
            return existing;
        }

        var config = ScriptableObject.CreateInstance<CombatEffectsConfig>();
        AssetDatabase.CreateAsset(config, path);
        Debug.Log($"[SetupCombatEffects] Created {path}.");
        return config;
    }

    // ------------------------------------------------------------------
    // 2. ImpactVolumeProfile — ChromaticAberration at 1.0 (fixed value)
    // ------------------------------------------------------------------

    private static VolumeProfile GetOrCreateImpactVolumeProfile()
    {
        string path     = $"{SettingsFolder}/{ImpactProfileAsset}.asset";
        var    existing = AssetDatabase.LoadAssetAtPath<VolumeProfile>(path);
        if (existing != null)
        {
            Debug.Log($"[SetupCombatEffects] {ImpactProfileAsset}.asset already exists — reusing.");
            return existing;
        }

        // Create the profile asset first so sub-assets can be attached to it.
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = ImpactProfileAsset;
        AssetDatabase.CreateAsset(profile, path);

        // Chromatic aberration at maximum — Volume.weight controls effective strength.
        var ca  = ScriptableObject.CreateInstance<ChromaticAberration>();
        ca.name = "ChromaticAberration";
        ca.active = true;
        ca.intensity.Override(1.0f);
        profile.components.Add(ca);
        AssetDatabase.AddObjectToAsset(ca, path);

        EditorUtility.SetDirty(profile);
        AssetDatabase.SaveAssets();

        Debug.Log($"[SetupCombatEffects] Created {path} with ChromaticAberration intensity 1.0.");
        return profile;
    }

    // ------------------------------------------------------------------
    // 3. ImpactEffectsVolume — global Volume at priority 10, weight 0
    // ------------------------------------------------------------------

    private static Volume SetupImpactVolumeGameObject(VolumeProfile profile)
    {
        var go = GameObject.Find(ImpactVolumeGoName);
        if (go == null)
        {
            go = new GameObject(ImpactVolumeGoName);
            Debug.Log($"[SetupCombatEffects] Created '{ImpactVolumeGoName}' GameObject.");
        }

        var volume = go.GetComponent<Volume>();
        if (volume == null)
            volume = go.AddComponent<Volume>();

        // Global volume with low initial weight — CameraImpactEffects animates
        // weight at runtime. Priority 10 exceeds the arena profile (priority 0)
        // so this volume's CA overrides the baseline when weight > 0.
        volume.isGlobal       = true;
        volume.priority       = 10f;
        volume.weight         = 0f;
        volume.sharedProfile  = profile;

        EditorUtility.SetDirty(go);
        return volume;
    }

    // ------------------------------------------------------------------
    // 4. GamePolish — reuse existing, or create if absent
    // ------------------------------------------------------------------

    private static GameObject GetOrCreatePolishGameObject()
    {
        var go = GameObject.Find(PolishGoName);
        if (go == null)
        {
            go = new GameObject(PolishGoName);
            Debug.Log($"[SetupCombatEffects] '{PolishGoName}' not found — created. " +
                "Run CyberBoss/Setup Polish first for full effect.");
        }
        return go;
    }

    // ------------------------------------------------------------------
    // 5. CombatVFXManager
    // ------------------------------------------------------------------

    private static void SetupCombatVFXManager(GameObject polishGo, CombatEffectsConfig config)
    {
        var mgr = EnsureComponent<CombatVFXManager>(polishGo);
        var so  = new SerializedObject(mgr);
        so.FindProperty("_config").objectReferenceValue = config;
        so.ApplyModifiedProperties();

        EditorUtility.SetDirty(polishGo);
    }

    // ------------------------------------------------------------------
    // 6. CameraImpactEffects — wires config, volume, and vcam
    // ------------------------------------------------------------------

    private static void SetupCameraImpactEffects(
        GameObject polishGo, CombatEffectsConfig config, Volume impactVolume)
    {
        var effects = EnsureComponent<CameraImpactEffects>(polishGo);
        var so      = new SerializedObject(effects);

        so.FindProperty("_config").objectReferenceValue       = config;
        so.FindProperty("_impactVolume").objectReferenceValue = impactVolume;

        // Wire the virtual camera reference so FOV punch works without a runtime Find call.
        var vcamGo = GameObject.Find(VCamName);
        if (vcamGo != null)
        {
            var vcam = vcamGo.GetComponent<CinemachineCamera>();
            if (vcam != null)
            {
                so.FindProperty("_vcam").objectReferenceValue = vcam;
                Debug.Log($"[SetupCombatEffects] Wired '{VCamName}' to CameraImpactEffects._vcam.");
            }
            else
            {
                Debug.LogWarning($"[SetupCombatEffects] CinemachineCamera not found on '{VCamName}'. " +
                    "FOV punch will fall back to a runtime Find call in CameraImpactEffects.Start().");
            }
        }
        else
        {
            Debug.LogWarning($"[SetupCombatEffects] '{VCamName}' not found in scene. " +
                "Run CyberBoss/CyberBoss Arena Setup first.");
        }

        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(polishGo);
    }

    // ------------------------------------------------------------------
    // Helper
    // ------------------------------------------------------------------

    /// <summary>Returns the existing component if present; adds and returns a new one if absent.</summary>
    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        if (existing != null) return existing;
        Debug.Log($"[SetupCombatEffects] Added {typeof(T).Name} to '{go.name}'.");
        return go.AddComponent<T>();
    }
}

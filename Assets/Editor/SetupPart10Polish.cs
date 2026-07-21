using Unity.Cinemachine;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// One-shot editor setup for scene polish — screen shake, hit freeze, camera feel,
/// and lighting/post-processing atmosphere boost.
///
/// Menu: CyberBoss / Setup Polish
/// Safe to run multiple times (idempotent).
///
/// Actions performed:
///   1. Creates ScreenShakeConfig.asset in Assets/ScriptableObjects/ if absent.
///   2. Creates a 'GamePolish' GameObject in the scene (or reuses existing) and adds:
///        - ScreenShakeManager
///        - HitFreezeManager
///        - CinemachineImpulseSource (Bump shape, Uniform propagation, channel 1)
///      Assigns the config to both managers via SerializedObject.
///   3. Finds CM_IsometricCamera and adds/configures:
///        - CinemachineImpulseListener (channel mask 1, gain 1, Additive mode)
///        - CinemachineBasicMultiChannelPerlin (Handheld_normal_mild, amplitude 0.3,
///          frequency 0.4) for subtle idle camera sway.
///   4. Configures CinemachineBrain default blend: EaseInOut, 0.5 s.
///   5. Boosts post-processing on CyberArenaVolumeProfile.asset:
///        - Bloom intensity 1.8 → 2.5, threshold 0.5 → 0.4, scatter 0.65 → 0.7,
///          tint shifted toward purple-cyan for neon atmosphere.
///        - ColorAdjustments postExposure 0.3 → 0.6, contrast 15 → 18, saturation 20 → 25.
///   6. Boosts ArenaLights group point light intensity × 1.5 (max 8 lights).
///   7. Marks scene dirty and saves.
/// </summary>
public static class SetupPart10Polish
{
    private const string SOFolder       = "Assets/ScriptableObjects";
    private const string SOAssetName    = "ScreenShakeConfig";
    private const string ProfilePath    = "Assets/Settings/CyberArenaVolumeProfile.asset";
    private const string PolishGoName   = "GamePolish";
    private const string VCamName       = "CM_IsometricCamera";
    private const string MainCamName    = "Main Camera";
    private const string ArenaLightsName = "ArenaLights";
    private const string NoiseProfilePath =
        "Packages/com.unity.cinemachine/Presets/Noise/Handheld_normal_mild.asset";

    [MenuItem("CyberBoss/Setup Polish")]
    public static void Execute()
    {
        var config = GetOrCreateShakeConfig();

        SetupPolishGameObject(config);
        SetupCinemachineCamera();
        SetupCinemachineBrain();
        BoostPostProcessing();
        BoostArenaLights();
        EnsurePart10EffectsRan();
        SeedImpactProfiles();
        WireCombatVFXManagerShieldConfig();

        EditorSceneManager.MarkAllScenesDirty();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/CyberArena.unity");

        Debug.Log("[SetupPolish] Polish setup complete. " +
            "GamePolish GO created, Cinemachine impulse + noise wired, " +
            "post-process boosted, arena lights brightened, per-skill impact " +
            "profiles seeded, scene saved.");
    }

    // ------------------------------------------------------------------
    // 1. ScreenShakeConfig ScriptableObject
    // ------------------------------------------------------------------

    private static ScreenShakeConfig GetOrCreateShakeConfig()
    {
        if (!AssetDatabase.IsValidFolder(SOFolder))
            AssetDatabase.CreateFolder("Assets", "ScriptableObjects");

        string assetPath = $"{SOFolder}/{SOAssetName}.asset";
        var existing = AssetDatabase.LoadAssetAtPath<ScreenShakeConfig>(assetPath);
        if (existing != null)
        {
            Debug.Log($"[SetupPolish] {SOAssetName}.asset already exists — reusing.");
            return existing;
        }

        var config = ScriptableObject.CreateInstance<ScreenShakeConfig>();
        AssetDatabase.CreateAsset(config, assetPath);
        Debug.Log($"[SetupPolish] Created {assetPath}.");
        return config;
    }

    // ------------------------------------------------------------------
    // 2. GamePolish GameObject — ScreenShakeManager, HitFreezeManager, ImpulseSource
    // ------------------------------------------------------------------

    private static void SetupPolishGameObject(ScreenShakeConfig config)
    {
        // Reuse existing GO for idempotency.
        var polishGo = GameObject.Find(PolishGoName);
        if (polishGo == null)
        {
            polishGo = new GameObject(PolishGoName);
            Debug.Log($"[SetupPolish] Created '{PolishGoName}' GameObject.");
        }

        // ScreenShakeManager ——————————————————————————————————————————
        var shakeManager = EnsureComponent<ScreenShakeManager>(polishGo);
        var shakeSo      = new SerializedObject(shakeManager);
        shakeSo.FindProperty("_config").objectReferenceValue = config;
        shakeSo.ApplyModifiedProperties();

        // HitFreezeManager ———————————————————————————————————————————
        var freezeManager = EnsureComponent<HitFreezeManager>(polishGo);
        var freezeSo      = new SerializedObject(freezeManager);
        freezeSo.FindProperty("_config").objectReferenceValue = config;
        freezeSo.ApplyModifiedProperties();

        // CinemachineImpulseSource ————————————————————————————————————
        var impulseSource = EnsureComponent<CinemachineImpulseSource>(polishGo);
        ConfigureImpulseSource(impulseSource);

        EditorUtility.SetDirty(polishGo);
    }

    private static void ConfigureImpulseSource(CinemachineImpulseSource source)
    {
        // Bump shape gives a quick snap-and-decay appropriate for hits.
        // ImpulseDuration is overwritten at runtime per shake tier.
        source.ImpulseDefinition.ImpulseShape   = CinemachineImpulseDefinition.ImpulseShapes.Bump;
        source.ImpulseDefinition.ImpulseDuration = 0.3f;
        source.ImpulseDefinition.ImpulseChannel  = 1;

        // Uniform propagation: every CinemachineCamera in the scene feels the shake
        // equally regardless of distance (single-camera boss fight, no falloff needed).
        source.ImpulseDefinition.ImpulseType         = CinemachineImpulseDefinition.ImpulseTypes.Uniform;
        source.ImpulseDefinition.DissipationDistance = 100f;

        // Downward default velocity — felt as a vertical camera thud.
        source.DefaultVelocity = Vector3.down;

        EditorUtility.SetDirty(source);
        Debug.Log("[SetupPolish] CinemachineImpulseSource configured (Bump, Uniform, channel 1).");
    }

    // ------------------------------------------------------------------
    // 3. CM_IsometricCamera — ImpulseListener + Noise
    // ------------------------------------------------------------------

    private static void SetupCinemachineCamera()
    {
        var vcamGo = GameObject.Find(VCamName);
        if (vcamGo == null)
        {
            Debug.LogError($"[SetupPolish] '{VCamName}' not found in scene. " +
                "Run the arena camera setup first.");
            return;
        }

        SetupImpulseListener(vcamGo);
        SetupCameraPerlin(vcamGo);
    }

    private static void SetupImpulseListener(GameObject vcamGo)
    {
        var listener = vcamGo.GetComponent<CinemachineImpulseListener>();
        if (listener == null)
        {
            listener = vcamGo.AddComponent<CinemachineImpulseListener>();
            Debug.Log("[SetupPolish] Added CinemachineImpulseListener to CM_IsometricCamera.");
        }

        // ChannelMask must match the source's ImpulseChannel (both 1) for events
        // to be received. Gain 1 = full-strength response.
        listener.ChannelMask = 1;
        listener.Gain        = 1f;

        // Additive mode: concurrent impulses stack — heavy Charge shake + medium
        // health-event shake combine for a larger displacement than lighter hits.
        listener.SignalCombinationMode = CinemachineImpulseListener.SignalCombinationModes.Additive;

        // Apply after the Aim stage so the shake displaces the fully-aimed position.
        listener.ApplyAfter = CinemachineCore.Stage.Aim;

        EditorUtility.SetDirty(vcamGo);
    }

    private static void SetupCameraPerlin(GameObject vcamGo)
    {
        // CinemachineBasicMultiChannelPerlin is tagged [DisallowMultipleComponent] —
        // check before adding to avoid an editor error on re-runs.
        var perlin = vcamGo.GetComponent<CinemachineBasicMultiChannelPerlin>();
        if (perlin == null)
        {
            perlin = vcamGo.AddComponent<CinemachineBasicMultiChannelPerlin>();
            Debug.Log("[SetupPolish] Added CinemachineBasicMultiChannelPerlin to CM_IsometricCamera.");
        }

        // Amplitude 0.3 / frequency 0.4 — subtle breathing effect that adds life
        // to the fixed isometric camera without distracting from combat reads.
        perlin.AmplitudeGain = 0.3f;
        perlin.FrequencyGain = 0.4f;

        // Load the built-in Handheld_normal_mild noise profile from the Cinemachine package.
        var noiseProfile = AssetDatabase.LoadAssetAtPath<NoiseSettings>(NoiseProfilePath);
        if (noiseProfile != null)
        {
            perlin.NoiseProfile = noiseProfile;
            Debug.Log("[SetupPolish] Assigned Handheld_normal_mild noise profile.");
        }
        else
        {
            Debug.LogWarning("[SetupPolish] Handheld_normal_mild.asset not found at " +
                $"'{NoiseProfilePath}'. Assign a noise profile manually in the Inspector.");
        }

        EditorUtility.SetDirty(vcamGo);
    }

    // ------------------------------------------------------------------
    // 4. CinemachineBrain — default blend
    // ------------------------------------------------------------------

    private static void SetupCinemachineBrain()
    {
        var mainCamGo = GameObject.Find(MainCamName);
        if (mainCamGo == null)
        {
            Debug.LogWarning($"[SetupPolish] '{MainCamName}' not found — " +
                "CinemachineBrain blend not configured.");
            return;
        }

        var brain = mainCamGo.GetComponent<CinemachineBrain>();
        if (brain == null)
        {
            Debug.LogWarning("[SetupPolish] CinemachineBrain not found on Main Camera — " +
                "blend not configured.");
            return;
        }

        brain.DefaultBlend = new CinemachineBlendDefinition(
            CinemachineBlendDefinition.Styles.EaseInOut, 0.5f);

        EditorUtility.SetDirty(mainCamGo);
        Debug.Log("[SetupPolish] CinemachineBrain default blend set to EaseInOut 0.5 s.");
    }

    // ------------------------------------------------------------------
    // 5. Post-processing — boost bloom and exposure for neon atmosphere
    // ------------------------------------------------------------------

    private static void BoostPostProcessing()
    {
        var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
        if (profile == null)
        {
            Debug.LogWarning($"[SetupPolish] VolumeProfile not found at '{ProfilePath}'. " +
                "Post-processing boost skipped.");
            return;
        }

        bool changed = false;

        // Bloom — more aggressive neon glow without blowing out the scene.
        // Threshold lowered so mid-brightness neon trim catches bloom earlier.
        if (profile.TryGet<Bloom>(out var bloom))
        {
            bloom.intensity.Override(2.5f);
            bloom.threshold.Override(0.4f);
            bloom.scatter.Override(0.7f);
            // Shift tint toward purple-cyan — warmer magenta in neon, cooler cyan in shadows.
            bloom.tint.Override(new Color(0.85f, 0.75f, 1.0f));
            changed = true;
            Debug.Log("[SetupPolish] Bloom: intensity 2.5, threshold 0.4, scatter 0.7, purple-cyan tint.");
        }
        else
        {
            Debug.LogWarning("[SetupPolish] Bloom component not found in VolumeProfile.");
        }

        // Color adjustments — lift overall brightness and punch up saturation.
        // Keeps the dark arena aesthetic while making neon colors pop more.
        if (profile.TryGet<ColorAdjustments>(out var colorAdj))
        {
            colorAdj.postExposure.Override(0.6f);
            colorAdj.contrast.Override(18f);
            colorAdj.saturation.Override(25f);
            changed = true;
            Debug.Log("[SetupPolish] ColorAdjustments: exposure 0.6, contrast 18, saturation 25.");
        }
        else
        {
            Debug.LogWarning("[SetupPolish] ColorAdjustments component not found in VolumeProfile.");
        }

        if (changed)
            EditorUtility.SetDirty(profile);
    }

    // ------------------------------------------------------------------
    // 6. Arena lights — boost neon point light intensity
    // ------------------------------------------------------------------

    private static void BoostArenaLights()
    {
        var arenaLightsGo = GameObject.Find(ArenaLightsName);
        if (arenaLightsGo == null)
        {
            Debug.LogWarning($"[SetupPolish] '{ArenaLightsName}' GameObject not found. " +
                "Arena light boost skipped.");
            return;
        }

        int boosted = 0;
        foreach (var light in arenaLightsGo.GetComponentsInChildren<Light>())
        {
            // Multiply by 1.5 to brighten neon atmosphere, capped at 12 to avoid
            // overexposure. Arena lights were already tuned to ~4–6 range; × 1.5
            // brings them to ~6–9, within the intended neon-saturated feel.
            light.intensity = Mathf.Min(light.intensity * 1.5f, 12f);
            EditorUtility.SetDirty(light.gameObject);
            boosted++;
        }

        if (boosted > 0)
            Debug.Log($"[SetupPolish] Boosted {boosted} arena light(s) × 1.5 (max 12).");
        else
            Debug.LogWarning($"[SetupPolish] No Light components found under '{ArenaLightsName}'.");
    }

    // ------------------------------------------------------------------
    // 7. Per-skill ImpactEffectProfile seeding
    // ------------------------------------------------------------------

    /// <summary>
    /// Plain data carrier mirroring ImpactEffectProfile's fields, used to author
    /// the section-2 design-table defaults for each of the 15 profile fields
    /// across the 10 skill configs, without touching UnityEngine.Object.
    /// </summary>
    private struct ProfileSeed
    {
        public bool  EnableShake;
        public float ShakeForce;
        public float ShakeDuration;
        public bool  ShakeIsSnap;
        public bool  EnableFreeze;
        public float FreezeTimeScale;
        public float FreezeRealSeconds;
        public float CaWeight;
        public bool  IsGlitchCa;
        public float FovPunchDelta;
        public Color VfxColor;
        public int   VfxParticleCount;
        public float VfxMinSpeed;
        public float VfxMaxSpeed;
        public float VfxLifetime;
        public float VfxSize;
    }

    /// <summary>
    /// Idempotently seeds every ImpactEffectProfile field on all 10 skill Config
    /// assets. Loads each asset directly by its known path (created by
    /// CyberBoss/Setup Player Skills and CyberBoss/Setup Boss Skills) — logs a
    /// warning and skips any asset not yet created, rather than failing the whole pass.
    /// </summary>
    private static void SeedImpactProfiles()
    {
        SeedPlayerSkillProfiles();
        SeedBossSkillProfiles();
    }

    private static void SeedPlayerSkillProfiles()
    {
        SeedConfig<DashSkillConfig>("SkillConfig_Dash", so =>
        {
            SeedProfile(so, "_impactProfile", new ProfileSeed
            {
                EnableShake = false, EnableFreeze = false, CaWeight = 0f,
                FovPunchDelta = 3f, // positive = zoom-out "speed" punch
                VfxColor = new Color(0.2f, 0.4f, 1f), VfxParticleCount = 14,
                VfxMinSpeed = 3f, VfxMaxSpeed = 7f, VfxLifetime = 0.18f, VfxSize = 0.09f
            });
            SeedProfile(so, "_perfectDodgeProfile", new ProfileSeed
            {
                EnableShake = false,
                EnableFreeze = true, FreezeTimeScale = 0.15f, FreezeRealSeconds = 0.033f,
                CaWeight = 0.3f, FovPunchDelta = 0f,
                VfxColor = new Color(0.4f, 0.7f, 2.5f), VfxParticleCount = 22,
                VfxMinSpeed = 4f, VfxMaxSpeed = 9f, VfxLifetime = 0.22f, VfxSize = 0.12f
            });
        });

        SeedConfig<ParrySkillConfig>("SkillConfig_Parry", so =>
        {
            SeedProfile(so, "_impactProfile", new ProfileSeed
            {
                EnableShake = false,
                EnableFreeze = true, FreezeTimeScale = 0.03f, FreezeRealSeconds = 0.045f,
                CaWeight = 0.25f, FovPunchDelta = -1.5f,
                VfxColor = new Color(2.5f, 2.2f, 0.8f), VfxParticleCount = 8,
                VfxMinSpeed = 0f, VfxMaxSpeed = 0f, VfxLifetime = 0.18f, VfxSize = 0.055f
            });
        });

        SeedConfig<RangedBlastSkillConfig>("SkillConfig_RangedBlast", so =>
        {
            SeedProfile(so, "_impactProfile", new ProfileSeed
            {
                EnableShake = true, ShakeForce = 0.35f, ShakeDuration = 0.15f, ShakeIsSnap = true,
                EnableFreeze = false, CaWeight = 0.15f, FovPunchDelta = 0f,
                VfxColor = new Color(2.5f, 1.4f, 0.2f), VfxParticleCount = 18,
                VfxMinSpeed = 5f, VfxMaxSpeed = 10f, VfxLifetime = 0.18f, VfxSize = 0.09f
            });
        });

        SeedConfig<BurstStrikeSkillConfig>("SkillConfig_BurstStrike", so =>
        {
            SeedProfile(so, "_impactProfile", new ProfileSeed
            {
                EnableShake = true, ShakeForce = 1.3f, ShakeDuration = 0.22f, ShakeIsSnap = true,
                EnableFreeze = true, FreezeTimeScale = 0.05f, FreezeRealSeconds = 0.1f,
                CaWeight = 0.65f, FovPunchDelta = -3f,
                VfxColor = new Color(2.5f, 0.1f, 1.0f), VfxParticleCount = 26,
                VfxMinSpeed = 6f, VfxMaxSpeed = 13f, VfxLifetime = 0.22f, VfxSize = 0.14f
            });
        });

        SeedConfig<BarrierSkillConfig>("SkillConfig_Barrier", so =>
        {
            SeedProfile(so, "_absorbProfile", new ProfileSeed
            {
                EnableShake = false, EnableFreeze = false, CaWeight = 0.2f, FovPunchDelta = 0f,
                VfxColor = new Color(0.1f, 2.0f, 2.2f), VfxParticleCount = 10,
                VfxMinSpeed = 2f, VfxMaxSpeed = 5f, VfxLifetime = 0.15f, VfxSize = 0.06f
            });
            SeedProfile(so, "_brokenProfile", new ProfileSeed
            {
                EnableShake = true, ShakeForce = 0.4f, ShakeDuration = 0.22f, ShakeIsSnap = true,
                EnableFreeze = false, CaWeight = 0f, FovPunchDelta = 0f,
                VfxColor = new Color(0.4f, 2.4f, 2.6f), VfxParticleCount = 28,
                VfxMinSpeed = 4f, VfxMaxSpeed = 9f, VfxLifetime = 0.22f, VfxSize = 0.1f
            });
            SeedProfile(so, "_expiredProfile", new ProfileSeed
            {
                EnableShake = false, EnableFreeze = false, CaWeight = 0f, FovPunchDelta = 0f,
                VfxColor = new Color(0.1f, 2.0f, 2.2f), VfxParticleCount = 1,
                VfxMinSpeed = 0f, VfxMaxSpeed = 0f, VfxLifetime = 0.3f, VfxSize = 0f
            });
        });
    }

    private static void SeedBossSkillProfiles()
    {
        SeedConfig<BossChargeConfig>("BossSkillConfig_Charge", so =>
        {
            SeedProfile(so, "_impactProfile", new ProfileSeed
            {
                EnableShake = true, ShakeForce = 1.2f, ShakeDuration = 0.2f, ShakeIsSnap = true,
                EnableFreeze = true, FreezeTimeScale = 0.05f, FreezeRealSeconds = 0.1f,
                CaWeight = 0.7f, FovPunchDelta = -4f,
                VfxColor = new Color(2.5f, 0.6f, 0f), VfxParticleCount = 26,
                VfxMinSpeed = 6f, VfxMaxSpeed = 14f, VfxLifetime = 0.2f, VfxSize = 0.13f
            });
        });

        SeedConfig<BossAoESlamConfig>("BossSkillConfig_AoeSlam", so =>
        {
            SeedProfile(so, "_impactProfile", new ProfileSeed
            {
                EnableShake = true, ShakeForce = 1.2f, ShakeDuration = 0.65f, ShakeIsSnap = false,
                EnableFreeze = true, FreezeTimeScale = 0.05f, FreezeRealSeconds = 0.1f,
                CaWeight = 0.7f, FovPunchDelta = -4f,
                VfxColor = new Color(2.2f, 0.3f, 0.05f), VfxParticleCount = 30,
                VfxMinSpeed = 5f, VfxMaxSpeed = 12f, VfxLifetime = 0.25f, VfxSize = 0.16f
            });
        });

        SeedConfig<BossProjectileBurstConfig>("BossSkillConfig_ProjectileBurst", so =>
        {
            SeedProfile(so, "_impactProfile", new ProfileSeed
            {
                EnableShake = true, ShakeForce = 0.4f, ShakeDuration = 0.12f, ShakeIsSnap = true,
                EnableFreeze = false, CaWeight = 0.3f, FovPunchDelta = 0f,
                VfxColor = new Color(2.2f, 0.2f, 0.1f), VfxParticleCount = 14,
                VfxMinSpeed = 3f, VfxMaxSpeed = 7f, VfxLifetime = 0.12f, VfxSize = 0.1f
            });
        });

        SeedConfig<BossTeleportStrikeConfig>("BossSkillConfig_TeleportStrike", so =>
        {
            SeedProfile(so, "_arrivalProfile", new ProfileSeed
            {
                EnableShake = false, EnableFreeze = false,
                CaWeight = 0.3f, IsGlitchCa = true, FovPunchDelta = 0f,
                VfxColor = new Color(1.5f, 0.1f, 2.2f), VfxParticleCount = 20,
                VfxMinSpeed = 4f, VfxMaxSpeed = 10f, VfxLifetime = 0.2f, VfxSize = 0.12f
            });
            SeedProfile(so, "_strikeProfile", new ProfileSeed
            {
                EnableShake = true, ShakeForce = 1.4f, ShakeDuration = 0.15f, ShakeIsSnap = true,
                EnableFreeze = true, FreezeTimeScale = 0.04f, FreezeRealSeconds = 0.06f,
                CaWeight = 0.6f, IsGlitchCa = true, FovPunchDelta = 0f,
                VfxColor = new Color(1.5f, 0.1f, 2.2f), VfxParticleCount = 24,
                VfxMinSpeed = 5f, VfxMaxSpeed = 12f, VfxLifetime = 0.2f, VfxSize = 0.13f
            });
        });

        SeedConfig<BossShieldPhaseConfig>("BossSkillConfig_ShieldPhase", so =>
        {
            SeedProfile(so, "_activationProfile", new ProfileSeed
            {
                EnableShake = false, EnableFreeze = false, CaWeight = 0.25f, FovPunchDelta = 0f,
                VfxColor = new Color(0.2f, 0.6f, 1f), VfxParticleCount = 18,
                VfxMinSpeed = 2f, VfxMaxSpeed = 5f, VfxLifetime = 0.3f, VfxSize = 0.1f
            });
            SeedProfile(so, "_blockDeflectProfile", new ProfileSeed
            {
                EnableShake = false, EnableFreeze = false, CaWeight = 0.2f, FovPunchDelta = 0f,
                VfxColor = new Color(1.0f, 2.2f, 2.4f), VfxParticleCount = 14,
                VfxMinSpeed = 4f, VfxMaxSpeed = 9f, VfxLifetime = 0.15f, VfxSize = 0.08f
            });
        });
    }

    /// <summary>
    /// Loads a skill Config asset by its known name under Assets/ScriptableObjects/
    /// and, if found, hands a SerializedObject to <paramref name="seedFields"/> then
    /// applies and saves. Logs a warning and skips (does not throw) if the asset
    /// hasn't been created yet — run CyberBoss/Setup Player Skills or
    /// CyberBoss/Setup Boss Skills first.
    /// </summary>
    private static void SeedConfig<T>(string assetName, System.Action<SerializedObject> seedFields)
        where T : ScriptableObject
    {
        string path = $"{SOFolder}/{assetName}.asset";
        var config = AssetDatabase.LoadAssetAtPath<T>(path);
        if (config == null)
        {
            Debug.LogWarning($"[SetupPolish] '{assetName}.asset' not found at '{path}' — " +
                "skipping impact-profile seeding for this skill. Run CyberBoss/Setup Player Skills " +
                "or CyberBoss/Setup Boss Skills first.");
            return;
        }

        var so = new SerializedObject(config);
        seedFields(so);
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(config);
    }

    /// <summary>
    /// Seeds one ImpactEffectProfile field's subfields from <paramref name="seed"/>.
    /// Idempotency check: only seeds if _vfxParticleCount is still its C# default (0)
    /// — once seeded (by this script or by hand in the Inspector), never overwritten
    /// on a re-run, so manual tuning is always preserved.
    /// </summary>
    private static void SeedProfile(SerializedObject so, string fieldName, ProfileSeed seed)
    {
        var prop = so.FindProperty(fieldName);
        if (prop == null)
        {
            Debug.LogError($"[SetupPolish] Property '{fieldName}' not found on " +
                $"'{so.targetObject.name}'. Check the field name matches exactly.");
            return;
        }

        var vfxCountProp = prop.FindPropertyRelative("_vfxParticleCount");
        if (vfxCountProp.intValue != 0)
            return; // Already seeded or hand-tuned — never clobber.

        prop.FindPropertyRelative("_enableShake").boolValue       = seed.EnableShake;
        prop.FindPropertyRelative("_shakeForce").floatValue        = seed.ShakeForce;
        prop.FindPropertyRelative("_shakeDuration").floatValue     = seed.ShakeDuration;
        prop.FindPropertyRelative("_shakeIsSnap").boolValue        = seed.ShakeIsSnap;
        prop.FindPropertyRelative("_enableFreeze").boolValue       = seed.EnableFreeze;
        prop.FindPropertyRelative("_freezeTimeScale").floatValue   = seed.FreezeTimeScale;
        prop.FindPropertyRelative("_freezeRealSeconds").floatValue = seed.FreezeRealSeconds;
        prop.FindPropertyRelative("_caWeight").floatValue          = seed.CaWeight;
        prop.FindPropertyRelative("_isGlitchCa").boolValue         = seed.IsGlitchCa;
        prop.FindPropertyRelative("_fovPunchDelta").floatValue     = seed.FovPunchDelta;
        prop.FindPropertyRelative("_vfxColor").colorValue          = seed.VfxColor;
        vfxCountProp.intValue                                      = seed.VfxParticleCount;
        prop.FindPropertyRelative("_vfxMinSpeed").floatValue       = seed.VfxMinSpeed;
        prop.FindPropertyRelative("_vfxMaxSpeed").floatValue       = seed.VfxMaxSpeed;
        prop.FindPropertyRelative("_vfxLifetime").floatValue       = seed.VfxLifetime;
        prop.FindPropertyRelative("_vfxSize").floatValue           = seed.VfxSize;

        Debug.Log($"[SetupPolish] Seeded ImpactEffectProfile '{fieldName}' on '{so.targetObject.name}'.");
    }

    // ------------------------------------------------------------------
    // 8. Self-healing dependency on Setup Combat Effects
    // ------------------------------------------------------------------

    /// <summary>
    /// SetupPart10Effects.Execute() is what actually adds CombatVFXManager (and
    /// CameraImpactEffects) to the GamePolish GameObject — this script only
    /// depends on that component existing (WireCombatVFXManagerShieldConfig()
    /// below). Calling it here — only if CombatVFXManager isn't already present —
    /// makes a single fresh run of this command sufficient regardless of run order.
    /// </summary>
    private static void EnsurePart10EffectsRan()
    {
        var polishGo = GameObject.Find(PolishGoName);
        var vfxManager = polishGo != null ? polishGo.GetComponent<CombatVFXManager>() : null;
        if (vfxManager != null) return;

        Debug.Log("[SetupPolish] CombatVFXManager not found — running " +
            "CyberBoss/Setup Combat Effects automatically before continuing.");
        SetupPart10Effects.Execute();
    }

    // ------------------------------------------------------------------
    // 9. CombatVFXManager — wire the Boss Shield Phase config for the
    //    reactive block-deflect cue (BossController.OnShieldBlockedDamage)
    // ------------------------------------------------------------------

    private static void WireCombatVFXManagerShieldConfig()
    {
        var polishGo = GameObject.Find(PolishGoName);
        var vfxManager = polishGo != null ? polishGo.GetComponent<CombatVFXManager>() : null;
        if (vfxManager == null)
        {
            Debug.LogWarning("[SetupPolish] CombatVFXManager still not found on " +
                $"'{PolishGoName}' after attempting CyberBoss/Setup Combat Effects — " +
                "check the Console above for why that command failed.");
            return;
        }

        string path = $"{SOFolder}/BossSkillConfig_ShieldPhase.asset";
        var shieldConfig = AssetDatabase.LoadAssetAtPath<BossShieldPhaseConfig>(path);
        if (shieldConfig == null)
        {
            Debug.LogWarning($"[SetupPolish] '{path}' not found — run " +
                "CyberBoss/Setup Boss Skills first, then re-run this command.");
            return;
        }

        var so = new SerializedObject(vfxManager);
        so.FindProperty("_shieldPhaseConfig").objectReferenceValue = shieldConfig;
        so.ApplyModifiedProperties();
        EditorUtility.SetDirty(vfxManager);
    }

    // ------------------------------------------------------------------
    // Helper
    // ------------------------------------------------------------------

    /// <summary>Returns the existing component if present; adds and returns a new one if absent.</summary>
    private static T EnsureComponent<T>(GameObject go) where T : Component
    {
        var existing = go.GetComponent<T>();
        if (existing != null) return existing;
        Debug.Log($"[SetupPolish] Added {typeof(T).Name} to '{go.name}'.");
        return go.AddComponent<T>();
    }
}

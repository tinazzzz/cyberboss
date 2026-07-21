using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Singleton manager for impact particle effects.
///
/// One generic pool for untracked player normal-attack hits:
///   _playerSparksPool — HDR cyan sparks for the player's basic (untracked) normal
///                       attack landing on the boss (PlayerMeleeHitbox.Hit()).
/// (The equivalent boss-side generic pool, _bossImpactPool/SpawnBossAttackImpact,
/// was removed — every boss-attack-on-player reaction is one of the 4
/// turn-selectable skills plus the reactive Shield block, all of which drive
/// their own SpawnImpact(profile) call, so the generic fallback had zero callers.)
///
/// Per-skill pools — data-driven. A Dictionary&lt;string,
/// ObjectPool&lt;PooledParticleSystem&gt;&gt; keyed by a skill id string (e.g.
/// "Dash", "BurstStrike", "BossCharge"), built lazily on first use from that
/// skill's ImpactEffectProfile VFX fields via SpawnImpact(pos, effectId, profile).
/// Each skill calls this directly from its own ISkill implementation (mirroring
/// the pre-existing BossChargeSkill.DealContactDamage() singleton-call pattern)
/// instead of this manager guessing which skill fired from a shared event —
/// this is why the old playerHealthSystem.OnDamageTaken auto-subscription (which
/// used to fire the now-removed SpawnBossAttackImpact() for every boss-damages-
/// player event) has been removed: Boss Charge, AoE Slam, Projectile Burst, and
/// Teleport Strike each now spawn their own distinctly-colored/shaped impact
/// directly at their own damage call site instead.
///
/// Each per-skill pool stays small (capacity 2, max 4) — only 1-2 instances of
/// any one skill's effect are ever active at once in a single-boss fight, so
/// this keeps pooled object count and memory bounded for WebGL even though the
/// total pool count grows from 2 to roughly 10.
///
/// Reactive Shield block feedback: subscribes directly to
/// BossController.OnShieldBlockedDamage (mirroring the existing player-side
/// subscription pattern) because that reaction is fired from BossController's
/// damage-routing code, not from a skill's own Execute() call site — Boss Shield
/// Phase's activation shimmer, by contrast, IS called directly from
/// BossShieldPhaseSkill.Execute() and does not need this subscription.
///
/// Usage example (per-skill signature, called directly from an ISkill):
/// <code>
///   CombatVFXManager.Instance?.SpawnImpact(worldPosition, "BurstStrike", _config.ImpactProfile);
/// </code>
/// </summary>
public class CombatVFXManager : MonoBehaviour
{
    public static CombatVFXManager Instance { get; private set; }

    [SerializeField] private CombatEffectsConfig   _config;

    /// <summary>
    /// Supplies the block-deflect ImpactEffectProfile for the reactive Shield-block
    /// cue, fired from BossController.OnShieldBlockedDamage — the one skill effect
    /// in this system triggered by an event rather than a direct per-skill call,
    /// since BossController (not BossShieldPhaseSkill) owns that damage-routing code.
    /// </summary>
    [SerializeField] private BossShieldPhaseConfig _shieldPhaseConfig;

    private ObjectPool<PooledParticleSystem> _playerSparksPool;

    private readonly Dictionary<string, ObjectPool<PooledParticleSystem>> _skillPools =
        new Dictionary<string, ObjectPool<PooledParticleSystem>>();

    private BossController  _bossController;
    private System.Action<Vector3> _onShieldBlockedDelegate;

    private const int SkillPoolDefaultCapacity = 2;
    private const int SkillPoolMaxSize         = 4;

    // ------------------------------------------------------------------
    // Public API — generic reactions (unchanged, not recolored)
    // ------------------------------------------------------------------

    /// <summary>
    /// Cyan HDR spark burst at the melee contact point.
    /// Call from PlayerMeleeHitbox.Hit() after TakeDamage() succeeds.
    /// </summary>
    public void SpawnPlayerMeleeImpact(Vector3 worldPosition)
        => SpawnFromPool(_playerSparksPool, worldPosition);

    // ------------------------------------------------------------------
    // Public API — per-skill signature
    // ------------------------------------------------------------------

    /// <summary>
    /// Spawns a one-shot particle burst configured from the given skill's
    /// ImpactEffectProfile VFX fields, using (and lazily creating) a small
    /// dedicated pool keyed by effectId. No-op if profile is null.
    /// </summary>
    public void SpawnImpact(Vector3 worldPosition, string effectId, ImpactEffectProfile profile)
    {
        if (profile == null) return;
        ObjectPool<PooledParticleSystem> pool = GetOrCreateSkillPool(effectId, profile);
        SpawnFromPool(pool, worldPosition);
    }

    /// <summary>
    /// Pre-sizes a skill pool to a caller-specified capacity/maxSize instead of the
    /// generic 2/4 default — for skills that legitimately need several concurrent
    /// instances of their own effect in a single frame (e.g. Boss Projectile Burst
    /// spawning one muzzle flash per projectile in its spread). Call once, before
    /// the skill's first SpawnImpact of the session (its own Init() is the right
    /// place) so normal operation never has to instantiate-then-immediately-destroy
    /// past the generic pool's smaller default. No-op if a pool for this effectId
    /// already exists (never shrinks/rebuilds an in-use pool) or profile is null.
    /// </summary>
    public void EnsureSkillPoolCapacity(string effectId, ImpactEffectProfile profile, int capacity, int maxSize)
    {
        if (profile == null || _skillPools.ContainsKey(effectId)) return;
        _skillPools[effectId] = CreateSkillPool(effectId, profile, capacity, maxSize);
    }

    /// <summary>
    /// Builds a flat ground-disc "shockwave ring" at worldPosition: a runtime
    /// Quad (same GameObject.CreatePrimitive + Unlit-transparent-material
    /// technique already used for the windup telegraph indicators in
    /// BossAoeSlamSkill/BossChargeSkill/BossTeleportStrikeSkill), animated from
    /// near-zero scale up to targetDiameter while fading alpha to 0 over
    /// durationSeconds, then destroying itself. A one-shot particle burst reads
    /// as "dots" no matter how it's tuned — this is a genuinely different
    /// technique for the "expanding ring on the ground" read, not a recolor of
    /// SpawnImpact. Self-contained coroutine (not pooled — infrequent, one per
    /// activation, tiny GameObject/Material footprint, auto-destroys on completion).
    /// </summary>
    public void SpawnGroundRing(Vector3 worldPosition, Color tint, float targetRadius, float durationSeconds)
    {
        StartCoroutine(GroundRingCoroutine(worldPosition, tint, targetRadius, durationSeconds));
    }

    private IEnumerator GroundRingCoroutine(Vector3 worldPosition, Color tint, float targetRadius, float durationSeconds)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        disc.name = "GroundRing";

        var col = disc.GetComponent<Collider>();
        if (col != null) Destroy(col);

        disc.transform.position   = worldPosition + Vector3.up * 0.05f;
        disc.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
        disc.transform.localScale = Vector3.one * 0.01f;

        Material mat = CreateGroundRingMaterial(tint);
        disc.GetComponent<Renderer>().sharedMaterial = mat;

        float duration  = Mathf.Max(0.05f, durationSeconds);
        float diameter  = Mathf.Max(0.01f, targetRadius * 2f);
        float startAlpha = tint.a > 0f ? tint.a : 0.8f;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            float scale = Mathf.Lerp(0.01f, diameter, t);
            disc.transform.localScale = new Vector3(scale, scale, 1f);

            float alpha = Mathf.Lerp(startAlpha, 0f, t);
            mat.SetColor("_BaseColor", new Color(tint.r, tint.g, tint.b, alpha));

            yield return null;
        }

        Destroy(mat);
        Destroy(disc);
    }

    /// <summary>
    /// URP Unlit + transparent alpha-blend material for the ground ring — same
    /// blend-state setup already used for the windup telegraph discs elsewhere
    /// in this codebase (Surface=Transparent, SrcAlpha/OneMinusSrcAlpha, ZWrite off).
    /// </summary>
    private static Material CreateGroundRingMaterial(Color tint)
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Sprites/Default");
        var mat = new Material(shader != null ? shader : Shader.Find("Standard"));

        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend",   0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite",   0);
        mat.SetColor("_BaseColor", tint);

        return mat;
    }

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        if (_config == null)
        {
            Debug.LogError("[CombatVFXManager] CombatEffectsConfig not assigned on " +
                $"'{name}'. Run CyberBoss/Setup Effects.");
            return;
        }

        BuildGenericPools();
        SubscribeToGameEvents();
    }

    private void OnDestroy()
    {
        UnsubscribeFromGameEvents();

        _playerSparksPool?.Dispose();

        foreach (var pool in _skillPools.Values)
            pool.Dispose();
        _skillPools.Clear();

        if (Instance == this)
            Instance = null;
    }

    // ------------------------------------------------------------------
    // Generic pool construction
    // ------------------------------------------------------------------

    private void BuildGenericPools()
    {
        var cfg = _config;

        _playerSparksPool = new ObjectPool<PooledParticleSystem>(
            createFunc:      () => CreatePlayerSparksParticleSystem(cfg),
            actionOnGet:     pps => pps.gameObject.SetActive(true),
            actionOnRelease: pps => pps.gameObject.SetActive(false),
            actionOnDestroy: pps => { if (pps != null) Destroy(pps.gameObject); },
            collectionCheck: false,
            defaultCapacity: 4,
            maxSize: 8
        );
    }

    private PooledParticleSystem CreatePlayerSparksParticleSystem(CombatEffectsConfig cfg)
    {
        ParticleSystem ps = CreateBaseParticleSystemGameObject("PlayerSparks", out PooledParticleSystem pps, out GameObject go);

        ConfigureBurstParticleSystem(ps,
            cfg.PlayerSparkColor, cfg.PlayerSparkCount,
            cfg.PlayerSparkMinSpeed, cfg.PlayerSparkMaxSpeed,
            cfg.PlayerSparkLifetime, cfg.PlayerSparkSize,
            shapeRadius: 0.15f, gravityModifier: 0.3f);

        go.SetActive(false);
        return pps;
    }

    // ------------------------------------------------------------------
    // Per-skill pool construction
    // ------------------------------------------------------------------

    private ObjectPool<PooledParticleSystem> GetOrCreateSkillPool(string effectId, ImpactEffectProfile profile)
    {
        if (_skillPools.TryGetValue(effectId, out var existing))
            return existing;

        var pool = CreateSkillPool(effectId, profile, SkillPoolDefaultCapacity, SkillPoolMaxSize);
        _skillPools[effectId] = pool;
        return pool;
    }

    /// <summary>
    /// Shared pool-construction helper behind both the default-sized lazy path
    /// (GetOrCreateSkillPool) and the caller-sized override (EnsureSkillPoolCapacity).
    /// </summary>
    private ObjectPool<PooledParticleSystem> CreateSkillPool(
        string effectId, ImpactEffectProfile profile, int capacity, int maxSize)
    {
        return new ObjectPool<PooledParticleSystem>(
            createFunc:      () => CreateSkillParticleSystem(effectId, profile),
            actionOnGet:     pps => pps.gameObject.SetActive(true),
            actionOnRelease: pps => pps.gameObject.SetActive(false),
            actionOnDestroy: pps => { if (pps != null) Destroy(pps.gameObject); },
            collectionCheck: false,
            defaultCapacity: capacity,
            maxSize: maxSize
        );
    }

    private PooledParticleSystem CreateSkillParticleSystem(string effectId, ImpactEffectProfile profile)
    {
        ParticleSystem ps = CreateBaseParticleSystemGameObject($"VFX_{effectId}", out PooledParticleSystem pps, out GameObject go);

        ConfigureBurstParticleSystem(ps,
            profile.VfxColor, profile.VfxParticleCount,
            profile.VfxMinSpeed, profile.VfxMaxSpeed,
            profile.VfxLifetime, profile.VfxSize,
            shapeRadius: 0.2f, gravityModifier: 0.3f);

        go.SetActive(false);
        return pps;
    }

    // ------------------------------------------------------------------
    // Shared particle system construction — no prefab assets, code-built only
    // ------------------------------------------------------------------

    private ParticleSystem CreateBaseParticleSystemGameObject(
        string goName, out PooledParticleSystem pps, out GameObject go)
    {
        go = new GameObject(goName);
        go.transform.SetParent(transform);

        var ps = go.AddComponent<ParticleSystem>();
        // Stop immediately — AddComponent starts playing (playOnAwake=true by default)
        // and Unity forbids setting main.duration on a playing system.
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        pps = go.AddComponent<PooledParticleSystem>();
        var psr = go.GetComponent<ParticleSystemRenderer>();
        AssignParticleMaterial(psr);

        return ps;
    }

    /// <summary>
    /// Configures the main/emission/shape/colorOverLifetime modules shared by
    /// every one-shot burst particle system this manager creates, generic and
    /// boss/player pools alike — single-burst-at-t=0, fade to transparent, world
    /// space, unscaled time (so bursts play at real speed through HitFreezeManager's
    /// Time.timeScale dip).
    /// </summary>
    private static void ConfigureBurstParticleSystem(
        ParticleSystem ps, Color color, int count, float minSpeed, float maxSpeed,
        float lifetime, float size, float shapeRadius, float gravityModifier)
    {
        var main = ps.main;
        main.loop            = false;
        main.playOnAwake     = false;
        main.stopAction      = ParticleSystemStopAction.None;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.maxParticles    = Mathf.Max(1, count * 2);
        main.duration        = 0.1f;
        main.startLifetime   = lifetime;
        main.startSpeed      = new ParticleSystem.MinMaxCurve(minSpeed, maxSpeed);
        main.startSize       = size;
        main.startColor      = new ParticleSystem.MinMaxGradient(color);
        main.gravityModifier = gravityModifier;

        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = 0f;
        emission.SetBursts(new[] { new ParticleSystem.Burst(0f, (short)Mathf.Max(1, count)) });

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Sphere;
        shape.radius    = shapeRadius;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            colorKeys: new GradientColorKey[]
            {
                new GradientColorKey(Color.white, 0f),
                new GradientColorKey(Color.white, 0.8f)
            },
            alphaKeys: new GradientAlphaKey[]
            {
                new GradientAlphaKey(1f, 0f),
                new GradientAlphaKey(1f, 0.7f),
                new GradientAlphaKey(0f, 1f)
            }
        );
        col.color = new ParticleSystem.MinMaxGradient(gradient);
    }

    /// <summary>
    /// Assigns a URP-compatible material so particles are not pink on WebGL.
    /// Falls back to Sprites/Default (always present) if the URP particle shader is absent.
    /// </summary>
    private static void AssignParticleMaterial(ParticleSystemRenderer psr)
    {
        const string urpParticleShader = "Universal Render Pipeline/Particles/Unlit";
        const string fallbackShader    = "Sprites/Default";

        var shader = Shader.Find(urpParticleShader) ?? Shader.Find(fallbackShader);
        if (shader == null) return; // leave default material

        var mat = new Material(shader);
        // White BaseColor so particle startColor drives the visible tint.
        // HDR startColor values > 1 feed into URP's bloom — the arena profile
        // is at intensity 2.5 with threshold 0.4, so HDR particles will glow.
        if (mat.HasProperty("_BaseColor"))
            mat.SetColor("_BaseColor", Color.white);

        psr.material = mat;
    }

    // ------------------------------------------------------------------
    // Event wiring — reactive Shield-block cue only (see class remarks)
    // ------------------------------------------------------------------

    private void SubscribeToGameEvents()
    {
        var bossGo = GameObject.FindWithTag("Boss");
        if (bossGo == null)
        {
            Debug.LogWarning("[CombatVFXManager] No GameObject tagged 'Boss' found. " +
                "Shield-block deflect VFX will not auto-spawn.");
            return;
        }

        _bossController = bossGo.GetComponent<BossController>();

        if (_bossController == null)
        {
            Debug.LogWarning("[CombatVFXManager] BossController not found on boss. " +
                "Shield-block deflect VFX will not auto-spawn.");
            return;
        }

        _onShieldBlockedDelegate = OnShieldBlockedDamage;
        _bossController.OnShieldBlockedDamage += _onShieldBlockedDelegate;
    }

    private void UnsubscribeFromGameEvents()
    {
        if (_bossController != null && _onShieldBlockedDelegate != null)
            _bossController.OnShieldBlockedDamage -= _onShieldBlockedDelegate;
    }

    private void OnShieldBlockedDamage(Vector3 hitPoint)
    {
        if (_shieldPhaseConfig == null)
        {
            Debug.LogWarning("[CombatVFXManager] BossShieldPhaseConfig not assigned — " +
                "shield-block deflect VFX skipped.");
            return;
        }

        ImpactEffectProfile profile = _shieldPhaseConfig.BlockDeflectProfile;
        SpawnImpact(hitPoint, "BossShieldBlock", profile);
        CameraImpactEffects.Instance?.ImpactCA(profile);
    }

    // ------------------------------------------------------------------
    // Pool helper
    // ------------------------------------------------------------------

    private static void SpawnFromPool(
        ObjectPool<PooledParticleSystem> pool, Vector3 worldPosition)
    {
        if (pool == null) return;
        var pps = pool.Get();
        if (pps != null)
            pps.PlayAt(worldPosition, pool);
    }
}

using UnityEngine;

/// <summary>
/// Dash skill implementation.
///
/// Moves the player in their facing direction over a short duration and
/// sets IsInvincible for that window. DamageHandler checks IsInvincible
/// before applying damage.
///
/// Movement is additive: PlayerController still applies normal input movement
/// during the dash (per spec — movement is not locked for Dash).
///
/// Cast feedback: a runtime-built TrailRenderer (child of the player),
/// not a particle burst — a burst of dots cannot read as a motion trail/
/// afterimage regardless of shape/color tuning. Emitting is enabled only for
/// the dash's DashDuration window so ordinary movement never leaves a trail.
/// No shake, no FOV punch (both felt dizzying on a skill spammable up to
/// 10/min in playtest) — only a very brief, shallow freeze + a light CA tick.
///
/// Boss counter: Teleport Strike.
/// </summary>
public class DashSkill : ISkill
{
    private readonly DashSkillConfig _config;
    private SkillCooldown _cooldown;

    private CharacterController _characterController;
    private Transform           _transform;
    private Animator            _animator;

    private bool    _isDashing;
    private float   _dashElapsed;
    private float   _iFrameElapsed;
    private Vector3 _dashVelocity;
    private Vector3 _primedDirection;

    // Motion-trail afterimage, built once in Init(), toggled per-dash.
    private TrailRenderer _dashTrail;
    private Material      _dashTrailMaterial;
    private const float   DashTrailWidth = 0.35f;

    // Cache Animator parameter hash to avoid per-frame string lookup.
    private static readonly int DashTriggerHash = Animator.StringToHash("DashTrigger");

    public bool  IsReady          => _cooldown.IsReady;
    public bool  IsInvincible     { get; private set; }
    public float CooldownProgress => _cooldown.NormalizedProgress;

    public DashSkill(DashSkillConfig config)
    {
        _config   = config;
        _cooldown = new SkillCooldown(config.Cooldown);
    }

    /// <param name="characterController">Player CharacterController — for per-frame dash move.</param>
    /// <param name="playerTransform">Player Transform — for facing direction at dash time.</param>
    /// <param name="animator">Player Animator — to fire DashTrigger.</param>
    public void Init(CharacterController characterController, Transform playerTransform, Animator animator)
    {
        _characterController = characterController;
        _transform           = playerTransform;
        _animator            = animator;

        _dashTrail = CreateDashTrail(playerTransform);
    }

    /// <summary>
    /// Builds the motion-trail TrailRenderer as a child of the player, matching
    /// this codebase's "build everything at runtime, no prefabs" convention (see
    /// PlayerCombatEffects' LineRenderers/primitives). Starts with emitting = false
    /// so ordinary movement between dashes never leaves a trail.
    /// </summary>
    private TrailRenderer CreateDashTrail(Transform playerTransform)
    {
        var go = new GameObject("DashTrail");
        go.transform.SetParent(playerTransform, worldPositionStays: false);
        go.transform.localPosition = new Vector3(0f, 0.9f, 0f); // roughly chest height

        var trail = go.AddComponent<TrailRenderer>();

        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Sprites/Default");
        _dashTrailMaterial = new Material(shader != null ? shader : Shader.Find("Standard"));
        if (_dashTrailMaterial.HasProperty("_BaseColor"))
            _dashTrailMaterial.SetColor("_BaseColor", Color.white);
        // Additive blend: the trail layers on top of the scene without darkening it.
        _dashTrailMaterial.SetFloat("_Surface", 1f);
        _dashTrailMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _dashTrailMaterial.SetOverrideTag("RenderType", "Transparent");
        _dashTrailMaterial.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        _dashTrailMaterial.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _dashTrailMaterial.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        _dashTrailMaterial.SetInt("_ZWrite",   0);

        Color tint = _config.ImpactProfile != null ? _config.ImpactProfile.VfxColor : new Color(0.2f, 0.4f, 1f);
        float fadeTime = _config.ImpactProfile != null && _config.ImpactProfile.VfxLifetime > 0f
            ? _config.ImpactProfile.VfxLifetime
            : 0.18f;

        trail.material           = _dashTrailMaterial;
        trail.time                = fadeTime;
        trail.startWidth          = DashTrailWidth;
        trail.endWidth             = 0f; // tapers to a point at the tail
        trail.minVertexDistance   = 0.05f;
        trail.autodestruct        = false;
        trail.emitting            = false;
        trail.shadowCastingMode   = UnityEngine.Rendering.ShadowCastingMode.Off;
        trail.receiveShadows      = false;

        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(tint, 0f), new GradientColorKey(tint, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.8f, 0f), new GradientAlphaKey(0f, 1f) });
        trail.colorGradient = gradient;

        return trail;
    }

    /// <summary>
    /// Set the direction this dash will travel. Call from PlayerSkills before Execute.
    /// Falls back to the character's facing direction if never called or set to zero.
    /// </summary>
    public void PrimeDirection(Vector3 direction) => _primedDirection = direction;

    /// <summary>
    /// Begin a dash in the primed direction (or facing direction if no prime set).
    /// Blocked while cooldown is active or a dash is already in progress.
    /// </summary>
    public void Execute(GameObject user)
    {
        if (!_cooldown.IsReady || _isDashing) return;

        _cooldown.Trigger();
        _isDashing     = true;
        _dashElapsed   = 0f;
        _iFrameElapsed = 0f;
        IsInvincible   = true;

        // Use the move-input direction if primed, fall back to character facing.
        Vector3 dir = _primedDirection.sqrMagnitude > 0.01f
            ? _primedDirection
            : _transform.forward;
        _primedDirection = Vector3.zero;

        // Constant velocity = distance / duration so total travel equals DashDistance.
        _dashVelocity = dir * (_config.DashDistance / _config.DashDuration);

        _animator.SetTrigger(DashTriggerHash);

        // No shake and no FOV punch on cast — an i-frame dodge should
        // read as "you're safe", not impactful, and FOV punches on a spammable
        // (up to 10/min) skill felt dizzying in playtest. Instead: a very brief,
        // shallow freeze + a light CA tick (kept minimal so it never feels naggy
        // at spam frequency) plus the motion-trail afterimage (Clear() first so a
        // stale trail from a previous dash never visibly "jumps" to the new start
        // position when emitting flips back on).
        HitFreezeManager.Instance?.Freeze(_config.ImpactProfile);
        CameraImpactEffects.Instance?.ImpactCA(_config.ImpactProfile);
        if (_dashTrail != null)
        {
            _dashTrail.Clear();
            _dashTrail.emitting = true;
        }
    }

    /// <summary>
    /// Called by PlayerSkills when DamageHandler.OnPerfectDodge fires (a boss attack
    /// fully absorbed by these dash i-frames). Plays a brighter re-trigger of the
    /// dash VFX plus a light freeze+CA reward — tuned independently from the cast-time
    /// ImpactProfile via DashSkillConfig.PerfectDodgeProfile. This moment still uses a
    /// one-shot particle burst (a rewarding "pop", not a motion trail).
    /// </summary>
    public void NotifyPerfectDodge()
    {
        if (_transform == null) return;

        HitFreezeManager.Instance?.Freeze(_config.PerfectDodgeProfile);
        CameraImpactEffects.Instance?.ImpactCA(_config.PerfectDodgeProfile);
        CombatVFXManager.Instance?.SpawnImpact(_transform.position, "DashPerfectDodge", _config.PerfectDodgeProfile);
    }

    /// <summary>
    /// Clears all in-flight dash state and the pending animator trigger. Call on respawn.
    /// </summary>
    public void Reset(Animator animator)
    {
        _isDashing       = false;
        _dashElapsed     = 0f;
        _iFrameElapsed   = 0f;
        _dashVelocity    = Vector3.zero;
        _primedDirection = Vector3.zero;
        IsInvincible     = false;
        _cooldown.Reset();
        animator.ResetTrigger(DashTriggerHash);

        if (_dashTrail != null)
        {
            _dashTrail.emitting = false;
            _dashTrail.Clear();
        }
    }

    /// <summary>
    /// Destroy the pre-allocated trail GameObject and material. Call from
    /// PlayerSkills.OnDestroy() — mirrors the boss skills' Cleanup() pattern
    /// (BossChargeSkill, BossAoESlamSkill, BossTeleportStrikeSkill).
    /// </summary>
    public void Cleanup()
    {
        if (_dashTrail != null)
        {
            Object.Destroy(_dashTrail.gameObject);
            _dashTrail = null;
        }

        if (_dashTrailMaterial != null)
        {
            Object.Destroy(_dashTrailMaterial);
            _dashTrailMaterial = null;
        }
    }

    /// <summary>
    /// Ticks the cooldown and, while a dash is active, applies per-frame displacement.
    /// Called by PlayerSkills.Update() every frame.
    /// </summary>
    public void UpdateCooldown(float deltaTime)
    {
        _cooldown.Tick(deltaTime);

        if (!_isDashing && !IsInvincible) return;

        if (_isDashing)
        {
            _characterController.Move(_dashVelocity * deltaTime);
            _dashElapsed += deltaTime;
            if (_dashElapsed >= _config.DashDuration)
            {
                _isDashing = false;
                if (_dashTrail != null)
                    _dashTrail.emitting = false;
            }
        }

        if (IsInvincible)
        {
            _iFrameElapsed += deltaTime;
            if (_iFrameElapsed >= _config.InvincibilityDuration)
                IsInvincible = false;
        }
    }
}

using System.Collections;
using UnityEngine;

/// <summary>
/// Boss Charge skill.
///
/// Moves the boss directly toward the player using Transform.position steps each
/// frame. The CyberSoldier boss has no CharacterController or NavMeshAgent —
/// direct position assignment is the correct movement method.
///
/// On contact (within ContactRadius) the boss deals damage through the player's
/// DamageHandler so the full defense chain (Dash invincibility, Parry, Barrier)
/// is always respected. If the boss travels MaxRange without contact the charge
/// ends cleanly without applying damage.
///
/// VFX: boss localScale is enlarged at charge start and restored on completion or
/// stagger interruption. BossSkills.ResetActiveSkill() calls StopExecution() so
/// scale is always restored even if the coroutine is killed mid-charge.
///
/// Counters the player's Ranged Blast — closes distance before the player can
/// maintain safe firing range.
/// </summary>
public class BossChargeSkill : ISkill
{
    private readonly BossChargeConfig _config;
    private SkillCooldown _cooldown;

    private MonoBehaviour _runner;
    private Transform     _bossTransform;
    private Transform     _playerTransform;
    private DamageHandler _playerDamageHandler;
    private Vector3       _originalScale;
    private Coroutine     _activeCoroutine;

    private Animator              _animator;
    private Renderer[]            _bossRenderers;
    private MaterialPropertyBlock _mpb;
    private PlayerSkills          _playerSkills;

    // Arena clamp values — prevent charge from pushing the boss through walls.
    private Vector3 _arenaCenter;
    private float   _arenaRadius;

    // Ground line indicator shown during windup — tells the player exactly where
    // the charge will travel. Updated each frame during windup so it tracks the boss aim.
    private Material   _chargeLineMaterial;
    private GameObject _chargeLineIndicator;
    private const float LineWidth = 1.0f;

    // Continuous dust trail while charging (windup + run). A looping
    // emitter is a different VFX category from the one-shot pooled bursts
    // CombatVFXManager handles (ImpactEffectProfile's schema is burst-oriented,
    // with no rate/loop fields) — built once here, tinted from the profile's
    // VfxColor, and toggled on/off rather than pooled since only one boss exists.
    private ParticleSystem _dustTrail;
    private const float DustTrailEmissionRate = 14f;
    private const float DustTrailMinSpeed     = 0.5f;
    private const float DustTrailMaxSpeed     = 1.5f;
    private const float DustTrailLifetime     = 0.5f;
    private const float DustTrailSize         = 0.18f;

    // Orange tint applied to boss renderers during the windup window so the
    // player has a clear visual cue to react ("red means danger").
    private static readonly Color      ChargeTintColor = new Color(1f, 0.35f, 0f, 1f);
    private static readonly int        BaseColorId     = Shader.PropertyToID("_BaseColor");

    // Must match SetupBossAnimator state names — update both if renaming.
    private const string ChargeWindupStateName = "ChargeWindup";
    private const string ChargeRunStateName    = "ChargeRun";

    // Wire "ChargeTrigger" in the boss Animator to a charge/rush animation.
    private static readonly int ChargeAnimHash = Animator.StringToHash("ChargeTrigger");

    public bool  IsReady          => _cooldown.IsReady;
    public bool  IsExecuting      { get; private set; }
    public float CooldownProgress => _cooldown.NormalizedProgress;

    public BossChargeSkill(BossChargeConfig config)
    {
        _config   = config;
        _cooldown = new SkillCooldown(config.Cooldown);
    }

    /// <param name="runner">MonoBehaviour coroutine host — BossSkills.</param>
    /// <param name="bossTransform">Root transform moved during the charge.</param>
    /// <param name="playerTransform">Target the boss dashes toward each frame.</param>
    /// <param name="playerDamageHandler">
    /// Explicit DamageHandler reference ensures the contact hit routes through the
    /// player's defense chain. Must not be null for damage to apply.
    /// </param>
    /// <param name="arenaCenter">World-space arena center for per-step bounds clamping.</param>
    /// <param name="arenaRadius">Max distance the boss may be from arenaCenter.</param>
    public void Init(
        MonoBehaviour runner,
        Transform     bossTransform,
        Transform     playerTransform,
        DamageHandler playerDamageHandler,
        Renderer[]    bossRenderers,
        Animator      animator,
        Vector3       arenaCenter,
        float         arenaRadius)
    {
        _runner              = runner;
        _bossTransform       = bossTransform;
        _playerTransform     = playerTransform;
        _playerDamageHandler = playerDamageHandler;
        _originalScale       = bossTransform.localScale;
        _bossRenderers       = bossRenderers;
        _mpb                 = new MaterialPropertyBlock();
        _animator            = animator;
        _arenaCenter         = arenaCenter;
        _arenaRadius         = arenaRadius;
        // One-time GetComponent in Init() — not in Update, so allocation is fine.
        _playerSkills        = playerDamageHandler?.GetComponent<PlayerSkills>();

        // All boss movement is code-driven (Transform.position). Root motion from
        // clips like Unarmed-Run-Forward would fight the coroutine and keep sliding
        // the boss after ChargeRoutine ends. Disable it here for all states.
        if (animator != null) animator.applyRootMotion = false;

        var urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        _chargeLineMaterial = new Material(
            urpUnlit != null ? urpUnlit : Shader.Find("Sprites/Default"));
        _chargeLineMaterial.SetFloat("_Surface", 1f);
        _chargeLineMaterial.SetFloat("_Blend",   0f);
        _chargeLineMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _chargeLineMaterial.SetOverrideTag("RenderType", "Transparent");
        _chargeLineMaterial.renderQueue =
            (int)UnityEngine.Rendering.RenderQueue.Transparent;
        _chargeLineMaterial.SetInt("_SrcBlend",
            (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _chargeLineMaterial.SetInt("_DstBlend",
            (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _chargeLineMaterial.SetInt("_ZWrite", 0);
        _chargeLineMaterial.SetColor("_BaseColor", new Color(1f, 0.35f, 0f, 0.45f));

        _dustTrail = CreateDustTrail();
    }

    /// <summary>
    /// Builds the continuous dust-trail particle system, parented to the boss so
    /// it follows automatically. Starts stopped — ChargeRoutine() plays/stops it
    /// for the windup+run duration only.
    /// </summary>
    private ParticleSystem CreateDustTrail()
    {
        var go = new GameObject("ChargeDustTrail");
        go.transform.SetParent(_bossTransform, worldPositionStays: false);
        go.transform.localPosition = new Vector3(0f, 0.1f, -0.4f); // trails behind the boss

        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

        var psr = go.GetComponent<ParticleSystemRenderer>();
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Sprites/Default");
        if (shader != null)
        {
            var mat = new Material(shader);
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", Color.white);
            psr.material = mat;
        }

        Color tint = _config.ImpactProfile != null ? _config.ImpactProfile.VfxColor : new Color(2.5f, 0.6f, 0f);

        var main = ps.main;
        main.loop            = true;
        main.playOnAwake     = false;
        main.simulationSpace = ParticleSystemSimulationSpace.World;
        main.useUnscaledTime = true;
        main.startLifetime   = DustTrailLifetime;
        main.startSpeed      = new ParticleSystem.MinMaxCurve(DustTrailMinSpeed, DustTrailMaxSpeed);
        main.startSize       = DustTrailSize;
        main.startColor      = new ParticleSystem.MinMaxGradient(tint);
        main.maxParticles    = 40;

        var emission = ps.emission;
        emission.enabled      = true;
        emission.rateOverTime = DustTrailEmissionRate;

        var shape = ps.shape;
        shape.enabled   = true;
        shape.shapeType = ParticleSystemShapeType.Cone;
        shape.angle     = 15f;
        shape.radius    = 0.2f;

        var col = ps.colorOverLifetime;
        col.enabled = true;
        var gradient = new Gradient();
        gradient.SetKeys(
            new GradientColorKey[] { new GradientColorKey(Color.white, 0f), new GradientColorKey(Color.white, 1f) },
            new GradientAlphaKey[] { new GradientAlphaKey(0.6f, 0f), new GradientAlphaKey(0f, 1f) });
        col.color = new ParticleSystem.MinMaxGradient(gradient);

        return ps;
    }

    /// <summary>
    /// Destroy the pre-allocated charge line material. Call from BossSkills.OnDestroy().
    /// </summary>
    public void Cleanup()
    {
        if (_chargeLineMaterial != null)
        {
            Object.Destroy(_chargeLineMaterial);
            _chargeLineMaterial = null;
        }

        if (_dustTrail != null)
        {
            Object.Destroy(_dustTrail.gameObject);
            _dustTrail = null;
        }
    }

    public void Execute(GameObject user)
    {
        if (!_cooldown.IsReady || _runner == null || _playerTransform == null) return;

        _cooldown.Trigger();
        IsExecuting      = true;
        if (_animator != null) _animator.SetTrigger(ChargeAnimHash);
        _activeCoroutine = _runner.StartCoroutine(ChargeRoutine());
    }

    public void UpdateCooldown(float deltaTime) => _cooldown.Tick(deltaTime);

    public void Reset(Animator animator)
    {
        StopExecution();
        _cooldown.Reset();
        animator?.ResetTrigger(ChargeAnimHash);
    }

    /// <summary>
    /// Stop the in-flight coroutine and restore any dirty state.
    /// Called by BossSkills.ResetActiveSkill() when stagger interrupts this skill.
    /// </summary>
    public void StopExecution()
    {
        if (_activeCoroutine != null && _runner != null)
        {
            _runner.StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }

        if (_bossTransform != null)
            _bossTransform.localScale = _originalScale;

        if (_animator != null)
        {
            _animator.ResetTrigger(ChargeAnimHash);
            _animator.CrossFade(ChargeWindupStateName, 0.15f, 0);
        }
        ClearTint();
        DestroyLineIndicator();
        _dustTrail?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        IsExecuting = false;
    }

    // ------------------------------------------------------------------
    // VFX — orange tint during charge windup (MaterialPropertyBlock, no allocation)
    // ------------------------------------------------------------------

    private void ApplyChargeTint()
    {
        if (_bossRenderers == null || _mpb == null) return;

        _mpb.SetColor(BaseColorId, ChargeTintColor);
        for (int i = 0; i < _bossRenderers.Length; i++)
        {
            if (_bossRenderers[i] != null)
                _bossRenderers[i].SetPropertyBlock(_mpb);
        }
    }

    private void ClearTint()
    {
        if (_bossRenderers == null || _mpb == null) return;

        _mpb.Clear();
        for (int i = 0; i < _bossRenderers.Length; i++)
        {
            if (_bossRenderers[i] != null)
                _bossRenderers[i].SetPropertyBlock(_mpb);
        }
    }

    // ------------------------------------------------------------------
    // Coroutine
    // ------------------------------------------------------------------

    private IEnumerator ChargeRoutine()
    {
        // Windup: scale up + orange tint, and show a ground line in the charge direction.
        // The line tracks the player in real-time during windup so it always shows where
        // the boss will actually charge when it fires.
        _bossTransform.localScale = _originalScale * _config.ChargeScaleMultiplier;
        ApplyChargeTint();
        _dustTrail?.Play();

        float windupTimer = 0f;
        while (windupTimer < _config.WindupDuration)
        {
            windupTimer += Time.deltaTime;

            if (_playerTransform != null)
            {
                Vector3 aimDir = _playerTransform.position - _bossTransform.position;
                aimDir.y = 0f;
                if (aimDir.sqrMagnitude > 0.001f)
                {
                    _bossTransform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
                    PlaceLineIndicator(aimDir.normalized);
                }
            }

            yield return null;
        }

        _bossTransform.localScale = _originalScale;
        ClearTint();
        DestroyLineIndicator();

        if (_playerTransform == null)
        {
            FinishCharge();
            yield break;
        }

        // Lock direction at the exact moment windup ends — not homing.
        // Dodging sideways during windup causes the boss to shoot past.
        Vector3 chargeDir = _playerTransform.position - _bossTransform.position;
        chargeDir.y = 0f;
        if (chargeDir.sqrMagnitude < 0.001f)
            chargeDir = _bossTransform.forward;
        chargeDir = chargeDir.normalized;

        _bossTransform.rotation = Quaternion.LookRotation(chargeDir, Vector3.up);
        if (_animator != null) _animator.CrossFade(ChargeRunStateName, 0.15f, 0);

        float distanceTravelled = 0f;
        bool  hitPlayer         = false;

        while (distanceTravelled < _config.MaxRange)
        {
            float step = _config.ChargeSpeed * Time.deltaTime;
            distanceTravelled       += step;
            _bossTransform.position += chargeDir * step;

            // Contact check AFTER moving — prevents skipping over the player when
            // ChargeSpeed * deltaTime exceeds ContactRadius in a single step.
            if (_playerTransform != null)
            {
                Vector3 bossXZ   = new Vector3(_bossTransform.position.x, 0f, _bossTransform.position.z);
                Vector3 playerXZ = new Vector3(_playerTransform.position.x, 0f, _playerTransform.position.z);
                if (Vector3.Distance(bossXZ, playerXZ) <= _config.ContactRadius)
                {
                    hitPlayer = true;
                    break;
                }
            }

            // Stop at the arena wall rather than sliding along the edge.
            if (_arenaRadius > 0f)
            {
                Vector3 pos      = _bossTransform.position;
                float   clampedX = Mathf.Clamp(pos.x, _arenaCenter.x - _arenaRadius, _arenaCenter.x + _arenaRadius);
                float   clampedZ = Mathf.Clamp(pos.z, _arenaCenter.z - _arenaRadius, _arenaCenter.z + _arenaRadius);
                if (clampedX != pos.x || clampedZ != pos.z)
                {
                    _bossTransform.position = new Vector3(clampedX, pos.y, clampedZ);
                    break;
                }
            }

            yield return null;
        }

        // Yield one extra frame before evaluating damage.
        // The charge coroutine and InputAction.performed callbacks share the same
        // Update phase — if the player pressed dash on the exact contact frame,
        // IsInvincible may not be set yet when the coroutine reaches this point.
        // Waiting one frame guarantees all input for the contact frame has settled
        // before DamageHandler.TakeDamage() checks IsInvincible.
        yield return null;

        if (hitPlayer) DealContactDamage();
        FinishCharge();
    }

    private void FinishCharge()
    {
        _bossTransform.localScale = _originalScale;
        ClearTint();
        DestroyLineIndicator();
        _dustTrail?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (_animator != null) _animator.CrossFade(ChargeWindupStateName, 0.15f, 0);
        _activeCoroutine = null;
        IsExecuting      = false;
    }

    // ------------------------------------------------------------------
    // Ground line indicator
    // ------------------------------------------------------------------

    private void PlaceLineIndicator(Vector3 dir)
    {
        if (_chargeLineMaterial == null) return;

        if (_chargeLineIndicator == null)
        {
            _chargeLineIndicator = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _chargeLineIndicator.name = "ChargeLineIndicator";

            var col = _chargeLineIndicator.GetComponent<Collider>();
            if (col != null)
                Object.Destroy(col);

            _chargeLineIndicator.GetComponent<Renderer>().sharedMaterial = _chargeLineMaterial;
        }

        float length = _config.MaxRange;
        float yaw    = Quaternion.LookRotation(dir, Vector3.up).eulerAngles.y;

        _chargeLineIndicator.transform.position   =
            _bossTransform.position + dir * (length * 0.5f) + Vector3.up * 0.05f;
        _chargeLineIndicator.transform.rotation   = Quaternion.Euler(90f, yaw, 0f);
        _chargeLineIndicator.transform.localScale = new Vector3(LineWidth, length, 1f);
    }

    private void DestroyLineIndicator()
    {
        if (_chargeLineIndicator != null)
        {
            Object.Destroy(_chargeLineIndicator);
            _chargeLineIndicator = null;
        }
    }

    private void DealContactDamage()
    {
        if (_playerDamageHandler == null)
        {
            Debug.LogWarning(
                "[BossChargeSkill] PlayerDamageHandler is null — charge hit deals no damage. " +
                "Run CyberBoss/Setup Player Combat to add DamageHandler to the player.");
            return;
        }

        // Capture defense state BEFORE applying damage — these flags can change as a
        // side effect of TakeDamage() (e.g. barrier absorption clears HasBarrier).
        // A dodge (Dash i-frames) or a successful parry means zero damage was ever
        // applied to the player — DamageHandler.TakeDamage() returns early for both
        // and gives no signal back, so the caller must check the same public flags
        // DamageHandler itself reads. Barrier-only used to be checked here, which let
        // a clean dodge or parry play the full contact-hit shake/freeze/CA/FOV/VFX
        // block as if the hit had actually landed.
        bool hadActiveDefense = _playerSkills != null &&
            (_playerSkills.HasBarrier || _playerSkills.IsInvincible || _playerSkills.IsParryActive);
        _playerDamageHandler.TakeDamage(_config.Damage, "Boss Charge");

        // Barrier absorbed the hit, or the player dodged/parried it entirely: no
        // camera/freeze effects here — PlayerCombatEffects' shield break flash (for
        // barrier) or the dodge/parry's own feedback already covers it.
        if (hadActiveDefense) return;

        // Profile-driven signature — short high-frequency "jolt" shake
        // (this skill previously had zero shake at all), own Heavy-equivalent
        // freeze/CA/FOV instance, and an orange impact burst at the contact point.
        ScreenShakeManager.Instance?.Shake(_config.ImpactProfile);
        HitFreezeManager.Instance?.Freeze(_config.ImpactProfile);
        CameraImpactEffects.Instance?.ImpactCA(_config.ImpactProfile);
        CameraImpactEffects.Instance?.TriggerFOVPunch(_config.ImpactProfile);
        CombatVFXManager.Instance?.SpawnImpact(_bossTransform.position, "BossCharge", _config.ImpactProfile);
    }
}

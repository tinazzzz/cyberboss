using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Boss Teleport Strike skill.
///
/// Instantly moves the boss to a position behind the player (computed as
/// playerPosition - playerForward * TeleportBehindOffset). The boss Y stays at
/// its current value to remain on the arena floor regardless of the player's
/// facing angle. After a brief delay, Physics.OverlapSphereNonAlloc performs
/// the strike AoE.
///
/// Damage routing: each hit target is queried for DamageHandler explicitly (not
/// the generic IDamageable) so the player's full defense chain applies. If a
/// hit collider has no DamageHandler, it is silently skipped.
///
/// VFX: on teleport arrival the boss scale pulses up for ScalePulseDuration
/// seconds and then restores to its original value. This signals the sudden
/// appearance to the player. The boss also immediately rotates to face the
/// player after teleporting.
///
/// _hitBuffer is pre-allocated — no heap allocation in the strike phase.
/// _strikeDelayWait is allocated once in Init(), not per use.
///
/// BossSkills.ResetActiveSkill() calls StopExecution() on stagger, which
/// restores the boss scale if interrupted during the scale pulse or delay.
///
/// Counters the player's Dash — teleports behind the player before evasion
/// cooldowns can reset.
///
/// Dodge prediction: the destination is shifted laterally (along camera-right)
/// by PlayerBehaviorTracker.DodgeDirectionBias, scaled by
/// BossTeleportStrikeConfig.PredictiveLateralOffset. A player who dashes
/// consistently to one side gets intercepted on that side instead of the boss
/// landing directly behind their current position — see ComputeDodgePrediction().
/// </summary>
public class BossTeleportStrikeSkill : ISkill
{
    private readonly BossTeleportStrikeConfig _config;
    private SkillCooldown _cooldown;

    private MonoBehaviour          _runner;
    private Transform              _bossTransform;
    private Transform              _playerTransform;
    private PlayerSkills            _playerSkills;
    private PlayerBehaviorTracker  _behaviorTracker;
    private Camera                 _mainCamera;
    private Vector3                _originalScale;
    private Coroutine              _activeCoroutine;
    private Animator               _animator;

    // Pre-allocated overlap buffer — zero allocation in StrikeAoE().
    private readonly Collider[] _hitBuffer = new Collider[16];

    // Pre-allocated to avoid heap allocation per activation.
    private readonly HashSet<DamageHandler> _hitHandlers = new HashSet<DamageHandler>();

    // Pre-cached yields allocated in Init() — avoids heap allocation per activation.
    private WaitForSeconds _strikeDelayWait;
    private WaitForSeconds _preTeleportWindupWait;

    // Purple ground disc shown at boss position from teleport arrival until the
    // strike fires. Gives the player a visible reaction window. Distinct colour
    // (purple) differentiates from AoE slam's orange disc.
    private Material   _strikeIndicatorMaterial;
    private GameObject _activeIndicator;

    // Red ground disc shown at the teleport destination BEFORE the boss moves.
    // Warns the player "the boss will appear here — dodge now."
    private Material   _warnIndicatorMaterial;
    private GameObject _activeWarnIndicator;

    // Tracks the animated scale-pulse coroutine so StopExecution() can interrupt
    // it cleanly (e.g. on stagger) instead of leaving it running after the scale
    // has already been force-reset.
    private Coroutine _scalePulseCoroutine;

    // Arena clamp values — prevent teleport destination from landing outside walls.
    private Vector3 _arenaCenter;
    private float   _arenaRadius;

    // Wire "TeleportTrigger" in the boss Animator to a teleport/appear animation.
    private static readonly int TeleportAnimHash = Animator.StringToHash("TeleportTrigger");

    public bool  IsReady          => _cooldown.IsReady;
    public bool  IsExecuting      { get; private set; }
    public float CooldownProgress => _cooldown.NormalizedProgress;

    public BossTeleportStrikeSkill(BossTeleportStrikeConfig config)
    {
        _config   = config;
        _cooldown = new SkillCooldown(config.Cooldown);
    }

    /// <param name="runner">MonoBehaviour coroutine host — BossSkills.</param>
    /// <param name="bossTransform">Teleport destination is offset from this.</param>
    /// <param name="playerTransform">
    /// Used to compute the behind-player position and the strike AoE direction.
    /// </param>
    /// <param name="arenaCenter">World-space center used for destination clamping.</param>
    /// <param name="arenaRadius">Max distance from center the destination may land.</param>
    /// <param name="behaviorTracker">
    /// Source of the live DodgeDirectionBias used to bias the teleport destination
    /// toward the player's predictable dodge side. May be null (e.g. tracker not
    /// yet present) — prediction is skipped and the boss lands directly behind
    /// the player, matching pre-prediction behaviour.
    /// </param>
    public void Init(MonoBehaviour runner, Transform bossTransform, Transform playerTransform,
                     Animator animator, Vector3 arenaCenter, float arenaRadius,
                     PlayerBehaviorTracker behaviorTracker)
    {
        _runner                = runner;
        _bossTransform         = bossTransform;
        _playerTransform       = playerTransform;
        _behaviorTracker       = behaviorTracker;
        _mainCamera            = Camera.main;
        _originalScale         = bossTransform.localScale;
        _strikeDelayWait       = new WaitForSeconds(_config.StrikeDelay);
        _preTeleportWindupWait = new WaitForSeconds(_config.PreTeleportWindup);
        _animator              = animator;
        _arenaCenter           = arenaCenter;
        _arenaRadius           = arenaRadius;
        // One-time GetComponent in Init() — not in Update, so allocation is fine.
        // Same pattern as BossAoESlamSkill/BossChargeSkill.
        _playerSkills          = playerTransform != null ? playerTransform.GetComponent<PlayerSkills>() : null;

        // Pre-allocate both indicator materials once for the whole session.
        // All GPU blend properties must be set explicitly; _Surface alone does not
        // change the blend equations (same pattern as BossAoESlamSkill).
        var urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        var fallback = Shader.Find("Sprites/Default");
        var baseShader = urpUnlit != null ? urpUnlit : fallback;

        _strikeIndicatorMaterial = new Material(baseShader);
        ConfigureTransparentMaterial(_strikeIndicatorMaterial, new Color(0.7f, 0.1f, 1f, 0.5f));

        // Red warning disc — shown at the destination BEFORE the teleport fires.
        _warnIndicatorMaterial = new Material(baseShader);
        ConfigureTransparentMaterial(_warnIndicatorMaterial, new Color(1f, 0.15f, 0.1f, 0.55f));
    }

    /// Apply the transparent URP Unlit blend state common to all ground disc materials.
    private static void ConfigureTransparentMaterial(Material mat, Color color)
    {
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend",   0f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        mat.SetInt("_ZWrite", 0);
        mat.SetColor("_BaseColor", color);
    }

    public void Execute(GameObject user)
    {
        if (!_cooldown.IsReady || _runner == null || _playerTransform == null) return;

        _cooldown.Trigger();
        IsExecuting      = true;
        _activeCoroutine = _runner.StartCoroutine(TeleportStrikeRoutine());
    }

    public void UpdateCooldown(float deltaTime) => _cooldown.Tick(deltaTime);

    public void Reset(Animator animator)
    {
        StopExecution();
        _cooldown.Reset();
        animator?.ResetTrigger(TeleportAnimHash);
    }

    /// <summary>
    /// Stop the in-flight coroutine and restore the boss scale if interrupted
    /// during the windup warning, scale pulse, or strike delay phase.
    /// Destroys both the red warning disc and the purple strike disc.
    /// </summary>
    public void StopExecution()
    {
        if (_activeCoroutine != null && _runner != null)
        {
            _runner.StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }

        if (_scalePulseCoroutine != null && _runner != null)
        {
            _runner.StopCoroutine(_scalePulseCoroutine);
            _scalePulseCoroutine = null;
        }

        if (_bossTransform != null)
            _bossTransform.localScale = _originalScale;

        if (_animator != null) _animator.ResetTrigger(TeleportAnimHash);
        DestroyWarnIndicator();
        DestroyIndicator();
        IsExecuting = false;
    }

    // ------------------------------------------------------------------
    // Coroutine
    // ------------------------------------------------------------------

    private IEnumerator TeleportStrikeRoutine()
    {
        // Compute the teleport destination once and clamp it to the arena before
        // anything else — the warning disc must sit inside the playable area.
        // Keep the boss Y unchanged so the destination stays on the arena floor
        // regardless of the player's Y (terrain offset differences).
        // Use the boss→player direction so the boss always appears on the far side of
        // the player relative to its current position. Using playerTransform.forward
        // caused the boss to land in arbitrary directions depending on where the
        // player model was facing, which felt random and hard to read.
        Vector3 bossToPlayer = _playerTransform.position - _bossTransform.position;
        bossToPlayer.y = 0f;
        Vector3 approachDir = bossToPlayer.sqrMagnitude > 0.001f
            ? bossToPlayer.normalized
            : _bossTransform.forward;

        Vector3 behindPlayer = _playerTransform.position
            + approachDir * _config.TeleportBehindOffset
            + ComputeDodgePrediction();
        behindPlayer.y = _bossTransform.position.y;
        behindPlayer   = ClampPositionToArena(behindPlayer);

        // Pre-teleport warning: red disc at the destination tells the player
        // "the boss will appear HERE — dodge now" before the teleport fires.
        _activeWarnIndicator = CreateWarnIndicator(behindPlayer);
        yield return _preTeleportWindupWait;
        DestroyWarnIndicator();

        // Teleport: move boss to the pre-computed (already clamped) position.
        _bossTransform.position = behindPlayer;

        // Immediately face the player after appearing behind them.
        Vector3 lookDir = _playerTransform.position - _bossTransform.position;
        lookDir.y = 0f;
        if (lookDir.sqrMagnitude > 0.001f)
            _bossTransform.rotation = Quaternion.LookRotation(lookDir, Vector3.up);

        // Purple "data glitch" flash-burst + a light glitch CA tick fire on arrival —
        // no shake/freeze on arrival, those are reserved for the strike.
        CombatVFXManager.Instance?.SpawnImpact(_bossTransform.position, "BossTeleportArrival", _config.ArrivalProfile);
        CameraImpactEffects.Instance?.ImpactCAGlitch(_config.ArrivalProfile);

        // VFX: animated scale pulse (up over ScalePulseDuration, then mirrored back
        // down) + purple ground disc both fire on arrival. Runs as its own coroutine
        // so it doesn't block the strike-delay wait below — previously the scale was
        // snapped instantly and held for the entire StrikeDelay, leaving
        // ScalePulseDuration completely unread.
        _activeIndicator = CreateStrikeIndicator();
        if (_animator != null) _animator.SetTrigger(TeleportAnimHash);
        _scalePulseCoroutine = _runner.StartCoroutine(AnimateScalePulseRoutine());

        yield return _strikeDelayWait;

        DestroyIndicator();
        StrikeAoE();

        _activeCoroutine = null;
        IsExecuting      = false;
    }

    /// <summary>
    /// Animates localScale from _originalScale up to _originalScale *
    /// ScalePulseMagnitude over ScalePulseDuration seconds, then mirrors back down
    /// over the same duration — replaces the previous instant snap-and-hold.
    ///
    /// The per-phase duration is defensively clamped to at most half of StrikeDelay:
    /// the outer TeleportStrikeRoutine only waits for _strikeDelayWait before firing
    /// StrikeAoE() and clearing IsExecuting, so an un-clamped round-trip (2x
    /// ScalePulseDuration) could outlive that window if the two fields are ever
    /// retuned independently in the Inspector — leaving this coroutine free to stomp
    /// a subsequently-selected skill's own localScale write after this one "finishes".
    /// </summary>
    private IEnumerator AnimateScalePulseRoutine()
    {
        float duration = Mathf.Clamp(_config.ScalePulseDuration, 0.01f, _config.StrikeDelay * 0.5f);
        Vector3 peakScale = _originalScale * _config.ScalePulseMagnitude;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _bossTransform.localScale = Vector3.Lerp(_originalScale, peakScale, elapsed / duration);
            yield return null;
        }
        _bossTransform.localScale = peakScale;

        elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            _bossTransform.localScale = Vector3.Lerp(peakScale, _originalScale, elapsed / duration);
            yield return null;
        }
        _bossTransform.localScale = _originalScale;
        _scalePulseCoroutine = null;
    }

    // ------------------------------------------------------------------
    // Dodge prediction
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns a world-space lateral offset (along camera-right) that biases the
    /// teleport destination toward the player's predictable dodge side.
    ///
    /// DodgeDirectionBias is 0.5 at neutral (no offset) and approaches 0 or 1 as
    /// the player commits to one side (full offset). Uses the same camera-right
    /// convention as PlayerBehaviorTracker.OnDashExecuted so "right" here matches
    /// what was classified as a rightward dash.
    ///
    /// The raw bias is a ratio (rightCount / total), so it is unreliable with few
    /// samples — a single early dash reads as bias=0.0 or 1.0 despite proving
    /// nothing statistically. The result is scaled by a confidence factor that
    /// ramps linearly from 0 to 1 as LateralDashSampleCount approaches
    /// DodgeBiasConfidenceSamples, so early predictions stay subtle and only
    /// commit fully once enough dashes have actually been observed.
    /// </summary>
    private Vector3 ComputeDodgePrediction()
    {
        if (_behaviorTracker == null || _config.PredictiveLateralOffset <= 0f)
            return Vector3.zero;

        Vector3 cameraRight = _mainCamera != null
            ? Vector3.ProjectOnPlane(_mainCamera.transform.right, Vector3.up).normalized
            : Vector3.right;

        float dodgeBias  = _behaviorTracker.GetCurrentVector().DodgeDirectionBias;
        float signedBias = (dodgeBias - 0.5f) * 2f; // -1 (pure left) .. 0 (neutral) .. +1 (pure right)

        float confidence = 1f;
        int   confidenceSamples = _config.DodgeBiasConfidenceSamples;
        if (confidenceSamples > 0)
        {
            int sampleCount = _behaviorTracker.LateralDashSampleCount;
            confidence = Mathf.Clamp01((float)sampleCount / confidenceSamples);
        }

        return cameraRight * signedBias * confidence * _config.PredictiveLateralOffset;
    }

    // ------------------------------------------------------------------
    // Damage
    // ------------------------------------------------------------------

    private void StrikeAoE()
    {
        LayerMask mask = _config.TargetLayerMask;
        int hitCount;

        if (mask == 0)
        {
            Debug.LogWarning(
                "[BossTeleportStrikeSkill] TargetLayerMask is Nothing (0). " +
                "Set it to your Player layer in BossSkillConfig_TeleportStrike — " +
                "using Everything as fallback.");
            hitCount = Physics.OverlapSphereNonAlloc(
                _bossTransform.position, _config.StrikeRadius, _hitBuffer);
        }
        else
        {
            hitCount = Physics.OverlapSphereNonAlloc(
                _bossTransform.position, _config.StrikeRadius, _hitBuffer, mask);
        }

        // Clear before each activation — field is pre-allocated, no heap work here.
        _hitHandlers.Clear();

        // Capture defense state before the damage loop — these flags can change as a
        // side effect of TakeDamage() (barrier absorption clears HasBarrier). A dodge
        // (Dash i-frames) or a successful parry means zero damage was ever applied —
        // barrier-only used to be checked here, which let a clean dodge or parry play
        // the full strike shake/freeze/CAGlitch/VFX block as if the hit had landed.
        bool hadActiveDefense = _playerSkills != null &&
            (_playerSkills.HasBarrier || _playerSkills.IsInvincible || _playerSkills.IsParryActive);

        for (int i = 0; i < hitCount; i++)
        {
            if (_hitBuffer[i].transform.IsChildOf(_bossTransform)) continue;

            // Explicitly target DamageHandler so the player's defense chain applies.
            DamageHandler handler = _hitBuffer[i].GetComponent<DamageHandler>()
                ?? _hitBuffer[i].GetComponentInParent<DamageHandler>();

            // Deduplicate: same multiple-collider guard as BossAoESlamSkill.
            if (handler == null || _hitHandlers.Contains(handler)) continue;

            _hitHandlers.Add(handler);
            handler.TakeDamage(_config.StrikeDamage, "Boss Teleport Strike");
        }

        // Fires only when the strike actually connected AND no active defense
        // (barrier/dodge/parry) absorbed or blocked it — mirrors
        // BossAoESlamSkill.DamagePlayersInRange()'s gating exactly.
        if (_hitHandlers.Count > 0 && !hadActiveDefense)
        {
            ScreenShakeManager.Instance?.Shake(_config.StrikeProfile);
            HitFreezeManager.Instance?.Freeze(_config.StrikeProfile);
            CameraImpactEffects.Instance?.ImpactCAGlitch(_config.StrikeProfile);
            CombatVFXManager.Instance?.SpawnImpact(_bossTransform.position, "BossTeleportStrike", _config.StrikeProfile);
        }
    }

    // ------------------------------------------------------------------
    // VFX — ground disc helpers
    // ------------------------------------------------------------------

    private GameObject CreateStrikeIndicator()
    {
        return CreateDisc("StrikeIndicator", _bossTransform.position, _strikeIndicatorMaterial);
    }

    private GameObject CreateWarnIndicator(Vector3 position)
    {
        return CreateDisc("WarnIndicator", position, _warnIndicatorMaterial);
    }

    /// Shared flat-quad disc factory. Y offset 0.05 lifts it above the floor.
    private GameObject CreateDisc(string discName, Vector3 worldPosition, Material mat)
    {
        var disc = GameObject.CreatePrimitive(PrimitiveType.Quad);
        disc.name = discName;

        var col = disc.GetComponent<Collider>();
        if (col != null)
            Object.Destroy(col);

        disc.transform.position   = worldPosition + Vector3.up * 0.05f;
        disc.transform.rotation   = Quaternion.Euler(90f, 0f, 0f);
        disc.transform.localScale = Vector3.one * (_config.StrikeRadius * 2f);

        disc.GetComponent<Renderer>().sharedMaterial = mat;

        return disc;
    }

    private void DestroyIndicator()
    {
        if (_activeIndicator != null)
        {
            Object.Destroy(_activeIndicator);
            _activeIndicator = null;
        }
    }

    private void DestroyWarnIndicator()
    {
        if (_activeWarnIndicator != null)
        {
            Object.Destroy(_activeWarnIndicator);
            _activeWarnIndicator = null;
        }
    }

    // ------------------------------------------------------------------
    // Arena clamping
    // ------------------------------------------------------------------

    /// Returns <paramref name="position"/> clamped to the arena circle.
    /// Returns unchanged if <see cref="_arenaRadius"/> was not set (zero).
    private Vector3 ClampPositionToArena(Vector3 position)
    {
        if (_arenaRadius <= 0f) return position;

        Vector2 xz = new Vector2(position.x - _arenaCenter.x, position.z - _arenaCenter.z);
        if (xz.magnitude > _arenaRadius)
        {
            xz = xz.normalized * _arenaRadius;
            position = new Vector3(_arenaCenter.x + xz.x, position.y, _arenaCenter.z + xz.y);
        }
        return position;
    }

    /// <summary>
    /// Destroy both indicator material instances — called from BossSkills.OnDestroy()
    /// to prevent material leaks on scene reload. UnityEngine.Object instances are
    /// not reclaimed by the C# GC and must be explicitly destroyed.
    /// </summary>
    public void Cleanup()
    {
        if (_strikeIndicatorMaterial != null)
        {
            Object.Destroy(_strikeIndicatorMaterial);
            _strikeIndicatorMaterial = null;
        }

        if (_warnIndicatorMaterial != null)
        {
            Object.Destroy(_warnIndicatorMaterial);
            _warnIndicatorMaterial = null;
        }
    }
}

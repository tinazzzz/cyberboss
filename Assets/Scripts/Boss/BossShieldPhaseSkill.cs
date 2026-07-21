using System.Collections;
using UnityEngine;

/// <summary>
/// Boss Shield Phase skill.
///
/// Sets IsShielded = true for ShieldDuration seconds. BossController.TakeDamage()
/// reads BossSkills.IsShielded (which delegates here) and returns early, blocking
/// all incoming damage for the duration.
///
/// VFX: all Renderer components in the boss hierarchy are tinted cyan-blue using
/// MaterialPropertyBlock while the shield is active. On expiry or stagger
/// interruption the tint is cleared and IsShielded is restored to false.
///
/// MaterialPropertyBlock is allocated once in Init() and reused for all tinting
/// operations in the session (no per-frame or per-shield allocation).
///
/// Stagger interaction: shield blocks damage, so stagger cannot fire while the
/// shield is active (OnHealthDamageTaken is never reached). After the shield
/// expires normally, stagger is possible on the next hit as usual.
///
/// Not a deliberate turn-based counter-pick — see the #5 rework in CLAUDE.md.
/// Execute() is called reactively by BossShieldReactiveTrigger (on the player's
/// attack events, weighted by Ranged Blast frequency + AggressionScore), never
/// by BossController's scripted-random or RL skill-selection loop — this skill
/// is deliberately excluded from BossSkills.SelectableSkills.
/// </summary>
public class BossShieldPhaseSkill : ISkill
{
    private readonly BossShieldPhaseConfig _config;
    private SkillCooldown _cooldown;

    private MonoBehaviour         _runner;
    private Transform             _bossTransform;
    private Renderer[]            _bossRenderers;
    private MaterialPropertyBlock _mpb;
    private Animator              _animator;
    private Coroutine             _activeCoroutine;

    // Pre-cached yield allocated in Init(), not per shield activation.
    private WaitForSeconds _shieldWait;

    // URP base color property ID.
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // Cyan-blue tint applied while the shield is active.
    private static readonly Color ShieldColor = new Color(0.2f, 0.6f, 1f);

    // Wire "ShieldTrigger" in the boss Animator to a shield/barrier animation.
    private static readonly int ShieldAnimHash = Animator.StringToHash("ShieldTrigger");

    public bool  IsReady          => _cooldown.IsReady;
    public bool  IsExecuting      { get; private set; }
    public bool  IsShielded       { get; private set; }
    public float CooldownProgress => _cooldown.NormalizedProgress;

    public BossShieldPhaseSkill(BossShieldPhaseConfig config)
    {
        _config   = config;
        _cooldown = new SkillCooldown(config.Cooldown);
    }

    /// <param name="runner">MonoBehaviour coroutine host — BossSkills.</param>
    /// <param name="bossTransform">
    /// Used as the activation-shimmer VFX spawn position.
    /// </param>
    /// <param name="bossRenderers">
    /// All Renderers in the boss hierarchy — collected by BossSkills.Start()
    /// via GetComponentsInChildren. All are tinted while the shield is active.
    /// </param>
    public void Init(MonoBehaviour runner, Transform bossTransform, Renderer[] bossRenderers,
                     Animator animator)
    {
        _runner        = runner;
        _bossTransform = bossTransform;
        _bossRenderers = bossRenderers;
        _mpb           = new MaterialPropertyBlock();
        _shieldWait    = new WaitForSeconds(_config.ShieldDuration);
        _animator      = animator;
    }

    public void Execute(GameObject user)
    {
        if (!_cooldown.IsReady || _runner == null) return;

        _cooldown.Trigger();
        IsExecuting      = true;
        IsShielded       = true;
        if (_animator != null) _animator.SetTrigger(ShieldAnimHash);

        // Cyan-blue shimmer ring on activation + a very light CA tick —
        // marks a discrete moment instead of the tint just fading in silently.
        // No shake or freeze — this skill never damages the player.
        if (_bossTransform != null)
            CombatVFXManager.Instance?.SpawnImpact(_bossTransform.position, "BossShieldActivation", _config.ActivationProfile);
        CameraImpactEffects.Instance?.ImpactCA(_config.ActivationProfile);

        _activeCoroutine = _runner.StartCoroutine(ShieldRoutine());
    }

    public void UpdateCooldown(float deltaTime) => _cooldown.Tick(deltaTime);

    public void Reset(Animator animator)
    {
        StopExecution();
        _cooldown.Reset();
        animator?.ResetTrigger(ShieldAnimHash);
    }

    /// <summary>
    /// Stop the in-flight coroutine, clear IsShielded, and restore boss material tint.
    /// Called by BossSkills.ResetActiveSkill() — ensures the boss is never
    /// permanently shielded after a stagger interrupts the phase.
    /// </summary>
    public void StopExecution()
    {
        if (_activeCoroutine != null && _runner != null)
        {
            _runner.StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }

        if (_animator != null) _animator.ResetTrigger(ShieldAnimHash);
        IsShielded = false;
        ClearTint();
        IsExecuting = false;
    }

    // ------------------------------------------------------------------
    // Coroutine
    // ------------------------------------------------------------------

    private IEnumerator ShieldRoutine()
    {
        ApplyShieldTint();

        yield return _shieldWait;

        ClearTint();
        IsShielded       = false;
        _activeCoroutine = null;
        IsExecuting      = false;
    }

    // ------------------------------------------------------------------
    // VFX — MaterialPropertyBlock tinting (no material instances created)
    // ------------------------------------------------------------------

    private void ApplyShieldTint()
    {
        if (_bossRenderers == null) return;

        _mpb.SetColor(BaseColorId, ShieldColor);
        for (int i = 0; i < _bossRenderers.Length; i++)
        {
            if (_bossRenderers[i] != null)
                _bossRenderers[i].SetPropertyBlock(_mpb);
        }
    }

    private void ClearTint()
    {
        if (_bossRenderers == null) return;

        _mpb.Clear(); // Clears all overrides — restores material defaults.
        for (int i = 0; i < _bossRenderers.Length; i++)
        {
            if (_bossRenderers[i] != null)
                _bossRenderers[i].SetPropertyBlock(_mpb);
        }
    }
}

using UnityEngine;

/// <summary>
/// Tunable parameters for the Dash skill.
/// Create asset: CyberBoss > Skills > DashConfig
/// Boss counter: Teleport Strike.
/// </summary>
[CreateAssetMenu(fileName = "SkillConfig_Dash", menuName = "CyberBoss/Skills/DashConfig")]
public class DashSkillConfig : ScriptableObject
{
    [SerializeField] private float _cooldown               = 2.0f;
    [SerializeField] private float _dashDistance           = 5.0f;
    [SerializeField] private float _dashDuration           = 0.15f;
    [SerializeField] private float _invincibilityDuration  = 0.5f;

    [Header("Impact Feedback")]
    [Tooltip("Fired on cast. Dash never shakes and never FOV-punches (an i-frame " +
             "dodge should read as 'you're safe', not impactful, and FOV punches " +
             "felt dizzying on a skill spammable up to 10/min in playtest) — carries " +
             "only a very brief/shallow freeze, a light CA tick, and the TrailRenderer " +
             "streak/afterimage VFX (built in DashSkill.cs, not driven by this profile's " +
             "particle-burst fields).")]
    [SerializeField] private ImpactEffectProfile _impactProfile;

    [Tooltip("Fired from DamageHandler.OnPerfectDodge — a brighter re-trigger of the " +
             "same blue puff plus a light freeze+CA reward, tuned independently from " +
             "the cast-time profile above.")]
    [SerializeField] private ImpactEffectProfile _perfectDodgeProfile;

    public float Cooldown              => _cooldown;
    public float DashDistance          => _dashDistance;
    public float DashDuration          => _dashDuration;
    public float InvincibilityDuration => _invincibilityDuration;

    public ImpactEffectProfile ImpactProfile        => _impactProfile;
    public ImpactEffectProfile PerfectDodgeProfile  => _perfectDodgeProfile;
}

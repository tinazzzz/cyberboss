using UnityEngine;

/// <summary>
/// Tunable parameters for the Boss Shield Phase skill.
///
/// The boss becomes temporarily immune to all damage. This is NOT a deliberate
/// turn-based counter-pick — see the #5 rework in CLAUDE.md: keying activation on
/// raw Parry frequency produced a stalemate (a player parrying without ever
/// attacking could lock the boss into repeatedly picking a zero-damage skill
/// forever). It is now purely reactive (BossShieldReactiveTrigger), firing on the
/// player's attack events with probability driven by Ranged Blast frequency +
/// AggressionScore — see CLAUDE.md for the exact trigger design.
///
/// Create asset: CyberBoss > Boss > ShieldPhaseConfig
/// </summary>
[CreateAssetMenu(fileName = "BossSkillConfig_ShieldPhase",
    menuName = "CyberBoss/Boss/ShieldPhaseConfig")]
public class BossShieldPhaseConfig : ScriptableObject
{
    [Header("Cooldown")]
    [SerializeField] private float _cooldown = 12f;

    [Header("Shield")]
    [SerializeField] private float _shieldDuration = 3f;

    [Header("Impact Feedback")]
    [Tooltip("Fired from Execute() — a cyan-blue shimmer ring expanding outward marks " +
             "a discrete activation moment (previously the tint just faded in " +
             "silently). No shake or freeze — this skill never damages the player.")]
    [SerializeField] private ImpactEffectProfile _activationProfile;

    [Tooltip("Fired from BossController.OnShieldBlockedDamage — a white-cyan deflect " +
             "spark on every successful block. Previously this path was a bare " +
             "Debug.Log with zero player-facing feedback.")]
    [SerializeField] private ImpactEffectProfile _blockDeflectProfile;

    public float Cooldown       => _cooldown;
    public float ShieldDuration => _shieldDuration;

    public ImpactEffectProfile ActivationProfile   => _activationProfile;
    public ImpactEffectProfile BlockDeflectProfile => _blockDeflectProfile;
}

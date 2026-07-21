using System;
using UnityEngine;

/// <summary>
/// Player incoming-damage router.
///
/// Implements IDamageable as the player's public combat entry point. Boss attacks
/// (projectiles, hitboxes) should find this component via GetComponent/GetComponentInParent
/// rather than calling HealthSystem.TakeDamage directly. The defense chain is:
///
///   1. Invincibility (Dash)  — skip entirely, no damage taken
///   2. Parry window          — reflect full damage to boss via OnParryReflect event
///   3. Barrier active        — absorb; only overflow continues to HP
///   4. HealthSystem.TakeDamage — apply remaining damage
///
/// BossController subscribes to OnParryReflect in its Start() to apply reflected
/// damage back to itself.
///
/// Must be on the same GameObject as HealthSystem and PlayerSkills.
/// Run CyberBoss/Setup Player Combat to add both components automatically.
/// </summary>
[RequireComponent(typeof(HealthSystem))]
public class DamageHandler : MonoBehaviour, IDamageable
{
    private HealthSystem _healthSystem;
    private PlayerSkills _playerSkills;

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------

    /// <summary>
    /// Fired when an incoming hit lands during the parry window.
    /// Argument is the full incoming damage amount — apply this to the boss.
    /// BossController subscribes here in Start() to implement parry reflect.
    /// </summary>
    public event Action<float> OnParryReflect;

    /// <summary>
    /// Fired when a boss attack is fully absorbed by dash i-frames (perfect dodge).
    /// PlayerSkills subscribes here to award Burst Strike energy for the timing skill.
    /// </summary>
    public event Action OnPerfectDodge;

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        _healthSystem = GetComponent<HealthSystem>();
        _playerSkills = GetComponent<PlayerSkills>();

        if (_healthSystem == null)
            throw new InvalidOperationException(
                $"[DamageHandler] HealthSystem is missing on '{name}'. " +
                "Run CyberBoss/Setup Player Combat to add it.");

        // PlayerSkills is optional — DamageHandler functions without defense skills
        // present (all damage passes through to HealthSystem).
        if (_playerSkills == null)
            Debug.LogWarning($"[DamageHandler] PlayerSkills not found on '{name}'. " +
                "Invincibility, parry, and barrier defenses will not function.");
    }

    // ------------------------------------------------------------------
    // IDamageable
    // ------------------------------------------------------------------

    /// <summary>
    /// Route incoming damage through the player's full defense chain.
    /// Always call this — never call HealthSystem.TakeDamage directly on the player.
    /// </summary>
    /// <summary>
    /// IDamageable contract — used by generic callers (projectiles via IDamageable, etc.).
    /// No source name available; routes to the overload with an empty source.
    /// </summary>
    public void TakeDamage(float damage) => TakeDamage(damage, string.Empty);

    /// <summary>
    /// Extended entry point used by boss skills that hold a direct DamageHandler reference.
    /// <paramref name="source"/> is the skill name and appears in the combat log when damage lands.
    /// </summary>
    public void TakeDamage(float damage, string source)
    {
        if (damage <= 0f) return;

        string from = string.IsNullOrEmpty(source) ? "" : $" from {source}";

        // 1. Invincibility (Dash window) — all damage blocked.
        if (_playerSkills != null && _playerSkills.IsInvincible)
        {
            Debug.Log($"[Combat] {name} blocked {damage:F0}{from} (invincible / dash i-frame)");
            OnPerfectDodge?.Invoke();
            return;
        }

        // 2. Parry window — reflect full damage; player takes none.
        if (_playerSkills != null && _playerSkills.IsParryActive)
        {
            Debug.Log($"[Combat] {name} parried {damage:F0}{from} — reflected to boss");
            OnParryReflect?.Invoke(damage);
            return;
        }

        float remainingDamage = damage;

        // 3. Barrier — absorb as much as possible; overflow continues.
        if (_playerSkills != null && _playerSkills.HasBarrier)
        {
            remainingDamage = _playerSkills.AbsorbBarrierDamage(damage);
            float absorbed = damage - remainingDamage;
            if (absorbed > 0f)
                Debug.Log($"[Combat] {name} barrier absorbed {absorbed:F0}{from}" +
                    (remainingDamage > 0f ? $", {remainingDamage:F0} passed through" : " (fully absorbed)"));
        }

        // 4. Apply remaining damage to health — [HP] log fires inside HealthSystem.
        if (remainingDamage > 0f)
        {
            Debug.Log($"[Combat] {name} took {remainingDamage:F0}{from}");
            _healthSystem.TakeDamage(remainingDamage);
        }
    }
}

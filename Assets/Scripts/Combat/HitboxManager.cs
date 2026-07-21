using UnityEngine;

/// <summary>
/// Trigger-collider hitbox controller.
///
/// Attach to a child GameObject that owns a Trigger Collider (e.g., a sphere at the
/// boss's fist position). Boss skills call Activate() when the attack window opens and
/// let the auto-deactivate-on-hit handle cleanup.
///
/// Auto-deactivates after the first successful IDamageable hit per activation to
/// prevent multi-hit within a single swing. Call Activate() again for each new swing.
///
/// Usage:
///   hitbox.Activate(damage, bossGameObject);  // at start of attack window
///   hitbox.Deactivate();                      // at end of attack window (if no hit occurred)
///
/// Animation events on the boss animator drive Activate/Deactivate timing.
/// </summary>
[RequireComponent(typeof(Collider))]
public class HitboxManager : MonoBehaviour
{
    private float      _activeDamage;
    private GameObject _owner;
    private bool       _isActive;

    private void Awake()
    {
        GetComponent<Collider>().isTrigger = true;
        _isActive = false;
    }

    /// <summary>
    /// Arm this hitbox for one hit.
    /// Stays armed until Deactivate() is explicitly called or a hit lands.
    /// </summary>
    /// <param name="damage">Damage to apply on first IDamageable contact.</param>
    /// <param name="owner">
    /// The attacker's root GameObject. Children of this GameObject are skipped
    /// in the collision check so the attacker cannot hit itself.
    /// </param>
    public void Activate(float damage, GameObject owner)
    {
        _activeDamage = damage;
        _owner        = owner;
        _isActive     = true;
    }

    /// <summary>Disarm this hitbox. Call at the end of an attack animation window.</summary>
    public void Deactivate()
    {
        _isActive = false;
    }

    private void OnTriggerEnter(Collider other)
    {
        if (!_isActive) return;
        if (other.gameObject == _owner) return;
        if (_owner != null && other.transform.IsChildOf(_owner.transform)) return;

        // Route through DamageHandler first so the player's full defense chain applies
        // (dash i-frames, parry, barrier). Fall back to IDamageable for non-player targets.
        DamageHandler damageHandler = other.GetComponent<DamageHandler>()
            ?? other.GetComponentInParent<DamageHandler>();

        if (damageHandler != null)
        {
            damageHandler.TakeDamage(_activeDamage);
            Deactivate();
            return;
        }

        IDamageable target = other.GetComponent<IDamageable>()
            ?? other.GetComponentInParent<IDamageable>();

        if (target == null) return;

        target.TakeDamage(_activeDamage);
        Deactivate();
    }
}

using System;
using UnityEngine;

/// <summary>
/// Pure health tracker used by player and boss.
///
/// Call Initialize(float) to set max health at runtime (used by BossController on
/// spawn and by respawn logic). If never called, defaults to the serialized
/// _maxHealth value set in Awake.
///
/// HUD contract (subscribe in HUDController.Awake):
///   OnHealthChanged(current, max) — drive health bar fill
///   OnDamageTaken(amount)         — trigger hit flash / screen shake hook
///   OnDied                        — transition to death / win screen
///
/// Note: this class is a pure data tracker; defense logic lives in
/// DamageHandler (player) and BossController (boss). Do not add if-checks here
/// for invincibility, shield, or parry — those belong in the routing layer.
/// </summary>
public class HealthSystem : MonoBehaviour, IDamageable
{
    [SerializeField] private float _maxHealth = 100f;

    [Header("Damage Text")]
    [Tooltip("Colour of the floating damage number. Green for player, red for boss.")]
    [SerializeField] private Color _damageTextColor = Color.red;
    [Tooltip("Vertical offset above this transform where the number spawns.")]
    [SerializeField] private float _damageTextHeightOffset = 2f;

    private float _currentHealth;
    private bool  _isDead;

    /// <summary>Current health. Read by HUDController and PlayerBehaviorTracker.</summary>
    public float CurrentHealth => _currentHealth;

    /// <summary>Max health. Read by HUDController for fill-bar normalization.</summary>
    public float MaxHealth => _maxHealth;

    /// <summary>True after health reaches zero. Prevents double-death events.</summary>
    public bool IsDead => _isDead;

    // ------------------------------------------------------------------
    // Events
    // ------------------------------------------------------------------

    /// <summary>
    /// Fired each time health changes. Parameters: (currentHealth, maxHealth).
    /// HUDController subscribes to drive the health bar fill value.
    /// </summary>
    public event Action<float, float> OnHealthChanged;

    /// <summary>
    /// Fired when damage is applied. Parameter: damage amount dealt.
    /// Used by HUD for hit flash and ScreenShakeManager for camera shake.
    /// </summary>
    public event Action<float> OnDamageTaken;

    /// <summary>Fired exactly once when health reaches zero.</summary>
    public event Action OnDied;

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        _currentHealth = _maxHealth;
    }

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    /// <summary>
    /// Set max health and restore to full. Call before enabling the entity.
    /// Used by BossController on spawn and by respawn logic.
    /// </summary>
    public void Initialize(float maxHealth)
    {
        _maxHealth     = maxHealth;
        _currentHealth = maxHealth;
        _isDead        = false;
        OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
    }

    /// <summary>Apply damage. No-op when already dead or damage is non-positive.</summary>
    public void TakeDamage(float damage)
    {
        if (_isDead || damage <= 0f) return;

        _currentHealth = Mathf.Max(0f, _currentHealth - damage);
        Debug.Log($"[HP] {gameObject.name} -{damage:F0} → {_currentHealth:F0}/{_maxHealth:F0}");

        FloatingDamageText.Spawn(
            transform.position + Vector3.up * _damageTextHeightOffset,
            damage,
            _damageTextColor);

        OnDamageTaken?.Invoke(damage);
        OnHealthChanged?.Invoke(_currentHealth, _maxHealth);

        if (_currentHealth <= 0f)
        {
            _isDead = true;
            Debug.Log($"[HP] {gameObject.name} died");
            OnDied?.Invoke();
        }
    }

    /// <summary>Restore health, clamped to max. No-op when dead or amount is non-positive.</summary>
    public void Heal(float amount)
    {
        if (_isDead || amount <= 0f) return;

        _currentHealth = Mathf.Min(_maxHealth, _currentHealth + amount);
        OnHealthChanged?.Invoke(_currentHealth, _maxHealth);
    }
}

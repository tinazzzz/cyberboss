using UnityEngine;

/// <summary>
/// Energy-based ready-check for Burst Strike — replaces the time-based SkillCooldown.
///
/// Energy accumulates from player actions (normal attack, ranged blast, dash, perfect
/// dodge, parry, taking damage). When energy reaches the threshold the skill becomes
/// ready. Using the skill resets energy to zero.
///
/// Tick() is a deliberate no-op — energy only moves through AddEnergy() calls.
/// NormalizedProgress fills 0→1 as energy builds, matching the CooldownUI fill bar
/// direction used by all other skills.
/// </summary>
public class EnergyCooldown
{
    private float _energy;
    private readonly float _threshold;

    public bool  IsReady            => _energy >= _threshold;
    public float NormalizedProgress => _threshold > 0f ? Mathf.Clamp01(_energy / _threshold) : 1f;

    public EnergyCooldown(float threshold)
    {
        _threshold = threshold;
    }

    /// <summary>
    /// Add <paramref name="amount"/> energy, capped at the threshold.
    /// Surplus is discarded — energy does not overflow beyond IsReady.
    /// </summary>
    public void AddEnergy(float amount)
    {
        _energy = Mathf.Min(_energy + amount, _threshold);
    }

    /// <summary>Consume the skill — resets energy to zero.</summary>
    public void Trigger() => _energy = 0f;

    /// <summary>Full state reset — called on death/respawn.</summary>
    public void Reset() => _energy = 0f;

    /// <summary>No-op. Energy is event-driven; time does not restore it.</summary>
    public void Tick(float deltaTime) { }
}

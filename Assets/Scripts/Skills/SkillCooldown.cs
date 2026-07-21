using UnityEngine;

/// <summary>
/// Reusable cooldown timer used by every player and boss skill.
/// Plain struct — zero heap allocation; store by value inside each skill class.
///
/// Typical usage:
///   _cooldown = new SkillCooldown(config.Cooldown);   // once, in skill constructor
///   if (_cooldown.IsReady) { _cooldown.Trigger(); }   // on Execute
///   _cooldown.Tick(deltaTime);                        // in UpdateCooldown every frame
/// </summary>
public struct SkillCooldown
{
    private float _maxDuration;
    private float _remaining;

    /// <summary>True when the skill may be executed again.</summary>
    public bool IsReady => _remaining <= 0f;

    /// <summary>
    /// Progress from 0 (just triggered) to 1 (fully recharged).
    /// Read by CooldownUI to drive fill-bar display.
    /// </summary>
    public float NormalizedProgress => _maxDuration > 0f
        ? 1f - (_remaining / _maxDuration)
        : 1f;

    public SkillCooldown(float duration)
    {
        _maxDuration = duration;
        _remaining   = 0f;
    }

    /// <summary>Start the cooldown. Call once per successful Execute.</summary>
    public void Trigger() => _remaining = _maxDuration;

    /// <summary>Advance the cooldown timer. Call every frame from UpdateCooldown.</summary>
    public void Tick(float deltaTime) =>
        _remaining = Mathf.Max(0f, _remaining - deltaTime);

    /// <summary>Clear remaining time so the skill is immediately ready. Call on respawn.</summary>
    public void Reset() => _remaining = 0f;
}

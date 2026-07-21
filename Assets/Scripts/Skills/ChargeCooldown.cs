using UnityEngine;

/// <summary>
/// Sequential charge recharge tracker — used by Ranged Blast (3 charges, 2 s each).
///
/// Charges refill one at a time. The recharge timer for the NEXT charge starts the
/// moment a shot is fired and the stack drops below max — firing a second shot while
/// the first is recharging does NOT reset the timer. This means firing rapidly empties
/// the stack but recharge begins immediately after the first shot.
///
/// NormalizedProgress gives a smooth continuous 0→1 fill value suitable for a fill bar:
///   (chargesRemaining + rechargeProgress) / maxCharges
/// so the bar occupies (chargesRemaining / maxCharges) at the start of each recharge
/// and smoothly grows toward (chargesRemaining + 1) / maxCharges over rechargeDuration.
/// The value is continuous — no jump when a charge is granted.
///
/// IsReady is true whenever at least one charge remains — firing is never blocked by
/// a recharge timer, only by having zero charges.
/// </summary>
public class ChargeCooldown
{
    private int   _charges;
    private float _rechargeTimer;

    private readonly int   _maxCharges;
    private readonly float _rechargeDuration;

    public int  ChargesRemaining => _charges;
    public int  MaxCharges       => _maxCharges;
    public bool IsReady          => _charges > 0;

    /// <summary>
    /// Smooth 0–1 fill: fraction of max charges filled, with the current recharge
    /// sub-progress interpolated in. Equals 1 when all charges are full.
    /// </summary>
    public float NormalizedProgress
    {
        get
        {
            if (_maxCharges <= 0) return 1f;
            float sub = _charges >= _maxCharges ? 0f
                : _rechargeDuration > 0f ? 1f - (_rechargeTimer / _rechargeDuration) : 0f;
            return Mathf.Clamp01(((float)_charges + sub) / _maxCharges);
        }
    }

    /// <param name="maxCharges">Maximum charges that can be stored.</param>
    /// <param name="rechargeDuration">Seconds to restore one charge.</param>
    public ChargeCooldown(int maxCharges, float rechargeDuration)
    {
        _maxCharges       = maxCharges;
        _rechargeDuration = rechargeDuration;
        _charges          = maxCharges; // start fight fully loaded
    }

    /// <summary>Consume one charge. Starts the recharge timer if not already running.</summary>
    public void Trigger()
    {
        if (_charges <= 0) return;
        _charges--;
        // Start timer only if not already counting — a mid-recharge fire does not reset it.
        if (_charges < _maxCharges && _rechargeTimer <= 0f)
            _rechargeTimer = _rechargeDuration;
    }

    /// <summary>Advance the recharge timer. Call every frame from UpdateCooldown().</summary>
    public void Tick(float deltaTime)
    {
        if (_charges >= _maxCharges) return;

        _rechargeTimer -= deltaTime;
        if (_rechargeTimer <= 0f)
        {
            _charges++;
            _rechargeTimer = _charges < _maxCharges ? _rechargeDuration : 0f;
        }
    }

    /// <summary>Restore all charges immediately. Call on death/respawn.</summary>
    public void Reset()
    {
        _charges       = _maxCharges;
        _rechargeTimer = 0f;
    }
}

using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Pool;

/// <summary>
/// Ranged Blast skill — charge-based ammo system (3 shots, 2 s recharge each).
///
/// The player may fire consecutively as long as at least one charge remains.
/// Charges refill sequentially: the timer for the next charge starts the moment
/// a shot is consumed, not when the last recharge completes.
///
/// Implements IChargeable so CooldownUI can show the live charge count as a number.
///
/// Cursor world position: resolved via Physics.Raycast on the floor layer.
/// Boss counter: Charge — closes distance.
/// </summary>
public class RangedBlastSkill : ISkill, IChargeable
{
    private readonly RangedBlastSkillConfig _config;
    private readonly GameObject             _projectilePrefab;
    private ChargeCooldown _cooldown;

    private Transform     _playerTransform;
    private Camera        _camera;
    private System.Action _onHitCallback;

    private ObjectPool<ProjectileBehavior> _projectilePool;

    private const float SpawnHeightOffset  = 1.0f;
    private const float SpawnForwardOffset = 0.6f;

    private static readonly int BlastTriggerHash = Animator.StringToHash("BlastTrigger");

    // ------------------------------------------------------------------
    // ISkill
    // ------------------------------------------------------------------

    public bool  IsReady          => _cooldown.IsReady;
    public float CooldownProgress => _cooldown.NormalizedProgress;

    // ------------------------------------------------------------------
    // IChargeable — read by CooldownUI to display the charge count label
    // ------------------------------------------------------------------

    public int ChargesRemaining => _cooldown.ChargesRemaining;
    public int MaxCharges       => _cooldown.MaxCharges;

    public RangedBlastSkill(RangedBlastSkillConfig config, GameObject projectilePrefab)
    {
        _config           = config;
        _projectilePrefab = projectilePrefab;
        _cooldown         = new ChargeCooldown(config.MaxCharges, config.RechargeDuration);
    }

    /// <summary>
    /// Registers a callback to invoke when a fired projectile lands a hit on an IDamageable
    /// target (i.e. the boss). Call once from PlayerSkills.Awake() to wire Burst Strike
    /// energy gain to confirmed hits rather than to the moment of firing.
    /// </summary>
    public void SetOnHitCallback(System.Action callback) => _onHitCallback = callback;

    /// <param name="playerTransform">Used for spawn position and fallback fire direction.</param>
    /// <param name="camera">Used to unproject the cursor to a world position.</param>
    public void Init(Transform playerTransform, Camera camera)
    {
        _playerTransform = playerTransform;
        _camera          = camera;

        if (_config.FloorLayerMask == 0)
            Debug.LogWarning("[RangedBlastSkill] FloorLayerMask is Nothing (0). " +
                "Set it to your floor/ground layer in SkillConfig_RangedBlast — " +
                "projectiles will always fire in the facing direction until this is fixed.");

        _projectilePool = new ObjectPool<ProjectileBehavior>(
            createFunc:      CreateProjectile,
            actionOnGet:     p => p.gameObject.SetActive(true),
            actionOnRelease: p => p.gameObject.SetActive(false),
            actionOnDestroy: p => Object.Destroy(p.gameObject),
            collectionCheck: true,
            defaultCapacity: 4,
            maxSize:         16
        );
    }

    public void Execute(GameObject user)
    {
        if (!_cooldown.IsReady) return;

        Vector3 fireDirection = ComputeFireDirection();
        _cooldown.Trigger();

        Vector3 spawnPosition = _playerTransform.position
            + Vector3.up             * SpawnHeightOffset
            + _playerTransform.forward * SpawnForwardOffset;

        ProjectileBehavior projectile = _projectilePool.Get();
        projectile.transform.SetPositionAndRotation(
            spawnPosition,
            Quaternion.LookRotation(fireDirection));

        projectile.Launch(
            fireDirection,
            _config.ProjectileSpeed,
            _config.ProjectileRange,
            _config.ProjectileDamage,
            _projectilePool,
            user,
            _onHitCallback,
            // Profile is tuned for the hit-confirm moment, not launch —
            // ProjectileBehavior fires Shake()/SpawnImpact() itself on collision.
            // PlayerSkills.OnSkillExecuted (which fires at launch) must never also
            // trigger these effects, or every shot double-fires.
            _config.ImpactProfile,
            "RangedBlast");
    }

    public void UpdateCooldown(float deltaTime) => _cooldown.Tick(deltaTime);

    public void Reset(Animator animator)
    {
        _cooldown.Reset();
        animator.ResetTrigger(BlastTriggerHash);
    }

    private Vector3 ComputeFireDirection()
    {
        Vector2 screenPos = Mouse.current != null
            ? Mouse.current.position.ReadValue()
            : new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

        Ray ray = _camera.ScreenPointToRay(screenPos);
        if (Physics.Raycast(ray, out RaycastHit hit, 100f, _config.FloorLayerMask))
        {
            Vector3 toTarget = hit.point - _playerTransform.position;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.01f)
                return toTarget.normalized;
        }

        return _playerTransform.forward;
    }

    private ProjectileBehavior CreateProjectile()
    {
        GameObject go = Object.Instantiate(_projectilePrefab);
        var pb = go.GetComponent<ProjectileBehavior>();

        if (pb == null)
            throw new System.InvalidOperationException(
                "[RangedBlastSkill] Projectile prefab is missing a ProjectileBehavior component.");

        return pb;
    }
}

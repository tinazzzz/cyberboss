using System.Collections;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Boss Projectile Burst skill.
///
/// After a brief windup, fires N projectiles in an angular spread aimed toward the
/// player. Reuses the ProjectileBehavior / ObjectPool pattern used by the player's
/// Ranged Blast skill.
///
/// Projectile prefab: assign a red/orange capsule prefab with ProjectileBehavior
/// and a Trigger Collider to BossSkills._bossProjectilePrefab. Run
/// CyberBoss/Setup Boss Skills to create this prefab automatically.
///
/// Damage routing: ProjectileBehavior.OnTriggerEnter finds the first IDamageable
/// on the player hierarchy, which is DamageHandler (not HealthSystem). The full
/// defense chain applies automatically.
///
/// ObjectPool is created once in Init() — no per-fire allocation in Execute().
/// The pool is stored on this skill instance; it is NOT shared with the player pool.
///
/// Counters the player's Barrier — punishes passive, stationary play.
/// </summary>
public class BossProjectileBurstSkill : ISkill
{
    private readonly BossProjectileBurstConfig _config;
    private readonly GameObject                _projectilePrefab;
    private SkillCooldown _cooldown;

    private MonoBehaviour _runner;
    private Transform     _bossTransform;
    private Transform     _playerTransform;
    private Coroutine     _activeCoroutine;
    private Animator      _animator;

    private ObjectPool<ProjectileBehavior> _projectilePool;

    // Ground line indicators — one quad per projectile direction, shown during windup.
    private Material     _lineMaterial;
    private GameObject[] _lineIndicators;
    private const float  LineWidth  = 0.35f;
    private const float  LineLength = 10f;

    // Spawn offset matches the player's RangedBlastSkill so both feel consistent.
    private const float SpawnHeightOffset  = 1.0f;
    private const float SpawnForwardOffset = 0.6f;

    // Each shot's muzzle-flash VFX is offset along that projectile's own
    // fired direction so the flashes actually fan out visually instead of stacking
    // 100% on top of each other at the shared spawnPos (all projectiles launch from
    // the same point, differing only in rotation) — this is what makes "one flash
    // per projectile" a real, readable signal rather than a wasted repeat spawn.
    private const float MuzzleFlashSpreadOffset = 0.5f;

    // This pool is sized to actually hold every shot in a burst concurrently
    // (see Init()) — 3-5 simultaneous instances is this skill's normal, designed-for
    // case, not overflow, so it must not fall back to CombatVFXManager's generic
    // 2/4 sizing meant for skills with 1-2 concurrent instances.
    private const int MuzzleFlashPoolMaxSizeBuffer = 2;

    // Wire "BurstTrigger" in the boss Animator to a ranged attack animation.
    private static readonly int BurstAnimHash = Animator.StringToHash("BurstTrigger");

    public bool  IsReady          => _cooldown.IsReady;
    public bool  IsExecuting      { get; private set; }
    public float CooldownProgress => _cooldown.NormalizedProgress;

    /// <param name="projectilePrefab">
    /// Must have ProjectileBehavior and a Trigger Collider.
    /// Created by CyberBoss/Setup Boss Skills as a red/orange capsule.
    /// </param>
    public BossProjectileBurstSkill(BossProjectileBurstConfig config, GameObject projectilePrefab)
    {
        _config           = config;
        _projectilePrefab = projectilePrefab;
        _cooldown         = new SkillCooldown(config.Cooldown);
    }

    /// <param name="runner">MonoBehaviour coroutine host — BossSkills.</param>
    /// <param name="bossTransform">Spawn position source.</param>
    /// <param name="playerTransform">Target direction for the projectile spread.</param>
    public void Init(MonoBehaviour runner, Transform bossTransform, Transform playerTransform,
                     Animator animator)
    {
        _runner          = runner;
        _bossTransform   = bossTransform;
        _playerTransform = playerTransform;
        _animator        = animator;

        // Pre-allocate indicator array sized to the max projectile count.
        _lineIndicators = new GameObject[_config.ProjectileCount];

        // Transparent red material for the aim lines — distinct from charge's orange.
        var urpUnlit = Shader.Find("Universal Render Pipeline/Unlit");
        _lineMaterial = new Material(urpUnlit != null ? urpUnlit : Shader.Find("Sprites/Default"));
        _lineMaterial.SetFloat("_Surface", 1f);
        _lineMaterial.SetFloat("_Blend",   0f);
        _lineMaterial.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        _lineMaterial.SetOverrideTag("RenderType", "Transparent");
        _lineMaterial.renderQueue =
            (int)UnityEngine.Rendering.RenderQueue.Transparent;
        _lineMaterial.SetInt("_SrcBlend",
            (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        _lineMaterial.SetInt("_DstBlend",
            (int)UnityEngine.Rendering.BlendMode.OneMinusSrcAlpha);
        _lineMaterial.SetInt("_ZWrite", 0);
        _lineMaterial.SetColor("_BaseColor", new Color(1f, 0.1f, 0.05f, 0.5f));

        if (_projectilePrefab == null)
        {
            Debug.LogError(
                "[BossProjectileBurstSkill] Projectile prefab is null — skill will not fire. " +
                "Run CyberBoss/Setup Boss Skills to create and assign the boss projectile prefab.");
            return;
        }

        _projectilePool = new ObjectPool<ProjectileBehavior>(
            createFunc:      CreateProjectile,
            actionOnGet:     p => p.gameObject.SetActive(true),
            actionOnRelease: p => p.gameObject.SetActive(false),
            actionOnDestroy: p => Object.Destroy(p.gameObject),
            collectionCheck: true,
            defaultCapacity: 8,
            maxSize:         20
        );

        // Pre-size the "BossProjectileBurst" muzzle-flash VFX pool to this
        // config's actual ProjectileCount instead of CombatVFXManager's generic 2/4
        // default — every shot in a single burst fires its own flash concurrently
        // (see FireSpread()), so that's the normal case, not overflow. Safe to call
        // here: CombatVFXManager.Instance is set in Awake(), which Unity guarantees
        // completes (for every active component) before any Start() runs, and this
        // Init() is called from BossSkills.Start().
        CombatVFXManager.Instance?.EnsureSkillPoolCapacity(
            "BossProjectileBurst", _config.ImpactProfile,
            capacity: _config.ProjectileCount,
            maxSize:  _config.ProjectileCount + MuzzleFlashPoolMaxSizeBuffer);
    }

    public void Execute(GameObject user)
    {
        if (!_cooldown.IsReady || _runner == null || _playerTransform == null) return;
        if (_projectilePool == null) return;

        _cooldown.Trigger();
        IsExecuting      = true;
        if (_animator != null) _animator.SetTrigger(BurstAnimHash);
        _activeCoroutine = _runner.StartCoroutine(BurstRoutine(user));
    }

    public void UpdateCooldown(float deltaTime) => _cooldown.Tick(deltaTime);

    public void Reset(Animator animator)
    {
        StopExecution();
        _cooldown.Reset();
        animator?.ResetTrigger(BurstAnimHash);
    }

    /// <summary>
    /// Stop the in-flight coroutine. Active projectiles already in-flight are
    /// managed by ProjectileBehavior and will expire normally via max range.
    /// </summary>
    public void StopExecution()
    {
        if (_activeCoroutine != null && _runner != null)
        {
            _runner.StopCoroutine(_activeCoroutine);
            _activeCoroutine = null;
        }

        if (_animator != null) _animator.ResetTrigger(BurstAnimHash);
        DestroyLineIndicators();
        IsExecuting = false;
    }

    // ------------------------------------------------------------------
    // Coroutine
    // ------------------------------------------------------------------

    private IEnumerator BurstRoutine(GameObject user)
    {
        // Per-frame windup: boss faces the player and aim lines track their position.
        float windupTimer = 0f;
        while (windupTimer < _config.WindupDuration)
        {
            windupTimer += Time.deltaTime;

            if (_playerTransform != null)
            {
                Vector3 aimDir = _playerTransform.position - _bossTransform.position;
                aimDir.y = 0f;
                if (aimDir.sqrMagnitude > 0.001f)
                {
                    _bossTransform.rotation = Quaternion.LookRotation(aimDir, Vector3.up);
                    UpdateLineIndicators(aimDir.normalized);
                }
            }

            yield return null;
        }

        DestroyLineIndicators();

        if (_playerTransform == null)
        {
            _activeCoroutine = null;
            IsExecuting      = false;
            yield break;
        }

        Vector3 toPlayer = _playerTransform.position - _bossTransform.position;
        toPlayer.y = 0f;
        Vector3 fireDir = toPlayer.sqrMagnitude > 0.001f
            ? toPlayer.normalized
            : _bossTransform.forward;

        Vector3 spawnPos = _bossTransform.position
            + Vector3.up * SpawnHeightOffset
            + fireDir    * SpawnForwardOffset;

        FireSpread(spawnPos, fireDir, user);

        _activeCoroutine = null;
        IsExecuting      = false;
    }

    // ------------------------------------------------------------------
    // Ground aim-line indicators
    // ------------------------------------------------------------------

    private void UpdateLineIndicators(Vector3 centerDir)
    {
        int   count      = _config.ProjectileCount;
        float totalAngle = _config.SpreadAngleDegrees;
        float stepAngle  = count > 1 ? totalAngle / (count - 1) : 0f;
        float startAngle = count > 1 ? -totalAngle * 0.5f : 0f;

        for (int i = 0; i < count; i++)
        {
            float   angle = startAngle + stepAngle * i;
            Vector3 dir   = Quaternion.AngleAxis(angle, Vector3.up) * centerDir;

            if (_lineIndicators[i] == null)
            {
                _lineIndicators[i] = GameObject.CreatePrimitive(PrimitiveType.Quad);
                _lineIndicators[i].name = $"BurstAimLine_{i}";
                var col = _lineIndicators[i].GetComponent<Collider>();
                if (col != null) Object.Destroy(col);
                _lineIndicators[i].GetComponent<Renderer>().sharedMaterial = _lineMaterial;
            }

            float yaw = Quaternion.LookRotation(dir, Vector3.up).eulerAngles.y;
            _lineIndicators[i].transform.position   =
                _bossTransform.position + dir * (LineLength * 0.5f) + Vector3.up * 0.05f;
            _lineIndicators[i].transform.rotation   = Quaternion.Euler(90f, yaw, 0f);
            _lineIndicators[i].transform.localScale = new Vector3(LineWidth, LineLength, 1f);
        }
    }

    private void DestroyLineIndicators()
    {
        if (_lineIndicators == null) return;
        for (int i = 0; i < _lineIndicators.Length; i++)
        {
            if (_lineIndicators[i] != null)
            {
                Object.Destroy(_lineIndicators[i]);
                _lineIndicators[i] = null;
            }
        }
    }

    /// <summary>
    /// Destroy the pre-allocated aim-line material. Call from BossSkills.OnDestroy().
    /// </summary>
    public void Cleanup()
    {
        DestroyLineIndicators();
        if (_lineMaterial != null)
        {
            Object.Destroy(_lineMaterial);
            _lineMaterial = null;
        }
    }

    private void FireSpread(Vector3 spawnPos, Vector3 fireDir, GameObject owner)
    {
        // Shake + CA fire ONCE for the whole volley (not per-projectile) —
        // a light, quick "staccato" pulse. Freeze is explicitly disabled on this
        // skill's profile so multi-shot volleys never feel choppy.
        ScreenShakeManager.Instance?.Shake(_config.ImpactProfile);
        CameraImpactEffects.Instance?.ImpactCA(_config.ImpactProfile);

        int   count      = _config.ProjectileCount;
        float totalAngle = _config.SpreadAngleDegrees;
        float stepAngle  = count > 1 ? totalAngle / (count - 1) : 0f;
        float startAngle = count > 1 ? -totalAngle * 0.5f : 0f;

        // Shared state for the one-hit-per-burst rule.
        // When any projectile connects, the callback cancels its siblings so the
        // burst deals its damage exactly once regardless of how many hit.
        var  active     = new ProjectileBehavior[count];
        bool hitOccurred = false;

        for (int i = 0; i < count; i++)
        {
            float   angle = startAngle + stepAngle * i;
            Vector3 dir   = Quaternion.AngleAxis(angle, Vector3.up) * fireDir;

            var projectile = _projectilePool.Get();
            active[i] = projectile;

            projectile.transform.SetPositionAndRotation(
                spawnPos, Quaternion.LookRotation(dir));

            // Muzzle-flash burst per shot fired — offset along this shot's own
            // direction so the flashes fan out instead of all stacking at the
            // identical spawnPos (every projectile launches from the same point,
            // differing only in rotation).
            Vector3 flashPos = spawnPos + dir * MuzzleFlashSpreadOffset;
            CombatVFXManager.Instance?.SpawnImpact(flashPos, "BossProjectileBurst", _config.ImpactProfile);

            var captured = projectile; // per-iteration capture for closure
            projectile.Launch(
                dir,
                _config.ProjectileSpeed,
                _config.ProjectileRange,
                _config.ProjectileDamage,
                _projectilePool,
                owner,
                onHit: () =>
                {
                    if (hitOccurred) return;
                    hitOccurred = true;
                    // Return all siblings that haven't already expired.
                    // Skip `captured` — it returns itself normally after this callback.
                    for (int j = 0; j < active.Length; j++)
                    {
                        var sibling = active[j];
                        if (sibling != null && sibling != captured
                            && sibling.gameObject.activeSelf)
                            _projectilePool.Release(sibling);
                    }
                });
        }
    }

    private ProjectileBehavior CreateProjectile()
    {
        var go = Object.Instantiate(_projectilePrefab);
        var pb = go.GetComponent<ProjectileBehavior>();

        if (pb == null)
            throw new System.InvalidOperationException(
                "[BossProjectileBurstSkill] Boss projectile prefab is missing a " +
                "ProjectileBehavior component. Run CyberBoss/Setup Boss Skills to " +
                "recreate the prefab correctly.");

        return pb;
    }
}

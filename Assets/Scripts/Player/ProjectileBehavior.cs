using UnityEngine;
using UnityEngine.Pool;

[RequireComponent(typeof(Collider))]
public class ProjectileBehavior : MonoBehaviour
{
    private float         _speed;
    private float         _maxRange;
    private float         _damage;
    private float         _travelledDistance;
    private bool          _returned;
    private Transform     _ownerTransform;
    private System.Action _onHit;
    private Renderer      _renderer;

    // Optional hit-confirm impact feedback (shake + VFX), set per-Launch.
    // Only Ranged Blast's own projectile passes these — boss projectiles hit the
    // player via DamageHandler (CheckDamageHandlerHits(), below) and don't take
    // this generic-IDamageable branch, so they simply leave these null.
    private ImpactEffectProfile _impactProfile;
    private string              _impactEffectId;

    private IObjectPool<ProjectileBehavior> _pool;

    // Pre-allocated — no heap allocation in the hot Update path.
    private static readonly Collider[] HitBuffer = new Collider[8];

    // Sphere radius used for the Update-based player hit check.
    // Slightly larger than the projectile's visual radius for forgiving feel.
    private const float HitRadius = 0.4f;

    // Shared MPB — lazily created on first Launch call (static field initializers
    // cannot call Unity API; MaterialPropertyBlock must be constructed at runtime).
    private static MaterialPropertyBlock _emissiveMpb;
    private static MaterialPropertyBlock EmissiveMpb
        => _emissiveMpb ??= new MaterialPropertyBlock();
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    private void Awake()
    {
        _renderer = GetComponent<Renderer>();
    }

    private void OnDisable()
    {
        _travelledDistance = 0f;
        _ownerTransform    = null;
        _onHit             = null;
        _impactProfile     = null;
        _impactEffectId    = null;
    }

    /// <param name="direction">Normalised world-space fire direction.</param>
    /// <param name="speed">Units per second.</param>
    /// <param name="maxRange">Lifetime distance in units.</param>
    /// <param name="damage">Damage applied on hit.</param>
    /// <param name="pool">Pool to return to on expiry or collision.</param>
    /// <param name="owner">The shooter — their colliders are ignored on impact.</param>
    /// <param name="onHit">Optional callback invoked when the projectile hits an IDamageable
    /// (non-DamageHandler) target. Used by RangedBlastSkill to credit Burst Strike energy
    /// only when the projectile actually contacts the boss.</param>
    /// <param name="impactProfile">Optional hit-confirm shake + VFX profile.
    /// Null for boss projectiles (they hit the player via DamageHandler, not this
    /// generic-IDamageable branch, so no profile is needed).</param>
    /// <param name="impactEffectId">Skill id string for CombatVFXManager's per-skill pool.</param>
    public void Launch(
        Vector3 direction,
        float speed,
        float maxRange,
        float damage,
        IObjectPool<ProjectileBehavior> pool,
        GameObject owner,
        System.Action onHit = null,
        ImpactEffectProfile impactProfile = null,
        string impactEffectId = null)
    {
        _speed             = speed;
        _maxRange          = maxRange;
        _damage            = damage;
        _travelledDistance = 0f;
        _returned          = false;
        _pool              = pool;
        _ownerTransform    = owner != null ? owner.transform : null;
        _onHit             = onHit;
        _impactProfile     = impactProfile;
        _impactEffectId    = impactEffectId;
        transform.forward  = direction;

        // Bright emissive bullet — HDR cyan triggers URP bloom.
        // Uses _BaseColor (not _EmissionColor) so it works on both Lit and Unlit
        // URP shaders. Value > 1 is HDR; tonemapping + bloom make it glow visibly.
        if (_renderer != null)
        {
            EmissiveMpb.SetColor(BaseColorId, new Color(0f, 2.5f, 2.5f, 1f));
            _renderer.SetPropertyBlock(EmissiveMpb);
        }
    }

    private void Update()
    {
        float step = _speed * Time.deltaTime;
        transform.position += transform.forward * step;
        _travelledDistance += step;

        if (_travelledDistance >= _maxRange)
        {
            ReturnToPool();
            return;
        }

        // DamageHandler targets (the player) are checked HERE in Update, AFTER the
        // Input System has processed dodge presses for this frame. This ensures dash
        // i-frames are respected even if the physical overlap occurred in a prior
        // FixedUpdate step — OnTriggerEnter fires during FixedUpdate (before input),
        // so it cannot see the same-frame dodge. Update can.
        CheckDamageHandlerHits();
    }

    // Handles non-DamageHandler targets only (e.g. boss uses BossController : IDamageable).
    // DamageHandler targets are intentionally skipped here — handled in Update above.
    private void OnTriggerEnter(Collider other)
    {
        if (_returned) return;
        if (_ownerTransform != null && other.transform.IsChildOf(_ownerTransform)) return;

        DamageHandler damageHandler = other.GetComponent<DamageHandler>()
            ?? other.GetComponentInParent<DamageHandler>();

        if (damageHandler != null) return; // Update() handles this — skip here.

        IDamageable target = other.GetComponent<IDamageable>()
            ?? other.GetComponentInParent<IDamageable>();
        var tc = target as Component;
        if (tc != null)
        {
            target.TakeDamage(_damage);
            _onHit?.Invoke();
            // Hit-confirm shake + VFX, tuned per-skill via _impactProfile.
            // Fires here (not at launch) specifically to avoid double-firing —
            // see the double-fire hazard note on RangedBlastSkill.Execute()/Launch().
            if (_impactProfile != null)
            {
                CombatVFXManager.Instance?.SpawnImpact(transform.position, _impactEffectId, _impactProfile);
                ScreenShakeManager.Instance?.Shake(_impactProfile);
            }
        }

        ReturnToPool();
    }

    private void CheckDamageHandlerHits()
    {
        if (_returned) return;

        int count = Physics.OverlapSphereNonAlloc(transform.position, HitRadius, HitBuffer);
        for (int i = 0; i < count; i++)
        {
            if (_ownerTransform != null &&
                HitBuffer[i].transform.IsChildOf(_ownerTransform)) continue;

            DamageHandler handler = HitBuffer[i].GetComponent<DamageHandler>()
                ?? HitBuffer[i].GetComponentInParent<DamageHandler>();

            if (handler == null) continue;

            string source = _ownerTransform != null
                ? $"{_ownerTransform.name} Projectile" : "Projectile";
            handler.TakeDamage(_damage, source);
            _onHit?.Invoke();
            ReturnToPool();
            return;
        }
    }

    private void ReturnToPool()
    {
        if (_returned) return;
        _returned = true;

        if (_pool != null)
            _pool.Release(this);
        else
            gameObject.SetActive(false);
    }
}

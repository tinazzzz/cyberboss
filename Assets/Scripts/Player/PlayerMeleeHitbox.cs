using UnityEngine;

/// <summary>
/// Receives the 'Hit' AnimationEvent from attack clips and immediately casts an
/// OverlapSphere to damage any IDamageable in range.
///
/// Uses OverlapSphere instead of trigger-enter detection because both the player
/// and boss use trigger colliders, and Unity does not fire OnTriggerEnter between
/// two triggers unless at least one has a Rigidbody.
/// </summary>
public class PlayerMeleeHitbox : MonoBehaviour
{
    [SerializeField] private float     _meleeDamage    = 10f;
    [SerializeField] private float     _hitboxRadius   = 0.55f;
    [SerializeField] private Vector3   _hitboxOffset   = new Vector3(0f, 1f, 0.6f);
    [SerializeField] private LayerMask _targetLayerMask = ~0; // Default: Everything

    private readonly Collider[] _hitBuffer = new Collider[8];

    /// <summary>Called by the 'Hit' AnimationEvent on Unarmed-Attack-R1 (basic melee).</summary>
    public void Hit()
    {
        Vector3 worldCenter = transform.TransformPoint(_hitboxOffset);
        int hitCount = Physics.OverlapSphereNonAlloc(worldCenter, _hitboxRadius, _hitBuffer, _targetLayerMask);

        for (int i = 0; i < hitCount; i++)
        {
            if (_hitBuffer[i].gameObject == gameObject) continue;
            if (_hitBuffer[i].transform.IsChildOf(transform)) continue;

            IDamageable target = _hitBuffer[i].GetComponent<IDamageable>()
                ?? _hitBuffer[i].GetComponentInParent<IDamageable>();

            if (target == null) continue;

            var targetComponent = target as Component;
            if (targetComponent != null && targetComponent.transform.IsChildOf(transform))
                continue;

            target.TakeDamage(_meleeDamage);
            // Cyan spark at the hitbox contact point.
            CombatVFXManager.Instance?.SpawnPlayerMeleeImpact(worldCenter);
            break;
        }
    }
}

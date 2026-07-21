using System.Collections;
using UnityEngine;
using UnityEngine.Pool;

/// <summary>
/// Lightweight MonoBehaviour wrapper that returns a pooled ParticleSystem to its
/// ObjectPool after the burst finishes playing.
///
/// Design notes:
///   - The pool reference is passed in PlayAt rather than at create time, which
///     avoids the chicken-and-egg dependency between pool construction and the
///     createFunc delegate that runs to populate it.
///   - WaitForSecondsRealtime is used so the return fires in real wall-clock time
///     even while HitFreezeManager has dipped Time.timeScale. Allocation per play
///     call is acceptable — impacts fire at most once per hit, not per frame.
///   - useUnscaledTime must be true on the ParticleSystem to match (set by
///     CombatVFXManager.CreateParticleSystem).
///   - collectionCheck: false on the pool prevents an exception if the coroutine
///     fires after the pool has already been disposed on scene teardown.
/// </summary>
[RequireComponent(typeof(ParticleSystem))]
public class PooledParticleSystem : MonoBehaviour
{
    private ParticleSystem _ps;

    private void Awake()
    {
        _ps = GetComponent<ParticleSystem>();
    }

    /// <summary>
    /// Moves to worldPosition, plays the configured burst, then auto-returns to pool.
    /// Safe to call on a freshly acquired (active) instance.
    /// </summary>
    /// <param name="worldPosition">World-space impact point.</param>
    /// <param name="pool">The pool this instance belongs to — used to release on completion.</param>
    public void PlayAt(Vector3 worldPosition, IObjectPool<PooledParticleSystem> pool)
    {
        transform.position = worldPosition;
        _ps.Play();

        // Wait for (burst duration + max particle lifetime) before returning.
        // The small safety margin handles floating-point timing edge cases.
        float returnDelay = _ps.main.duration + _ps.main.startLifetime.constantMax + 0.05f;
        StartCoroutine(ReturnCoroutine(returnDelay, pool));
    }

    private IEnumerator ReturnCoroutine(float realSeconds, IObjectPool<PooledParticleSystem> pool)
    {
        yield return new WaitForSecondsRealtime(realSeconds);
        pool?.Release(this);
    }
}

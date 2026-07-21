using UnityEngine;

/// <summary>
/// Spawns a world-space floating damage number that rises and fades over its lifetime.
/// Uses TextMesh (no TextMeshPro dependency) — works in URP / WebGL without extra setup.
/// Called by HealthSystem.TakeDamage() after the defense chain has resolved and HP
/// has actually changed. Never call this before the defense chain — it must only
/// appear when a real HP loss occurred.
/// </summary>
public class FloatingDamageText : MonoBehaviour
{
    private TextMesh _textMesh;
    private Camera   _mainCamera;
    private float    _elapsed;
    private Color    _baseColor;

    private const float Duration   = 1.2f;
    private const float RiseSpeed  = 2.2f;
    private const float FadeStart  = 0.5f;  // fraction of Duration before alpha fades
    private const float FontSize   = 72;
    private const float CharSize   = 0.06f;

    /// <summary>
    /// Create a floating damage label at <paramref name="worldPosition"/>.
    /// Caller is responsible for choosing the right colour and offset above the entity.
    /// </summary>
    public static void Spawn(Vector3 worldPosition, float damage, Color color)
    {
        var go = new GameObject("FloatingDamage");
        go.transform.position = worldPosition;

        // Build the TextMesh in code — no prefab required.
        var tm        = go.AddComponent<TextMesh>();
        tm.text       = $"-{damage:0}";
        tm.color      = color;
        tm.fontSize   = (int)FontSize;
        tm.characterSize = CharSize;
        tm.anchor     = TextAnchor.MiddleCenter;
        tm.alignment  = TextAlignment.Center;
        tm.fontStyle  = FontStyle.Bold;

        // Face the camera immediately so the text is readable from spawn.
        var cam = Camera.main;
        if (cam != null)
            go.transform.rotation = cam.transform.rotation;

        go.AddComponent<FloatingDamageText>();
    }

    private void Awake()
    {
        _textMesh   = GetComponent<TextMesh>();
        _mainCamera = Camera.main;
        _baseColor  = _textMesh.color;
    }

    private void Update()
    {
        _elapsed += Time.deltaTime;

        // Rise straight up.
        transform.position += Vector3.up * (RiseSpeed * Time.deltaTime);

        // Always face the camera (isometric view changes per frame with follow).
        if (_mainCamera != null)
            transform.rotation = _mainCamera.transform.rotation;

        // Fade alpha in the second half of the lifetime.
        float fadeThreshold = Duration * FadeStart;
        if (_elapsed > fadeThreshold)
        {
            float t = (_elapsed - fadeThreshold) / (Duration - fadeThreshold);
            Color c = _baseColor;
            c.a = Mathf.Lerp(1f, 0f, t);
            _textMesh.color = c;
        }

        if (_elapsed >= Duration)
            Destroy(gameObject);
    }
}

using System.Collections;
using UnityEngine;

/// <summary>
/// Visual effects for player defensive skills.
///
/// Barrier shield: a translucent sphere around the player, shown while the shield
/// is active. Pulses gently with a low-alpha HDR tint sourced from
/// BarrierSkillConfig.AbsorbProfile.VfxColor (the skill's "pure cyan" identity
/// color). Barrier now drives 3 visibly distinct end-states via PlayerSkills'
/// Barrier event fan-out (subscribed here instead of polling HasBarrier):
///   OnBarrierDamageAbsorbed — shield stays up; small deflect spark + light CA tick.
///   OnBarrierShieldBroken   — bright break flash + wider "shatter" burst + light shake.
///   OnBarrierShieldExpired  — soft fade only, no flash, no shake, no burst.
///
/// Parry flash: two supplementary effects, both tinted from
/// ParrySkillConfig.ImpactProfile.VfxColor (white-gold — recolored from the
/// original hardcoded cyan-white so Parry no longer overlaps Barrier/Ranged
/// Blast/the generic hit-spark on the same hue):
///   1. A brief flat quad "shield sheet" flash positioned between the player and
///      the boss, facing the boss — the explicit ask for a genuinely distinct
///      SHAPE (not just a distinct color) so a successful parry reads as an
///      obvious "I blocked that" plane, unmistakable from any particle burst.
///   2. The original 8 thin sharp spark lines (LineRenderers) bursting outward
///      from waist height in a ring, angles randomised per activation so they
///      never look robotic. Lines extend quickly on a power-curve, then fade.
/// HDR values on both drive bloom.
///
/// Both effects use unscaled time so they play correctly through HitFreezeManager's
/// Time.timeScale dip.
///
/// Setup: run CyberBoss/Setup Player Combat Effects to add this component and wire
/// _parryConfig / _barrierConfig.
/// </summary>
[RequireComponent(typeof(PlayerSkills))]
public class PlayerCombatEffects : MonoBehaviour
{
    [SerializeField] private ParrySkillConfig   _parryConfig;
    [SerializeField] private BarrierSkillConfig _barrierConfig;

    // ---- shield ----
    private GameObject _shieldVisual;
    private Renderer   _shieldRenderer;
    private float      _shieldPulseTimer;
    private bool       _shieldActive;
    private bool       _shieldEnding;
    private Coroutine  _shieldEndCoroutine;

    // ---- parry sparks ----
    private const int SparkCount = 8;
    private LineRenderer[] _sparkLines;
    private float[]        _sparkAngles; // pre-allocated; randomised on each activation
    private Material       _sparkMat;
    private Coroutine      _parryCoroutine;

    // ---- parry shield-sheet flash ----
    private GameObject _parrySheetVisual;
    private Material   _parrySheetMaterial;
    private Coroutine  _parrySheetCoroutine;
    private Transform  _bossTransform; // cached for the sheet's position/orientation

    private const float ParrySheetMaxOffset = 1.1f; // meters in front of the player, capped
    private const float ParrySheetWidth      = 1.3f;
    private const float ParrySheetHeight     = 1.8f;

    private PlayerSkills       _playerSkills;
    private System.Action<int> _onSkillExecutedDelegate;
    private System.Action      _onBarrierDamageAbsorbedDelegate;
    private System.Action      _onBarrierShieldBrokenDelegate;
    private System.Action      _onBarrierShieldExpiredDelegate;

    // Shared MPB for shield pulses — lazily created (no Unity API in static initialiser).
    private static MaterialPropertyBlock _shieldMpb;
    private static MaterialPropertyBlock ShieldMpb => _shieldMpb ??= new MaterialPropertyBlock();
    private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

    // Fallback tints used only if _parryConfig/_barrierConfig aren't wired yet
    // (e.g. before CyberBoss/Setup Player Combat Effects has run) — keeps this
    // component degrading gracefully rather than throwing.
    private static readonly Color FallbackParryTint  = new Color(2.5f, 2.2f, 0.8f);
    private static readonly Color FallbackShieldTint = new Color(0.1f, 2.0f, 2.2f);

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        _playerSkills  = GetComponent<PlayerSkills>();
        _sparkAngles   = new float[SparkCount];

        BuildShieldVisual();
        BuildParrySparks();
        BuildParrySheet();
    }

    private void Start()
    {
        // Cached once — mirrors the same GameObject.FindWithTag("Boss") pattern
        // used by ScreenShakeManager/CameraImpactEffects/CombatVFXManager/BossController.
        var bossGo = GameObject.FindWithTag("Boss");
        if (bossGo != null)
            _bossTransform = bossGo.transform;
        else
            Debug.LogWarning("[PlayerCombatEffects] No GameObject tagged 'Boss' found. " +
                "Parry's shield-sheet flash will face the player's forward direction instead.");

        if (_playerSkills == null) return;

        _onSkillExecutedDelegate         = OnSkillExecuted;
        _onBarrierDamageAbsorbedDelegate = HandleBarrierDamageAbsorbed;
        _onBarrierShieldBrokenDelegate   = HandleBarrierShieldBroken;
        _onBarrierShieldExpiredDelegate  = HandleBarrierShieldExpired;

        _playerSkills.OnSkillExecuted        += _onSkillExecutedDelegate;
        _playerSkills.OnBarrierDamageAbsorbed += _onBarrierDamageAbsorbedDelegate;
        _playerSkills.OnBarrierShieldBroken   += _onBarrierShieldBrokenDelegate;
        _playerSkills.OnBarrierShieldExpired  += _onBarrierShieldExpiredDelegate;

        if (_parryConfig == null)
            Debug.LogWarning($"[PlayerCombatEffects] ParrySkillConfig not assigned on '{name}'. " +
                "Parry spark ring will fall back to a default tint.");
        if (_barrierConfig == null)
            Debug.LogWarning($"[PlayerCombatEffects] BarrierSkillConfig not assigned on '{name}'. " +
                "Barrier visuals will fall back to a default tint and skip impact profiles.");
    }

    private void OnDestroy()
    {
        if (_playerSkills != null)
        {
            if (_onSkillExecutedDelegate         != null) _playerSkills.OnSkillExecuted         -= _onSkillExecutedDelegate;
            if (_onBarrierDamageAbsorbedDelegate != null) _playerSkills.OnBarrierDamageAbsorbed -= _onBarrierDamageAbsorbedDelegate;
            if (_onBarrierShieldBrokenDelegate   != null) _playerSkills.OnBarrierShieldBroken   -= _onBarrierShieldBrokenDelegate;
            if (_onBarrierShieldExpiredDelegate  != null) _playerSkills.OnBarrierShieldExpired  -= _onBarrierShieldExpiredDelegate;
        }

        if (_sparkMat != null)
        {
            Destroy(_sparkMat);
            _sparkMat = null;
        }

        if (_parrySheetMaterial != null)
        {
            Destroy(_parrySheetMaterial);
            _parrySheetMaterial = null;
        }
    }

    private void Update()
    {
        UpdateShieldPulse();
    }

    // ------------------------------------------------------------------
    // Skill event dispatch
    // ------------------------------------------------------------------

    private void OnSkillExecuted(int skillIndex)
    {
        if (skillIndex == PlayerSkills.SkillIndexParry)
            TriggerParryFlash();
        else if (skillIndex == PlayerSkills.SkillIndexBarrier)
            ActivateShield();
    }

    // ------------------------------------------------------------------
    // Shield visual — activation, ambient pulse, and 3 distinct end-states
    // ------------------------------------------------------------------

    private void ActivateShield()
    {
        if (_shieldEndCoroutine != null)
        {
            StopCoroutine(_shieldEndCoroutine);
            _shieldEndCoroutine = null;
        }

        _shieldEnding     = false;
        _shieldActive     = true;
        _shieldPulseTimer = 0f;
        _shieldVisual.SetActive(true);
    }

    private void UpdateShieldPulse()
    {
        if (!_shieldActive || _shieldEnding) return;

        Color tint = _barrierConfig != null && _barrierConfig.AbsorbProfile != null
            ? _barrierConfig.AbsorbProfile.VfxColor
            : FallbackShieldTint;

        _shieldPulseTimer += Time.deltaTime * 5f;
        float t     = (Mathf.Sin(_shieldPulseTimer) + 1f) * 0.5f;
        // Dim, translucent pulse: intensity 0.35–0.7, alpha 0.07–0.16.
        float intensity = Mathf.Lerp(0.35f, 0.70f, t);
        float alpha     = Mathf.Lerp(0.07f, 0.16f, t);

        ShieldMpb.SetColor(BaseColorId, new Color(tint.r * intensity, tint.g * intensity, tint.b * intensity, alpha));
        if (_shieldRenderer != null)
            _shieldRenderer.SetPropertyBlock(ShieldMpb);
    }

    /// <summary>Shield absorbed a hit and is still up — small deflect spark + light CA tick.</summary>
    private void HandleBarrierDamageAbsorbed()
    {
        ImpactEffectProfile profile = _barrierConfig != null ? _barrierConfig.AbsorbProfile : null;
        CombatVFXManager.Instance?.SpawnImpact(ShieldWorldPosition(), "BarrierAbsorb", profile);
        CameraImpactEffects.Instance?.ImpactCA(profile);
    }

    /// <summary>Shield health depleted to 0 — light shake + bright break flash + wider shatter burst.</summary>
    private void HandleBarrierShieldBroken()
    {
        if (_shieldEnding) return;
        _shieldActive = false;
        _shieldEnding = true;

        ImpactEffectProfile profile = _barrierConfig != null ? _barrierConfig.BrokenProfile : null;
        ScreenShakeManager.Instance?.Shake(profile);
        CombatVFXManager.Instance?.SpawnImpact(ShieldWorldPosition(), "BarrierBroken", profile);
        _shieldEndCoroutine = StartCoroutine(ShieldBreakFlash(profile));
    }

    /// <summary>Duration ran out, undamaged — soft fade only. No flash, no shake, no burst.</summary>
    private void HandleBarrierShieldExpired()
    {
        if (_shieldEnding) return;
        _shieldActive = false;
        _shieldEnding = true;
        _shieldEndCoroutine = StartCoroutine(ShieldFadeOut());
    }

    private Vector3 ShieldWorldPosition() => _shieldVisual != null ? _shieldVisual.transform.position : transform.position;

    /// <summary>
    /// Bright flash when the barrier breaks — reads as a failure. Keeps the shield
    /// sphere visible for the flash, then hides it.
    /// </summary>
    private IEnumerator ShieldBreakFlash(ImpactEffectProfile profile)
    {
        _shieldVisual.SetActive(true);

        Color tint = profile != null ? profile.VfxColor : FallbackShieldTint;
        float elapsed  = 0f;
        float duration = 0.2f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Flash from bright → gone over the duration.
            float k     = Mathf.Lerp(1.3f, 0f, t);
            float alpha = Mathf.Lerp(0.65f, 0f, t);

            ShieldMpb.SetColor(BaseColorId, new Color(tint.r * k, tint.g * k, tint.b * k, alpha));
            if (_shieldRenderer != null)
                _shieldRenderer.SetPropertyBlock(ShieldMpb);
            yield return null;
        }

        _shieldVisual.SetActive(false);
        _shieldEnding       = false;
        _shieldEndCoroutine = null;
    }

    /// <summary>
    /// Soft, calm fade-out for a clean, successful (undamaged) shield use — no
    /// brightness spike, distinguishing it clearly from ShieldBreakFlash's failure read.
    /// </summary>
    private IEnumerator ShieldFadeOut()
    {
        Color tint = _barrierConfig != null && _barrierConfig.AbsorbProfile != null
            ? _barrierConfig.AbsorbProfile.VfxColor
            : FallbackShieldTint;

        const float startIntensity = 0.5f;
        const float startAlpha     = 0.1f;
        float elapsed  = 0f;
        float duration = 0.35f;

        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            float k     = Mathf.Lerp(startIntensity, 0f, t);
            float alpha = Mathf.Lerp(startAlpha, 0f, t);

            ShieldMpb.SetColor(BaseColorId, new Color(tint.r * k, tint.g * k, tint.b * k, alpha));
            if (_shieldRenderer != null)
                _shieldRenderer.SetPropertyBlock(ShieldMpb);
            yield return null;
        }

        _shieldVisual.SetActive(false);
        _shieldEnding       = false;
        _shieldEndCoroutine = null;
    }

    // ------------------------------------------------------------------
    // Parry spark lines
    // ------------------------------------------------------------------

    private void TriggerParryFlash()
    {
        TriggerParryShieldSheet();

        if (_sparkLines == null) return;

        if (_parryCoroutine != null)
        {
            StopCoroutine(_parryCoroutine);
            foreach (var lr in _sparkLines)
                if (lr != null) lr.enabled = false;
        }

        // Fresh random base angle each activation — never looks mechanical.
        float step       = 360f / SparkCount;
        float baseOffset = Random.Range(0f, step);
        for (int i = 0; i < SparkCount; i++)
            _sparkAngles[i] = baseOffset + step * i + Random.Range(-9f, 9f);

        _parryCoroutine = StartCoroutine(ParrySparkCoroutine());
    }

    // ------------------------------------------------------------------
    // Parry shield-sheet flash — a flat plane between player and boss
    // ------------------------------------------------------------------

    /// <summary>
    /// Positions and orients the shield-sheet between the player and the boss
    /// (offset capped at ParrySheetMaxOffset so it always reads as "in front of
    /// the player" regardless of engagement range), then starts the flash.
    /// </summary>
    private void TriggerParryShieldSheet()
    {
        if (_parrySheetVisual == null) return;

        Vector3 toBoss = _bossTransform != null
            ? _bossTransform.position - transform.position
            : transform.forward;
        toBoss.y = 0f;
        if (toBoss.sqrMagnitude < 0.001f) toBoss = transform.forward;
        toBoss.Normalize();

        float offset = _bossTransform != null
            ? Mathf.Min(ParrySheetMaxOffset, Vector3.Distance(_bossTransform.position, transform.position) * 0.5f)
            : ParrySheetMaxOffset;

        _parrySheetVisual.transform.position = transform.position + Vector3.up * 1.0f + toBoss * offset;
        _parrySheetVisual.transform.rotation = Quaternion.LookRotation(toBoss, Vector3.up);

        if (_parrySheetCoroutine != null)
            StopCoroutine(_parrySheetCoroutine);
        _parrySheetCoroutine = StartCoroutine(ParrySheetFlashCoroutine());
    }

    /// <summary>Bright flash that fades fast (~0.15-0.2s) — an obvious, unmistakable "I blocked that" plane.</summary>
    private IEnumerator ParrySheetFlashCoroutine()
    {
        Color tint = _parryConfig != null && _parryConfig.ImpactProfile != null
            ? _parryConfig.ImpactProfile.VfxColor
            : FallbackParryTint;
        float duration = _parryConfig != null && _parryConfig.ImpactProfile != null
            ? Mathf.Max(0.05f, _parryConfig.ImpactProfile.VfxLifetime)
            : 0.18f;

        _parrySheetVisual.SetActive(true);

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t     = Mathf.Clamp01(elapsed / duration);
            float k     = Mathf.Lerp(1f, 0f, t); // bright -> gone
            float alpha = Mathf.Lerp(0.85f, 0f, t);

            if (_parrySheetMaterial != null)
                _parrySheetMaterial.SetColor(BaseColorId, new Color(tint.r * k, tint.g * k, tint.b * k, alpha));
            yield return null;
        }

        _parrySheetVisual.SetActive(false);
        _parrySheetCoroutine = null;
    }

    private IEnumerator ParrySparkCoroutine()
    {
        Color tint = _parryConfig != null && _parryConfig.ImpactProfile != null
            ? _parryConfig.ImpactProfile.VfxColor
            : FallbackParryTint;
        float duration = _parryConfig != null && _parryConfig.ImpactProfile != null
            ? _parryConfig.ImpactProfile.VfxLifetime
            : 0.18f;
        const float maxLength = 1.1f;

        // Capture origin in world space once — player may move during the flash.
        Vector3 origin = transform.position + new Vector3(0f, 0.72f, 0f);

        foreach (var lr in _sparkLines)
            if (lr != null) lr.enabled = true;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t      = Mathf.Clamp01(elapsed / duration);
            // Power curve: lines extend fast out of the gate, tail off at end.
            float length = maxLength * Mathf.Pow(t, 0.30f);
            float alpha  = 1f - t;
            // Normalized brightness envelope (1 -> ~0.12) applied to the config's
            // white-gold tint — > 1 components still feed URP bloom for a sharp glow.
            float k      = Mathf.Lerp(1f, 0.12f, t);

            var colBase = new Color(tint.r * k, tint.g * k, tint.b * k, alpha);
            var colTip  = new Color(tint.r * k, tint.g * k, tint.b * k, 0f);

            for (int i = 0; i < SparkCount; i++)
            {
                var lr = _sparkLines[i];
                if (lr == null) continue;

                float   rad = _sparkAngles[i] * Mathf.Deg2Rad;
                Vector3 dir = new Vector3(Mathf.Cos(rad), 0f, Mathf.Sin(rad));

                lr.SetPosition(0, origin + dir * 0.06f);
                lr.SetPosition(1, origin + dir * length);
                lr.startColor = colBase;
                lr.endColor   = colTip;
            }

            yield return null;
        }

        foreach (var lr in _sparkLines)
            if (lr != null) lr.enabled = false;

        _parryCoroutine = null;
    }

    // ------------------------------------------------------------------
    // Construction
    // ------------------------------------------------------------------

    private void BuildShieldVisual()
    {
        _shieldVisual      = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        _shieldVisual.name = "BarrierShieldVisual";
        _shieldVisual.transform.SetParent(transform);
        // Centre on the player body (capsule ≈ 1.6 m tall → 0.8 m Y offset).
        _shieldVisual.transform.localPosition = new Vector3(0f, 0.8f, 0f);
        _shieldVisual.transform.localScale    = new Vector3(1.6f, 1.6f, 1.6f);

        var col = _shieldVisual.GetComponent<Collider>();
        if (col != null) Destroy(col);

        _shieldRenderer = _shieldVisual.GetComponent<Renderer>();
        if (_shieldRenderer != null)
        {
            _shieldRenderer.sharedMaterial    = CreateShieldMaterial();
            _shieldRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _shieldRenderer.receiveShadows    = false;
        }

        _shieldVisual.SetActive(false);
    }

    private static Material CreateShieldMaterial()
    {
        // URP Unlit: no lighting — HDR BaseColor drives bloom directly.
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Sprites/Default");
        if (shader == null)
            return new Material(Shader.Find("Standard"));

        var mat = new Material(shader);

        // Additive-ish transparent: dark areas become invisible, bright HDR areas glow.
        mat.SetFloat("_Surface", 1f); // Transparent
        mat.SetFloat("_Blend",   1f); // Additive
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite",   0);

        // Both faces visible — reads as a shell around the player.
        mat.SetInt("_Cull", (int)UnityEngine.Rendering.CullMode.Off);

        // Starting color — overwritten each frame by UpdateShieldPulse().
        mat.SetColor(BaseColorId, new Color(0f, 0.5f, 0.5f, 0.10f));

        return mat;
    }

    private void BuildParrySparks()
    {
        _sparkMat   = CreateLineMaterial();
        _sparkLines = new LineRenderer[SparkCount];

        for (int i = 0; i < SparkCount; i++)
        {
            var go = new GameObject($"ParrySpark_{i}");
            go.transform.SetParent(transform);
            go.transform.localPosition = Vector3.zero;

            var lr = go.AddComponent<LineRenderer>();
            lr.material             = _sparkMat;
            lr.positionCount        = 2;
            lr.startWidth           = 0.055f;   // tapers to a point at the tip
            lr.endWidth             = 0f;
            lr.numCapVertices       = 0;
            lr.shadowCastingMode    = UnityEngine.Rendering.ShadowCastingMode.Off;
            lr.receiveShadows       = false;
            lr.useWorldSpace        = true;
            lr.enabled              = false;

            _sparkLines[i] = lr;
        }
    }

    private void BuildParrySheet()
    {
        _parrySheetVisual = GameObject.CreatePrimitive(PrimitiveType.Quad);
        _parrySheetVisual.name = "ParryShieldSheet";
        // Not parented to the player — TriggerParryShieldSheet() positions/orients
        // it in world space each activation (it needs to point at the boss, not
        // just sit in the player's local frame).

        var col = _parrySheetVisual.GetComponent<Collider>();
        if (col != null) Destroy(col);

        var sheetRenderer = _parrySheetVisual.GetComponent<Renderer>();
        _parrySheetMaterial = CreateSheetMaterial();
        if (sheetRenderer != null)
        {
            sheetRenderer.sharedMaterial    = _parrySheetMaterial;
            sheetRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            sheetRenderer.receiveShadows    = false;
        }

        _parrySheetVisual.transform.localScale = new Vector3(ParrySheetWidth, ParrySheetHeight, 1f);
        _parrySheetVisual.SetActive(false);
    }

    private static Material CreateSheetMaterial()
    {
        var shader = Shader.Find("Universal Render Pipeline/Unlit")
                  ?? Shader.Find("Sprites/Default");
        if (shader == null)
            return new Material(Shader.Find("Standard"));

        var mat = new Material(shader);

        // Additive transparent, visible from both sides (the player could be on
        // either side of the sheet relative to the boss depending on facing).
        mat.SetFloat("_Surface", 1f);
        mat.SetFloat("_Blend",   1f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite",   0);
        mat.SetInt("_Cull",     (int)UnityEngine.Rendering.CullMode.Off);

        mat.SetColor(BaseColorId, Color.white); // overwritten each flash by ParrySheetFlashCoroutine()

        return mat;
    }

    private static Material CreateLineMaterial()
    {
        // Particles/Unlit respects vertex colors (startColor/endColor on LineRenderer)
        // and supports HDR values for URP bloom. Fallback to Sprites/Default.
        var shader = Shader.Find("Universal Render Pipeline/Particles/Unlit")
                  ?? Shader.Find("Sprites/Default");
        if (shader == null)
            return new Material(Shader.Find("Standard"));

        var mat = new Material(shader);

        // White base so vertex color passes through unmodified.
        if (mat.HasProperty(BaseColorId))
            mat.SetColor(BaseColorId, Color.white);

        // Additive blend: sparks layer on top of the scene without darkening it.
        mat.SetFloat("_Surface", 1f);
        mat.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        mat.SetOverrideTag("RenderType", "Transparent");
        mat.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
        mat.SetInt("_SrcBlend", (int)UnityEngine.Rendering.BlendMode.SrcAlpha);
        mat.SetInt("_DstBlend", (int)UnityEngine.Rendering.BlendMode.One);
        mat.SetInt("_ZWrite",   0);

        return mat;
    }
}

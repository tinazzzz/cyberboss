using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Drives 5 cooldown fill images, slot border glows, and charge count labels.
///
/// Skill order matches PlayerSkills.SkillIndex constants:
///   0 = Dash, 1 = Parry, 2 = Ranged Blast, 3 = Burst Strike, 4 = Barrier
///
/// CooldownProgress semantics (0 = just triggered / empty, 1 = fully recharged / ready):
///   fillAmount is assigned directly — no inversion needed.
///
/// Charge count labels: for skills that implement IChargeable (Ranged Blast), a Text
/// child is created at runtime inside the slot and updated every frame with the
/// remaining shot count. Non-charge slots have no label.
///
/// Fill and border colors:
///   Ready          → CooldownReadyColor (neon cyan)
///   Charging/empty → EnergyChargeColor  (amber) for BurstStrike and Ranged Blast at 0 charges
///   Depleted       → CooldownDepletedColor (dark grey) for all other timed cooldowns
///
/// Update() reads 5 floats + optional int per frame — no allocation, no string ops
/// (charge text uses a pre-allocated char buffer for the digit conversion).
/// </summary>
public class CooldownUI : MonoBehaviour
{
    [SerializeField] private Image[] _skillFills;
    [SerializeField] private Image[] _slotBorders;
    [SerializeField] private HUDConfig _config;

    private IReadOnlyList<ISkill> _skills;
    private bool[]                _wasReady;
    private IChargeable[]         _chargeables;   // null for non-charge skills
    private Text[]                _chargeLabels;  // null for non-charge skills
    private bool                  _initialized;

    // Pre-allocated char array for int→string conversion without heap allocation.
    private readonly char[] _charBuffer = new char[4];

    // ------------------------------------------------------------------
    // Public API
    // ------------------------------------------------------------------

    public void Initialize(IReadOnlyList<ISkill> skills)
    {
        if (_initialized) return;
        _initialized = true;

        _skills      = skills;
        _wasReady    = new bool[_skillFills.Length];
        _chargeables = new IChargeable[_skillFills.Length];
        _chargeLabels = new Text[_skillFills.Length];

        int count = Mathf.Min(_skillFills.Length, skills.Count);
        for (int i = 0; i < count; i++)
        {
            if (skills[i] is IChargeable chargeable)
            {
                _chargeables[i]  = chargeable;
                _chargeLabels[i] = CreateChargeLabel(_skillFills[i]);
            }

            bool ready   = skills[i].IsReady;
            _wasReady[i] = ready;
            ApplyReadyState(i, ready);
        }
    }

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Update()
    {
        if (_skills == null) return;

        int count = Mathf.Min(_skillFills.Length, _skills.Count);
        for (int i = 0; i < count; i++)
        {
            if (_skillFills[i] == null) continue;

            _skillFills[i].fillAmount = _skills[i].CooldownProgress;

            // Charge count label — update every frame (discrete jumps on each shot).
            if (_chargeLabels[i] != null && _chargeables[i] != null)
                UpdateChargeLabel(i);

            bool isReady = _skills[i].IsReady;
            if (isReady == _wasReady[i]) continue;

            ApplyReadyState(i, isReady);
            _wasReady[i] = isReady;
        }
    }

    // ------------------------------------------------------------------
    // Color / glow
    // ------------------------------------------------------------------

    private void ApplyReadyState(int index, bool isReady)
    {
        if (_config == null) return;

        if (_skillFills != null && index < _skillFills.Length && _skillFills[index] != null)
        {
            Color fillColor;
            if (isReady)
                fillColor = _config.CooldownReadyColor;
            else if (index == PlayerSkills.SkillIndexBurstStrike ||
                     index == PlayerSkills.SkillIndexRangedBlast)
                fillColor = _config.EnergyChargeColor; // amber — visible at low fill amounts
            else
                fillColor = _config.CooldownDepletedColor;

            _skillFills[index].color = fillColor;
        }

        if (_slotBorders != null && index < _slotBorders.Length && _slotBorders[index] != null)
            _slotBorders[index].color = isReady
                ? _config.SlotBorderReadyColor
                : _config.SlotBorderDepletedColor;
    }

    // ------------------------------------------------------------------
    // Charge label helpers
    // ------------------------------------------------------------------

    /// <summary>
    /// Spawn a centered Text child inside the skill slot to display the charge count.
    /// Created once per IChargeable slot in Initialize(); never reallocated.
    /// </summary>
    private Text CreateChargeLabel(Image fillImage)
    {
        var parent = fillImage.transform.parent;
        if (parent == null) parent = fillImage.transform;

        var go = new GameObject("ChargeCount");
        go.transform.SetParent(parent, false);

        var rt = go.AddComponent<RectTransform>();
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.offsetMin        = Vector2.zero;
        rt.offsetMax        = Vector2.zero;

        var text             = go.AddComponent<Text>();
        text.font            = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize        = 20;
        text.fontStyle       = FontStyle.Bold;
        text.alignment       = TextAnchor.MiddleCenter;
        text.color           = Color.white;
        text.raycastTarget   = false;

        return text;
    }

    /// <summary>
    /// Write the charge count into the label using the pre-allocated char buffer
    /// (avoids the int.ToString() heap allocation on every frame).
    /// Format: "X/Y" e.g. "2/3".
    /// </summary>
    private void UpdateChargeLabel(int index)
    {
        IChargeable c    = _chargeables[index];
        Text        label = _chargeLabels[index];

        int remaining = c.ChargesRemaining;
        int max       = c.MaxCharges;

        // Write "R/M" into _charBuffer without allocating.
        int pos = 0;
        pos = WriteInt(_charBuffer, pos, remaining);
        _charBuffer[pos++] = '/';
        pos = WriteInt(_charBuffer, pos, max);

        label.text = new string(_charBuffer, 0, pos);

        // Tint: cyan when full, amber when partially charged, red when empty.
        label.color = remaining == max ? _config.CooldownReadyColor
            : remaining > 0            ? Color.white
            :                            new Color(1f, 0.35f, 0.1f);
    }

    /// Writes a non-negative integer ≤ 99 into <paramref name="buf"/> at
    /// <paramref name="offset"/>. Returns the new write offset.
    private static int WriteInt(char[] buf, int offset, int value)
    {
        if (value >= 10)
            buf[offset++] = (char)('0' + value / 10);
        buf[offset++] = (char)('0' + value % 10);
        return offset;
    }
}

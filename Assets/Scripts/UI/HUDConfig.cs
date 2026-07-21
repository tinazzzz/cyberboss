using UnityEngine;

/// <summary>
/// All tunable HUD values — colors, messages, and cyberpunk styling.
///
/// Assigned to CooldownUI and GameOverScreen via SetupHUD.cs.
/// No magic values live in the UI MonoBehaviours; everything configurable is here.
///
/// To extend: add transition AnimationCurves or font references here if needed.
/// Localization note: message strings are ScriptableObject-backed. When a
/// localization system is added, replace these with localization keys.
/// </summary>
[CreateAssetMenu(fileName = "HUDConfig", menuName = "CyberBoss/HUDConfig")]
public class HUDConfig : ScriptableObject
{
    [Header("Health Bar Colors")]
    [SerializeField] private Color _playerBarColor = new Color(0.10f, 0.95f, 0.30f); // neon green
    [SerializeField] private Color _bossBarColor   = new Color(1.00f, 0.15f, 0.10f); // neon red

    [Header("Cooldown Fill Colors")]
    [SerializeField] private Color _cooldownReadyColor    = new Color(0.10f, 0.90f, 1.00f); // neon cyan
    [SerializeField] private Color _cooldownDepletedColor = new Color(0.15f, 0.15f, 0.15f); // dark grey
    [SerializeField] private Color _energyChargeColor     = new Color(1.00f, 0.75f, 0.10f); // amber — Burst Strike charging

    [Header("Cooldown Slot Border Colors")]
    [SerializeField] private Color _slotBorderReadyColor    = new Color(0.10f, 0.90f, 1.00f, 1.00f); // neon cyan glow
    [SerializeField] private Color _slotBorderDepletedColor = new Color(0.18f, 0.18f, 0.18f, 1.00f); // dim grey

    [Header("Game Over — Death")]
    [SerializeField] private Color  _deathMessageColor = new Color(0.95f, 0.10f, 0.10f); // neon red
    [SerializeField] private string _subtitleDeathText = "SYSTEM FAILURE";

    [Header("Game Over — Victory")]
    [SerializeField] private Color  _victoryMessageColor = new Color(0.10f, 0.95f, 0.85f); // neon cyan
    [SerializeField] private string _subtitleVictoryText = "TARGET ELIMINATED";

    [Header("Game Over Text")]
    [SerializeField] private string _deathMessage    = "YOU DIED";
    [SerializeField] private string _victoryMessage  = "BOSS DEFEATED";
    [SerializeField] private string _restartHintText = "[ PRESS T TO RESTART ]";

    [Header("Main Menu")]
    [SerializeField] private Color  _menuTitleColor    = new Color(0.10f, 0.90f, 1.00f); // neon cyan
    [SerializeField] private Color  _menuSubtitleColor = new Color(0.85f, 0.10f, 0.75f); // neon magenta
    [SerializeField] private string _menuTitleText     = "CYBERBOSS";
    [SerializeField] private string _menuSubtitleText  = "RL-TRAINED CYBERPUNK BOSS FIGHT";
    [SerializeField] private string _menuStartHintText  = "[ PRESS ENTER TO START ]";
    [SerializeField] private string _menuResumeHintText = "[ PRESS ENTER TO RESUME ]";
    [SerializeField] private Color  _menuControlKeyColor = new Color(0.10f, 0.90f, 1.00f); // neon cyan
    [SerializeField] [TextArea(4, 8)] private string _menuControlKeysText =
        "WASD\nSHIFT\nSPACE\nQ\nR\nE\nF\nLEFT CLICK\nM";
    [SerializeField] [TextArea(4, 8)] private string _menuControlActionsText =
        "Move\nRun\nDash\nParry\nRanged Blast\nBurst Strike\nBarrier\nAttack\nPause";

    // ── Public accessors ─────────────────────────────────────────────────

    public Color  PlayerBarColor           => _playerBarColor;
    public Color  BossBarColor             => _bossBarColor;
    public Color  CooldownReadyColor       => _cooldownReadyColor;
    public Color  CooldownDepletedColor    => _cooldownDepletedColor;
    public Color  EnergyChargeColor        => _energyChargeColor;
    public Color  SlotBorderReadyColor     => _slotBorderReadyColor;
    public Color  SlotBorderDepletedColor  => _slotBorderDepletedColor;
    public Color  DeathMessageColor        => _deathMessageColor;
    public string SubtitleDeathText        => _subtitleDeathText;
    public Color  VictoryMessageColor      => _victoryMessageColor;
    public string SubtitleVictoryText      => _subtitleVictoryText;
    public string DeathMessage             => _deathMessage;
    public string VictoryMessage           => _victoryMessage;
    public string RestartHintText          => _restartHintText;

    public Color  MenuTitleColor           => _menuTitleColor;
    public Color  MenuSubtitleColor        => _menuSubtitleColor;
    public string MenuTitleText            => _menuTitleText;
    public string MenuSubtitleText         => _menuSubtitleText;
    public string MenuStartHintText        => _menuStartHintText;
    public string MenuResumeHintText       => _menuResumeHintText;
    public Color  MenuControlKeyColor      => _menuControlKeyColor;
    public string MenuControlKeysText      => _menuControlKeysText;
    public string MenuControlActionsText   => _menuControlActionsText;
}

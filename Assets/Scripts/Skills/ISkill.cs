using UnityEngine;

/// <summary>
/// Shared interface for all skills — player and boss.
/// Do not rename or remove members — combat, HUD, and AI systems depend on this contract.
///
/// IsReady          — gate-check before calling Execute; also used by CooldownUI
/// CooldownProgress — 0–1 fill value for HUD; 1 = ready, 0 = fully cooling down
/// Execute          — trigger the skill on the given user GameObject
/// UpdateCooldown   — advance all time-based state; call from MonoBehaviour.Update() every frame
/// Reset            — clear all in-flight state and pending animator triggers; call on respawn
/// </summary>
public interface ISkill
{
    bool  IsReady          { get; }
    float CooldownProgress { get; }
    void  Execute(GameObject user);
    void  UpdateCooldown(float deltaTime);
    void  Reset(UnityEngine.Animator animator);
}

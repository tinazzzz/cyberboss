using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Wires ExplosiveLLC Unarmed animation clips into the Muryotaisu AnimatorController.
/// Run via CyberBoss/Setup RPG Animations.
///
/// Changes made:
///   - Idle state    → Unarmed-Idle clip
///   - Walk state    → Unarmed-Run-Forward clip (also used for sprint, feels best isometrically)
///   - Adds Dash state → Unarmed-DiveRoll-Forward1, DashTrigger from Idle/Walk, exits to Idle
///   - Adds Attack state → Unarmed-Attack-R1, triggered by StrikeTrigger, exits to Idle
///
/// Skill trigger params (DashTrigger, ParryTrigger, BlastTrigger, StrikeTrigger, BarrierTrigger)
/// are already present in the controller; no parameters are added here.
/// </summary>
public class SetupRPGAnimations
{
    private const string ControllerPath = "Assets/Mryotaisu/Animators/Muryotaisu.controller";

    private const string IdleClipPath   = "Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack FREE/Animations/Unarmed/RPG-Character@Unarmed-Idle.FBX";
    private const string RunClipPath    = "Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack FREE/Animations/Unarmed/RPG-Character@Unarmed-Run-Forward.FBX";
    private const string DashClipPath   = "Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack FREE/Animations/Unarmed/RPG-Character@Unarmed-DiveRoll-Forward1.FBX";
    private const string AttackClipPath = "Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack FREE/Animations/Unarmed/RPG-Character@Unarmed-Attack-R1.FBX";

    [MenuItem("CyberBoss/Setup RPG Animations")]
    public static void Execute()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) { Debug.LogError($"[SetupRPGAnimations] Controller not found: {ControllerPath}"); return; }

        AnimationClip idleClip   = LoadClip(IdleClipPath,   "Unarmed-Idle");
        AnimationClip runClip    = LoadClip(RunClipPath,    "Unarmed-Run-Forward");
        AnimationClip dashClip   = LoadClip(DashClipPath,   "Unarmed-DiveRoll-Forward1");
        AnimationClip attackClip = LoadClip(AttackClipPath, "Unarmed-Attack-R1");

        if (idleClip == null || runClip == null || dashClip == null || attackClip == null)
        {
            Debug.LogError("[SetupRPGAnimations] One or more clips could not be loaded. Aborting.");
            return;
        }

        AnimatorStateMachine baseLayer = controller.layers[0].stateMachine;

        // --- Replace motions on existing states ---
        SetStateMotion(baseLayer, "Idle", idleClip);
        SetStateMotion(baseLayer, "Walk", runClip);

        // --- Dash state ---
        AnimatorState dashState = GetOrAddState(baseLayer, "Dash");
        dashState.motion = dashClip;
        dashState.speed  = 1.2f;

        // DashTrigger transitions from Idle and Walk into Dash (instant, no exit time)
        AddTriggerTransitionIfMissing(baseLayer, "Idle", "Dash", "DashTrigger", controller);
        AddTriggerTransitionIfMissing(baseLayer, "Walk", "Dash", "DashTrigger", controller);

        // Dash exits to Idle automatically after the clip finishes
        AddExitTimeTransitionIfMissing(dashState, GetState(baseLayer, "Idle"));

        // --- Strike/Attack state ---
        AnimatorState attackState = GetOrAddState(baseLayer, "Strike");
        attackState.motion = attackClip;
        attackState.speed  = 1.2f;

        AddTriggerTransitionIfMissing(baseLayer, "Idle", "Strike", "StrikeTrigger", controller);
        AddTriggerTransitionIfMissing(baseLayer, "Walk", "Strike", "StrikeTrigger", controller);
        AddExitTimeTransitionIfMissing(attackState, GetState(baseLayer, "Idle"));

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[SetupRPGAnimations] Done. Idle, Walk, Dash, and Strike states updated.");
    }

    // ------------------------------------------------------------------

    private static AnimationClip LoadClip(string fbxPath, string clipName)
    {
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (obj is AnimationClip clip && clip.name == clipName)
                return clip;
        }
        Debug.LogError($"[SetupRPGAnimations] Clip '{clipName}' not found in {fbxPath}");
        return null;
    }

    private static void SetStateMotion(AnimatorStateMachine sm, string stateName, Motion motion)
    {
        foreach (ChildAnimatorState cs in sm.states)
        {
            if (cs.state.name == stateName)
            {
                cs.state.motion = motion;
                Debug.Log($"[SetupRPGAnimations] Set '{stateName}' motion -> {motion.name}");
                return;
            }
        }
        Debug.LogWarning($"[SetupRPGAnimations] State '{stateName}' not found in {sm.name}");
    }

    private static AnimatorState GetState(AnimatorStateMachine sm, string stateName)
    {
        foreach (ChildAnimatorState cs in sm.states)
            if (cs.state.name == stateName) return cs.state;
        return null;
    }

    private static AnimatorState GetOrAddState(AnimatorStateMachine sm, string stateName)
    {
        AnimatorState existing = GetState(sm, stateName);
        if (existing != null) return existing;
        AnimatorState newState = sm.AddState(stateName);
        Debug.Log($"[SetupRPGAnimations] Added state '{stateName}'");
        return newState;
    }

    private static void AddTriggerTransitionIfMissing(
        AnimatorStateMachine sm,
        string fromStateName,
        string toStateName,
        string triggerParam,
        AnimatorController controller)
    {
        AnimatorState from = GetState(sm, fromStateName);
        AnimatorState to   = GetState(sm, toStateName);
        if (from == null || to == null) return;

        foreach (AnimatorStateTransition t in from.transitions)
            if (t.destinationState == to) return; // already exists

        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime            = false;
        transition.duration               = 0.1f;
        transition.AddCondition(AnimatorConditionMode.If, 0f, triggerParam);
        Debug.Log($"[SetupRPGAnimations] Added transition {fromStateName} -> {toStateName} ({triggerParam})");
    }

    private static void AddExitTimeTransitionIfMissing(AnimatorState from, AnimatorState to)
    {
        if (from == null || to == null) return;

        foreach (AnimatorStateTransition t in from.transitions)
            if (t.destinationState == to) return;

        AnimatorStateTransition transition = from.AddTransition(to);
        transition.hasExitTime            = true;
        transition.exitTime               = 0.9f;
        transition.duration               = 0.1f;
        Debug.Log($"[SetupRPGAnimations] Added exit-time transition {from.name} -> {to.name}");
    }
}

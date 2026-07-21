using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Fixes the Muryotaisu animator after SetupRPGAnimations:
///   - Idle / Walk states reverted to original Mryotaisu clips
///   - Dash state motion cleared (no animation — dash movement still works)
///   - Parry state added (Unarmed-Stunned, fast speed) triggered by ParryTrigger
///   - AttackTrigger parameter added so left-click fires the Strike animation
/// Run via CyberBoss/Fix Animator V2.
/// </summary>
public class FixAnimatorV2
{
    private const string ControllerPath    = "Assets/Mryotaisu/Animators/Muryotaisu.controller";
    private const string MryotaisuFbxPath  = "Assets/Mryotaisu/Models/Muryotaisu.fbx";
    private const string StunnedClipPath   = "Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack FREE/Animations/Unarmed/RPG-Character@Unarmed-Stunned.FBX";

    [MenuItem("CyberBoss/Fix Animator V2")]
    public static void Execute()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) { Debug.LogError($"[FixAnimatorV2] Controller not found: {ControllerPath}"); return; }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // --- 1. Revert Idle / Walk to original Mryotaisu clips ---
        AnimationClip idleClip = LoadClipFromFbx(MryotaisuFbxPath, "Armature|IdleA");
        AnimationClip walkClip = LoadClipFromFbx(MryotaisuFbxPath, "Armature|Walk");
        if (idleClip != null) SetStateMotion(sm, "Idle", idleClip);
        if (walkClip != null) SetStateMotion(sm, "Walk", walkClip);

        // --- 2. Clear Dash state motion (movement still works, no odd animation) ---
        AnimatorState dashState = GetState(sm, "Dash");
        if (dashState != null)
        {
            dashState.motion = null;
            Debug.Log("[FixAnimatorV2] Cleared Dash state motion.");
        }

        // --- 3. Add Parry state triggered by ParryTrigger ---
        AnimationClip stunnedClip = LoadClipFromFbx(StunnedClipPath, "Unarmed-Stunned");
        AnimatorState parryState  = GetOrAddState(sm, "Parry");
        if (stunnedClip != null) parryState.motion = stunnedClip;
        parryState.speed = 2.5f; // 2s clip at 2.5× → ~0.8s, matches typical parry window

        EnsureParamExists(controller, "ParryTrigger", AnimatorControllerParameterType.Trigger);
        AddTriggerTransitionIfMissing(sm, "Idle", "Parry", "ParryTrigger");
        AddTriggerTransitionIfMissing(sm, "Walk", "Parry", "ParryTrigger");
        AddExitTimeTransitionIfMissing(parryState, GetState(sm, "Idle"));

        // --- 4. Add AttackTrigger parameter + left-click → Strike state ---
        EnsureParamExists(controller, "AttackTrigger", AnimatorControllerParameterType.Trigger);
        // Strike state was created by SetupRPGAnimations; add AttackTrigger paths to it too.
        AddTriggerTransitionIfMissing(sm, "Idle", "Strike", "AttackTrigger");
        AddTriggerTransitionIfMissing(sm, "Walk", "Strike", "AttackTrigger");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[FixAnimatorV2] Done.");
    }

    // ------------------------------------------------------------------

    private static AnimationClip LoadClipFromFbx(string fbxPath, string clipName)
    {
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
        {
            if (obj is AnimationClip clip && clip.name == clipName)
                return clip;
        }
        Debug.LogError($"[FixAnimatorV2] Clip '{clipName}' not found in {fbxPath}");
        return null;
    }

    private static void SetStateMotion(AnimatorStateMachine sm, string stateName, Motion motion)
    {
        foreach (ChildAnimatorState cs in sm.states)
        {
            if (cs.state.name != stateName) continue;
            cs.state.motion = motion;
            Debug.Log($"[FixAnimatorV2] {stateName} → {motion.name}");
            return;
        }
        Debug.LogWarning($"[FixAnimatorV2] State '{stateName}' not found.");
    }

    private static AnimatorState GetState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState cs in sm.states)
            if (cs.state.name == name) return cs.state;
        return null;
    }

    private static AnimatorState GetOrAddState(AnimatorStateMachine sm, string name)
    {
        AnimatorState s = GetState(sm, name);
        if (s != null) return s;
        s = sm.AddState(name);
        Debug.Log($"[FixAnimatorV2] Added state '{name}'");
        return s;
    }

    private static void EnsureParamExists(AnimatorController controller, string paramName, AnimatorControllerParameterType type)
    {
        foreach (var p in controller.parameters)
            if (p.name == paramName) return;
        controller.AddParameter(paramName, type);
        Debug.Log($"[FixAnimatorV2] Added parameter '{paramName}'");
    }

    private static void AddTriggerTransitionIfMissing(AnimatorStateMachine sm, string from, string to, string trigger)
    {
        AnimatorState fromState = GetState(sm, from);
        AnimatorState toState   = GetState(sm, to);
        if (fromState == null || toState == null) return;

        foreach (AnimatorStateTransition t in fromState.transitions)
        {
            if (t.destinationState != toState) continue;
            foreach (var c in t.conditions)
                if (c.parameter == trigger) return; // already exists
        }

        AnimatorStateTransition tr = fromState.AddTransition(toState);
        tr.hasExitTime = false;
        tr.duration    = 0.1f;
        tr.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        Debug.Log($"[FixAnimatorV2] {from} → {to} ({trigger})");
    }

    private static void AddExitTimeTransitionIfMissing(AnimatorState from, AnimatorState to)
    {
        if (from == null || to == null) return;
        foreach (AnimatorStateTransition t in from.transitions)
            if (t.destinationState == to && t.hasExitTime) return;

        AnimatorStateTransition tr = from.AddTransition(to);
        tr.hasExitTime = true;
        tr.exitTime    = 0.9f;
        tr.duration    = 0.1f;
        Debug.Log($"[FixAnimatorV2] {from.name} → {to.name} (exit time)");
    }
}

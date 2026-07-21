using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;

/// <summary>
/// Patch 4: Add a separate BurstStrike animator state (Unarmed-Attack-R3)
/// triggered by StrikeTrigger (E key), distinct from the basic Attack state
/// (Unarmed-Attack-R1, left click / AttackTrigger).
/// Run via CyberBoss/Fix Animator V4.
/// </summary>
public class FixAnimatorV4
{
    private const string ControllerPath    = "Assets/Mryotaisu/Animators/Muryotaisu.controller";
    private const string BurstClipPath     = "Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack FREE/Animations/Unarmed/RPG-Character@Unarmed-Attack-R3.FBX";

    [MenuItem("CyberBoss/Fix Animator V4")]
    public static void Execute()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) { Debug.LogError("[FixAnimatorV4] Controller not found."); return; }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        AnimationClip burstClip = LoadClip(BurstClipPath, "Unarmed-Attack-R3");
        if (burstClip == null) return;

        // Add BurstStrike state (distinct from Strike which is used by left click)
        AnimatorState burstState = GetOrAddState(sm, "BurstStrike");
        burstState.motion = burstClip;
        burstState.speed  = 1.2f;

        EnsureParamExists(controller, "StrikeTrigger", AnimatorControllerParameterType.Trigger);
        AddTriggerTransitionIfMissing(sm, "Idle", "BurstStrike", "StrikeTrigger");
        AddTriggerTransitionIfMissing(sm, "Walk", "BurstStrike", "StrikeTrigger");
        AddExitTimeTransitionIfMissing(burstState, GetState(sm, "Idle"));

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[FixAnimatorV4] Done — BurstStrike state added with Unarmed-Attack-R3.");
    }

    private static AnimationClip LoadClip(string fbxPath, string clipName)
    {
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(fbxPath))
            if (obj is AnimationClip c && c.name == clipName) return c;
        Debug.LogError($"[FixAnimatorV4] Clip '{clipName}' not found in {fbxPath}");
        return null;
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
        Debug.Log($"[FixAnimatorV4] Added state '{name}'");
        return s;
    }

    private static void EnsureParamExists(AnimatorController ac, string name, AnimatorControllerParameterType type)
    {
        foreach (var p in ac.parameters)
            if (p.name == name) return;
        ac.AddParameter(name, type);
        Debug.Log($"[FixAnimatorV4] Added parameter '{name}'");
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
                if (c.parameter == trigger) return;
        }

        var tr = fromState.AddTransition(toState);
        tr.hasExitTime = false;
        tr.duration    = 0.05f;
        tr.AddCondition(AnimatorConditionMode.If, 0f, trigger);
        Debug.Log($"[FixAnimatorV4] {from} → {to} ({trigger})");
    }

    private static void AddExitTimeTransitionIfMissing(AnimatorState from, AnimatorState to)
    {
        if (from == null || to == null) return;
        foreach (AnimatorStateTransition t in from.transitions)
            if (t.destinationState == to && t.hasExitTime) return;

        var tr = from.AddTransition(to);
        tr.hasExitTime = true;
        tr.exitTime    = 0.9f;
        tr.duration    = 0.1f;
    }
}

using UnityEngine;
using UnityEditor;
using UnityEditor.Animations;
using System.Collections.Generic;

/// <summary>
/// Patch 3:
///   - Delete Dash state entirely (null motion caused T-pose/sit on exit)
///   - Remove StrikeTrigger transitions (left click already handles attack via AttackTrigger)
///   - Speed up Parry state from 2.5 to 4.0 (2s clip → 0.5s flash)
/// Run via CyberBoss/Fix Animator V3.
/// </summary>
public class FixAnimatorV3
{
    private const string ControllerPath = "Assets/Mryotaisu/Animators/Muryotaisu.controller";

    [MenuItem("CyberBoss/Fix Animator V3")]
    public static void Execute()
    {
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) { Debug.LogError($"[FixAnimatorV3] Controller not found."); return; }

        AnimatorStateMachine sm = controller.layers[0].stateMachine;

        // --- 1. Delete Dash state entirely ---
        AnimatorState dashState = GetState(sm, "Dash");
        if (dashState != null)
        {
            sm.RemoveState(dashState);
            Debug.Log("[FixAnimatorV3] Removed Dash state.");
        }
        else
        {
            Debug.Log("[FixAnimatorV3] Dash state not found (already removed?).");
        }

        // --- 2. Remove StrikeTrigger transitions from all states → Strike ---
        AnimatorState strikeState = GetState(sm, "Strike");
        if (strikeState != null)
        {
            foreach (ChildAnimatorState cs in sm.states)
            {
                RemoveTransitionsWithTrigger(cs.state, strikeState, "StrikeTrigger");
            }
        }

        // --- 3. Speed up Parry state ---
        AnimatorState parryState = GetState(sm, "Parry");
        if (parryState != null)
        {
            parryState.speed = 4.0f; // 2s clip at 4× → 0.5s
            Debug.Log("[FixAnimatorV3] Parry speed set to 4.0.");
        }

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[FixAnimatorV3] Done.");
    }

    private static AnimatorState GetState(AnimatorStateMachine sm, string name)
    {
        foreach (ChildAnimatorState cs in sm.states)
            if (cs.state.name == name) return cs.state;
        return null;
    }

    private static void RemoveTransitionsWithTrigger(AnimatorState from, AnimatorState to, string trigger)
    {
        var toRemove = new List<AnimatorStateTransition>();
        foreach (AnimatorStateTransition t in from.transitions)
        {
            if (t.destinationState != to) continue;
            foreach (var c in t.conditions)
            {
                if (c.parameter == trigger) { toRemove.Add(t); break; }
            }
        }
        foreach (var t in toRemove)
        {
            from.RemoveTransition(t);
            Debug.Log($"[FixAnimatorV3] Removed {from.name} → {to.name} ({trigger})");
        }
    }
}

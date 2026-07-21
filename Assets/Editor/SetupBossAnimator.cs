using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Editor utility — boss animation wiring.
///
/// Creates Assets/Animators/BossAnimator.controller with eight skill states
/// and assigns it to the boss Animator component.  Run AFTER
/// CyberBoss/Setup Boss so the Boss-tagged object exists in the scene.
///
/// Prerequisite: CyberSoldier.fbx must be configured as Humanoid in its Rig
/// import settings (Inspector > Rig > Animation Type > Humanoid > Apply).
/// If it is left as Generic or Legacy the RPG Character clips will not
/// retarget onto the CyberSoldier skeleton.
///
/// State names are declared as internal constants so BossChargeSkill.cs can
/// reference them via the same literal strings in its CrossFade calls.
/// Any rename requires updating both files.
/// </summary>
public static class SetupBossAnimator
{
    private const string ControllerFolder = "Assets/Animators";
    private const string ControllerPath   = "Assets/Animators/BossAnimator.controller";
    private const string AnimsFolder      =
        "Assets/ExplosiveLLC/RPG Character Mecanim Animation Pack FREE/Animations/Unarmed/";

    // State names — must match the literal strings in BossChargeSkill.cs CrossFade calls.
    internal const string ChargeWindupState   = "ChargeWindup";
    internal const string ChargeRunState      = "ChargeRun";
    internal const string BurstFireState      = "BurstFire";
    internal const string AoeSlamState        = "AoeSlam";
    internal const string ShieldActiveState   = "ShieldActive";
    internal const string TeleportStrikeState = "TeleportStrike";
    internal const string StaggerState        = "Stagger";
    internal const string DeadState           = "Dead";

    [MenuItem("CyberBoss/Setup Boss Animator")]
    public static void Run()
    {
        if (!AssetDatabase.IsValidFolder(ControllerFolder))
            AssetDatabase.CreateFolder("Assets", "Animators");

        // Delete then recreate so re-running is fully idempotent.
        AssetDatabase.DeleteAsset(ControllerPath);
        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        var rootSM     = controller.layers[0].stateMachine;

        AddTriggers(controller);

        // loop:true  — self-transition enforces looping regardless of clip import settings.
        // loop:false — clip plays once and freezes on the last frame.
        AnimatorState chargeWindup   = CreateState(rootSM, ChargeWindupState,   "RPG-Character@Unarmed-Idle-Static.FBX",  "Unarmed-Idle-Static",  loop: true);
        AnimatorState chargeRun      = CreateState(rootSM, ChargeRunState,      "RPG-Character@Unarmed-Run-Forward.FBX", "Unarmed-Run-Forward",  loop: true);
        AnimatorState burstFire      = CreateState(rootSM, BurstFireState,      "RPG-Character@Unarmed-Attack-R1.FBX",   "Unarmed-Attack-R1",    loop: false);
        AnimatorState aoeSlam        = CreateState(rootSM, AoeSlamState,        "RPG-Character@Unarmed-Attack-R3.FBX",   "Unarmed-Attack-R3",    loop: false);
        AnimatorState shieldActive   = CreateState(rootSM, ShieldActiveState,   "RPG-Character@Unarmed-Idle-Static.FBX", "Unarmed-Idle-Static",  loop: true);
        AnimatorState teleportStrike = CreateState(rootSM, TeleportStrikeState, "RPG-Character@Unarmed-Attack-L1.FBX",  "Unarmed-Attack-L1",    loop: false);
        AnimatorState stagger        = CreateState(rootSM, StaggerState,        "RPG-Character@Unarmed-Stunned.FBX",    "Unarmed-Stunned",      loop: true);
        AnimatorState dead           = CreateState(rootSM, DeadState,           "RPG-Character@Unarmed-Knockdown1.FBX", "Unarmed-Knockdown1",   loop: false);

        // Boss starts in ChargeWindup (Idle-Static clip) — neutral standing pose.
        rootSM.defaultState = chargeWindup;

        // All skill triggers use AnyState transitions so they fire from whatever
        // pose the boss is currently in without needing an Idle hub state.
        AddAnyStateTrigger(rootSM, chargeWindup,   "ChargeTrigger");
        AddAnyStateTrigger(rootSM, burstFire,      "BurstTrigger");
        AddAnyStateTrigger(rootSM, aoeSlam,        "SlamTrigger");
        AddAnyStateTrigger(rootSM, shieldActive,   "ShieldTrigger");
        AddAnyStateTrigger(rootSM, teleportStrike, "TeleportTrigger");

        // Stagger and Dead must not re-enter themselves (canTransitionToSelf=false)
        // so a second hit while staggered does not reset the stagger animation.
        AddAnyStateTrigger(rootSM, stagger, "StaggerTrigger", canTransitionToSelf: false);
        AddAnyStateTrigger(rootSM, dead,    "DeathTrigger",   canTransitionToSelf: false);

        // ChargeWindup → ChargeRun is driven by Animator.CrossFade in BossChargeSkill
        // after the windup duration elapses — no trigger transition needed here.

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        AssignToBoss();

        Debug.Log("[SetupBossAnimator] BossAnimator.controller created and assigned.");
    }

    // ------------------------------------------------------------------
    // Helpers
    // ------------------------------------------------------------------

    private static void AddTriggers(AnimatorController ac)
    {
        string[] names =
        {
            "ChargeTrigger", "BurstTrigger", "SlamTrigger",
            "ShieldTrigger", "TeleportTrigger", "StaggerTrigger", "DeathTrigger"
        };
        foreach (string name in names)
            ac.AddParameter(name, AnimatorControllerParameterType.Trigger);
    }

    private static AnimatorState CreateState(
        AnimatorStateMachine sm,
        string               stateName,
        string               fbxFilename,
        string               clipName,
        bool                 loop)
    {
        AnimatorState state = sm.AddState(stateName);
        AnimationClip clip  = LoadClip(fbxFilename, clipName);

        if (clip != null)
            state.motion = clip;
        else
            Debug.LogWarning(
                $"[SetupBossAnimator] Clip '{clipName}' not found — " +
                $"'{stateName}' will have no motion.");

        if (loop)
        {
            // Self-transition at clip end creates a reliable manual loop.
            AnimatorStateTransition selfLoop = state.AddTransition(state);
            selfLoop.hasExitTime      = true;
            selfLoop.exitTime         = 1.0f;
            selfLoop.duration         = 0f;
            selfLoop.hasFixedDuration = false;
        }

        return state;
    }

    private static void AddAnyStateTrigger(
        AnimatorStateMachine sm,
        AnimatorState        dest,
        string               triggerName,
        bool                 canTransitionToSelf = true)
    {
        AnimatorStateTransition t = sm.AddAnyStateTransition(dest);
        t.AddCondition(AnimatorConditionMode.If, 0f, triggerName);
        t.hasExitTime         = false;
        t.duration            = 0.1f;
        t.canTransitionToSelf = canTransitionToSelf;
    }

    /// <summary>Loads a named clip from a single-clip FBX asset.</summary>
    private static AnimationClip LoadClip(string fbxFilename, string clipName)
    {
        string path = AnimsFolder + fbxFilename;
        foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (obj is AnimationClip clip && clip.name == clipName)
                return clip;
        }
        Debug.LogWarning($"[SetupBossAnimator] Clip '{clipName}' not found in '{path}'.");
        return null;
    }

    private static void AssignToBoss()
    {
        var controller = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        if (controller == null)
        {
            Debug.LogError("[SetupBossAnimator] Controller asset missing after creation.");
            return;
        }

        var bossGo = GameObject.FindWithTag("Boss");
        if (bossGo == null)
        {
            Debug.LogWarning(
                "[SetupBossAnimator] No 'Boss' tagged object found. " +
                "Run CyberBoss/Setup Boss first, then re-run Setup Boss Animator.");
            return;
        }

        var animator = bossGo.GetComponent<Animator>();
        if (animator == null)
            animator = bossGo.AddComponent<Animator>();

        animator.runtimeAnimatorController = controller;
        EditorUtility.SetDirty(bossGo);
        Debug.Log($"[SetupBossAnimator] Controller assigned to '{bossGo.name}'.");
    }
}

using UnityEditor;
using UnityEngine;

/// <summary>
/// Automatically runs SetupPart10Polish.Execute() once after the first domain
/// reload that includes the polish scripts (ScreenShakeManager,
/// HitFreezeManager, ScreenShakeConfig).
///
/// Uses SessionState so this fires once per editor session. Because
/// SetupPart10Polish is fully idempotent (all steps guard against duplicates),
/// re-runs on subsequent sessions are harmless but unnecessary — the flag
/// prevents redundant logs and scene dirty-marks after the first run.
/// </summary>
[InitializeOnLoad]
public static class AutoRunSetupPart10
{
    private const string RanKey = "Part10PolishAutoRan";

    static AutoRunSetupPart10()
    {
        if (SessionState.GetBool(RanKey, false)) return;
        SessionState.SetBool(RanKey, true);

        // Defer one frame so all [InitializeOnLoad] constructors finish and
        // the scene is fully loaded before we modify it.
        EditorApplication.delayCall += RunSetup;
    }

    private static void RunSetup()
    {
        Debug.Log("[AutoRunSetupPart10] Triggering polish setup automatically...");
        SetupPart10Polish.Execute();
    }
}

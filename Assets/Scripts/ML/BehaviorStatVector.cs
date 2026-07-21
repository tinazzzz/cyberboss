using System.Runtime.InteropServices;

/// <summary>
/// Flat 9-float observation vector describing the player's combat style.
///
/// Field order is LOCKED — BossRLAgent and the ONNX model read by positional index.
/// All fields are normalized to [0, 1] before being stored.
///
/// Index mapping for ONNX/Sentis consumers (BossRLAgent):
///   [0] DodgeDirectionBias     — 0=pure left, 0.5=neutral, 1=pure right
///                                rightDashCount / (leftDashCount + rightDashCount)
///                                Forward/back dashes excluded; 0.5 when no data.
///   [1] SkillUsageFrequency0   — Dash uses/min ÷ maxUsesPerMin[0], clamped 0–1
///   [2] SkillUsageFrequency1   — Parry uses/min ÷ maxUsesPerMin[1], clamped 0–1
///   [3] SkillUsageFrequency2   — Ranged Blast uses/min ÷ maxUsesPerMin[2], clamped 0–1
///   [4] SkillUsageFrequency3   — Burst Strike uses/min ÷ maxUsesPerMin[3], clamped 0–1
///   [5] SkillUsageFrequency4   — Barrier uses/min ÷ maxUsesPerMin[4], clamped 0–1
///   [6] AggressionScore        — 0.6 × (timeInCloseRange ÷ totalElapsedTime)
///                                + 0.4 × (normalAttacksPerMinute ÷ MaxNormalAttacksPerMinute);
///                                0 when no data
///   [7] AverageEngagementRange — meanDistanceToBoss ÷ maxArenaRange; 0.5 when no data
///   [8] PositionalBias         — 1 − (meanDistFromCenter ÷ arenaRadius); 1=center, 0=edge
///                                0.5 when no data
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct BehaviorStatVector
{
    public float DodgeDirectionBias;
    public float SkillUsageFrequency0;
    public float SkillUsageFrequency1;
    public float SkillUsageFrequency2;
    public float SkillUsageFrequency3;
    public float SkillUsageFrequency4;
    public float AggressionScore;
    public float AverageEngagementRange;
    public float PositionalBias;
}

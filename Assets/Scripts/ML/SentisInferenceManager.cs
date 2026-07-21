
using UnityEngine;

/// <summary>
/// Loads BossBrain.onnx via Unity Sentis and runs synchronous CPU inference.
///
/// Assign the .onnx ModelAsset in the Inspector and wire this component to
/// BossRLAgent._sentisManager. Enable BossRLAgent._useRLPolicy to activate RL.
///
/// WebGL requirement: BackendType.CPU must be used — the GPU/compute backend is
/// not supported in WebGL. Inference is synchronous; call from the boss decision
/// tick (BossRLAgent.SelectSkillIndex), never from Update().
///
/// Tensor lifecycle: the input tensor is allocated once in Awake() and reused
/// across all inference calls to avoid per-decision heap allocation.
/// The output tensor is owned by the worker — never cache or dispose it externally.
/// </summary>
public class SentisInferenceManager : MonoBehaviour
{
    [SerializeField] private Unity.InferenceEngine.ModelAsset _onnxModel;

    private Unity.InferenceEngine.Model         _runtimeModel;
    private Unity.InferenceEngine.Worker        _worker;
    private Unity.InferenceEngine.Tensor<float> _inputTensor;

    // ShieldPhase excluded — as of the #5 rework it's a reactive skill
    // (BossShieldReactiveTrigger), never part of the ONNX model's action space.
    private static readonly string[] _skillNames =
        { "Charge", "ProjectileBurst", "AoESlam", "TeleportStrike" };

    // Must stay identical to PARITY_TEST_VECTORS / PARITY_TEST_VECTOR_NAMES in
    // Assets/ML/Training/cyberboss_env.py — RunParityCheck() and
    // check_onnx_parity() are meant to be diffed against each other by hand.
    private static readonly string[] _parityVectorNames =
    {
        "neutral", "pure_left_dash", "pure_right_dash", "aggressive_max",
        "ranged_max", "barrier_passive_max", "parry_heavy_max", "dash_heavy_max",
    };

    private static readonly float[][] _parityVectors =
    {
        new[] { 0.5f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.5f, 0.5f },
        new[] { 0.0f, 0.9f, 0.0f, 0.0f, 0.0f, 0.0f, 0.1f, 0.3f, 0.5f },
        new[] { 1.0f, 0.9f, 0.0f, 0.0f, 0.0f, 0.0f, 0.1f, 0.3f, 0.5f },
        new[] { 0.5f, 0.1f, 0.05f, 0.1f, 0.9f, 0.05f, 0.9f, 0.1f, 0.7f },
        new[] { 0.5f, 0.1f, 0.05f, 0.9f, 0.05f, 0.05f, 0.05f, 0.9f, 0.4f },
        new[] { 0.5f, 0.05f, 0.05f, 0.2f, 0.05f, 0.9f, 0.05f, 0.4f, 0.9f },
        new[] { 0.5f, 0.2f, 0.9f, 0.2f, 0.1f, 0.1f, 0.3f, 0.4f, 0.6f },
        new[] { 0.9f, 0.9f, 0.1f, 0.2f, 0.2f, 0.05f, 0.4f, 0.4f, 0.5f },
    };

    // ------------------------------------------------------------------
    // Unity lifecycle
    // ------------------------------------------------------------------

    private void Awake()
    {
        if (_onnxModel == null)
        {
            Debug.LogError(
                $"[SentisInferenceManager] ModelAsset not assigned on '{name}'. " +
                "Drag Assets/ML/Models/BossBrain.onnx onto this field in the Inspector.");
            return;
        }

        _runtimeModel = Unity.InferenceEngine.ModelLoader.Load(_onnxModel);
        _worker       = new Unity.InferenceEngine.Worker(_runtimeModel, Unity.InferenceEngine.BackendType.CPU);

        // Pre-allocate input tensor once — shape (1, 9) matches ONNX input "obs_0".
        // Reused across all inference calls to avoid per-decision heap allocation.
        _inputTensor = new Unity.InferenceEngine.Tensor<float>(new Unity.InferenceEngine.TensorShape(1, 9));
    }

    private void OnDestroy()
    {
        _inputTensor?.Dispose();
        _worker?.Dispose();
    }

    // ------------------------------------------------------------------
    // Inference — synchronous, CPU-only, safe for WebGL
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs a forward pass through BossBrain.onnx and returns the argmax skill index.
    ///
    /// <paramref name="obs"/> must be the 9-float vector from
    /// BossRLAgent.CollectObservations() in the exact locked field order.
    ///
    /// Returns skill index 0–3:
    ///   0=Charge, 1=ProjectileBurst, 2=AoESlam, 3=TeleportStrike.
    /// </summary>
    public int InferSkillIndex(float[] obs)
    {
        if (_worker == null)
        {
            Debug.LogError(
                $"[SentisInferenceManager] Worker is null on '{name}'. " +
                "Model failed to load — check that BossBrain.onnx is assigned.");
            return 0;
        }

        // Write observation values into the pre-allocated tensor (no heap allocation).
        for (int i = 0; i < 9; i++)
            _inputTensor[i] = obs[i];

        _worker.SetInput("obs_0", _inputTensor);
        _worker.Schedule();

        // PeekOutput returns the worker-owned output tensor, which may still be pending.
        // ReadbackAndClone() blocks until computation is complete and copies the result
        // to a new managed Tensor<float> on the CPU side — required before indexing.
        // The clone is 4 floats (16 bytes) disposed immediately; negligible at the 3s skill interval.
        var raw = _worker.PeekOutput("discrete_actions") as Unity.InferenceEngine.Tensor<float>;
        using var output = raw.ReadbackAndClone();
        return ArgmaxSkill(output);
    }

    // ------------------------------------------------------------------
    // Helper
    // ------------------------------------------------------------------

    /// <summary>
    /// Returns the index of the highest logit across the boss skill outputs.
    /// Ties broken by lowest index — matches np.argmax in the Python training env.
    /// Loop bound derived from _skillNames.Length rather than a literal, so the
    /// action-space size (4, post-#5 ShieldPhase removal) can't silently drift
    /// out of sync between this method and the name lookup table.
    /// </summary>
    private static int ArgmaxSkill(Unity.InferenceEngine.Tensor<float> logits)
    {
        int   bestIndex = 0;
        float bestLogit = logits[0];
        for (int i = 1; i < _skillNames.Length; i++)
        {
            float v = logits[i];
            if (v > bestLogit)
            {
                bestLogit = v;
                bestIndex = i;
            }
        }
        return bestIndex;
    }

    // ------------------------------------------------------------------
    // Parity check — Sentis vs. the Python-side ONNX/PyTorch comparison
    // ------------------------------------------------------------------

    /// <summary>
    /// Runs the same fixed observation vectors used by
    /// <c>check_onnx_parity()</c> in <c>cyberboss_env.py</c> and logs each
    /// resulting skill choice. Run this in Play mode (right-click the
    /// component header -> Run Parity Check), then diff the printed skill
    /// names against the Python script's console output for the same vector
    /// names. This is the only way to confirm Sentis interprets the ONNX
    /// graph the same way onnxruntime/PyTorch does — the Python check alone
    /// can't reach Sentis, since Sentis is Unity/C#-only.
    /// </summary>
    [ContextMenu("Run Parity Check")]
    private void RunParityCheck()
    {
        if (_worker == null)
        {
            Debug.LogError("[SentisInferenceManager] Cannot run parity check — " +
                "worker is not initialised. Enter Play mode first.");
            return;
        }

        Debug.Log("[SentisInferenceManager] Parity check — compare these results " +
            "against `python cyberboss_env.py --check-parity` output:");

        for (int i = 0; i < _parityVectors.Length; i++)
        {
            int skillIndex = InferSkillIndex(_parityVectors[i]);
            Debug.Log($"  {_parityVectorNames[i],-20} -> {_skillNames[skillIndex]}");
        }
    }
}

/// <summary>
/// Receives animation events baked into imported humanoid clips (e.g. Mixamo).
/// Must live on the same GameObject as the Animator — events fire on the Animator's owner.
/// </summary>
public class AnimationEventReceiver : UnityEngine.MonoBehaviour
{
    // Footstep events from run/walk animations.
    private void FootR() { }
    private void FootL() { }

    // Melee hit event from Unarmed-Attack-R1 / Unarmed-Attack-L1.
    private void Hit() { }
}

using UnityEngine;

/// <summary>
/// Tunable movement parameters for the player character.
/// </summary>
[CreateAssetMenu(fileName = "PlayerConfig", menuName = "CyberBoss/PlayerConfig")]
public class PlayerConfig : ScriptableObject
{
    [SerializeField] private float _walkSpeed     = 4.5f;
    [SerializeField] private float _runSpeed      = 8.0f;
    [SerializeField] private float _rotationSpeed = 720f;
    [SerializeField] private float _gravity       = -18f;

    public float WalkSpeed     => _walkSpeed;
    public float RunSpeed      => _runSpeed;
    public float RotationSpeed => _rotationSpeed;
    public float Gravity       => _gravity;
}

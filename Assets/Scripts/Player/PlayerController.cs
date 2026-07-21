using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Isometric player movement controller.
/// Uses CharacterController and Unity New Input System (PlayerInput SendMessages).
/// Exposes Velocity and IsMoving as the public contract for PlayerBehaviorTracker.
/// </summary>
[RequireComponent(typeof(CharacterController))]
[RequireComponent(typeof(Animator))]
public class PlayerController : MonoBehaviour
{
    [SerializeField] private PlayerConfig          _config;
    [SerializeField] private BehaviorTrackerConfig _trackerConfig;

    private CharacterController _characterController;
    private Animator _animator;

    private Vector2 _moveInput;
    private bool _isSprintHeld;
    private float _verticalVelocity;
    private Vector3 _velocity;

    private Camera _mainCamera;

    // Null-safe reference — PlayerSkills may not be present in non-combat scenes.
    private PlayerSkills _playerSkills;

    // Suppresses repeated log spam when BehaviorTrackerConfig is unassigned.
    private bool _hasWarnedTrackerNull;

    // Cached hash IDs avoid per-frame string lookups in Animator calls.
    private static readonly int SpeedHash    = Animator.StringToHash("Speed");
    private static readonly int WalkFlagHash = Animator.StringToHash("walkFlag");
    private static readonly int IdleFlagHash = Animator.StringToHash("idleFlag");

    /// Current horizontal velocity vector. Read by PlayerBehaviorTracker.
    public Vector3 Velocity => _velocity;

    /// True when the character has meaningful horizontal movement.
    public bool IsMoving => _velocity.sqrMagnitude > 0.01f;

    /// Current intended move direction from input (world space, XZ plane).
    /// Read by PlayerSkills to prime the Dash direction each frame.
    public Vector3 CurrentMoveDirection { get; private set; }

    private void Awake()
    {
        if (_config == null)
            throw new System.InvalidOperationException(
                $"[PlayerController] PlayerConfig is not assigned on '{name}'. " +
                "Run CyberBoss/Setup Player Character or assign it in the Inspector.");

        _characterController = GetComponent<CharacterController>();
        _animator            = GetComponent<Animator>();
        _mainCamera   = Camera.main;
        _playerSkills = GetComponent<PlayerSkills>();

        if (_mainCamera == null)
            throw new System.InvalidOperationException(
                "[PlayerController] Camera.main is null — ensure exactly one camera " +
                "in the scene is tagged 'MainCamera'.");
    }

    // Called by PlayerInput (SendMessages behavior) via reflection.
    // Method names must match the action names in InputSystem_Actions exactly.
    private void OnMove(InputValue value)   => _moveInput    = value.Get<Vector2>();
    private void OnSprint(InputValue value) => _isSprintHeld = value.isPressed;

    private void Update()
    {
        // Gravity must still run when movement is locked (Parry stance),
        // otherwise the CharacterController loses its grounded state.
        ApplyGravity();

        if (_playerSkills != null && _playerSkills.IsMovementLocked)
        {
            UpdateAnimator(0f); // Show idle pose during parry stance.
        }
        else
        {
            float   speed     = ComputeCurrentSpeed();
            Vector3 direction = ComputeMoveDirection();

            ApplyMovement(direction, speed);
            RotateTowardMovement(direction);
            UpdateAnimator(speed);
        }

        // Always clamp after all movement — catches both normal walk and gravity drift.
        ClampToArenaBounds();
    }

    private float ComputeCurrentSpeed()
    {
        if (_moveInput.sqrMagnitude < 0.01f) return 0f;
        return _isSprintHeld ? _config.RunSpeed : _config.WalkSpeed;
    }

    private Vector3 ComputeMoveDirection()
    {
        if (_moveInput.sqrMagnitude < 0.01f)
        {
            CurrentMoveDirection = Vector3.zero;
            return Vector3.zero;
        }
        // Derive axes from the live camera transform each frame so the correct
        // isometric directions are always read regardless of camera angle or
        // Cinemachine update ordering.
        Vector3 camForward = Vector3.ProjectOnPlane(
            _mainCamera.transform.forward, Vector3.up).normalized;
        Vector3 camRight = Vector3.ProjectOnPlane(
            _mainCamera.transform.right, Vector3.up).normalized;
        CurrentMoveDirection = (camForward * _moveInput.y + camRight * _moveInput.x).normalized;
        return CurrentMoveDirection;
    }

    private void ApplyMovement(Vector3 direction, float speed)
    {
        _velocity = direction * speed;
        _characterController.Move(_velocity * Time.deltaTime);
    }

    private void ApplyGravity()
    {
        // Keep a small negative value when grounded so isGrounded remains reliable
        // across frames — setting to zero causes CharacterController to float briefly.
        if (_characterController.isGrounded && _verticalVelocity < 0f)
            _verticalVelocity = -2f;

        _verticalVelocity += _config.Gravity * Time.deltaTime;
        _characterController.Move(Vector3.up * _verticalVelocity * Time.deltaTime);
    }

    private void RotateTowardMovement(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.01f) return;

        Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
        transform.rotation = Quaternion.RotateTowards(
            transform.rotation,
            targetRotation,
            _config.RotationSpeed * Time.deltaTime);
    }

    /// Warp the player back inside the square arena boundary.
    /// Disables the CharacterController during the warp — setting transform.position
    /// directly while the CC is enabled causes it to fight the move next frame.
    private void ClampToArenaBounds()
    {
        if (_trackerConfig == null)
        {
            if (!_hasWarnedTrackerNull)
            {
                Debug.LogWarning(
                    "[PlayerController] BehaviorTrackerConfig not assigned — " +
                    "arena bounds clamping disabled. Assign it in the Inspector.");
                _hasWarnedTrackerNull = true;
            }
            return;
        }

        Vector3 center     = _trackerConfig.ArenaCenterPosition;
        float   halfExtent = _trackerConfig.ArenaRadius;
        Vector3 pos        = transform.position;

        float clampedX = Mathf.Clamp(pos.x, center.x - halfExtent, center.x + halfExtent);
        float clampedZ = Mathf.Clamp(pos.z, center.z - halfExtent, center.z + halfExtent);

        if (clampedX != pos.x || clampedZ != pos.z)
        {
            _characterController.enabled = false;
            transform.position           = new Vector3(clampedX, pos.y, clampedZ);
            _characterController.enabled = true;
        }
    }

    private void UpdateAnimator(float speed)
    {
        bool isMoving = speed > 0f;

        // Speed float: 0 = idle, 0.5 = walk, 1.0 = run.
        // The existing Muryotaisu animator uses bool transitions (walkFlag/idleFlag);
        // Speed is set here for future blend trees and for PlayerBehaviorTracker reads.
        float speedParam = isMoving ? (_isSprintHeld ? 1f : 0.5f) : 0f;

        _animator.SetFloat(SpeedHash,    speedParam, 0.1f, Time.deltaTime);
        _animator.SetBool(WalkFlagHash,  isMoving);
        _animator.SetBool(IdleFlagHash,  !isMoving);
    }
}

using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(CharacterController))]
public class StraightLineLockLeftStick : MonoBehaviour
{
    [Header("Input (New Input System)")]
    [Tooltip("LEFT-hand Move (primary2DAxis).")]
    public InputActionProperty leftMove;
    [Tooltip("RIGHT-hand A / primaryButton.")]
    public InputActionProperty rightAButton;

    [Header("References")]
    [Tooltip("XR Main Camera (defines straight-line direction).")]
    public Transform forwardSource;
    public CharacterController characterController;

    [Header("Movement")]
    public float moveSpeed = 2.8f;
    public float gravity = -9.81f;
    public float stepDown = 0.25f;

    [Header("Lock Behaviour")]
    [Tooltip("Hold A to lock. If true, tap A to toggle lock on/off.")]
    public bool useToggleMode = false;
    [Range(0f, 0.5f)] public float deadzone = 0.2f;
    public bool alignYawOnLock = false;

    // state
    private bool _isLocked;
    private bool _prevA;
    private Vector3 _lockDir;
    private float _vy;

    void Awake()
    {
        if (!characterController) characterController = GetComponent<CharacterController>();
        if (!forwardSource)
        {
            var cam = Camera.main;
            if (cam) forwardSource = cam.transform;
        }
    }
    void OnEnable()  { leftMove.action?.Enable(); rightAButton.action?.Enable(); }
    void OnDisable() { leftMove.action?.Disable(); rightAButton.action?.Disable(); }

    void Update()
    {
        if (!characterController || !forwardSource) return;

        Vector2 stick = leftMove.action != null ? leftMove.action.ReadValue<Vector2>() : Vector2.zero;
        bool aPressed = rightAButton.action != null && rightAButton.action.ReadValue<float>() > 0.5f;

        if (useToggleMode)
        {
            if (aPressed && !_prevA) { _isLocked = !_isLocked; if (_isLocked) CaptureLockDir(); }
        }
        else
        {
            if (aPressed && !_prevA) { _isLocked = true; CaptureLockDir(); }
            else if (!aPressed)      { _isLocked = false; }
        }
        _prevA = aPressed;

        if (stick.magnitude < deadzone) stick = Vector2.zero;

        Vector3 horiz;
        if (_isLocked)
        {
            // STRICT: only forward/back along cached lock direction; no lateral at all
            horiz = _lockDir * (stick.y * moveSpeed);
        }
        else
        {
            Vector3 fwd = forwardSource.forward; fwd.y = 0; fwd.Normalize();
            Vector3 right = forwardSource.right; right.y = 0; right.Normalize();
            horiz = (fwd * stick.y + right * stick.x) * moveSpeed;
        }

        if (characterController.isGrounded && _vy < 0) _vy = -2f;
        _vy += gravity * Time.deltaTime;

        Vector3 motion = (horiz + Vector3.up * _vy) * Time.deltaTime;
        if (characterController.isGrounded && _vy <= 0) motion += Vector3.down * stepDown * Time.deltaTime;

        characterController.Move(motion);
    }

    void CaptureLockDir()
    {
        _lockDir = forwardSource.forward; _lockDir.y = 0f;
        if (_lockDir.sqrMagnitude < 1e-6f) _lockDir = Vector3.forward;
        _lockDir.Normalize();

        if (alignYawOnLock)
        {
            Quaternion yaw = Quaternion.LookRotation(_lockDir, Vector3.up);
            Vector3 e = yaw.eulerAngles;
            transform.rotation = Quaternion.Euler(0f, e.y, 0f);
        }
    }
}

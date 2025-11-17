using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit;

public class NorthSouthOnlyTeleport : MonoBehaviour
{
    [Header("References")]
    public TeleportationProvider teleportationProvider; // Put this on XR Origin
    public Transform headTransform;                     // Main Camera

    [Header("Input (XRI Default Input Actions)")]
    public InputActionProperty joystickClick;   // RightHand / Teleport Mode Activate
    public InputActionProperty trigger;         // RightHand / Select
    public InputActionProperty joystickAxis;    // RightHand / Move (2D axis)

    [Header("Settings")]
    public bool useTriggerToConfirm = true;
    public float forwardBackThreshold = 0.2f;   // how far stick must be pushed
    public float distance = 3f;                 // how far forward/back to teleport
    public float upOffset = 0f;

    private bool teleportModeActive;

    void OnEnable()
    {
        joystickClick.action?.Enable();
        trigger.action?.Enable();
        joystickAxis.action?.Enable();
    }

    void OnDisable()
    {
        joystickClick.action?.Disable();
        trigger.action?.Disable();
        joystickAxis.action?.Disable();
    }

    void Update()
    {
        if (!teleportationProvider || !headTransform) return;

        if (joystickClick.action.WasPressedThisFrame())
            teleportModeActive = true;

        if (!teleportModeActive) return;

        if (useTriggerToConfirm)
        {
            if (trigger.action.WasPressedThisFrame())
                TryTeleport();

            if (joystickClick.action.WasReleasedThisFrame() && !trigger.action.IsPressed())
                CancelTeleport();
        }
        else
        {
            if (joystickClick.action.WasReleasedThisFrame())
                TryTeleport();
        }
    }

    bool IsNorthSouth(out int direction)
    {
        direction = 0;
        if (joystickAxis.action == null) return false;
        Vector2 a = joystickAxis.action.ReadValue<Vector2>();

        if (Mathf.Abs(a.y) >= forwardBackThreshold)
        {
            direction = a.y > 0 ? 1 : -1; // forward or back
            return true;
        }
        return false;
    }

    void TryTeleport()
    {
        if (!IsNorthSouth(out int dir)) { CancelTeleport(); return; }

        // Take camera forward vector, flatten on ground plane
        Vector3 forward = Vector3.ProjectOnPlane(headTransform.forward, Vector3.up).normalized;

        // Destination: forward or backward
        Vector3 destination = headTransform.position + forward * (distance * dir);

        var req = new TeleportRequest
        {
            destinationPosition = destination + Vector3.up * upOffset,
            matchOrientation = MatchOrientation.WorldSpaceUp
        };

        teleportationProvider.QueueTeleportRequest(req);
        CancelTeleport();
    }

    void CancelTeleport()
    {
        teleportModeActive = false;
    }
}

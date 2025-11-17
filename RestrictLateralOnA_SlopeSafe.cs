using UnityEngine;
using UnityEngine.XR;

/// While A (right controller primary button) is held:
///   - Movement is constrained to a straight line projected onto the current ground plane
///   - Sideways (off-axis) velocity IN THE PLANE is removed/suppressed
/// Normal (perpendicular-to-ground) velocity is preserved so the box follows slopes.
[RequireComponent(typeof(Rigidbody))]
public class RestrictLateralOnA_SlopeSafe : MonoBehaviour
{
    [Header("Direction Source")]
    [Tooltip("Drag your RightHand controller (recommended) or Main Camera.")]
    public Transform forwardSource;

    [Header("Optional Lock")]
    public AutoLockToBox lockManager;
    public bool requireLock = false;

    [Header("Tuning")]
    [Tooltip("How fast to blend planar velocity toward the axis (6–20). Higher = tighter.")]
    public float clampLerp = 14f;
    [Tooltip("Extra acceleration to fight lateral (off-axis) velocity in the plane (20–120).")]
    public float lateralBrake = 80f;
    [Tooltip("Ignore clamping at very low speeds to prevent jitter.")]
    public float minSpeedToClamp = 0.05f;
    [Tooltip("Ray length used to detect the ground normal under the box.")]
    public float groundRayLength = 2f;

    private Rigidbody rb;
    private InputDevice rightHand;
    private bool aHeld;

    // Axis captured on A-press (world space); we will re-project it to the current ground plane each frame.
    private Vector3 capturedAxis;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        // Helps smoothness
        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
    }

    void Update()
    {
        if (requireLock && lockManager && !lockManager.IsLocked) { aHeld = false; return; }

        rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool aPressed);

        if (!aHeld && aPressed)
        {
            // Capture a forward direction (flattened rough idea; final axis is reprojected to ground each frame)
            Vector3 fwd = forwardSource ? forwardSource.forward
                                        : (Camera.main ? Camera.main.transform.forward : Vector3.forward);
            fwd.y = 0f;
            capturedAxis = (fwd.sqrMagnitude > 1e-6f ? fwd.normalized : Vector3.forward);
            aHeld = true;
        }
        else if (aHeld && !aPressed)
        {
            aHeld = false;
        }
    }

    void FixedUpdate()
    {
        if (!aHeld) return;
        if (requireLock && lockManager && !lockManager.IsLocked) return;

        // 1) Get ground normal (fallback to up if no hit)
        Vector3 groundNormal = Vector3.up;
        if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out var hit, groundRayLength))
            groundNormal = hit.normal;

        // 2) Build the current axis ON THE GROUND PLANE
        Vector3 axisOnPlane = Vector3.ProjectOnPlane(capturedAxis, groundNormal);
        if (axisOnPlane.sqrMagnitude < 1e-6f) axisOnPlane = Vector3.ProjectOnPlane(Vector3.forward, groundNormal);
        axisOnPlane.Normalize();

        // 3) Decompose velocity: normal vs planar; within plane: along-axis vs lateral
        Vector3 v = rb.velocity;
        float speed = v.magnitude;
        if (speed < minSpeedToClamp) return;

        Vector3 vNormal = Vector3.Project(v, groundNormal);
        Vector3 vPlanar = v - vNormal;

        Vector3 vAxis   = Vector3.Project(vPlanar, axisOnPlane);
        Vector3 vSide   = vPlanar - vAxis;

        // 4) Smoothly blend planar velocity toward axis (keeps normal untouched)
        Vector3 vPlanarNew = Vector3.Lerp(vPlanar, vAxis, Time.fixedDeltaTime * Mathf.Max(0f, clampLerp));
        rb.velocity = vNormal + vPlanarNew;

        // 5) Extra lateral brake to kill any residual sideways drift IN THE PLANE
        if (lateralBrake > 0f && vSide.sqrMagnitude > 1e-6f)
            rb.AddForce(-vSide * lateralBrake, ForceMode.Acceleration);
    }
}

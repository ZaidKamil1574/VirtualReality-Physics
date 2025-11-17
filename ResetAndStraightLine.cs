using UnityEngine;
using UnityEngine.XR;

[DefaultExecutionOrder(20)]
[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class ResetAndStraightLine : MonoBehaviour
{
    public enum AxisMode
    {
        CaptureForwardProjectedOnGround, // safest default
        SlopeDownhill,
        WorldX, WorldZ,
        CustomTransformForward,
        CaptureBoxForwardOnPress
    }
    public enum StraightMode { ToggleOnPress, WhileHeld }

    [Header("References")]
    public Rigidbody rb;
    public Transform resetToTransform;          // optional if you want a precise yaw after ground align
    public Transform straightLineReference;     // optional

    [Header("Behavior")]
    public StraightMode straightMode = StraightMode.WhileHeld;
    public AxisMode axisMode = AxisMode.CaptureForwardProjectedOnGround;

    [Tooltip("Reset rotation ONE TIME at activation. If Align To Ground is ON, up aligns to ground normal.")]
    public bool resetRotationOnActivate = true;

    [Tooltip("When resetting, align the box to the slope (up = ground normal, forward = projected).")]
    public bool alignToGroundOnReset = true;

    [Tooltip("Keep motion constrained to the line while active.")]
    public bool correctPositionDrift = true;
    [Range(0f, 1f)] public float driftSnapStrength = 0.35f;
    public bool lockLineOriginOnActivate = true;

    [Header("Ground Detection")]
    public LayerMask groundLayers = ~0;
    public float groundCheckDistance = 2.0f;   // a bit larger for reliability
    public float groundCheckRadius = 0.2f;

    [Header("Input (Right Controller A)")]
    public XRNode controllerNode = XRNode.RightHand;
    public float edgeDeadzone = 0.02f;

    // state
    Vector3 lineOrigin;
    Vector3 lineDirection;       // normalized
    bool straightActive = false;
    bool prevPrimaryButton = false;
    float lastEdgeTime = -999f;

    // ground/contact state
    bool hasContactNormal = false;
    Vector3 contactNormalAvg = Vector3.up;
    Vector3 lastGoodGroundNormal = Vector3.up;

    // ensure reset happens only once per activation
    bool resetDoneThisActivation = false;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        rb.interpolation = RigidbodyInterpolation.Interpolate;
    }

    // --- CONTACT-BASED NORMAL (most reliable on slopes)
    void OnCollisionStay(Collision c)
    {
        if (((1 << c.gameObject.layer) & groundLayers) == 0) return;

        Vector3 sum = Vector3.zero;
        int count = 0;
        foreach (var cp in c.contacts)
        {
            sum += cp.normal;
            count++;
        }
        if (count > 0)
        {
            contactNormalAvg = (sum / Mathf.Max(1, count)).normalized;
            hasContactNormal = true;
            lastGoodGroundNormal = contactNormalAvg;
        }
    }
    void OnCollisionExit(Collision c)
    {
        if (((1 << c.gameObject.layer) & groundLayers) == 0) return;
        hasContactNormal = false; // will fall back to spherecast/raycast
    }

    void Update()
    {
        var device = InputDevices.GetDeviceAtXRNode(controllerNode);
        bool primaryDown = false;
        device.TryGetFeatureValue(CommonUsages.primaryButton, out primaryDown);

        bool pressedThisFrame = primaryDown && !prevPrimaryButton && (Time.time - lastEdgeTime > edgeDeadzone);
        if (pressedThisFrame)
        {
            lastEdgeTime = Time.time;
            if (straightMode == StraightMode.ToggleOnPress)
                SetStraightActive(!straightActive);
            else
                SetStraightActive(true);
        }

        if (straightMode == StraightMode.WhileHeld && straightActive && !primaryDown)
            SetStraightActive(false);

        prevPrimaryButton = primaryDown;
    }

    void FixedUpdate()
    {
        if (!straightActive) return;

        // Always have a valid axis
        if (lineDirection.sqrMagnitude < 1e-6f) lineDirection = Vector3.forward;

        // 1) Remove any velocity component into/out of the surface (prevents “air driving”)
        Vector3 groundN = GetGroundNormal();
        rb.velocity -= Vector3.Project(rb.velocity, groundN);

        // 2) Project remaining velocity onto the line
        rb.velocity = Vector3.Project(rb.velocity, lineDirection);

        // 3) Optional: softly pull position back to the line
        if (correctPositionDrift)
        {
            Vector3 origin = lockLineOriginOnActivate ? lineOrigin : GetLiveOrigin();
            Vector3 toBox = rb.position - origin;
            Vector3 onLinePos = origin + Vector3.Project(toBox, lineDirection);
            rb.MovePosition(Vector3.Lerp(rb.position, onLinePos, driftSnapStrength));
        }
    }

    void SetStraightActive(bool newActive)
    {
        if (newActive == straightActive) return;
        straightActive = newActive;

        if (straightActive)
        {
            // ----- One-time rotation reset at activation -----
            if (resetRotationOnActivate && !resetDoneThisActivation)
            {
                Vector3 n = GetGroundNormal();

                if (alignToGroundOnReset)
                {
                    // forward projected onto slope
                    Vector3 fwd = transform.forward;
                    if (resetToTransform) fwd = resetToTransform.forward;

                    Vector3 projectedFwd = Vector3.ProjectOnPlane(fwd, n);
                    if (projectedFwd.sqrMagnitude < 1e-6f) projectedFwd = Vector3.ProjectOnPlane(Vector3.forward, n);
                    Quaternion target = Quaternion.LookRotation(projectedFwd.normalized, n);
                    rb.MoveRotation(target);
                }
                else
                {
                    // plain upright or custom
                    rb.MoveRotation(resetToTransform ? resetToTransform.rotation : Quaternion.identity);
                }
                rb.angularVelocity = Vector3.zero;
                resetDoneThisActivation = true;
            }

            // ----- Compute line direction ON the slope plane -----
            lineDirection = ComputeAxisOnSlope().normalized;
            if (lineDirection.sqrMagnitude < 1e-6f) lineDirection = Vector3.ProjectOnPlane(Vector3.forward, GetGroundNormal()).normalized;

            // capture origin if requested
            lineOrigin = GetLiveOrigin();
        }
        else
        {
            resetDoneThisActivation = false;
        }
    }

    Vector3 ComputeAxisOnSlope()
    {
        Vector3 n = GetGroundNormal();

        switch (axisMode)
        {
            case AxisMode.CaptureForwardProjectedOnGround:
            {
                Vector3 projected = Vector3.ProjectOnPlane(transform.forward, n);
                return projected.sqrMagnitude > 1e-6f ? projected : Vector3.ProjectOnPlane(Vector3.forward, n);
            }
            case AxisMode.SlopeDownhill:
            {
                Vector3 downhill = Vector3.ProjectOnPlane(Physics.gravity, n);
                return downhill.sqrMagnitude > 1e-6f ? downhill : Vector3.ProjectOnPlane(Vector3.forward, n);
            }
            case AxisMode.WorldX: return Vector3.ProjectOnPlane(Vector3.right, n);
            case AxisMode.WorldZ: return Vector3.ProjectOnPlane(Vector3.forward, n);
            case AxisMode.CustomTransformForward:
                return straightLineReference ? Vector3.ProjectOnPlane(straightLineReference.forward, n) : Vector3.ProjectOnPlane(Vector3.forward, n);
            case AxisMode.CaptureBoxForwardOnPress:
            default:
                return Vector3.ProjectOnPlane(transform.forward, n);
        }
    }

    Vector3 GetLiveOrigin()
    {
        return straightLineReference ? straightLineReference.position : transform.position;
    }

    Vector3 GetGroundNormal()
    {
        if (hasContactNormal) return contactNormalAvg; // best source

        // fallback spherecast
        RaycastHit hit;
        Vector3 origin = rb.worldCenterOfMass;
        if (Physics.SphereCast(origin, groundCheckRadius, Vector3.down, out hit, groundCheckDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            lastGoodGroundNormal = hit.normal.normalized;
            return lastGoodGroundNormal;
        }
        if (Physics.Raycast(origin, Vector3.down, out hit, groundCheckDistance, groundLayers, QueryTriggerInteraction.Ignore))
        {
            lastGoodGroundNormal = hit.normal.normalized;
            return lastGoodGroundNormal;
        }
        return lastGoodGroundNormal; // fall back to last known (defaults to up)
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Vector3 origin = Application.isPlaying && lockLineOriginOnActivate ? lineOrigin : GetLiveOrigin();
        Vector3 dir = Application.isPlaying ? lineDirection : Vector3.forward;
        Gizmos.DrawLine(origin - dir * 4f, origin + dir * 4f);
        Gizmos.DrawSphere(origin, 0.03f);

        // ground probe viz
        if (rb)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawWireSphere(rb.worldCenterOfMass + Vector3.down * groundCheckDistance, groundCheckRadius);
        }
    }
#endif
}

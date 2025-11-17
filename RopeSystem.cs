// RopeSystem.cs
using UnityEngine;
using UnityEngine.XR;

[RequireComponent(typeof(LineRenderer))]
public class RopeSystem : MonoBehaviour
{
    [Header("Scene References")]
    [Tooltip("Left-hand/controller transform (rope starts here).")]
    public Transform leftHandTransform;
    [Tooltip("Optional fixed target (skip raycast). Leave null to attach by aiming.")]
    public Rigidbody targetBoxRb;
    [Tooltip("Optional: if using a fixed target, you can give its collider directly.")]
    public Collider targetBoxCollider;
    [Tooltip("Layers that can be attached to when raycasting.")]
    public LayerMask attachableLayers = ~0;

    [Header("Attach Settings")]
    public bool useRaycastToAttach = true;
    public float maxAttachDistance = 4f;      // raycast distance
    public float ropeLength = 3f;             // comfortable length
    [Range(1f, 2f)] public float maxStretch = 1.2f; // stretch beyond ropeLength

    [Header("Joint (Pull) Tuning")]
    public float spring = 1200f;
    public float damper = 60f;
    public float massScale = 1f;

    [Header("Input")]
    public KeyCode keyboardToggle = KeyCode.E;   // optional keyboard
    // X button on LEFT controller == CommonUsages.primaryButton for LeftHand
    private readonly InputFeatureUsage<bool> xrToggle = CommonUsages.primaryButton;

    [Header("Rope Visual (LineRenderer)")]
    public float ropeWidth = 0.01f;
    [Min(2)] public int ropeSegments = 16;
    [Tooltip("Adds a small gravity-based sag to the rope curve.")]
    public float ropeSagFactor = 0.06f;

    // --- runtime ---
    private LineRenderer lr;
    private Rigidbody handAnchorRb;     // kinematic anchor for joint
    private SpringJoint activeJoint;
    private Rigidbody currentTarget;
    private Collider currentTargetCol;
    private Vector3 attachPointWorld;
    private bool isAttached;

    // XR device state
    private InputDevice leftHandDevice;
    private bool prevXPressed;

    void Awake()
    {
        // LineRenderer setup
        lr = GetComponent<LineRenderer>();
        lr.useWorldSpace = true;
        lr.positionCount = ropeSegments;
        lr.widthMultiplier = ropeWidth;

        // Kinematic hand anchor that follows the controller
        var anchor = new GameObject("RopeHandAnchor");
        anchor.transform.SetPositionAndRotation(leftHandTransform.position, leftHandTransform.rotation);
        anchor.transform.SetParent(transform, worldPositionStays: true);
        handAnchorRb = anchor.AddComponent<Rigidbody>();
        handAnchorRb.isKinematic = true;

        RefreshLeftDevice();
    }

    void RefreshLeftDevice()
    {
        var devs = new System.Collections.Generic.List<InputDevice>();
        InputDevices.GetDevicesAtXRNode(XRNode.LeftHand, devs);
        if (devs.Count > 0) leftHandDevice = devs[0];
    }

    void Update()
    {
        // keep anchor on the left hand
        if (leftHandTransform != null)
        {
            handAnchorRb.position = leftHandTransform.position;
            handAnchorRb.rotation = leftHandTransform.rotation;
        }

        // input edge detection
        bool toggleDown = Input.GetKeyDown(keyboardToggle) || XRButtonDown();
        if (toggleDown)
        {
            if (isAttached) Detach();
            else TryAttach();
        }

        // draw rope if attached (refresh the end point every frame)
        if (isAttached && currentTarget != null)
        {
            // Follow the joint’s local anchor on the moving target
            Vector3 anchorWorld = currentTarget.transform.TransformPoint(activeJoint.anchor);

            // Keep the rope end stuck to the surface to avoid floating/overshoot
            if (currentTargetCol != null)
                anchorWorld = currentTargetCol.ClosestPoint(anchorWorld);

            attachPointWorld = anchorWorld;

            DrawRope(handAnchorRb.position, attachPointWorld);
        }
        else
        {
            // hide rope when not attached (keep positions ready)
            lr.positionCount = 0;
            lr.positionCount = ropeSegments;
        }
    }

    bool XRButtonDown()
    {
        if (!leftHandDevice.isValid) RefreshLeftDevice();
        if (leftHandDevice.isValid && leftHandDevice.TryGetFeatureValue(xrToggle, out bool pressed))
        {
            bool down = pressed && !prevXPressed;  // rising edge
            prevXPressed = pressed;
            return down;
        }
        prevXPressed = false;
        return false;
    }

    void TryAttach()
    {
        Rigidbody rb = targetBoxRb;
        Collider col = targetBoxCollider;
        attachPointWorld = Vector3.zero;

        if (rb == null && useRaycastToAttach)
        {
            if (Physics.Raycast(leftHandTransform.position,
                                leftHandTransform.forward,
                                out RaycastHit hit,
                                maxAttachDistance,
                                attachableLayers,
                                QueryTriggerInteraction.Ignore))
            {
                rb = hit.rigidbody;
                col = hit.collider;
                attachPointWorld = hit.point;
            }
        }

        if (rb == null) return; // nothing to attach

        currentTarget = rb;
        currentTargetCol = col != null ? col : rb.GetComponent<Collider>();

        // If we didn't get a surface point (e.g., fixed target), compute nearest point using collider
        if (attachPointWorld == Vector3.zero ||
            (currentTargetCol == null && (attachPointWorld - rb.worldCenterOfMass).sqrMagnitude < 1e-6f))
        {
            if (currentTargetCol != null)
                attachPointWorld = currentTargetCol.ClosestPoint(leftHandTransform.position);
            else
                attachPointWorld = rb.worldCenterOfMass; // safe fallback
        }

        // Create spring joint on the target so it gets pulled by the hand anchor
        activeJoint = currentTarget.gameObject.AddComponent<SpringJoint>();
        activeJoint.autoConfigureConnectedAnchor = false;
        activeJoint.connectedBody = handAnchorRb;

        // Set anchor on the target at the local attach point
        activeJoint.anchor = currentTarget.transform.InverseTransformPoint(attachPointWorld);
        activeJoint.connectedAnchor = Vector3.zero;

        // Limit stretch
        activeJoint.minDistance = 0f;
        activeJoint.maxDistance = ropeLength * Mathf.Max(1f, maxStretch);

        // Spring tuning
        activeJoint.spring = spring;
        activeJoint.damper = damper;
        activeJoint.massScale = massScale;

        isAttached = true;
    }

    void Detach()
    {
        if (activeJoint != null) Destroy(activeJoint);
        activeJoint = null;
        currentTarget = null;
        currentTargetCol = null;
        attachPointWorld = Vector3.zero;
        isAttached = false;
    }

    void DrawRope(Vector3 start, Vector3 end)
    {
        // Visual clamp to the same limit as the joint (prevents the rope from looking longer than allowed)
        float maxLen = ropeLength * Mathf.Max(1f, maxStretch);
        Vector3 toEnd = end - start;
        if (toEnd.magnitude > maxLen)
            end = start + toEnd.normalized * maxLen;

        for (int i = 0; i < ropeSegments; i++)
        {
            float t = i / (float)(ropeSegments - 1);
            Vector3 p = Vector3.Lerp(start, end, t);

            // add sag in gravity direction
            Vector3 sag = Physics.gravity.normalized * ropeSagFactor * Mathf.Sin(Mathf.PI * t);
            p += sag;

            lr.SetPosition(i, p);
        }
    }

    void OnDrawGizmosSelected()
    {
        if (useRaycastToAttach && leftHandTransform)
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawLine(leftHandTransform.position,
                            leftHandTransform.position + leftHandTransform.forward * maxAttachDistance);
        }
    }
}
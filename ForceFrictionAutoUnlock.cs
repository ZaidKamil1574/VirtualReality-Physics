using UnityEngine;
using UnityEngine.XR;

[DefaultExecutionOrder(20)] // run after SlopeSlidingBox
public class ForceFrictionAutoUnlock : MonoBehaviour
{
    [Header("References")]
    public SlopeSlidingBox slope;
    public AutoLockToBox  autoLock;

    [Header("Unlock Logic")]
    public float threshold = 0.21f;
    public float hysteresis = 0.03f;
    public float minToggleInterval = 0.25f;
    public bool  autoReenable = true;
    public bool  alsoUnlockIfLocked = true;

    [Header("Manual Push While Unlocked")]
    [Tooltip("Require hands to be within AutoLockToBox.handLockDistance to allow push.")]
    public bool restrictToHandLockDistance = true;

    [Tooltip("Mirror 'Require Both Hands To Lock' from AutoLockToBox.")]
    public bool respectBothHandRule = true;

    public XRNode stickNode = XRNode.LeftHand;
    [Range(0f,1f)] public float stickDeadzone = 0.25f;
    public float  pushMultiplier = 1f;

    // internals
    private bool   wasUnlocked;
    private float  lastToggleTime;
    private InputDevice stick;
    private Rigidbody rb;
    private Collider  boxCol;   // prefer the box collider for precise closest-point distance

    void Reset()
    {
        if (!slope)   slope   = GetComponent<SlopeSlidingBox>();
        if (!autoLock) autoLock = FindObjectOfType<AutoLockToBox>();
    }

    void Awake()
    {
        rb = slope ? slope.GetComponent<Rigidbody>() : GetComponent<Rigidbody>();

        // Try to get a collider on the assigned box transform; else on this GO
        if (autoLock && autoLock.boxTransform)
            boxCol = autoLock.boxTransform.GetComponent<Collider>();
        if (!boxCol && slope)
            boxCol = slope.GetComponent<Collider>();
        if (!boxCol && autoLock && autoLock.boxTransform)
        {
            // last-resort: any collider in children
            boxCol = autoLock.boxTransform.GetComponentInChildren<Collider>();
        }
    }

    void OnEnable()
    {
        stick = InputDevices.GetDeviceAtXRNode(stickNode);
    }

    void Update()
    {
        if (!slope || !autoLock || !rb) return;
        if (!stick.isValid) stick = InputDevices.GetDeviceAtXRNode(stickNode);

        float diff = Mathf.Clamp01(slope.pushForce) - GetCurrentFriction01();

        // ---- exceed threshold → disable autolock (unlock) ----
        if (diff >= threshold)
        {
            if (!wasUnlocked && Time.time - lastToggleTime >= minToggleInterval)
            {
                if (alsoUnlockIfLocked && autoLock.IsLocked) autoLock.UnlockFromBox();
                autoLock.enabled = false;
                wasUnlocked = true;
                lastToggleTime = Time.time;
            }

            // Gate pushing by the SAME lock distance that AutoLock uses
            bool inBubble = !restrictToHandLockDistance || HandsInsideLockBubble();

            // Only allow Slope to accept pushes if inside the bubble
            slope.requireLockToPush = !inBubble ? true : false;

            if (inBubble)
                TryManualPush(); // continuous push while stick held
        }
        // ---- drop below hysteresis → re-enable autolock ----
        else if (autoReenable && wasUnlocked && diff <= (threshold - hysteresis))
        {
            if (Time.time - lastToggleTime >= minToggleInterval)
            {
                autoLock.enabled = true;
                wasUnlocked = false;
                lastToggleTime = Time.time;
            }
            slope.requireLockToPush = true; // restore default
        }
    }

    private void TryManualPush()
    {
        if (!stick.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
            return;
        if (axis.magnitude < stickDeadzone) return;

        // Same direction basis as SlopeSlidingBox
        Vector3 local = new Vector3(axis.x, 0f, axis.y);
        Transform basis = slope.forwardSource
                          ? slope.forwardSource
                          : (Camera.main ? Camera.main.transform : null);

        Vector3 worldDir = basis ? basis.TransformDirection(local) : local;
        worldDir.y = 0f;
        if (worldDir.sqrMagnitude < 1e-4f) return;

        // Same push strength as SlopeSlidingBox (respect your sliders)
        float Fpush = slope.pushForce * slope.maxPushForceN * pushMultiplier;

        // Project to ground plane like SlopeSlidingBox
        Vector3 n = Vector3.up;
        if (Physics.Raycast(rb.worldCenterOfMass + Vector3.up * 0.05f, Vector3.down, out RaycastHit hit, 1.0f))
            n = hit.normal;

        Vector3 pushDir = Vector3.ProjectOnPlane(worldDir.normalized, n).normalized;
        rb.AddForce(pushDir * Fpush, ForceMode.Force);
    }

    // ---------- STRICT bubble test using AutoLockToBox.handLockDistance ----------
    private bool HandsInsideLockBubble()
    {
        // must have at least one hand reference
        if (!autoLock.leftHandTransform && !autoLock.rightHandTransform) return false;

        float lockR = Mathf.Max(0f, autoLock.handLockDistance);
        bool needBoth = respectBothHandRule && autoLock.requireBothHandsToLock;

        bool leftIn  = HandWithinRadius(autoLock.leftHandTransform,  lockR);
        bool rightIn = HandWithinRadius(autoLock.rightHandTransform, lockR);

        if (needBoth)
        {
            bool haveLeft  = autoLock.leftHandTransform  != null;
            bool haveRight = autoLock.rightHandTransform != null;
            if (haveLeft && haveRight) return leftIn && rightIn;
            if (haveLeft && !haveRight) return leftIn;
            if (!haveLeft && haveRight) return rightIn;
            return false;
        }
        else
        {
            return (leftIn || rightIn);
        }
    }

    private bool HandWithinRadius(Transform hand, float radius)
    {
        if (!hand) return false;

        Vector3 boxRef = boxCol ? boxCol.ClosestPoint(hand.position)
                                : (autoLock.boxTransform ? autoLock.boxTransform.position
                                                         : transform.position);

        // Horizontal distance like AutoLockToBox does
        Vector3 a = hand.position; a.y = boxRef.y;
        return Vector3.Distance(a, boxRef) <= radius;
    }

    private float GetCurrentFriction01()
    {
        if (slope.emulateCoulombFriction) return Mathf.Clamp01(slope.muStatic);
        if (slope.frictionSlider != null)  return Mathf.Clamp01(slope.frictionSlider.value);
        if (slope.boxPhysicsMaterial != null) return Mathf.Clamp01(slope.boxPhysicsMaterial.dynamicFriction);
        return Mathf.Clamp01(slope.muStatic);
    }
}

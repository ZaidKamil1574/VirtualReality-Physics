using UnityEngine;
using UnityEngine.XR;

[RequireComponent(typeof(Rigidbody))]
public class BoxForceWhileLocked : MonoBehaviour
{
    public enum ControlMode { Joystick, PullToHand }

    [Header("References")]
    public AutoLockToBox autoLock;          // must be assigned
    public Transform leftHandTransform;     // optional (PullToHand)
    public Transform rightHandTransform;    // optional (PullToHand)

    [Header("Control")]
    public ControlMode controlMode = ControlMode.Joystick;
    public XRNode joystickOn = XRNode.LeftHand;

    [Header("Force")]
    [Tooltip("Magnitude of force to apply when there is input and you are locked.")]
    public float maxForce = 60f;

    [Tooltip("Ignore tiny stick inputs (Joystick mode).")]
    public float deadZone = 0.15f;

    [Tooltip("Use Acceleration so force is mass-independent. Off = regular Force.")]
    public bool useAcceleration = false;

    private Rigidbody rb;
    private InputDevice device;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        // Do NOT touch drag, gravity, etc. Leave other physics alone.
    }

    void Update()
    {
        if (controlMode == ControlMode.Joystick && (!device.isValid))
            device = InputDevices.GetDeviceAtXRNode(joystickOn);
    }

    void FixedUpdate()
    {
        if (autoLock == null || rb == null) return;
        if (!autoLock.IsLocked) return;                // absolutely no force when not locked

        Vector3 force = Vector3.zero;

        if (controlMode == ControlMode.Joystick)
        {
            Vector2 axis = Vector2.zero;
            if (device.isValid)
                device.TryGetFeatureValue(CommonUsages.primary2DAxis, out axis);

            if (axis.sqrMagnitude >= deadZone * deadZone)
            {
                // plane-relative movement using player facing
                Vector3 fwd = Vector3.forward;
                Vector3 right = Vector3.right;

                if (autoLock.playerTransform != null)
                {
                    fwd = Vector3.ProjectOnPlane(autoLock.playerTransform.forward, Vector3.up).normalized;
                    if (fwd.sqrMagnitude < 1e-6f) fwd = Vector3.forward;
                    right = Vector3.Cross(Vector3.up, fwd);
                }

                Vector3 dir = (fwd * axis.y + right * axis.x);
                if (dir.sqrMagnitude > 1e-6f) force = dir.normalized * maxForce;
            }
        }
        else // PullToHand
        {
            Transform hand = ChooseActiveHand();
            if (hand != null)
            {
                Vector3 dir = hand.position - transform.position;
                dir.y = 0f;                              // keep it planar (remove to allow vertical)
                if (dir.sqrMagnitude > 1e-6f) force = dir.normalized * maxForce;
            }
        }

        if (force.sqrMagnitude > 1e-6f)
            rb.AddForce(force, useAcceleration ? ForceMode.Acceleration : ForceMode.Force);
        // else: do nothing — no hidden smoothing/decay.
    }

    private Transform ChooseActiveHand()
    {
        if (leftHandTransform == null && rightHandTransform == null) return null;
        if (leftHandTransform != null && rightHandTransform == null) return leftHandTransform;
        if (rightHandTransform != null && leftHandTransform == null) return rightHandTransform;

        float dl = (leftHandTransform.position - transform.position).sqrMagnitude;
        float dr = (rightHandTransform.position - transform.position).sqrMagnitude;
        return (dl <= dr) ? leftHandTransform : rightHandTransform;
    }
}

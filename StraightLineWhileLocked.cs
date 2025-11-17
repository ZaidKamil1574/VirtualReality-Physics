using UnityEngine;
using UnityEngine.XR;
using XRCommonUsages = UnityEngine.XR.CommonUsages;

[RequireComponent(typeof(Rigidbody))]
public class StraightLineWhileLocked : MonoBehaviour
{
    [Header("Required")]
    public AutoLockToBox lockManager;    // must expose public bool IsLocked
    public MonoBehaviour slope;          // your SlopeSlidingBox (any script with the fields)
    public Transform forwardSource;      // e.g. LeftHand controller transform

    [Header("Field names on 'slope'")]
    public string pushField = "pushForce";     // set to your actual push float name
    public string frictionField = "friction";  // set to your actual friction float name

    [Header("Fallbacks (used only if field names not found)")]
    public float fallbackPush = 20f;
    public float fallbackFriction = 0.4f;

    Rigidbody rb;
    Vector3 axisCached = Vector3.forward;
    bool aHeld;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        if (!forwardSource) forwardSource = transform;
    }

    void Update()
    {
        bool locked = IsLocked();

        var right = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        bool aNow = false;
        if (right.isValid) right.TryGetFeatureValue(XRCommonUsages.primaryButton, out aNow);

        if (!aHeld && aNow && locked)
        {
            axisCached = Vector3.ProjectOnPlane(forwardSource.forward, Vector3.up).normalized;
            if (slope) slope.enabled = false;   // avoid double forces
        }
        if ((aHeld && !aNow) || (aNow && !locked))
        {
            if (slope) slope.enabled = true;
        }
        aHeld = aNow && locked;
    }

    void FixedUpdate()
    {
        if (!aHeld) return;

        var left = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        Vector2 stick = Vector2.zero;
        if (!left.isValid || !left.TryGetFeatureValue(XRCommonUsages.primary2DAxis, out stick)) return;
        if (stick.magnitude < 0.12f) return;

        float push = ReadFloat(slope, pushField, fallbackPush);
        float mu   = ReadFloat(slope, frictionField, fallbackFriction);

        Vector3 axis = axisCached.sqrMagnitude > 1e-6f
            ? axisCached
            : Vector3.ProjectOnPlane(forwardSource.forward, Vector3.up).normalized;

        float input = stick.y;

        // push along the cached straight line
        rb.AddForce(axis * (push * input), ForceMode.Acceleration);

        // same friction model (single μ) used by your slope script
        float g = Physics.gravity.magnitude;
        float vAlong = Vector3.Dot(rb.velocity, axis);

        if (Mathf.Abs(vAlong) < 0.02f && Mathf.Abs(input * push) < mu * g)
        {
            rb.velocity -= axis * vAlong; // static stick
            return;
        }
        if (Mathf.Abs(vAlong) > 0.0001f)
        {
            float sign = Mathf.Sign(vAlong);
            rb.AddForce(-axis * (mu * g * sign), ForceMode.Acceleration);
        }
    }

    float ReadFloat(object obj, string name, float fallback)
    {
        if (obj == null || string.IsNullOrEmpty(name)) return fallback;
        var t = obj.GetType();
        var f = t.GetField(name);     if (f != null && f.FieldType == typeof(float)) return (float)f.GetValue(obj);
        var p = t.GetProperty(name);  if (p != null && p.PropertyType == typeof(float)) return (float)p.GetValue(obj);
        return fallback;
    }

    bool IsLocked()
    {
        if (!lockManager) return true;
        var p = lockManager.GetType().GetProperty("IsLocked");
        if (p != null && p.PropertyType == typeof(bool)) return (bool)p.GetValue(lockManager);
        var f = lockManager.GetType().GetField("IsLocked");
        if (f != null && f.FieldType == typeof(bool)) return (bool)f.GetValue(lockManager);
        return true;
    }

    void OnDisable()
    {
        if (slope && !slope.enabled) slope.enabled = true;
    }
}

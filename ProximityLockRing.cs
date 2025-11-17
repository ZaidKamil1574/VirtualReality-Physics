using UnityEngine;

/// Draws a pulsing circular hologram on the ground near the box when the player
/// is within locking distance. Uses a LineRenderer (created at runtime).
[RequireComponent(typeof(Collider))]
public class ProximityLockRing : MonoBehaviour
{
    [Header("References")]
    public Transform playerTransform;       // XR Origin / Camera Offset
    public Transform boxTransform;          // The box to lock to (usually this.transform)
    public LayerMask groundMask = ~0;       // Ground layers for the raycast

    [Header("Distances")]
    public float lockDistance = 1.5f;       // Show green ring when player <= this
    public float showDistanceMul = 1.4f;    // Ring shows (faint) until lockDistance * mul
    public float yOffset = 0.01f;           // Lift ring a tiny bit to avoid z-fighting

    [Header("Ring Look")]
    public int segments = 64;
    public float ringWidth = 0.04f;
    public float pulseSpeed = 2.0f;         // Hz
    public float pulseAmplitude = 0.05f;    // extra meters added to radius via pulse
    public Color farColor   = new Color(0f, 1f, 1f, 0.25f);  // cyan (faint)
    public Color nearColor  = new Color(0.2f, 1f, 0.2f, 0.85f); // green (solid)

    [Header("Optional: read from your lock script")]
    public bool useLockDistanceFromScript = false;
    public AutoLockToBox autoLockScript;    // If assigned and toggle above is true

    // --- internals ---
    private LineRenderer lr;
    private Collider boxCol;
    private Vector3[] pts;
    private float baseRadius;
    private const float upRay = 2.0f;

    void Awake()
    {
        if (!boxTransform) boxTransform = transform;
        boxCol = GetComponent<Collider>();

        lr = gameObject.AddComponent<LineRenderer>();
        lr.loop = true;
        lr.useWorldSpace = true;
        lr.positionCount = segments;
        lr.widthMultiplier = ringWidth;
        lr.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        lr.receiveShadows = false;
        lr.material = new Material(Shader.Find("Sprites/Default"));
        lr.enabled = false; // hidden until in range

        pts = new Vector3[segments];
        baseRadius = lockDistance;
    }

    void Update()
    {
        if (!playerTransform || !boxTransform) return;

        // Optionally keep lockDistance in sync with your AutoLockToBox
        if (useLockDistanceFromScript && autoLockScript != null)
            lockDistance = Mathf.Max(0.01f, autoLockScript != null ? autoLockScript.lockDistance : lockDistance);

        float d = Vector3.Distance(playerTransform.position, boxTransform.position);
        float showDist = lockDistance * Mathf.Max(1.01f, showDistanceMul);

        // Show only when reasonably near
        bool shouldShow = d <= showDist;
        lr.enabled = shouldShow;
        if (!shouldShow) return;

        // Choose color (near vs far)
        Color c = (d <= lockDistance) ? nearColor : farColor;
        lr.startColor = lr.endColor = c;

        // Where does the ring sit? -> ground under the box (or above collider bottom)
        Vector3 origin = boxTransform.position + Vector3.up * upRay;
        Vector3 center = boxTransform.position;
        Vector3 n = Vector3.up;

        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, upRay + 5f, groundMask, QueryTriggerInteraction.Ignore))
        {
            center = hit.point + hit.normal * yOffset;
            n = hit.normal;
        }
        else
        {
            // Fallback: just above collider’s bottom
            Bounds b = boxCol.bounds;
            center = new Vector3(b.center.x, b.min.y + yOffset, b.center.z);
            n = Vector3.up;
        }

        // Build an orthonormal basis on the ground plane (n, t, b)
        Vector3 t = Vector3.Cross(n, Vector3.right);
        if (t.sqrMagnitude < 1e-6f) t = Vector3.Cross(n, Vector3.forward);
        t.Normalize();
        Vector3 b2 = Vector3.Cross(n, t);

        // Pulse the radius a bit for a hologram vibe
        float pulse = Mathf.Sin(Time.time * Mathf.PI * 2f * pulseSpeed) * pulseAmplitude;
        float radius = Mathf.Max(0.05f, lockDistance + pulse);

        // Draw the circle
        for (int i = 0; i < segments; i++)
        {
            float a = (i / (float)segments) * Mathf.PI * 2f;
            Vector3 onPlane = t * Mathf.Cos(a) + b2 * Mathf.Sin(a);
            pts[i] = center + onPlane * radius;
        }
        lr.SetPositions(pts);
    }

    // Optional helper if you want to set color externally when actually locked
    public void SetLockedVisual(bool locked)
    {
        lr.startColor = lr.endColor = locked ? nearColor : farColor;
    }
}
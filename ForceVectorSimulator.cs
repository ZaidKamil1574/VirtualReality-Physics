using UnityEngine;

/// Attach to the REAL physics box (has a Rigidbody).
/// Spawns a ghost diagram box beside it and shows four force arrows:
/// 1) Applied (yellow), 2) Normal (blue), 3) Gravity (green), 4) Friction (red).
public class ForceVectorDiagramSimple : MonoBehaviour
{
    [Header("Refs")]
    public Rigidbody rb;                   // auto-filled if left null
    public GameObject arrowPrefab;         // arrow model aligned on +Z
    public GameObject diagramBoxPrefab;    // simple visual cube (no RB)

    [Header("Diagram (Ghost)")]
    public Vector3 diagramOffset = new Vector3(1.5f, 0.5f, 0f);
    public float diagramScale = 0.7f;
    public bool diagramFollowsRotation = true;
    public bool smoothFollow = true;
    [Range(0.01f, 20f)] public float followLerp = 8f;

    [Header("Ground Detection")]
    public float groundCheckDist = 1.0f;
    public LayerMask groundMask = ~0;      // set to your Ground layer(s) for best results

    [Header("Friction")]
    public float muStatic = 0.6f;
    public float muKinetic = 0.5f;

    [Header("Arrow Visuals")]
    public float lengthPerNewton = 0.005f; // ↓ reduce if arrows look big
    public float minArrowLength = 0.02f;
    public float maxArrowLength = 1.2f;    // clamp so nothing gets huge

    [Header("Optional arrow materials (colors)")]
    public Material appliedMat;            // yellow
    public Material normalMat;             // blue
    public Material gravityMat;            // green
    public Material frictionMat;           // red

    [Header("Applied force (set from your push script)")]
    public Vector3 externalForceWorld;     // call SetAppliedForce() when you AddForce()
    public void SetAppliedForce(Vector3 f) => externalForceWorld = f;

    // internals
    Transform diagramRoot, dBox;
    Transform aApp, aN, aG, aF;

    void Awake()
    {
        if (!rb) rb = GetComponent<Rigidbody>();
        if (!rb) Debug.LogError("ForceVectorDiagramSimple: Rigidbody required.");

        // Ghost root
        diagramRoot = new GameObject("ForceDiagramRoot").transform;
        diagramRoot.position = transform.position + diagramOffset;
        diagramRoot.rotation = diagramFollowsRotation ? transform.rotation : Quaternion.identity;
        diagramRoot.localScale = Vector3.one * Mathf.Max(0.01f, diagramScale);

        // Ghost box
        if (diagramBoxPrefab)
        {
            dBox = Instantiate(diagramBoxPrefab, diagramRoot).transform;
            dBox.localPosition = Vector3.zero;
            dBox.localRotation = Quaternion.identity;
            dBox.localScale = Vector3.one;
        }
        else
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            DestroyImmediate(cube.GetComponent<Collider>());
            cube.name = "DiagramBox";
            dBox = cube.transform;
            dBox.SetParent(diagramRoot, false);
        }

        // Arrows
        aApp = SpawnArrow("F_app", appliedMat);
        aN   = SpawnArrow("N",     normalMat);
        aG   = SpawnArrow("W",     gravityMat);
        aF   = SpawnArrow("F_f",   frictionMat);
    }

    Transform SpawnArrow(string name, Material mat)
    {
        var go = Instantiate(arrowPrefab, diagramRoot.position, Quaternion.identity, diagramRoot);
        go.name = name;
        if (mat)
        {
            foreach (var r in go.GetComponentsInChildren<Renderer>())
                r.material = mat;
        }
        return go.transform;
    }

    void Update()
    {
        // 1) Forces on the REAL box
        Vector3 origin = rb.worldCenterOfMass;
        Vector3 W = rb.mass * Physics.gravity; // gravity

        // Contact normal
        Vector3 n = Vector3.up;
        bool grounded = false;
        if (Physics.Raycast(origin, Vector3.down, out RaycastHit hit, groundCheckDist, groundMask, QueryTriggerInteraction.Ignore))
        {
            n = hit.normal.normalized;
            grounded = true;
        }

        // Normal
        Vector3 N = Vector3.zero;
        if (grounded)
        {
            float nMag = Mathf.Max(0f, Vector3.Dot(-W, n));
            N = n * nMag;
        }

        // Applied
        Vector3 Fapp = externalForceWorld;

        // Friction (static vs kinetic) along tangent
        Vector3 Ffric = Vector3.zero;
        if (grounded)
        {
            Vector3 Wtan   = Vector3.ProjectOnPlane(W, n);
            Vector3 FappTan= Vector3.ProjectOnPlane(Fapp, n);
            Vector3 vTan   = Vector3.ProjectOnPlane(rb.velocity, n);

            float Nmag = N.magnitude;
            if (vTan.sqrMagnitude > 1e-6f)
                Ffric = -vTan.normalized * (muKinetic * Nmag); // kinetic opposes motion
            else
                Ffric = Vector3.ClampMagnitude(-(Wtan + FappTan), muStatic * Nmag); // static opposes impending
        }

        // 2) Move ghost beside the real box
        Vector3 targetPos = transform.position + diagramOffset;
        Quaternion targetRot = diagramFollowsRotation ? transform.rotation : Quaternion.identity;

        if (smoothFollow)
        {
            float t = 1f - Mathf.Exp(-followLerp * Time.deltaTime);
            diagramRoot.position = Vector3.Lerp(diagramRoot.position, targetPos, t);
            diagramRoot.rotation = Quaternion.Slerp(diagramRoot.rotation, targetRot, t);
        }
        else
        {
            diagramRoot.position = targetPos;
            diagramRoot.rotation = targetRot;
        }
        diagramRoot.localScale = Vector3.one * Mathf.Max(0.01f, diagramScale);

        // 3) Update the four arrows at the ghost
        Vector3 upHint = n;
        Vector3 dOrigin = diagramRoot.position;

        UpdateArrow(aApp, dOrigin, Fapp,  upHint);
        UpdateArrow(aN,   dOrigin, N,     upHint);
        UpdateArrow(aG,   dOrigin, W,     upHint);
        UpdateArrow(aF,   dOrigin, Ffric, upHint);
    }

    void UpdateArrow(Transform arrow, Vector3 originWorld, Vector3 vecWorld, Vector3 upHint)
    {
        if (!arrow) return;

        float len = Mathf.Clamp(vecWorld.magnitude * lengthPerNewton, minArrowLength, maxArrowLength);

        arrow.position = originWorld;

        if (vecWorld.sqrMagnitude > 1e-10f)
            arrow.rotation = Quaternion.LookRotation(vecWorld.normalized, upHint);

        arrow.localScale = new Vector3(1f, 1f, len);
        arrow.gameObject.SetActive(vecWorld.sqrMagnitude > 1e-10f);
    }
}

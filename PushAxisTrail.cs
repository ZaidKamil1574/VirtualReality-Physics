// PushAxisTrail.cs
using UnityEngine;
using UnityEngine.XR;

[RequireComponent(typeof(Rigidbody), typeof(Collider))]
public class PushAxisTrail : MonoBehaviour
{
    [Header("XR Input")]
    public Transform rightHandTransform;          // your right controller
    public float faceDetectDistance = 3f;

    [Header("Push")]
    public float pushForce = 20f;                 // impulse magnitude per press
    public bool useAcceleration = true;           // true = continuous accel while held; false = impulse

    [Header("Trails (history tracks)")]
    public bool enableHistoryTrail = true;
    public Material trailMaterial;
    [Tooltip("Infinity = permanent tracks")]
    public float trailTime = Mathf.Infinity;
    public float trailWidth = 0.02f;
    public float trailMinVertexDistance = 0.02f;
    public int   trailSortingOrder = 0;
    [Tooltip("Vertical offset to avoid z-fighting with the floor")]
    public float trailHeightOffset = 0.005f;
    [Tooltip("How far from center the two trails sit (perpendicular to motion)")]
    public float trailInset = 0.25f;

    Rigidbody rb;
    Collider boxCol;
    InputDevice leftHand, rightHand;

    enum MoveAxis { None, X, Z }
    MoveAxis activeAxis = MoveAxis.None;

    // trails
    Transform leftTrailAnchor, rightTrailAnchor;
    TrailRenderer leftTrail, rightTrail;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        boxCol = GetComponent<Collider>();

        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        leftHand  = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        if (enableHistoryTrail)
        {
            leftTrailAnchor  = new GameObject("LeftTrailAnchor").transform;
            rightTrailAnchor = new GameObject("RightTrailAnchor").transform;

            leftTrail  = AddTrail(leftTrailAnchor.gameObject, "LeftTrail");
            rightTrail = AddTrail(rightTrailAnchor.gameObject, "RightTrail");
        }
    }

    TrailRenderer AddTrail(GameObject host, string name)
    {
        host.name = name;
        var tr = host.AddComponent<TrailRenderer>();
        tr.time = trailTime;
        tr.minVertexDistance = trailMinVertexDistance;
        tr.widthMultiplier = trailWidth;
        tr.numCornerVertices = 2;
        tr.numCapVertices = 2;
        tr.sortingOrder = trailSortingOrder;
        if (trailMaterial) tr.material = trailMaterial;
        tr.emitting = true;
        return tr;
    }

    void Update()
    {
        if (!rightHandTransform) return;

        // 1) Determine which face we’re pushing => which axis we allow
        activeAxis = GetAxisFromHandFace();

        // 2) Read input and apply push strictly along that axis
        rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool aPressed);
        leftHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);

        if (aPressed && stick.sqrMagnitude > 0.01f && activeAxis != MoveAxis.None)
        {
            Vector3 push = Vector3.zero;

            if (activeAxis == MoveAxis.Z && Mathf.Abs(stick.y) > 0.01f)
                push = new Vector3(0f, 0f, Mathf.Sign(stick.y));
            else if (activeAxis == MoveAxis.X && Mathf.Abs(stick.x) > 0.01f)
                push = new Vector3(Mathf.Sign(stick.x), 0f, 0f);

            if (push != Vector3.zero)
            {
                if (useAcceleration)
                    rb.AddForce(push * pushForce, ForceMode.Acceleration);
                else
                    rb.AddForce(push * pushForce, ForceMode.Impulse);
            }
        }

        // 3) Keep trail anchors glued to the active “front” corners
        UpdateTrailAnchors();
    }

    void FixedUpdate()
    {
        // Constrain velocity to the active axis (prevent sideways drift)
        var v = rb.velocity;
        if (activeAxis == MoveAxis.X) v.z = 0f;
        else if (activeAxis == MoveAxis.Z) v.x = 0f;
        rb.velocity = v;
    }

    MoveAxis GetAxisFromHandFace()
    {
        // Ray from hand to box; use the hit normal to pick axis
        Vector3 dir = (rb.worldCenterOfMass - rightHandTransform.position).normalized;

        if (Physics.Raycast(rightHandTransform.position, dir, out RaycastHit hit, faceDetectDistance))
        {
            if (hit.collider == boxCol)
            {
                Vector3 n = hit.normal; // face normal
                // Z faces (front/back)
                if (Mathf.Abs(n.z) > 0.9f && Mathf.Abs(n.x) < 0.2f && Mathf.Abs(n.y) < 0.6f)
                    return MoveAxis.Z;
                // X faces (left/right)
                if (Mathf.Abs(n.x) > 0.9f && Mathf.Abs(n.z) < 0.2f && Mathf.Abs(n.y) < 0.6f)
                    return MoveAxis.X;
            }
        }
        return MoveAxis.None;
    }

    void UpdateTrailAnchors()
    {
        if (!enableHistoryTrail || leftTrailAnchor == null || rightTrailAnchor == null) return;

        Bounds b = boxCol.bounds;
        float y = b.min.y + trailHeightOffset;

        if (activeAxis == MoveAxis.Z || activeAxis == MoveAxis.None)
        {
            // Trails placed at front edge, offset in X
            float inset = Mathf.Max(trailInset, b.extents.x * 0.6f);
            Vector3 frontLeft  = new Vector3(transform.position.x - inset, y, b.max.z);
            Vector3 frontRight = new Vector3(transform.position.x + inset, y, b.max.z);
            leftTrailAnchor.position  = frontLeft;
            rightTrailAnchor.position = frontRight;
        }
        else // MoveAxis.X
        {
            // Trails placed at right edge, offset in Z
            float inset = Mathf.Max(trailInset, b.extents.z * 0.6f);
            Vector3 sideNear = new Vector3(b.max.x, y, transform.position.z - inset);
            Vector3 sideFar  = new Vector3(b.max.x, y, transform.position.z + inset);
            leftTrailAnchor.position  = sideNear;
            rightTrailAnchor.position = sideFar;
        }
    }
}
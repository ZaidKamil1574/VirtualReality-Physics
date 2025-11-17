using System.Collections;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.UI;
using TMPro;

[RequireComponent(typeof(Rigidbody))]
public class SlopeSlidingBox : MonoBehaviour
{
    [Header("Physics & Movement")]
    [Tooltip("Normalized force (0..1) driven by the slider. 1.0 maps to maxPushForceN.")]
    [Range(0f, 1f)] public float pushForce = 0.5f; // normalized 0..1
    public float gravityMultiplier = 1f;

    [Header("Force Scaling (0..1 -> Newtons)")]
    [Tooltip("Real force (N) when the slider is 1.0.")]
    public float maxPushForceN = 1000f;
    [Tooltip("If ON, the UI shows 0..1 instead of Newtons.")]
    public bool showForceAsNormalized = true;

    [Header("Movement Input")]
    public Transform forwardSource;
    [Range(0f, 1f)] public float stickDeadzone = 0.12f;
    public bool requireLockToPush = true;

    [Header("Surface Options")]
    [Tooltip("If ON, we apply gravity ourselves (once) and disable Rigidbody.useGravity to avoid double gravity.")]
    public bool useSurfaceGravity = false;
    public bool alignToSurface   = false;
    public float alignDeadbandDegrees = 0.5f;

    [Header("Coulomb Friction (accurate behavior)")]
    public bool emulateCoulombFriction = true;
    [Range(0f, 2f)] public float muStatic  = 0.6f;
    [Range(0f, 2f)] public float muKinetic = 0.5f;
    [Range(0f, 5f)] public float groundDamping = 0.4f;

    [Header("Force UI")]
    public Slider forceSlider;
    public Text   forceValueText;

    [Header("Friction UI (also drives μ values when emulateCoulombFriction=OFF)")]
    public Slider         frictionSlider;
    public Text           frictionValueText;
    public PhysicMaterial boxPhysicsMaterial;

    [Header("Lock System")]
    public AutoLockToBox autoLock;
    public Transform     xrOrigin;

    [Header("Reset System")]
    public Button resetButton;
    [SerializeField] float resetFreezeSeconds = 0.15f;
    [SerializeField] float freezeAngularSeconds = 0.20f;

    [Header("Debug UI")]
    public TextMeshProUGUI velocityText;
    public TextMeshProUGUI accelerationText;

    [Header("Manual Input Fields")]
    public TMP_InputField forceInputField;      // expects 0..1
    public TMP_InputField frictionInputField;   // expects 0..1
    public Button         applyButton;

    // ---- internals ----
    private Rigidbody rb;
    private InputDevice leftHand;
    private Vector3 previousVelocity;
    private float   currentAcceleration;

    private Vector3   initialBoxPosition;
    private Quaternion initialBoxRotation;
    private Vector3   initialPlayerPosition;
    private Quaternion initialPlayerRotation;

    private bool  isResetting = false;
    private float ignoreInputUntil = -1f;
    private float allowSpinAt = -1f;

    private Vector3 queuedPushDir = Vector3.zero;  // world-space, normalized
    private float   queuedPushMag = 0f;            // 0..1 from stick

    private bool hasGround;
    private RaycastHit groundHit;

    // ---------- helpers ----------
    private bool IsPushAllowed()
    {
        if (isResetting || Time.time < ignoreInputUntil) return false;
        if (!requireLockToPush) return true;
        return (autoLock != null && autoLock.IsLocked);
    }

    void SyncGravityMode()
    {
        if (!rb) return;
        rb.useGravity = !useSurfaceGravity; // if we add gravity manually, turn off built-in gravity
    }

    void ApplyPhysicMaterial(float value01)
    {
        if (!boxPhysicsMaterial) return;

        if (emulateCoulombFriction)
        {
            // Avoid double-counting friction while using custom Coulomb friction.
            boxPhysicsMaterial.staticFriction  = 0f;
            boxPhysicsMaterial.dynamicFriction = 0f;
            boxPhysicsMaterial.frictionCombine = PhysicMaterialCombine.Minimum;
        }
        else
        {
            float v = Mathf.Clamp01(value01);
            boxPhysicsMaterial.staticFriction  = v;
            boxPhysicsMaterial.dynamicFriction = v;
            boxPhysicsMaterial.frictionCombine = PhysicMaterialCombine.Multiply;
        }
    }
    // -------------------------------

    void OnValidate()
    {
        pushForce = Mathf.Clamp01(pushForce);
        maxPushForceN = Mathf.Max(0f, maxPushForceN);
        if (forceSlider)
        {
            forceSlider.minValue = 0f; forceSlider.maxValue = 1f; forceSlider.wholeNumbers = false;
        }
        ApplyPhysicMaterial(frictionSlider ? frictionSlider.value : muStatic);
        if (rb) SyncGravityMode();
    }

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);

        if (forwardSource == null && Camera.main != null)
            forwardSource = Camera.main.transform;

        initialBoxPosition = transform.position;
        initialBoxRotation = transform.rotation;

        if (xrOrigin != null)
        {
            initialPlayerPosition = xrOrigin.position;
            initialPlayerRotation = xrOrigin.rotation;
        }

        // Force slider: 0..1 normalized
        if (forceSlider != null)
        {
            forceSlider.wholeNumbers = false;
            forceSlider.minValue = 0f; forceSlider.maxValue = 1f;
            forceSlider.onValueChanged.AddListener(OnForceSliderChanged);
            forceSlider.value = pushForce;
        }
        UpdateForceText();

        // Friction slider and PhysicMaterial sync
        if (frictionSlider != null)
        {
            frictionSlider.onValueChanged.AddListener(OnFrictionSliderChanged);
            float startFric = boxPhysicsMaterial ? boxPhysicsMaterial.dynamicFriction : frictionSlider.value;
            startFric = Mathf.Clamp01(startFric);
            muStatic  = emulateCoulombFriction ? muStatic  : startFric;
            muKinetic = emulateCoulombFriction ? muKinetic : Mathf.Clamp01(startFric * 0.9f);
            frictionSlider.value = emulateCoulombFriction ? muStatic : startFric;
            ApplyPhysicMaterial(frictionSlider.value);
            UpdateFrictionText(frictionSlider.value);
        }
        else
        {
            ApplyPhysicMaterial(muStatic);
        }

        if (applyButton) applyButton.onClick.AddListener(ApplyManualValues);
        if (resetButton) resetButton.onClick.AddListener(ResetScene);

        SyncGravityMode();
    }

    void FixedUpdate()
    {
        // Ground probe near COM for normals
        hasGround = Physics.Raycast(rb.worldCenterOfMass + Vector3.up * 0.05f,
                                    Vector3.down, out groundHit, 1.0f);

        // Gravity: apply ONCE (either Unity or us)
        if (useSurfaceGravity)
        {
            // Apply Physics.gravity * mass once (decomposed is equivalent but clearer with multiplier)
            Vector3 g = Physics.gravity * gravityMultiplier * rb.mass;
            rb.AddForce(g, ForceMode.Force);
        }

        // brief angular settle after reset
        if (Time.time < allowSpinAt) rb.angularVelocity = Vector3.zero;

        // Tangential push dir relative to ground normal
        Vector3 n = hasGround ? groundHit.normal : Vector3.up;
        Vector3 pushTangent = queuedPushDir;
        if (pushTangent.sqrMagnitude > 1e-6f)
            pushTangent = Vector3.ProjectOnPlane(pushTangent, n).normalized;

        // ----- Coulomb friction implementation -----
        if (queuedPushMag > 0f && pushTangent.sqrMagnitude > 1e-6f)
        {
            // Convert normalized push (0..1) into Newtons using maxPushForceN
            float Fpush = (pushForce * maxPushForceN) * queuedPushMag;

            if (emulateCoulombFriction && hasGround)
            {
                // Normal force using actual gravity direction
                float N = rb.mass * Physics.gravity.magnitude *
                          Mathf.Max(0f, Vector3.Dot(n, -Physics.gravity.normalized));

                float FstaticMax = muStatic * N;

                // Tangential speed to decide static vs kinetic
                Vector3 vTan = Vector3.ProjectOnPlane(rb.velocity, n);
                float   vTanMag = vTan.magnitude;
                bool atRestTangentially = vTanMag < 0.02f;

                if (atRestTangentially && Fpush < FstaticMax)
                {
                    // Below static threshold -> no motion
                }
                else
                {
                    // Kinetic regime: apply push and kinetic friction opposing motion
                    Vector3 opposeDir = (vTanMag > 0.01f) ? -vTan.normalized : -pushTangent;
                    rb.AddForce(pushTangent * Fpush, ForceMode.Force);
                    rb.AddForce(opposeDir * (muKinetic * N), ForceMode.Force);
                }
            }
            else
            {
                // Plain push (PhysX materials decide friction)
                rb.AddForce(pushTangent * Fpush, ForceMode.Force);
            }
        }

        // Optional extra damping to help settle when μ is low
        if (emulateCoulombFriction && hasGround && groundDamping > 0f)
        {
            Vector3 vTan = Vector3.ProjectOnPlane(rb.velocity, n);
            rb.AddForce(-vTan * groundDamping, ForceMode.Acceleration);
        }

        // Debug readouts
        float dt = Time.fixedDeltaTime;
        currentAcceleration = (rb.velocity.magnitude - previousVelocity.magnitude) / dt;
        if (velocityText != null)     velocityText.text     = $"{rb.velocity.magnitude:F2} m/s";
        if (accelerationText != null) accelerationText.text = $"{currentAcceleration:F2} m/s²";
        previousVelocity = rb.velocity;

        // clear until next Update fill
        queuedPushMag = 0f;
    }

    void Update()
    {
        if (!leftHand.isValid) leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        if (!IsPushAllowed()) { queuedPushMag = 0f; return; }

        if (leftHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick)
            && stick.sqrMagnitude > stickDeadzone * stickDeadzone)
        {
            float mag = Mathf.Clamp01(stick.magnitude);

            Vector3 pushDir = new Vector3(stick.x, 0f, stick.y);
            Transform basis = forwardSource != null ? forwardSource
                                : (Camera.main ? Camera.main.transform : null);

            Vector3 worldDir = basis ? basis.TransformDirection(pushDir) : pushDir;
            worldDir.y = 0f;

            if (worldDir.sqrMagnitude > 1e-6f)
            {
                queuedPushDir = worldDir.normalized;
                queuedPushMag = mag;
            }
        }
    }

    // --- Forces (kept for external callers) ---
    public void ApplyPush(Vector3 direction)
    {
        queuedPushDir = direction.normalized;
        queuedPushMag = 1f;
    }

    // --- UI hooks ---
    void OnForceSliderChanged(float value)
    {
        pushForce = Mathf.Clamp01(value); // 0..1
        UpdateForceText();
    }

    void UpdateForceText()
    {
        if (!forceValueText) return;
        if (showForceAsNormalized)
            forceValueText.text = " " + pushForce.ToString("F2");                 // 0..1
        else
            forceValueText.text = " " + (pushForce * maxPushForceN).ToString("F1"); // Newtons
    }

    void OnFrictionSliderChanged(float value)
    {
        value = Mathf.Clamp01(value);

        if (!emulateCoulombFriction)
        {
            // When not emulating, PhysX material carries friction
            ApplyPhysicMaterial(value);
            muStatic  = value;
            muKinetic = Mathf.Clamp01(value * 0.9f);
        }
        else
        {
            // Emulating: keep PhysX friction at 0, use sliders for μ
            muStatic  = value;
            muKinetic = Mathf.Clamp01(value * 0.9f);
            ApplyPhysicMaterial(0f);
        }

        UpdateFrictionText(value);
    }

    void UpdateFrictionText(float value)
    {
        if (frictionValueText != null) frictionValueText.text = " " + value.ToString("F2");
    }

    public void ApplyManualValues()
{
    // FORCE (0..1)
    if (forceInputField && float.TryParse(forceInputField.text, out float f01))
    {
        pushForce = Mathf.Clamp01(f01);
        if (forceSlider) forceSlider.SetValueWithoutNotify(pushForce);
        OnForceSliderChanged(pushForce);       // updates label & any side effects
    }

    // FRICTION μ (0..1)
    if (frictionInputField && float.TryParse(frictionInputField.text, out float mu))
    {
        mu = Mathf.Clamp01(mu);
        if (frictionSlider) frictionSlider.SetValueWithoutNotify(mu);
        OnFrictionSliderChanged(mu);           // updates μs/μk & PhysicMaterial per mode
    }
}


    // --- Reset (physics-safe) ---
    public void ResetScene()
    {
        if (!gameObject.activeInHierarchy) return;
        if (!isResetting) StartCoroutine(DoReset());
    }

    private IEnumerator DoReset()
    {
        isResetting = true;

        var oldInterp = rb.interpolation;
        rb.interpolation = RigidbodyInterpolation.None;

        bool autoLockWasEnabled = false;
        if (autoLock != null) { autoLockWasEnabled = autoLock.enabled; autoLock.enabled = false; }

        // 1) Reset player first
        if (xrOrigin != null)
        {
            xrOrigin.position = initialPlayerPosition;
            xrOrigin.rotation = initialPlayerRotation;
            yield return null;
        }

        // 2) Freeze physics
        rb.isKinematic = true;
        rb.detectCollisions = false;
        rb.velocity = Vector3.zero;
        rb.angularVelocity = Vector3.zero;

        // 3) Teleport via Rigidbody
        rb.position = initialBoxPosition;
        rb.rotation = initialBoxRotation;

        // 4) Let physics acknowledge pose
        yield return new WaitForFixedUpdate();

        // 5) Restore
        rb.detectCollisions = true;
        rb.isKinematic = false;
        rb.interpolation = oldInterp;

        // 6) Grace windows
        ignoreInputUntil = Time.time + resetFreezeSeconds;
        rb.angularVelocity = Vector3.zero;
        rb.Sleep();
        allowSpinAt = Time.time + freezeAngularSeconds;

        if (autoLock != null && autoLockWasEnabled) autoLock.enabled = true;

        isResetting = false;
        SyncGravityMode();
        Debug.Log("🔁 Reset done.");
    }
}
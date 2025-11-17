using UnityEngine;
using UnityEngine.XR;
using UnityEngine.UI;
using TMPro;
using UnityEngine.SceneManagement;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit;

[RequireComponent(typeof(Rigidbody))]
public class BoxControllerWithFriction : MonoBehaviour
{
    [Header("References")]
    public Transform rightHandTransform;
    public Rigidbody boxRb;
    public Transform xrOrigin;

    [Tooltip("Optional: reference to AutoLockToBox. If assigned, push is allowed only when this reports IsLocked.")]
    public AutoLockToBox lockManager;   // <-- assign in Inspector if you use AutoLockToBox

    [Header("Force Visuals")]
    public GameObject pushArrowPrefab;
    public GameObject velocityArrowPrefab;
    public GameObject normalArrowPrefab;
    public TextMeshPro pushArrowLabel;
    public TextMeshPro velocityArrowLabel;
    public TextMeshPro normalArrowLabel;

    private GameObject pushArrowInstance;
    private GameObject velocityArrowInstance;
    private GameObject normalArrowInstance;

    [Header("Pushing")]
    public float pushForce = 100f;
    public float maxDistance = 3f;
    public float maxPushAcceleration = 50f;

    [Header("Friction & Mass")]
    [Range(0, 1)] public float staticFrictionCoefficient = 0.5f;
    [Range(0, 1)] public float kineticFrictionCoefficient = 0.3f;
    public float boxMass = 10f;
    public float gravity = 9.81f;

    [Header("UI")]
    public Slider pushForceSlider, staticFrictionSlider, boxMassSlider, maxDistanceSlider;
    public TextMeshProUGUI pushForceText, staticFrictionText, boxMassText, maxDistanceText;
    public TextMeshProUGUI velocityText, normalForceText, frictionForceText;
    public Button resetButton, lockButton, sceneButton;
    public string nextScene = "";

    [Header("Manual Input Fields")]
    public GameObject pushForceFieldGO;
    public GameObject frictionFieldGO;
    public Button applyManualValuesButton;

    private TMP_InputField pushForceInputField;
    private TMP_InputField frictionInputField;

    [Header("Reset Pose")]
    public Vector3 resetPosition;
    public Vector3 resetRotation;

    [Header("Force Labels in Quad Canvas")]
    public TextMeshProUGUI velocityLabelUI;
    public TextMeshProUGUI normalForceLabelUI;
    public TextMeshProUGUI frictionForceLabelUI;

    const float startRadiusFraction = 0.5f;
    float pushStartSqr, pushStopSqr;

    InputDevice leftHand, rightHand;
    Vector3 handPos;
    bool userWantsToPush;

    // Local fallback lock state (only used if lockManager is not assigned)
    bool localIsLocked;
    Vector3 lockOffsetLocal;

    // ----- helpers -----
    bool Locked() => (lockManager != null) ? lockManager.IsLocked : localIsLocked;

    void SetLocked(bool value)
    {
        if (lockManager != null)
        {
            if (value) lockManager.LockToBox();
            else lockManager.UnlockFromBox();
        }
        else
        {
            localIsLocked = value;
        }
    }
    // -------------------

    void Awake()
    {
        if (!boxRb) boxRb = GetComponent<Rigidbody>();
        if (!xrOrigin) Debug.LogWarning("XR Origin not assigned!", this);

        boxRb.mass = boxMass;
        boxRb.interpolation = RigidbodyInterpolation.Interpolate;
        boxRb.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

        RecalculatePushDistanceThresholds();

        leftHand = InputDevices.GetDeviceAtXRNode(XRNode.LeftHand);
        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        if (pushArrowPrefab) pushArrowInstance = Instantiate(pushArrowPrefab);
        if (velocityArrowPrefab) velocityArrowInstance = Instantiate(velocityArrowPrefab);
        if (normalArrowPrefab) normalArrowInstance = Instantiate(normalArrowPrefab);

        if (resetButton) resetButton.onClick.AddListener(ResetObjectPosition);
        if (lockButton) lockButton.onClick.AddListener(ToggleLockXR);

        if (sceneButton && !string.IsNullOrEmpty(nextScene))
            sceneButton.onClick.AddListener(() => SceneManager.LoadScene(nextScene));
    }

    void Start()
    {
        if (pushForceFieldGO)
            pushForceInputField = pushForceFieldGO.GetComponentInChildren<TMP_InputField>();

        if (frictionFieldGO)
            frictionInputField = frictionFieldGO.GetComponentInChildren<TMP_InputField>();

        if (applyManualValuesButton)
            applyManualValuesButton.onClick.AddListener(ApplyManualInputs);

        if (pushForceSlider && pushForceText)
        {
            pushForceText.text = $"{pushForceSlider.value:F1}";
            pushForceSlider.onValueChanged.AddListener(val =>
            {
                pushForce = Mathf.Max(1f, val);
                pushForceText.text = $"{pushForce:F1}";
            });
        }

        if (staticFrictionSlider && staticFrictionText)
        {
            staticFrictionText.text = $"{staticFrictionSlider.value:F2}";
            staticFrictionSlider.onValueChanged.AddListener(val =>
            {
                staticFrictionCoefficient = val;
                staticFrictionText.text = $"{val:F2}";
            });
        }

        if (boxMassSlider && boxMassText)
        {
            boxMassText.text = $"{boxMassSlider.value:F1}";
            boxMassSlider.onValueChanged.AddListener(val =>
            {
                boxMass = val;
                boxRb.mass = val;
                boxMassText.text = $"{val:F1}";
            });
        }

        if (maxDistanceSlider && maxDistanceText)
        {
            maxDistanceText.text = $"{maxDistanceSlider.value:F1}";
            maxDistanceSlider.onValueChanged.AddListener(val =>
            {
                maxDistance = val;
                RecalculatePushDistanceThresholds();
                maxDistanceText.text = $"{val:F1}";
            });
        }
    }

    void Update()
    {
        if (!rightHandTransform) return;
        handPos = rightHandTransform.position;

        // Inputs
        rightHand.TryGetFeatureValue(CommonUsages.primaryButton, out bool aPressed); // only for friction readout
        leftHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);

        float distSqr = (boxRb.position - handPos).sqrMagnitude;
        bool isHoveringUI = EventSystem.current != null && EventSystem.current.IsPointerOverGameObject();

        // Require: locked + joystick active + near + not over UI
        bool stickActive = stick.sqrMagnitude > 0.01f;
        bool nearEnough = distSqr < pushStartSqr;

        userWantsToPush = Locked() && stickActive && nearEnough && !isHoveringUI;

        // Leash stop radius
        if (distSqr > pushStopSqr) userWantsToPush = false;

        // HUD
        if (velocityText) velocityText.text = $"{boxRb.velocity.magnitude:F2} m/s";
        if (aPressed) ShowStaticFriction();
        else if (frictionForceText) frictionForceText.text = "0.00 N";

        UpdateQuadCanvasUI();
    }

    void FixedUpdate() => ApplyForces();

    void LateUpdate()
    {
        // Only move the rig here if no AutoLockToBox is managing follow.
        if (lockManager == null && Locked() && xrOrigin)
            xrOrigin.position = transform.TransformPoint(lockOffsetLocal);

        UpdateForceArrows();
    }

    void ApplyForces()
    {
        if (pushForce < 0.1f) return;

        Vector3 surfaceNormal = Vector3.up;

        if (Physics.Raycast(transform.position + Vector3.up * 0.1f, Vector3.down, out RaycastHit hit, 1f))
            surfaceNormal = hit.normal;

        Vector3 gravityDir = -surfaceNormal.normalized;
        Vector3 gravityForce = gravityDir * gravity * boxMass;
        boxRb.AddForce(gravityForce);

        Vector3 velocity = boxRb.velocity;
        float normalForceMag = Vector3.Dot(surfaceNormal, gravityForce) * -1;

        if (velocity.sqrMagnitude > 0.001f)
        {
            Vector3 friction = -velocity.normalized * kineticFrictionCoefficient * normalForceMag;
            boxRb.AddForce(friction);
        }

        // Block user push unless locked & intent true
        if (!Locked() || !userWantsToPush) return;

        leftHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
        if (stick.sqrMagnitude < 0.01f) return;

        Vector3 inputDir = rightHandTransform.TransformDirection(new Vector3(stick.x, 0, stick.y).normalized);
        Vector3 pushDir = Vector3.ProjectOnPlane(inputDir, surfaceNormal).normalized;

        float accel = Mathf.Clamp(pushForce / boxMass, 0f, maxPushAcceleration);
        boxRb.AddForce(pushDir * accel, ForceMode.Acceleration);
    }

    void UpdateForceArrows()
    {
        UpdateArrow(velocityArrowInstance, boxRb.velocity, Color.blue, velocityArrowLabel, "Velocity");
        UpdateArrow(normalArrowInstance, Vector3.up * boxMass * gravity, Color.green, normalArrowLabel, "Normal");

        if (Locked() && userWantsToPush)
        {
            leftHand.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 stick);
            Vector3 pushDir = rightHandTransform.TransformDirection(new Vector3(stick.x, 0, stick.y).normalized);
            UpdateArrow(pushArrowInstance, pushDir * pushForce, Color.red, pushArrowLabel, "Push");
        }
        else if (pushArrowInstance)
        {
            pushArrowInstance.SetActive(false);
            if (pushArrowLabel) pushArrowLabel.gameObject.SetActive(false);
        }
    }

    void UpdateArrow(GameObject arrow, Vector3 dir, Color color, TextMeshPro label, string labelText)
    {
        float length = dir.magnitude;
        if (length < 0.01f)
        {
            arrow?.SetActive(false);
            if (label) label.gameObject.SetActive(false);
            return;
        }

        Vector3 offset = dir.normalized * 0.3f;
        Vector3 pos = boxRb.worldCenterOfMass + offset;

        if (arrow)
        {
            arrow.SetActive(true);
            arrow.transform.position = pos;
            arrow.transform.rotation = Quaternion.LookRotation(dir);
            arrow.transform.localScale = new Vector3(0.02f, 0.02f, length);
        }

        if (label)
        {
            label.gameObject.SetActive(true);
            label.text = labelText;
            label.transform.position = pos + Vector3.up * 0.1f;
            label.transform.rotation = Quaternion.LookRotation(Camera.main.transform.forward);
        }
    }

    void ShowStaticFriction()
    {
        float normalForce = boxMass * gravity;
        float fs = staticFrictionCoefficient * normalForce;
        if (frictionForceText)
            frictionForceText.text = $"{fs:F2} N";
    }

    void UpdateQuadCanvasUI()
    {
        if (velocityLabelUI) velocityLabelUI.text = $"{boxRb.velocity.magnitude:F2} m/s";
        if (normalForceLabelUI) normalForceLabelUI.text = $"{(boxMass * gravity):F2} N";
        if (frictionForceLabelUI) frictionForceLabelUI.text = $"{(staticFrictionCoefficient * boxMass * gravity):F2} N";
    }

    public void ResetObjectPosition()
    {
        transform.position = resetPosition;
        transform.eulerAngles = resetRotation;
        boxRb.velocity = Vector3.zero;
        boxRb.angularVelocity = Vector3.zero;
    }

    // --- Lock controls (work with or without AutoLockToBox) ---

    public void LockToXR()
    {
        if (!xrOrigin)
        {
            Debug.LogWarning("XR Origin not assigned!");
            return;
        }
        SetLocked(true);
        lockOffsetLocal = transform.InverseTransformPoint(xrOrigin.position);
        // If you froze physics on unlock: boxRb.isKinematic = false;
    }

    public void UnlockFromXR()
    {
        SetLocked(false);
        userWantsToPush = false; // stop pushing immediately

        if (pushArrowInstance) pushArrowInstance.SetActive(false);
        if (pushArrowLabel) pushArrowLabel.gameObject.SetActive(false);

        // If you want the box inert while unlocked: boxRb.isKinematic = true;
    }

    public void ToggleLockXR()
    {
        if (!xrOrigin)
        {
            Debug.LogWarning("XR Origin not assigned!");
            return;
        }
        bool wasLocked = Locked();
        SetLocked(!wasLocked);

        if (lockButton && lockButton.GetComponentInChildren<TextMeshProUGUI>())
            lockButton.GetComponentInChildren<TextMeshProUGUI>().text = Locked() ? "Unlock" : "Lock";

        if (wasLocked && !Locked())
        {
            userWantsToPush = false;
            if (pushArrowInstance) pushArrowInstance.SetActive(false);
            if (pushArrowLabel) pushArrowLabel.gameObject.SetActive(false);
            // Optional: boxRb.isKinematic = true;
        }
        else if (!wasLocked && Locked())
        {
            // Optional: boxRb.isKinematic = false;
        }
    }

    public void ApplyManualInputs()
    {
        if (pushForceInputField && float.TryParse(pushForceInputField.text, out float manualForce))
        {
            pushForce = manualForce;
            if (pushForceText) pushForceText.text = $"{manualForce:F1}";
            if (pushForceSlider) pushForceSlider.value = manualForce;
        }

        if (frictionInputField && float.TryParse(frictionInputField.text, out float manualFriction))
        {
            staticFrictionCoefficient = Mathf.Clamp01(manualFriction);
            if (staticFrictionText) staticFrictionText.text = $"{staticFrictionCoefficient:F2}";
            if (staticFrictionSlider) staticFrictionSlider.value = staticFrictionCoefficient;
        }

        RecalculatePushDistanceThresholds();
        Debug.Log($"✅ Manual Inputs Applied: Force = {pushForce}, Friction = {staticFrictionCoefficient}");
    }

    void RecalculatePushDistanceThresholds()
    {
        pushStartSqr = Mathf.Pow(maxDistance * startRadiusFraction, 2);
        pushStopSqr = maxDistance * maxDistance;
    }
}

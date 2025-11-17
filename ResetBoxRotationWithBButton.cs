using UnityEngine;
using UnityEngine.XR;

public class ResetRotationOnlyOnB : MonoBehaviour
{
    [Header("Target")]
    public GameObject targetBox;                 // If left empty, uses this GameObject

    [Header("Behavior")]
    public bool zeroAngularVelocity = true;      // Stop spinning but keep linear motion
    public bool enforceWhileHeld = true;         // If true, keep applying while B is held

    private InputDevice rightHand;
    private bool prevB = false;
    private bool requestOneShotReset = false;    // For single-press resets
    private Rigidbody rb;

    void Start()
    {
        if (targetBox == null) targetBox = this.gameObject;
        rb = targetBox.GetComponent<Rigidbody>();
        rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
    }

    void Update()
    {
        // Reacquire device if it drops
        if (!rightHand.isValid)
            rightHand = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);

        // Read B (secondary) button
        if (rightHand.TryGetFeatureValue(CommonUsages.secondaryButton, out bool bPressed))
        {
            // Edge trigger: one-shot reset on press
            if (bPressed && !prevB)
                requestOneShotReset = true;

            prevB = bPressed;
        }
    }

    void FixedUpdate()
    {
        bool bHeld = prevB; // last sampled state from Update()

        // Do we need to reset this tick?
        if (!requestOneShotReset && !(enforceWhileHeld && bHeld))
            return;

        // Consume one-shot flag (holding still enforces every tick)
        requestOneShotReset = false;

        if (rb != null)
        {
            if (zeroAngularVelocity)
                rb.angularVelocity = Vector3.zero; // keep linear velocity untouched

            // Reset rotation via physics-safe API
            rb.MoveRotation(Quaternion.identity);
        }
        else
        {
            targetBox.transform.rotation = Quaternion.identity;
        }
    }
}

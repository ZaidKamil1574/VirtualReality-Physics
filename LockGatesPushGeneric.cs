using UnityEngine;

/// Add this to the box. It enables the push component only while AutoLockToBox.IsLocked is true.
[DisallowMultipleComponent]
public class LockGatesPushGeneric : MonoBehaviour
{
    [Header("References")]
    public AutoLockToBox lockManager;           // drag your AutoLockToBox here
    [Tooltip("The component that actually applies push to the box (e.g., BoxForceWhileLocked, BoxControllerWithFriction, etc.).")]
    public Behaviour pushComponent;             // drag your pushing script component here

    [Header("Behavior")]
    [Tooltip("If checked, we keep Rigidbody.useGravity on even when push is disabled. (This only affects RB gravity, not your push script's custom gravity.)")]
    public Rigidbody rb;                        // optional; assign your box RB if you want to be explicit
    public bool keepRigidbodyGravity = true;    // optional

    void Awake()
    {
        if (!lockManager)
            Debug.LogWarning("[LockGatesPushGeneric] No AutoLockToBox assigned.", this);
        if (!pushComponent)
            Debug.LogError("[LockGatesPushGeneric] No push component assigned.", this);
        if (!rb) rb = GetComponent<Rigidbody>();
    }

    void Update()
    {
        if (!lockManager || !pushComponent) return;

        bool locked = lockManager.IsLocked;

        // Enable/disable the pushing script
        if (pushComponent.enabled != locked)
            pushComponent.enabled = locked;

        // Optional: leave RB gravity alone (most users want this on)
        if (rb)
            rb.useGravity = keepRigidbodyGravity;
    }
}

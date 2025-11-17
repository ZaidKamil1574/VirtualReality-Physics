using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class AlwaysGravity : MonoBehaviour
{
    [Tooltip("Extra gravity multiplier (1 = normal Unity gravity).")]
    public float gravityScale = 1f;

    [Header("References")]
    public AutoLockToBox autoLock;   // drag your AutoLockToBox here

    private Rigidbody rb;

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.useGravity = false; // we’ll apply gravity manually
    }

    void FixedUpdate()
    {
        // If AutoLockToBox is assigned and we're locked, do nothing.
        if (autoLock && autoLock.IsLocked) return;

        // Otherwise (unlocked, or no reference), apply Unity gravity
        rb.AddForce(Physics.gravity * gravityScale, ForceMode.Acceleration);
    }
}

using UnityEngine;
using UnityEngine.XR;

public class AutoLockToBox : MonoBehaviour
{
    public enum UnlockMode { DistanceFromHand }

    [Header("References")]
    public Transform playerTransform;   // XR Origin or Camera Offset (movable rig root)
    public Transform boxTransform;      // The box to follow

    [Header("Hands")]
    public Transform leftHandTransform;
    public Transform rightHandTransform;
    public bool requireBothHandsToLock = false;

    [Header("Hand Lock/Unlock Distances")]
    [Tooltip("Radius within which hands must enter to auto-lock.")]
    public float handLockDistance = 0.6f;
    [Tooltip("Radius beyond which hands must remain for 'hold' time to auto-unlock.")]
    public float handUnlockDistance = 0.9f;
    [Tooltip("How long hands must be outside unlock distance before unlocking.")]
    public float handUnlockHoldSeconds = 0.5f;

    [Header("Follow")]
    [Tooltip("If true, playerTransform snaps/lerps to follow the box while locked.")]
    public bool followRigWhileLocked = false;
    public bool lockInstantly = true;
    public float followLerp = 6f;

    [Header("Relock Cooldown")]
    public float relockCooldownSeconds = 0.4f;

    [Header("(Optional) Ring Sync")]
    public float lockDistance = 0.6f;

    [Header("Lock Behavior")]
    [Tooltip("If OFF, distance will NOT auto-unlock; lock persists until you call UnlockFromBox().")]
    public bool autoUnlockByDistance = true;

    [Tooltip("Set to true externally (e.g., while pushing) to pause distance-based unlock timing.")]
    public bool externalPushActive = false;

    // ---------- Lock Effects ----------
    [Header("Lock Effects")]
    [Tooltip("When true, disables all locomotion/teleport/turn providers while locked.")]
    public bool disableLocomotionWhileLocked = true;

    [Tooltip("Drag ContinuousMoveProvider/Turn/TeleportationProvider/etc here.")]
    public MonoBehaviour[] locomotionComponents;

    [Tooltip("Any extra input/ray scripts that can move the rig. (Optional)")]
    public MonoBehaviour[] otherInputsToDisable;

    private bool[] _locomotionWasEnabled;
    private bool[] _otherInputsWasEnabled;

    // --- State (public read-only) ---
    public bool IsLocked { get; private set; }

    // Internals
    public UnlockMode unlockMode = UnlockMode.DistanceFromHand;
    private Vector3 offset;                 // player - box at lock time
    private float outOfRangeTimer = 0f;     // accumulates while hands are out of range
    private float relockCooldown = 0f;
    private Collider _boxCollider;

    void Reset()
    {
        lockDistance = handLockDistance;
    }

    void Awake()
    {
        if (locomotionComponents != null)
            _locomotionWasEnabled = new bool[locomotionComponents.Length];

        if (otherInputsToDisable != null)
            _otherInputsWasEnabled = new bool[otherInputsToDisable.Length];

        if (boxTransform)
            _boxCollider = boxTransform.GetComponent<Collider>();
    }

    void OnValidate()
    {
        lockDistance = handLockDistance;
        handUnlockDistance = Mathf.Max(handUnlockDistance, handLockDistance);
    }

    void Update()
    {
        if (!playerTransform || !boxTransform) return;

        if (relockCooldown > 0f) relockCooldown -= Time.deltaTime;

        // distances (horizontal only, to closest point on box collider if present)
        float lDist = float.PositiveInfinity;
        float rDist = float.PositiveInfinity;

        if (leftHandTransform)  lDist = HorizontalDistance(leftHandTransform.position, BoxClosest(leftHandTransform.position));
        if (rightHandTransform) rDist = HorizontalDistance(rightHandTransform.position, BoxClosest(rightHandTransform.position));

        bool leftInRange   = leftHandTransform  && (lDist <= handLockDistance);
        bool rightInRange  = rightHandTransform && (rDist <= handLockDistance);

        bool leftOutUnlock  = !leftHandTransform  || (lDist > handUnlockDistance);
        bool rightOutUnlock = !rightHandTransform || (rDist > handUnlockDistance);

        // 1) Auto-lock by hand proximity
        if (!IsLocked && relockCooldown <= 0f)
        {
            bool canLock;
            if (requireBothHandsToLock)
            {
                bool needLeft  = leftHandTransform  != null;
                bool needRight = rightHandTransform != null;

                if (needLeft && needRight)        canLock = (leftInRange && rightInRange);
                else if (needLeft && !needRight)  canLock = leftInRange;
                else if (!needLeft && needRight)  canLock = rightInRange;
                else                               canLock = false;
            }
            else
            {
                bool anyAssigned = (leftHandTransform != null) || (rightHandTransform != null);
                bool anyIn       = (leftInRange || rightInRange);
                canLock = anyAssigned && anyIn;
            }

            if (canLock) LockToBox();
        }
        // 2) While locked: optional distance-based auto-unlock
        else if (IsLocked)
        {
            if (autoUnlockByDistance && !externalPushActive)
            {
                bool shouldCountOut;
                if (requireBothHandsToLock)
                {
                    // If either hand leaves, start counting out
                    shouldCountOut = (leftOutUnlock || rightOutUnlock);
                }
                else
                {
                    // If neither hand is within unlock distance, start counting out
                    bool leftInUnlockRange  = leftHandTransform  && (lDist <= handUnlockDistance);
                    bool rightInUnlockRange = rightHandTransform && (rDist <= handUnlockDistance);
                    shouldCountOut = !(leftInUnlockRange || rightInUnlockRange);
                }

                if (shouldCountOut)
                {
                    outOfRangeTimer += Time.deltaTime;
                    if (outOfRangeTimer >= handUnlockHoldSeconds)
                    {
                        UnlockFromBox();
                        return;
                    }
                }
                else
                {
                    outOfRangeTimer = 0f;
                }
            }

            if (followRigWhileLocked) Follow();
        }
    }

    void LateUpdate()
    {
        if (IsLocked && followRigWhileLocked) Follow();
    }

    private void Follow()
    {
        // Player follows the box
        Vector3 target = boxTransform.position + offset;
        if (lockInstantly)
        {
            playerTransform.position = target;
        }
        else
        {
            playerTransform.position = Vector3.Lerp(
                playerTransform.position,
                target,
                Time.deltaTime * Mathf.Max(1f, followLerp)
            );
        }
    }

    // ------------ Public control API ------------
    public void LockToBox()
    {
        if (!playerTransform || !boxTransform) return;
        IsLocked = true;
        offset = playerTransform.position - boxTransform.position;
        outOfRangeTimer = 0f;

        ApplyLockedEffects(true);
    }

    public void UnlockFromBox()
    {
        if (!IsLocked) return;
        IsLocked = false;
        relockCooldown = relockCooldownSeconds;
        outOfRangeTimer = 0f;

        externalPushActive = false; // ensure clean state
        ApplyLockedEffects(false);
    }
    // -------------------------------------------

    private void ApplyLockedEffects(bool locked)
    {
        if (!disableLocomotionWhileLocked) return;

        // Disable/restore locomotion providers
        if (locomotionComponents != null)
        {
            for (int i = 0; i < locomotionComponents.Length; i++)
            {
                var comp = locomotionComponents[i];
                if (!comp) continue;

                if (locked)
                {
                    _locomotionWasEnabled[i] = comp.enabled;
                    comp.enabled = false;
                }
                else
                {
                    comp.enabled = _locomotionWasEnabled[i];
                }
            }
        }

        // Disable/restore any other inputs you listed
        if (otherInputsToDisable != null)
        {
            for (int i = 0; i < otherInputsToDisable.Length; i++)
            {
                var comp = otherInputsToDisable[i];
                if (!comp) continue;

                if (locked)
                {
                    _otherInputsWasEnabled[i] = comp.enabled;
                    comp.enabled = false;
                }
                else
                {
                    comp.enabled = _otherInputsWasEnabled[i];
                }
            }
        }

        // If the rig uses a CharacterController via a move provider, clear residual motion.
        var cc = playerTransform ? playerTransform.GetComponentInChildren<CharacterController>() : null;
        if (cc) cc.Move(Vector3.zero);
    }

    // --------- Helpers for robust distance ----------
    private Vector3 BoxClosest(Vector3 fromWorld)
    {
        if (_boxCollider) return _boxCollider.ClosestPoint(fromWorld);
        return boxTransform ? boxTransform.position : fromWorld;
    }

    private static float HorizontalDistance(Vector3 a, Vector3 b)
    {
        a.y = b.y; // ignore height differences
        return Vector3.Distance(a, b);
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (!boxTransform) return;
        Gizmos.color = new Color(0f, 1f, 1f, 0.5f);
        Gizmos.DrawWireSphere(boxTransform.position, handLockDistance);
        Gizmos.color = new Color(1f, 0.3f, 0f, 0.5f);
        Gizmos.DrawWireSphere(boxTransform.position, handUnlockDistance);
    }
#endif
}
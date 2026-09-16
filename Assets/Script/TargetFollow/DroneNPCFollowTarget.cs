using System.Collections.Generic;
using UnityEngine;

public class DroneNPCFollowTarget : MonoBehaviour
{
    [Header("Target")]
    public Transform targetBox;

    [Header("Target Lock Settings")]
    [Tooltip("If true, the drone will ONLY follow when target is properly locked. Existence of a target alone will never make the drone start following.")]
    public bool requireTargetLock = true;

    [Tooltip("Whether the target is currently properly locked.")]
    public bool isTargetLocked = false;

    [Tooltip("Maximum distance from target before lock is automatically lost and movement stops. 0 disables distance auto-unlock.")]
    public float autoUnlockDistance = 50f;

    [Header("Movement")]
    public float moveSpeed = 5f;
    public float stopDistance = 2f;

    [Header("Raycast Avoidance - Horizontal")]
    public float rayDistance = 3f;
    public float rayHeight = 0.5f;
    public float sideAngle = 45f;

    [Header("Raycast Avoidance - Vertical")]
    public float verticalAngle = 35f;

    public LayerMask obstacleLayers = ~0;

    [Header("Ignored Colliders (anti-orbit)")]
    public List<Collider> ignoredColliders = new List<Collider>();

    [Header("Steering Smoothing (jitter fix)")]
    public float steeringSmoothSpeed = 8f;
    public float avoidCommitTime = 0.25f;

    [Header("Rotation - Yaw / Pitch / Roll")]
    public float rotationSpeed = 6f;
    public float bankAmount = 1.2f;
    public float maxBankAngle = 35f;

    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];
    private Vector3 _currentMoveDir;
    private Vector3 _committedAvoidDir;
    private float _avoidCommitTimer;
    private bool _isAvoiding;
    private DroneHealth _droneHealth;
    private Rigidbody _rb;

    /// <summary>
    /// Caches the Rigidbody and forces AI control mode on DroneInputs to prevent key conflicts with cannon controls.
    /// </summary>
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.useGravity = false;
            _rb.isKinematic = true;
        }

        // Ensure this NPC drone does not capture manual player keyboard inputs (WASD)
        DroneInputs inputs = GetComponent<DroneInputs>();
        if (inputs != null)
        {
            inputs.isAIControlled = true;
        }

        // If FlightControlSystem is on this same drone, disable it so its motor mixing does not fight NPC waypoint navigation
        FlightControlSystem fcs = GetComponent<FlightControlSystem>();
        if (fcs != null && enabled)
        {
            fcs.enabled = false;
        }
    }

    /// <summary>
    /// Validates initial target lock requirements on startup.
    /// </summary>
    private void Start()
    {
        // If targetBox is explicitly an FDrone, enforce target lock
        if (targetBox != null && targetBox.name.ToLower().Contains("fdrone"))
        {
            requireTargetLock = true;
        }
    }

    /// <summary>
    /// Evaluates target validity, lock state, distance limits, and steers the drone towards the target with obstacle avoidance.
    /// </summary>
    private void Update()
    {
        // 1. Target existence alone should never make the drone start following if lock is required
        // When unlocked (manually or automatically), movement must stop IMMEDIATELY!
        if (requireTargetLock && !isTargetLocked)
        {
            StopFollowing();
            return;
        }

        if (targetBox == null)
        {
            StopFollowing();
            return;
        }

        // 2. If target object itself is destroyed/inactive, automatically unlock and stop immediately
        if (!IsTargetValid(targetBox))
        {
            UnlockTarget();
            return;
        }

        Vector3 toTarget = targetBox.position - transform.position;
        float distance = toTarget.magnitude;

        // 3. Automatic distance-based unlock: If target moved beyond autoUnlockDistance, immediately stop!
        if (requireTargetLock && autoUnlockDistance > 0f && distance > autoUnlockDistance)
        {
            UnlockTarget();
            return;
        }

        // Smooth stop deceleration when approaching stop distance to eliminate 1-frame jitter
        if (distance <= stopDistance)
        {
            _currentMoveDir = Vector3.Lerp(_currentMoveDir, Vector3.zero, 10f * Time.deltaTime);
            if (_currentMoveDir.sqrMagnitude < 0.001f)
            {
                _currentMoveDir = Vector3.zero;
                return;
            }
        }

        Vector3 desiredDir = toTarget.normalized;

        float forwardDot = Vector3.Dot(transform.forward, desiredDir);
        bool waypointBehind = forwardDot < 0f && distance < rayDistance * 2f;
        if (waypointBehind)
        {
            _isAvoiding = false;
            _avoidCommitTimer = 0f;
            _currentMoveDir = desiredDir;
        }

        Vector3 safeDir = GetSafeDirection(desiredDir);
        if (safeDir.sqrMagnitude < 0.001f)
            return;

        if (_currentMoveDir.sqrMagnitude < 0.001f)
        {
            _currentMoveDir = safeDir;
        }
        else
        {
            float blendSpeed = waypointBehind ? steeringSmoothSpeed * 3f : steeringSmoothSpeed;
            _currentMoveDir = Vector3.Slerp(_currentMoveDir, safeDir, blendSpeed * Time.deltaTime).normalized;
        }

        float effectiveSpeed = moveSpeed;
        if (_droneHealth == null)
        {
            _droneHealth = GetComponent<DroneHealth>() ?? GetComponentInParent<DroneHealth>() ?? GetComponentInChildren<DroneHealth>();
        }

        // Apply 30% speed reduction exclusively to the exact drone targeted by the laser; all others fly at 100% normal speed
        if (_droneHealth != null && _droneHealth.IsTargetedByLaser)
        {
            effectiveSpeed *= 0.70f;
        }

        float moveAmount = Mathf.Min(effectiveSpeed * Time.deltaTime, Mathf.Max(0f, distance - stopDistance));
        if (moveAmount > 0.0001f)
        {
            transform.Translate(_currentMoveDir * moveAmount, Space.World);
        }

        if (_rb != null && !_rb.isKinematic)
        {
            _rb.linearVelocity = _currentMoveDir * effectiveSpeed;
        }

        ApplyFlightRotation(_currentMoveDir);
    }

    /// <summary>
    /// Resets active obstacle avoidance commitment direction and timer.
    /// </summary>
    public void ResetAvoidance()
    {
        _isAvoiding = false;
        _avoidCommitTimer = 0f;
        _committedAvoidDir = Vector3.zero;
    }

    /// <summary>
    /// Smoothly rotates the drone towards its travel direction with aerodynamic banking (roll into turn).
    /// </summary>
    /// <param name="direction">World space movement velocity vector.</param>
    private void ApplyFlightRotation(Vector3 direction)
    {
        if (direction.sqrMagnitude < 0.001f)
            return;

        Vector3 upReference = Mathf.Abs(Vector3.Dot(direction, Vector3.up)) > 0.99f
            ? transform.forward
            : Vector3.up;

        Quaternion lookRotation = Quaternion.LookRotation(direction, upReference);

        float yawDelta = Vector3.SignedAngle(transform.forward, direction, Vector3.up);
        float bankAngle = Mathf.Clamp(-yawDelta * bankAmount, -maxBankAngle, maxBankAngle);
        Quaternion bankRotation = Quaternion.AngleAxis(bankAngle, Vector3.forward);

        transform.rotation = Quaternion.Slerp(
            transform.rotation,
            lookRotation * bankRotation,
            rotationSpeed * Time.deltaTime
        );
    }

    /// <summary>
    /// Evaluates raycast candidates to determine an unobstructed flight path towards the target.
    /// </summary>
    /// <param name="desiredDir">Direct unblocked line-of-sight direction vector.</param>
    /// <returns>Clear candidate direction vector or Vector3.zero if all are blocked.</returns>
    private Vector3 GetSafeDirection(Vector3 desiredDir)
    {
        Vector3 rayOrigin = transform.position + Vector3.up * rayHeight;

        if (_isAvoiding)
        {
            _avoidCommitTimer -= Time.deltaTime;
            if (_avoidCommitTimer > 0f && !IsRayBlocked(rayOrigin, _committedAvoidDir))
                return _committedAvoidDir;
            _isAvoiding = false;
        }

        if (!IsRayBlocked(rayOrigin, desiredDir))
            return desiredDir;

        Vector3[] candidates = BuildCandidateDirections(desiredDir);

        System.Array.Sort(candidates, (a, b) =>
        {
            float dA = Vector3.SqrMagnitude(transform.position + a * rayDistance - targetBox.position);
            float dB = Vector3.SqrMagnitude(transform.position + b * rayDistance - targetBox.position);
            return dA.CompareTo(dB);
        });

        foreach (Vector3 candidate in candidates)
        {
            if (!IsRayBlocked(rayOrigin, candidate))
            {
                _committedAvoidDir = candidate;
                _avoidCommitTimer = avoidCommitTime;
                _isAvoiding = true;
                return candidate;
            }
        }

        return Vector3.zero;
    }

    /// <summary>
    /// Casts an obstacle detection ray checking for environmental colliders excluding self and ignored peers.
    /// </summary>
    /// <param name="origin">World raycast origin point.</param>
    /// <param name="direction">World raycast heading direction.</param>
    /// <returns>True if blocked by valid obstacle geometry.</returns>
    private bool IsRayBlocked(Vector3 origin, Vector3 direction)
    {
        int hitCount = Physics.RaycastNonAlloc(
            origin, direction, _hitBuffer, rayDistance,
            obstacleLayers, QueryTriggerInteraction.Ignore
        );

        for (int i = 0; i < hitCount; i++)
        {
            Collider col = _hitBuffer[i].collider;
            if (col == null)
                continue;

            if (col.transform.IsChildOf(transform))
                continue;

            if (ignoredColliders != null && ignoredColliders.Contains(col))
                continue;

            return true;
        }

        return false;
    }

    /// <summary>
    /// Generates fan array of candidate avoidance headings angled across pitch and yaw offsets.
    /// </summary>
    /// <param name="desiredDir">Base forward heading direction.</param>
    /// <returns>Array of candidate direction unit vectors.</returns>
    private Vector3[] BuildCandidateDirections(Vector3 desiredDir)
    {
        Vector3 rightAxis = Vector3.Cross(Vector3.up, desiredDir);
        if (rightAxis.sqrMagnitude < 0.0001f)
            rightAxis = transform.right;
        rightAxis.Normalize();

        Quaternion yawLeft = Quaternion.AngleAxis(-sideAngle, Vector3.up);
        Quaternion yawRight = Quaternion.AngleAxis(sideAngle, Vector3.up);
        Quaternion pitchUp = Quaternion.AngleAxis(-verticalAngle, rightAxis);
        Quaternion pitchDn = Quaternion.AngleAxis(verticalAngle, rightAxis);

        return new Vector3[]
        {
            yawLeft * desiredDir,
            yawRight * desiredDir,
            pitchUp * desiredDir,
            pitchDn * desiredDir,
            yawLeft * pitchUp * desiredDir,
            yawRight * pitchUp * desiredDir,
            yawLeft * pitchDn * desiredDir,
            yawRight * pitchDn * desiredDir,
        };
    }

    /// <summary>
    /// Checks whether target exists, is active in hierarchy, and is alive.
    /// </summary>
    public bool IsTargetValid(Transform target)
    {
        if (target == null) return false;
        if (!target.gameObject.activeInHierarchy) return false;

        DroneHealth health = target.GetComponent<DroneHealth>() ?? target.GetComponentInChildren<DroneHealth>() ?? target.GetComponentInParent<DroneHealth>();
        if (health != null && (health.IsDestroyed || health.health <= 0f)) return false;

        return true;
    }

    /// <summary>
    /// Locks onto a target and starts following it.
    /// </summary>
    public void LockTarget(Transform target)
    {
        targetBox = target;
        isTargetLocked = true;
        _isAvoiding = false;
        _avoidCommitTimer = 0f;
    }

    /// <summary>
    /// Unlocks target and stops following immediately.
    /// </summary>
    public void UnlockTarget()
    {
        isTargetLocked = false;
        StopFollowing();
    }

    /// <summary>
    /// Immediately stops all following behavior and ends tracking.
    /// </summary>
    public void StopFollowing()
    {
        isTargetLocked = false;
        _currentMoveDir = Vector3.zero;
        _committedAvoidDir = Vector3.zero;
        _avoidCommitTimer = 0f;
        _isAvoiding = false;

        Rigidbody rb = GetComponent<Rigidbody>();
        if (rb != null)
        {
            rb.linearVelocity = Vector3.zero;
            rb.angularVelocity = Vector3.zero;
        }
    }
}
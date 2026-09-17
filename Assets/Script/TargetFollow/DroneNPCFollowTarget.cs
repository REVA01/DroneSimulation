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

    [Header("Final Formation Arrival")]
    [Tooltip("Whether the drone is currently executing final formation approach and alignment.")]
    public bool isFormationMode = false;
    [Tooltip("Target world position of assigned formation slot.")]
    public Vector3 formationTargetPos;
    [Tooltip("Target forward heading direction vector for final formation orientation.")]
    public Vector3 formationTargetHeading = Vector3.forward;
    [Tooltip("Maximum allowed distance error from formation slot before marking position complete.")]
    public float formationPosTolerance = 0.35f;
    [Tooltip("Maximum allowed yaw heading error in degrees before marking rotation complete.")]
    public float formationRotTolerance = 4.0f;
    [Tooltip("Distance from slot where smooth deceleration begins.")]
    public float formationDecelDist = 3.0f;
    [Tooltip("Yaw alignment responsiveness multiplier.")]
    public float formationAlignStrength = 1.6f;
    [Tooltip("Minimum safe ground clearance in meters to continuously respect.")]
    public float minGroundClearance = 2.0f;
    [Tooltip("LayerMask used to check ground/terrain beneath drone.")]
    public LayerMask groundCheckLayers = ~0;
    [Tooltip("True once both position and rotation tolerances are satisfied.")]
    public bool isFormationComplete = false;

    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];
    private Vector3 _currentMoveDir;
    private Vector3 _committedAvoidDir;
    private float _avoidCommitTimer;
    private bool _isAvoiding;
    private DroneHealth _droneHealth;
    private Rigidbody _rb;
    private DroneInputs _droneInputs;
    private FlightControlSystem _fcs;
    private DroneHardware _droneHardware;

    /// <summary>
    /// Configures Rigidbody for physics simulation and connects DroneInputs, FlightControlSystem, and DroneHardware.
    /// </summary>
    private void Awake()
    {
        _rb = GetComponent<Rigidbody>();
        if (_rb != null)
        {
            _rb.isKinematic = false;
            _rb.useGravity = true;
        }

        _droneInputs = GetComponent<DroneInputs>();
        if (_droneInputs == null)
            _droneInputs = gameObject.AddComponent<DroneInputs>();
        _droneInputs.isAIControlled = true;

        _fcs = GetComponent<FlightControlSystem>();
        if (_fcs == null)
            _fcs = gameObject.AddComponent<FlightControlSystem>();
        _fcs.enabled = true;

        _droneHardware = GetComponent<DroneHardware>();
        if (_droneHardware == null)
            _droneHardware = gameObject.AddComponent<DroneHardware>();
        _droneHardware.enabled = true;
    }

    /// <summary>
    /// Validates initial target lock requirements and synchronizes with DroneDirector on startup.
    /// </summary>
    private void Start()
    {
        // If targetBox is explicitly an FDrone, enforce target lock
        if (targetBox != null && targetBox.name.ToLower().Contains("fdrone"))
        {
            requireTargetLock = true;
        }

        if (DroneDirector.Instance != null)
        {
            SyncFromDirector(DroneDirector.Instance);
        }
    }

    /// <summary>
    /// Applies authoritative navigation, rotation, and avoidance parameters centrally from DroneDirector.
    /// </summary>
    public void SyncFromDirector(DroneDirector director)
    {
        if (director == null) return;

        moveSpeed = director.cruiseSpeed;
        stopDistance = director.stopDistance;
        autoUnlockDistance = director.detectionRange;
        rotationSpeed = director.turnSpeed;
        bankAmount = director.bankAmount;
        maxBankAngle = director.maxBankAngle;
        steeringSmoothSpeed = director.steeringSmoothing;
        requireTargetLock = false;
        isTargetLocked = true;
    }

    /// <summary>
    /// Evaluates target validity, lock state, distance limits, and converts obstacle-avoiding flight intent into normalized DroneInputs.
    /// </summary>
    private void Update()
    {
        // Dedicated final formation arrival mode
        if (isFormationMode)
        {
            UpdateFormationArrival();
            return;
        }

        // 1. Target existence alone should never make the drone start following if lock is required
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

        // 3. Automatic distance-based unlock: If target moved beyond autoUnlockDistance, immediately stop
        if (requireTargetLock && autoUnlockDistance > 0f && distance > autoUnlockDistance)
        {
            UnlockTarget();
            return;
        }

        float driveFactor = 0f;
        if (distance > stopDistance)
        {
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
                safeDir = desiredDir;

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

            // Dynamic speed and arrival modulation
            float maxSpeed = (_fcs != null && _fcs.maxForwardSpeed > 0f) ? _fcs.maxForwardSpeed : Mathf.Max(moveSpeed, 10f);
            float cruiseSpeedFactor = Mathf.Clamp01(effectiveSpeed / maxSpeed);
            float arrivalFactor = Mathf.Clamp01((distance - stopDistance) / Mathf.Max(stopDistance * 1.5f, 1.5f));
            driveFactor = cruiseSpeedFactor * arrivalFactor;
        }
        else
        {
            _currentMoveDir = Vector3.zero;
        }

        // Transform world-space avoidance direction into drone local frame for cyclic control (Pitch & Roll)
        Vector3 localDir = (distance <= stopDistance || _currentMoveDir.sqrMagnitude < 0.001f)
            ? Vector3.zero
            : transform.InverseTransformDirection(_currentMoveDir);
        float pitch = Mathf.Clamp(localDir.z * driveFactor, -1f, 1f);
        float roll = Mathf.Clamp(localDir.x * driveFactor, -1f, 1f);

        // Altitude control (Throttle)
        float altitudeDelta = targetBox.position.y - transform.position.y;
        float throttle = 0f;
        if (Mathf.Abs(altitudeDelta) > 0.15f)
        {
            throttle = Mathf.Clamp(altitudeDelta / 2.0f, -1f, 1f);
        }

        // Heading / Yaw alignment: orient drone nose toward the target
        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        Vector3 flatAim = Vector3.ProjectOnPlane(toTarget, Vector3.up);
        float yaw = 0f;
        if (flatForward.sqrMagnitude > 0.001f && flatAim.sqrMagnitude > 0.001f)
        {
            float yawAngle = Vector3.SignedAngle(flatForward, flatAim, Vector3.up);
            if (Mathf.Abs(yawAngle) > 2.0f)
            {
                yaw = Mathf.Clamp(yawAngle / 35.0f, -1f, 1f);
            }
        }

        // Feed flight intentions to the physical FlightControlSystem pipeline
        if (_droneInputs != null)
        {
            _droneInputs.SetAIInputs(pitch, roll, yaw, throttle);
        }
    }

    /// <summary>
    /// Governs precise approach, smooth deceleration, exact heading alignment, continuous ground clearance, and PID hover stabilization for final formation slots.
    /// </summary>
    private void UpdateFormationArrival()
    {
        Vector3 myPos = transform.position;
        Vector3 toSlot = formationTargetPos - myPos;
        Vector3 horizToSlot = new Vector3(toSlot.x, 0f, toSlot.z);
        float horizDistance = horizToSlot.magnitude;
        float altError = Mathf.Abs(toSlot.y);
        float totalDistance = toSlot.magnitude;

        Vector3 flatForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
        if (flatForward.sqrMagnitude < 0.001f) flatForward = Vector3.forward;
        flatForward.Normalize();

        Vector3 flatHeading = Vector3.ProjectOnPlane(formationTargetHeading, Vector3.up);
        if (flatHeading.sqrMagnitude < 0.001f) flatHeading = Vector3.forward;
        flatHeading.Normalize();

        float yawAngle = Vector3.SignedAngle(flatForward, flatHeading, Vector3.up);
        float yawError = Mathf.Abs(yawAngle);

        bool inPosTolerance = (horizDistance <= formationPosTolerance) && (altError <= formationPosTolerance);
        bool inRotTolerance = yawError <= formationRotTolerance;

        // 1. Formation Complete Check & Hysteresis Lock
        if (isFormationComplete)
        {
            // If perturbed significantly outside deadband, re-engage gentle corrections
            if (horizDistance > formationPosTolerance * 1.5f || altError > formationPosTolerance * 1.5f || yawError > formationRotTolerance * 2.0f)
            {
                isFormationComplete = false;
            }
            else
            {
                // Drone is settled into final slot: stop sending corrections so PID cascade holds rock-solid hover
                if (_droneInputs != null)
                {
                    _droneInputs.SetAIInputs(0f, 0f, 0f, 0f);
                }
                return;
            }
        }

        if (inPosTolerance && inRotTolerance)
        {
            isFormationComplete = true;
            if (_droneInputs != null)
            {
                _droneInputs.SetAIInputs(0f, 0f, 0f, 0f);
            }
            return;
        }

        // 2. Approach Speed with Smooth Deceleration near slot
        float decelDist = Mathf.Max(formationDecelDist, 1.0f);
        float targetSpeed = moveSpeed;
        if (horizDistance <= decelDist)
        {
            float t = Mathf.Clamp01(horizDistance / decelDist);
            // Smooth ease out into arrival, with a healthy minimum speed (1.2 m/s) to avoid creeping stall
            targetSpeed = Mathf.Lerp(1.2f, moveSpeed, Mathf.SmoothStep(0f, 1f, t));
        }

        float maxForwardSpeed = (_fcs != null && _fcs.maxForwardSpeed > 0f) ? _fcs.maxForwardSpeed : 25f;
        float driveFactor = Mathf.Clamp01(targetSpeed / maxForwardSpeed);

        // 3. Cyclic Pitch & Roll in local frame
        float pitch = 0f;
        float roll = 0f;
        if (horizDistance > formationPosTolerance)
        {
            Vector3 desiredDir = horizToSlot.normalized;
            // Project desired move direction into drone's local frame
            Vector3 localDir = transform.InverseTransformDirection(desiredDir);
            pitch = Mathf.Clamp(localDir.z * driveFactor, -1f, 1f);
            roll = Mathf.Clamp(localDir.x * driveFactor, -1f, 1f);
        }

        // 4. Altitude Throttle Control
        float altDelta = formationTargetPos.y - myPos.y;
        float throttle = 0f;
        if (Mathf.Abs(altDelta) > 0.08f)
        {
            throttle = Mathf.Clamp(altDelta / 1.5f, -1f, 1f);
        }

        // 5. Active Ground Clearance Protection: continuously respect minGroundClearance while approaching & stabilizing
        if (minGroundClearance > 0.05f)
        {
            Vector3 rayOrigin = myPos + Vector3.up * 0.5f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit groundHit, minGroundClearance + 3.0f, groundCheckLayers, QueryTriggerInteraction.Ignore))
            {
                if (groundHit.collider != null &&
                    groundHit.collider.GetComponentInParent<FlightControlSystem>() == null &&
                    groundHit.collider.GetComponentInParent<DroneNPCFollowTarget>() == null)
                {
                    float currentClearance = myPos.y - groundHit.point.y;
                    if (currentClearance < minGroundClearance)
                    {
                        float clearanceDeficit = minGroundClearance - currentClearance;
                        float climbUrgency = Mathf.Clamp01(0.6f + (clearanceDeficit / minGroundClearance) * 0.4f);
                        throttle = Mathf.Max(throttle, climbUrgency);
                    }
                }
            }
        }

        // 6. Heading / Yaw Alignment to formation orientation
        float yaw = 0f;
        if (yawError > formationRotTolerance)
        {
            // Responsive yaw: progressive proportional control with minimum threshold
            float normalizedYaw = Mathf.Clamp(yawAngle / 30.0f, -1f, 1f);
            yaw = Mathf.Sign(yawAngle) * Mathf.Max(Mathf.Abs(normalizedYaw) * formationAlignStrength, 0.2f);
            yaw = Mathf.Clamp(yaw, -1f, 1f);
        }

        // 7. Feed commands to FlightControlSystem
        if (_droneInputs != null)
        {
            _droneInputs.SetAIInputs(pitch, roll, yaw, throttle);
        }
    }

    /// <summary>
    /// Activates dedicated final formation guidance with exact target position, heading, arrival tolerances, and ground clearance.
    /// </summary>
    public void EnterFormationMode(
        Vector3 targetPos,
        Vector3 targetHeading,
        float speed,
        float decelDist,
        float posTolerance,
        float rotTolerance,
        float alignStrength,
        float minClearance = 2.0f,
        LayerMask? groundLayers = null)
    {
        isFormationMode = true;
        formationTargetPos = targetPos;
        formationTargetHeading = targetHeading;
        moveSpeed = speed;
        formationDecelDist = decelDist;
        formationPosTolerance = posTolerance;
        formationRotTolerance = rotTolerance;
        formationAlignStrength = alignStrength;
        minGroundClearance = minClearance;
        if (groundLayers.HasValue) groundCheckLayers = groundLayers.Value;
        isFormationComplete = false;
        _isAvoiding = false;
        _avoidCommitTimer = 0f;
        _currentMoveDir = Vector3.zero;
    }

    /// <summary>
    /// Updates live formation target position and heading while remaining in formation mode.
    /// </summary>
    public void UpdateFormationTarget(Vector3 targetPos, Vector3 targetHeading)
    {
        formationTargetPos = targetPos;
        formationTargetHeading = targetHeading;
    }

    /// <summary>
    /// Updates live formation parameters during runtime tuning.
    /// </summary>
    public void UpdateFormationParameters(
        float speed,
        float decelDist,
        float posTolerance,
        float rotTolerance,
        float alignStrength,
        float minClearance = 2.0f,
        LayerMask? groundLayers = null)
    {
        moveSpeed = speed;
        formationDecelDist = decelDist;
        formationPosTolerance = posTolerance;
        formationRotTolerance = rotTolerance;
        formationAlignStrength = alignStrength;
        minGroundClearance = minClearance;
        if (groundLayers.HasValue) groundCheckLayers = groundLayers.Value;
    }

    /// <summary>
    /// Exits formation mode and returns to standard waypoint navigation.
    /// </summary>
    public void ExitFormationMode()
    {
        isFormationMode = false;
        isFormationComplete = false;
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
    /// Immediately stops all following behavior and commands the flight control system into a stable hover.
    /// </summary>
    public void StopFollowing()
    {
        isTargetLocked = false;
        isFormationMode = false;
        isFormationComplete = false;
        _currentMoveDir = Vector3.zero;
        _committedAvoidDir = Vector3.zero;
        _avoidCommitTimer = 0f;
        _isAvoiding = false;

        if (_droneInputs != null)
        {
            _droneInputs.SetAIInputs(0f, 0f, 0f, 0f);
        }
    }
}
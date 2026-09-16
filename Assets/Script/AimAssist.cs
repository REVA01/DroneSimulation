using System;
using UnityEngine;

/// <summary>
/// AimAssist: Automatically follows a drone whenever the laser points at or hits it.
/// 
/// How it works:
/// 1. No manual Inspector target setup: Target is detected 100% automatically in real-time.
/// 2. Automatic Laser Lock: Whenever you point the laser ray/beam at ANY active drone,
///    the system automatically detects that drone and begins tracking/following it.
/// 3. Continuous Following: The cannon rotates horizontally (base yaw) and vertically (barrel pitch)
///    to keep the laser centered on the drone until the drone is destroyed.
/// 4. Manual Override: If you press WASD/Arrows to steer away from the drone, the lock breaks.
/// 5. Safe: Never touches the drone's flight or physics systems; only rotates the cannon.
/// </summary>
public class AimAssist : MonoBehaviour
{
    [Header("--- Cannon Hierarchy References ---")]
    [Tooltip("The horizontal rotating base of the cannon (Yaw). Auto-detected if unassigned.")]
    public Transform canonBase;

    [Tooltip("The vertically tilting barrel of the cannon (Pitch). Auto-detected if unassigned.")]
    public Transform canonRotate;

    [Tooltip("The point where the laser beam originates. Auto-detected if unassigned.")]
    public Transform firePoint;

    [Header("--- Tracking Settings ---")]
    [Tooltip("Rotation speed (degrees per second) when following a locked drone.")]
    public float trackingSpeed = 100f;

    [Tooltip("Maximum pitch angle UP (negative degrees, e.g. -90).")]
    public float minVerticalAngle = -90f;

    [Tooltip("Maximum pitch angle DOWN (positive degrees, e.g. 30).")]
    public float maxVerticalAngle = 30f;

    [Tooltip("Angle in degrees from aim line to target to break lock when manually steering away.")]
    public float breakLockAngle = 20f;

    [Header("--- Target Release & Snap-Back Prevention ---")]
    [Tooltip("How long (in seconds) a released/unselected target is ignored by auto-lock to prevent snapping back.")]
    public float targetReleaseCooldown = 1.5f;

    [Tooltip("If true, any manual steering input (WASD / Arrows / Mouse) immediately breaks the lock without fighting tracking speed.")]
    public bool breakLockOnAnyManualInput = true;

    [Tooltip("Allow Right Mouse Click to immediately unselect / release the current target.")]
    public bool allowRightClickUnselect = true;

    [Tooltip("Key to explicitly unselect / drop the current target lock.")]
    public KeyCode explicitUnselectKey = KeyCode.Mouse1;

    [Tooltip("Require direct line-of-sight aim (raycast) to re-lock a previously released target instead of wide spherecast.")]
    public bool requireDirectAimToReacquire = true;

    [Header("--- Laser Detection Settings ---")]
    [Tooltip("Radius of the laser beam used for hit detection.")]
    public float beamHitRadius = 0.5f;

    [Tooltip("Maximum distance the laser can detect a drone.")]
    public float maxDistance = 150f;

    [Tooltip("Which physics layers the laser detection ray can hit.")]
    public LayerMask targetLayers = ~0;

    // The currently locked drone target (NOT serialized - no manual inspector setup)
    private GameObject currentTarget = null;

    // The most recently released target and cooldown timer to prevent instant snap-back
    private GameObject releasedTarget = null;
    private float releaseCooldownTimer = 0f;

    /// <summary>True while actively tracking a locked drone target and firing.</summary>
    public bool IsTracking => currentTarget != null && IsPlayerFiring();

    /// <summary>The currently locked drone target GameObject (read only).</summary>
    public GameObject CurrentTarget => currentTarget;

    /// <summary>The drone target that was most recently released/unselected.</summary>
    public GameObject ReleasedTarget => releasedTarget;

    /// <summary>Remaining cooldown time before released target can be re-locked.</summary>
    public float ReleaseCooldownRemaining => releaseCooldownTimer;

    private float currentPitch = 0f;
    private Quaternion initialGunRotation;
    private CanonMovement canonMovement;

    /// <summary>
    /// Captures pristine initial barrel rotation in Awake.
    /// </summary>
    private void Awake()
    {
        canonMovement = GetComponent<CanonMovement>() ?? GetComponentInParent<CanonMovement>() ?? GetComponentInChildren<CanonMovement>();
        if (canonMovement != null)
        {
            if (canonBase == null) canonBase = canonMovement.canonBase;
            if (canonRotate == null) canonRotate = canonMovement.canonRotate;
            if (firePoint == null) firePoint = canonMovement.firePoint;
        }

        if (canonRotate != null)
        {
            initialGunRotation = canonRotate.localRotation;
        }
    }

    /// <summary>
    /// Initializes cannon hierarchy references and links with FinalGame on Start.
    /// </summary>
    private void Start()
    {
        canonMovement = GetComponent<CanonMovement>() ?? GetComponentInParent<CanonMovement>() ?? GetComponentInChildren<CanonMovement>();

        // Auto-assign references from CanonMovement if not set in Inspector
        if (canonMovement != null)
        {
            if (canonBase == null) canonBase = canonMovement.canonBase;
            if (canonRotate == null) canonRotate = canonMovement.canonRotate;
            if (firePoint == null) firePoint = canonMovement.firePoint;

            minVerticalAngle = canonMovement.minVerticalAngle;
            maxVerticalAngle = canonMovement.maxVerticalAngle;

            if (canonMovement.beamHitRadius > 0.05f)
            {
                beamHitRadius = canonMovement.beamHitRadius;
            }

            if (canonMovement.maxDistance > 0f)
            {
                maxDistance = canonMovement.maxDistance;
            }

            targetLayers = canonMovement.hitLayers;
        }

        // Cache initial barrel rotation
        if (canonRotate != null)
        {
            initialGunRotation = canonRotate.localRotation;
        }
    }

    /// <summary>
    /// Evaluated every frame:
    /// - Decrements target-release cooldown timer.
    /// - Checks for explicit target unselection input.
    /// - If the player is not firing the laser, aim assist is disabled and any target lock is released.
    /// - If the player is firing and a target is locked, follows it unless manual steering overrides.
    /// - If the player is firing and no target is locked, checks if the laser points at any valid drone.
    /// </summary>
    private void Update()
    {
        // 1. Update target release cooldown timer
        if (releaseCooldownTimer > 0f)
        {
            releaseCooldownTimer -= Time.deltaTime;
            if (releaseCooldownTimer <= 0f)
            {
                releaseCooldownTimer = 0f;
            }
        }

        // 2. Check for explicit unselection action (Right Click or custom key)
        if (allowRightClickUnselect && (Input.GetKeyDown(explicitUnselectKey) || Input.GetMouseButtonDown(1)))
        {
            if (currentTarget != null)
            {
                ReleaseCurrentTarget("Explicit unselect button pressed");
                return;
            }
        }

        // 3. Aim assist only works while the player is actively firing the laser
        if (!IsPlayerFiring())
        {
            if (currentTarget != null)
            {
                ClearCurrentTargetOnFireStop();
            }
            return;
        }

        // 4. If currently following a drone
        if (currentTarget != null)
        {
            // If the drone was destroyed or deactivated, stop following immediately
            if (!IsDroneAlive(currentTarget))
            {
                ReleaseCurrentTarget("Target destroyed or inactive");
                return;
            }

            // Keep following the drone
            TrackTarget();
        }
        else
        {
            // 5. No target currently locked:
            // Only check if user is not actively providing manual steering input
            if (!HasManualSteeringInput())
            {
                CheckLaserRayHit();
            }
        }
    }

    /// <summary>
    /// Detects whether the user is actively providing manual steering input via WASD, Arrows, or Mouse.
    /// </summary>
    public bool HasManualSteeringInput()
    {
        bool hasHorizontal = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ||
                             Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);

        bool hasVertical = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ||
                           Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);

        bool hasMouse = (canonMovement != null && canonMovement.allowMouseFire) &&
                        (Mathf.Abs(Input.GetAxis("Mouse X")) > 0.08f || Mathf.Abs(Input.GetAxis("Mouse Y")) > 0.08f);

        return hasHorizontal || hasVertical || hasMouse;
    }

    /// <summary>
    /// Checks whether a drone is currently eligible for target locking.
    /// Strictly rejects drones that are on release cooldown or when manual steering is active.
    /// </summary>
    public bool CanLockTarget(GameObject drone)
    {
        if (drone == null || !IsDroneAlive(drone)) return false;

        // Never lock while player is providing manual steering input
        if (breakLockOnAnyManualInput && HasManualSteeringInput())
        {
            return false;
        }

        // Enforce release cooldown: the unselected drone cannot be re-locked until cooldown expires
        if (drone == releasedTarget && releaseCooldownTimer > 0f)
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Immediately releases the current target, clears all references, and applies the release cooldown.
    /// </summary>
    public void ReleaseCurrentTarget(string reason = "")
    {
        if (currentTarget != null)
        {
            releasedTarget = currentTarget;
            releaseCooldownTimer = targetReleaseCooldown;

            GameObject oldTarget = currentTarget;
            currentTarget = null;

            if (canonMovement != null)
            {
                canonMovement.ClearTargetDrone(oldTarget);
            }

            Debug.Log($"<color=yellow>[AimAssist] Target released ({reason}): {oldTarget.name}. Cooldown: {targetReleaseCooldown:F1}s.</color>");
        }
    }

    /// <summary>
    /// Clears the current target when the laser ceases firing.
    /// </summary>
    public void ClearCurrentTargetOnFireStop()
    {
        if (currentTarget != null)
        {
            GameObject old = currentTarget;
            currentTarget = null;
            if (canonMovement != null)
            {
                canonMovement.ClearTargetDrone(old);
            }
        }
    }

    /// <summary>
    /// Called when a drone is destroyed, disabled, or removed from the scene.
    /// Completely wipes all cached references and prevents snapping to its old position.
    /// </summary>
    public void OnDroneDestroyedOrDisabled(GameObject drone)
    {
        if (drone == null) return;

        if (currentTarget == drone)
        {
            currentTarget = null;
        }

        if (releasedTarget == drone)
        {
            releasedTarget = null;
            releaseCooldownTimer = 0f;
        }

        if (canonMovement != null)
        {
            currentPitch = canonMovement.CurrentPitch;
        }
    }

    /// <summary>
    /// Checks whether the player is currently firing the laser.
    /// Aim assist only activates when the laser is actively firing.
    /// </summary>
    public bool IsPlayerFiring()
    {
        if (canonMovement == null)
        {
            canonMovement = GetComponent<CanonMovement>() ?? GetComponentInParent<CanonMovement>() ?? GetComponentInChildren<CanonMovement>();
        }

        if (canonMovement != null)
        {
            if (canonMovement.IsLaserActive)
            {
                return true;
            }

            bool isKeyHeld = Input.GetKey(canonMovement.fireKey) ||
                             (canonMovement.allowMouseFire && Input.GetMouseButton(0));
            if (isKeyHeld) return true;
        }
        else
        {
            if (Input.GetKey(KeyCode.F) || Input.GetMouseButton(0))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Casts a ray along the laser direction. If it points at any eligible drone,
    /// that drone is locked as the target to follow.
    /// Prioritizes direct precision Raycast before falling back to SphereCast,
    /// and strictly enforces the target release cooldown.
    /// </summary>
    private void CheckLaserRayHit()
    {
        if (breakLockOnAnyManualInput && HasManualSteeringInput()) return;

        Transform originTrans = firePoint != null ? firePoint : canonRotate;
        if (originTrans == null) return;

        Vector3 rayOrigin = originTrans.position;
        Vector3 rayDirection = originTrans.forward;

        LayerMask mask = canonMovement != null ? canonMovement.hitLayers : targetLayers;

        // 1. Direct precision Raycast check first:
        // Allows deliberate re-locking if the user aims directly at a target whose cooldown expired
        if (Physics.Raycast(rayOrigin, rayDirection, out RaycastHit directHit, maxDistance, mask, QueryTriggerInteraction.Ignore))
        {
            if (!IsPartOfCannon(directHit.collider))
            {
                GameObject drone = ResolveDroneRoot(directHit.collider);
                if (drone != null && IsDroneAlive(drone) && CanLockTarget(drone))
                {
                    TryLockTarget(drone);
                    return;
                }
            }
        }

        // 2. Secondary SphereCast check for general target acquisition:
        // Strictly excludes released targets from wide spherecast snapping!
        RaycastHit[] hits = Physics.SphereCastAll(rayOrigin, beamHitRadius, rayDirection, maxDistance, mask, QueryTriggerInteraction.Ignore);
        Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        for (int i = 0; i < hits.Length; i++)
        {
            Collider col = hits[i].collider;
            if (col == null || IsPartOfCannon(col)) continue;

            GameObject drone = ResolveDroneRoot(col);
            if (drone != null && IsDroneAlive(drone))
            {
                // Never allow a wide spherecast to snag a previously released target
                if (drone == releasedTarget && requireDirectAimToReacquire)
                {
                    continue;
                }

                if (CanLockTarget(drone))
                {
                    TryLockTarget(drone);
                    return;
                }
            }
        }
    }

    /// <summary>
    /// Automatically rotates the cannon base (Yaw) and barrel (Pitch) to follow the locked drone.
    /// Manual input immediately breaks the lock cleanly without fighting the player.
    /// </summary>
    private void TrackTarget()
    {
        if (currentTarget == null || canonBase == null || canonRotate == null) return;

        // 1. Immediate Manual Steering Override:
        // If user actively provides any manual steering input, break lock instantly!
        if (breakLockOnAnyManualInput && HasManualSteeringInput())
        {
            ReleaseCurrentTarget("Manual steering input override");
            return;
        }

        // Target position (center of collider if present)
        Vector3 targetPos = currentTarget.transform.position;
        Collider col = currentTarget.GetComponentInChildren<Collider>();
        if (col != null)
        {
            targetPos = col.bounds.center;
        }

        Vector3 origin = firePoint != null ? firePoint.position : canonRotate.position;
        Vector3 toTarget = targetPos - origin;
        float distance = toTarget.magnitude;

        if (distance < 0.1f) return;

        Vector3 toTargetDir = toTarget / distance;
        Vector3 aimDir = firePoint != null ? firePoint.forward : canonRotate.forward;

        // Secondary angle threshold check (if breakLockOnAnyManualInput is disabled)
        float angleToTarget = Vector3.Angle(aimDir, toTargetDir);
        if (HasManualSteeringInput() && angleToTarget > breakLockAngle)
        {
            ReleaseCurrentTarget("Manual steering angle threshold exceeded");
            return;
        }

        bool hasHorizontalInput = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ||
                                  Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);

        bool hasVerticalInput = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ||
                                Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);

        // 1. Horizontal Base Yaw Rotation (only if user is not providing horizontal steering)
        if (!hasHorizontalInput)
        {
            Vector3 aimFlat = firePoint.forward;
            aimFlat.y = 0f;
            Vector3 targetFlat = toTarget;
            targetFlat.y = 0f;

            if (aimFlat.sqrMagnitude > 0.001f && targetFlat.sqrMagnitude > 0.001f)
            {
                float yawDelta = Vector3.SignedAngle(aimFlat.normalized, targetFlat.normalized, Vector3.up);
                float yawStep = Mathf.MoveTowards(0f, yawDelta, trackingSpeed * Time.deltaTime);
                canonBase.Rotate(Vector3.up, yawStep, Space.World);
            }
        }

        // 2. Vertical Barrel Pitch Tilt (only if user is not providing vertical steering)
        if (!hasVerticalInput)
        {
            float horizontalDistance = Mathf.Sqrt(toTarget.x * toTarget.x + toTarget.z * toTarget.z);
            float targetElevation = Mathf.Atan2(toTarget.y, horizontalDistance) * Mathf.Rad2Deg;

            float aimHorizontalDist = Mathf.Sqrt(firePoint.forward.x * firePoint.forward.x + firePoint.forward.z * firePoint.forward.z);
            float currentElevation = Mathf.Atan2(firePoint.forward.y, Mathf.Max(0.001f, aimHorizontalDist)) * Mathf.Rad2Deg;

            float elevationError = targetElevation - currentElevation;

            float pitchStep = Mathf.MoveTowards(0f, elevationError, trackingSpeed * Time.deltaTime);
            currentPitch -= pitchStep;
            currentPitch = Mathf.Clamp(currentPitch, minVerticalAngle, maxVerticalAngle);

            canonRotate.localRotation = initialGunRotation * Quaternion.Euler(currentPitch, 0f, 0f);

            if (canonMovement != null)
            {
                canonMovement.SyncPitch(currentPitch);
            }
        }
        else
        {
            if (canonMovement != null)
            {
                currentPitch = canonMovement.CurrentPitch;
            }
        }
    }

    /// <summary>
    /// Locks onto the specified drone target and starts automatic following.
    /// Strictly respects CanLockTarget to prevent snap-back to released targets.
    /// </summary>
    /// <param name="drone">The drone GameObject to lock onto.</param>
    /// <returns>True if the lock was successfully established.</returns>
    public bool TryLockTarget(GameObject drone)
    {
        if (!IsPlayerFiring()) return false;
        if (!CanLockTarget(drone)) return false;

        currentTarget = drone;

        // If explicitly re-locking the released target, clear release state
        if (drone == releasedTarget)
        {
            releasedTarget = null;
            releaseCooldownTimer = 0f;
        }

        if (canonMovement != null)
        {
            currentPitch = canonMovement.CurrentPitch;
            canonMovement.SetTargetDrone(drone);
        }

        Debug.Log($"<color=cyan>[AimAssist] Laser locked on drone: {drone.name}. Following target.</color>");
        return true;
    }

    /// <summary>
    /// Checks whether a drone GameObject is currently active and alive.
    /// </summary>
    public bool IsDroneAlive(GameObject drone)
    {
        if (drone == null || !drone.activeInHierarchy) return false;

        DroneHealth health = drone.GetComponent<DroneHealth>() ?? drone.GetComponentInChildren<DroneHealth>();
        if (health != null && (health.IsDestroyed || health.health <= 0f))
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Resolves the root GameObject of a drone from any of its child colliders.
    /// </summary>
    public GameObject ResolveDroneRoot(Collider col)
    {
        if (col == null) return null;

        // 1. Tag check
        if (col.CompareTag("Drone")) return col.gameObject;
        if (col.transform.root != null && col.transform.root.CompareTag("Drone")) return col.transform.root.gameObject;

        // 2. Drone components check
        DroneHealth health = col.GetComponentInParent<DroneHealth>();
        if (health != null) return health.gameObject;

        DroneNPCFollowTarget follow = col.GetComponentInParent<DroneNPCFollowTarget>();
        if (follow != null) return follow.gameObject;

        FlightControlSystem fcs = col.GetComponentInParent<FlightControlSystem>();
        if (fcs != null) return fcs.gameObject;

        // 3. Rigidbody check
        if (col.attachedRigidbody != null)
        {
            string rbName = col.attachedRigidbody.name.ToLower();
            if (rbName.Contains("drone") || rbName.Contains("tactical"))
            {
                return col.attachedRigidbody.gameObject;
            }
        }

        // 4. Name check
        string rootName = col.transform.root.name.ToLower();
        string colName = col.name.ToLower();
        if (rootName.Contains("drone") || rootName.Contains("tactical") || colName.Contains("drone"))
        {
            return col.transform.root.gameObject;
        }

        return null;
    }

    /// <summary>
    /// Checks whether a collider is part of the cannon structure to prevent self-collision.
    /// </summary>
    private bool IsPartOfCannon(Collider col)
    {
        if (col == null) return false;
        if (col.transform.IsChildOf(transform)) return true;
        if (canonBase != null && (col.transform == canonBase || col.transform.IsChildOf(canonBase))) return true;
        if (canonRotate != null && (col.transform == canonRotate || col.transform.IsChildOf(canonRotate))) return true;
        if (transform.root != null && col.transform.root == transform.root) return true;
        return false;
    }
}
using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// AimAssist: Automatic Target-Lock and Tracking System.
/// 
/// Expected Flow:
/// User Locks Drone -> Aim Assist Activated -> Automatically Follow Target ->
/// Target Destroyed -> Aim Assist Deactivated -> Target Permanently Completed -> User Input Has No Effect.
/// </summary>
public class AimAssist : MonoBehaviour
{
    [Header("--- Cannon & Aim References ---")]
    [Tooltip("Parent base object that rotates horizontally (yaw 360°). Auto-detected if null.")]
    public Transform canonBase;

    [Tooltip("Gun barrel that tilts vertically (pitch up/down). Auto-detected if null.")]
    public Transform canonRotate;

    [Tooltip("Point from where the aim raycast/laser originates. Auto-detected if null.")]
    public Transform firePoint;

    [Header("--- Target Lock Settings ---")]
    [Tooltip("Key to lock onto the drone currently aimed at.")]
    public KeyCode lockKey = KeyCode.E;

    [Tooltip("Allow locking with Right Mouse Button.")]
    public bool allowRightClickLock = true;

    [Tooltip("If true, laser contact can establish lock.")]
    public bool lockOnLaserTouch = true;

    [Tooltip("Cooldown period after target destruction before a new lock can be acquired.")]
    public float postDestructionLockCooldown = 0.5f;

    [Tooltip("Maximum distance to acquire a lock.")]
    public float lockMaxDistance = 150f;

    [Tooltip("Lock detection beam/cone radius.")]
    public float lockRadius = 1.5f;

    [Tooltip("Max angle from aim line to snap lock onto a drone when pressing lock button.")]
    public float lockConeAngle = 25f;

    [Tooltip("Layers to consider when raycasting for a lock.")]
    public LayerMask targetLayers = ~0;

    [Header("--- Smooth Tracking Settings ---")]
    [Tooltip("Smoothing time (seconds) to ease tracking onto the drone. Higher = smoother/softer, Lower = more snappy.")]
    [Range(0.01f, 0.5f)]
    public float trackingSmoothTime = 0.1f;

    [Tooltip("Maximum horizontal rotation speed (degrees/sec) when smoothly following the locked drone.")]
    public float trackingYawSpeed = 120f;

    [Tooltip("Maximum vertical tilt speed (degrees/sec) when smoothly following the locked drone.")]
    public float trackingPitchSpeed = 80f;

    [Tooltip("Maximum pitch angle UP (negative degrees, e.g. -60).")]
    public float minVerticalAngle = -60f;

    [Tooltip("Maximum pitch angle DOWN (positive degrees, e.g. 10).")]
    public float maxVerticalAngle = 10f;

    [Header("--- Laser Integration ---")]
    [Tooltip("Laser firing is decoupled from Aim Assist. Kept false so Aim Assist never auto-fires.")]
    public bool autoFireLaser = false;

    [Header("--- Manual Override / Disengage Settings ---")]
    [Tooltip("Angle in degrees between cannon aim direction and target drone required to break lock when manually steering.")]
    public float breakLockAngle = 15f;

    [Tooltip("Maximum angle (degrees) from crosshairs before the drone is considered lost/left.")]
    public float maxTrackingAngle = 30f;

    [Tooltip("Time in seconds the laser can be off the target before Aim Assist disengages.")]
    public float maxOffTargetDuration = 0.5f;

    [Tooltip("Cooldown period preventing immediate re-lock onto the drone that was just manually abandoned or unlocked.")]
    public float disengagedTargetCooldown = 1.5f;

    [Header("--- Status (Read Only) ---")]
    [Tooltip("Whether Aim Assist is currently actively tracking a locked target.")]
    [SerializeField] private bool isTracking = false;

    [Tooltip("The currently locked target GameObject.")]
    [SerializeField] private GameObject currentTarget = null;

    [Tooltip("Total count of permanently completed/destroyed targets.")]
    [SerializeField] private int completedTargetCount = 0;

    // Public properties
    public bool IsTracking => isTracking;
    public GameObject CurrentTarget => currentTarget;

    /// <summary>
    /// True when AimAssist is in a cooldown period after unlock/disengage/destruction,
    /// preventing automatic re-lock via lockOnLaserTouch.
    /// </summary>
    public bool IsInCooldown => lockCooldownTimer > 0f || disengagedCooldownTimer > 0f;

    // Set of permanently completed/destroyed targets (cannot be re-locked or re-tracked)
    private readonly HashSet<int> completedTargetIds = new HashSet<int>();

    private float currentPitch = 0f;
    private Quaternion initialGunRotation;
    private float lockCooldownTimer = 0f;
    private bool hadManualInputLastFrame = false;
    private GameObject disengagedTarget = null;
    private float disengagedCooldownTimer = 0f;
    private float yawVelocity = 0f;
    private float pitchVelocity = 0f;
    private float offTargetTimer = 0f;
    private FinalGame finalGame;

    private void Awake()
    {
        InitializeReferences();
    }

    private void Start()
    {
        if (canonRotate != null)
        {
            initialGunRotation = canonRotate.localRotation;
        }

        if (finalGame == null)
        {
            finalGame = GetComponent<FinalGame>() ?? GetComponentInParent<FinalGame>() ?? GetComponentInChildren<FinalGame>();
        }
    }

    private void Update()
    {
        if (lockCooldownTimer > 0f)
        {
            lockCooldownTimer -= Time.deltaTime;
        }

        if (disengagedCooldownTimer > 0f)
        {
            disengagedCooldownTimer -= Time.deltaTime;
            if (disengagedCooldownTimer <= 0f)
            {
                disengagedTarget = null;
            }
        }

        // 1. Check if we have an active lock
        if (isTracking && currentTarget != null)
        {
            // Check if target is destroyed or inactive
            if (!IsTargetAlive(currentTarget))
            {
                OnTargetDestroyed();
                return;
            }

            // If player is not firing (not holding F / Left Mouse), laser is off -> stop everything!
            if (finalGame != null)
            {
                bool isFireHeld = Input.GetKey(finalGame.fireKey) || (finalGame.allowMouseFire && Input.GetMouseButton(0));
                if (!isFireHeld)
                {
                    DisengageLock();
                    return;
                }
            }

            // Allow player to toggle lock off by pressing lock key (E or Right Click)
            bool lockKeyPressed = Input.GetKeyDown(lockKey) || (allowRightClickLock && Input.GetMouseButtonDown(1));
            if (lockKeyPressed)
            {
                DisengageLock();
                return;
            }

            // Automatically follow and maintain aim on the locked drone
            TrackLockedTarget();
        }
        else
        {
            // If somehow tracking state was dangling without target, reset everything
            if (isTracking || yawVelocity != 0f || pitchVelocity != 0f)
            {
                isTracking = false;
                currentTarget = null;
                yawVelocity = 0f;
                pitchVelocity = 0f;
            }

            // 2. Not locked: Listen for user lock input (E or Right Mouse Click)
            CheckForLockInput();
        }
    }

    #region Initialization

    private void InitializeReferences()
    {
        // Auto-find canonBase if unassigned
        if (canonBase == null)
        {
            if (transform.name.ToLower().Contains("base"))
                canonBase = transform;
            else if (transform.parent != null && transform.parent.name.ToLower().Contains("base"))
                canonBase = transform.parent;
            else if (transform.root != null)
                canonBase = transform.root;
            else
                canonBase = transform;
        }

        // Auto-find canonRotate if unassigned
        if (canonRotate == null && canonBase != null)
        {
            Transform[] children = canonBase.GetComponentsInChildren<Transform>(true);
            foreach (var child in children)
            {
                string lower = child.name.ToLower();
                if (lower == "gun" || lower.Contains("barrel") || lower.Contains("rotate"))
                {
                    canonRotate = child;
                    break;
                }
            }
        }

        // Auto-find firePoint if unassigned
        if (firePoint == null && canonBase != null)
        {
            Transform[] allChildren = canonBase.GetComponentsInChildren<Transform>(true);
            foreach (var child in allChildren)
            {
                string lower = child.name.ToLower();
                if (lower == "firepoint" || lower.Contains("muzzle"))
                {
                    firePoint = child;
                    break;
                }
            }

            if (firePoint == null)
            {
                firePoint = (canonRotate != null) ? canonRotate : transform;
            }
        }

        if (finalGame == null)
        {
            finalGame = GetComponent<FinalGame>() ?? GetComponentInParent<FinalGame>() ?? GetComponentInChildren<FinalGame>();
        }
    }

    #endregion

    #region Target Lock Logic

    /// <summary>
    /// Checks for user lock input (E key or Right Mouse Click).
    /// </summary>
    private void CheckForLockInput()
    {
        if (lockCooldownTimer > 0f) return;

        bool lockKeyPressed = Input.GetKeyDown(lockKey) || (allowRightClickLock && Input.GetMouseButtonDown(1));

        if (lockKeyPressed)
        {
            // Explicit user lock request overrides disengaged target cooldown
            disengagedTarget = null;
            disengagedCooldownTimer = 0f;
            TryAcquireLockFromAim();
        }
    }

    /// <summary>
    /// Attempts to lock onto a drone directly touched by the crosshair/aim direction.
    /// Does not auto-snap to nearby drones in a wide cone.
    /// </summary>
    public bool TryAcquireLockFromAim()
    {
        if (lockCooldownTimer > 0f) return false;
        if (firePoint == null) return false;

        Vector3 startPos = firePoint.position;
        Vector3 direction = firePoint.forward;

        // Perform ray/sphere cast to detect a drone directly touched by the crosshair
        RaycastHit[] hits = Physics.SphereCastAll(startPos, lockRadius, direction, lockMaxDistance, targetLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (IsPartOfCannon(hit.collider)) continue;

            GameObject potentialDrone = ResolveDroneRoot(hit.collider);
            if (potentialDrone != null && IsTargetAlive(potentialDrone))
            {
                return TryLockTarget(potentialDrone);
            }
        }

        return false;
    }

    /// <summary>
    /// Locks onto a specific drone GameObject.
    /// </summary>
    public bool TryLockTarget(GameObject drone)
    {
        if (drone == null) return false;
        if (lockCooldownTimer > 0f) return false;

        // If player recently manually steered away from this drone, do not re-lock until cooldown expires
        if (drone == disengagedTarget && disengagedCooldownTimer > 0f)
        {
            return false;
        }

        // Target must be alive and active
        if (!IsTargetAlive(drone))
        {
            return false;
        }

        // Sync initial pitch to avoid sudden jerk when tracking begins
        if (canonRotate != null)
        {
            Quaternion relRot = Quaternion.Inverse(initialGunRotation) * canonRotate.localRotation;
            Vector3 euler = relRot.eulerAngles;
            currentPitch = (euler.x > 180f) ? euler.x - 360f : euler.x;
        }

        // Lock established
        currentTarget = drone;
        isTracking = true;
        hadManualInputLastFrame = false;
        offTargetTimer = 0f;
        yawVelocity = 0f;
        pitchVelocity = 0f;

        Debug.Log($"<color=cyan>[AimAssist] TARGET LOCKED: {drone.name}! Aim Assist activated. Automatic tracking engaged.</color>");
        return true;
    }

    public void SyncInitialRotation(Quaternion rot)
    {
        initialGunRotation = rot;
    }

    #endregion

    #region Automatic Tracking
 
    /// <summary>
    /// Rule 2: Automatically tracks and follows the locked drone.
    /// Controls base yaw (horizontal) and barrel pitch (vertical) continuously.
    /// If player provides manual input (WASD / Arrows), manual input takes priority on that axis.
    /// Aim Assist continues following unpressed axes and auto-firing the laser to destroy the drone.
    /// As soon as manual input stops, Aim Assist immediately resumes following on both axes.
    /// </summary>
    private void TrackLockedTarget()
    {
        if (currentTarget == null || canonBase == null || canonRotate == null) return;

        Vector3 targetCenter = currentTarget.transform.position;

        // Try to get center of collider if available
        Collider col = currentTarget.GetComponentInChildren<Collider>();
        if (col != null)
        {
            targetCenter = col.bounds.center;
        }

        Vector3 origin = (firePoint != null) ? firePoint.position : canonRotate.position;
        Vector3 toTarget = targetCenter - origin;

        if (toTarget.sqrMagnitude < 0.01f) return;

        // Check if player is providing manual movement input (WASD / Arrows)
        bool hasHorizontalInput = Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow) ||
                                  Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow);

        bool hasVerticalInput = Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow) ||
                                Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow);

        bool hasManualInput = hasHorizontalInput || hasVerticalInput;

        Vector3 aimDir = (firePoint != null) ? firePoint.forward : canonRotate.forward;
        float angleToTarget = Vector3.Angle(aimDir, toTarget);

        // USER INPUT HAS HIGHEST PRIORITY:
        // If the user provides manual input (WASD / Arrows), manual input immediately takes 100% priority.
        // If the user steers away from the drone target, immediately break the lock so user has complete freedom!
        if (hasManualInput)
        {
            hadManualInputLastFrame = true;

            if (angleToTarget > breakLockAngle)
            {
                DisengageLock();
                return;
            }
        }
        else
        {
            if (hadManualInputLastFrame && angleToTarget > breakLockAngle)
            {
                DisengageLock();
                return;
            }
            hadManualInputLastFrame = false;
        }

        // Check 1: Target behind the cannon or aim direction - NEVER follow from back!
        Vector3 baseForward = (canonBase != null) ? canonBase.forward : transform.forward;
        if (Vector3.Dot(baseForward, toTarget) <= 0f || Vector3.Dot(aimDir, toTarget) <= 0f)
        {
            DisengageLock();
            return;
        }

        // Check 2: Angle to target exceeds maxTrackingAngle (front cone) - automatically unlock!
        if (angleToTarget > maxTrackingAngle)
        {
            DisengageLock();
            return;
        }

        // Check 3: Target flew out of combat range
        if (toTarget.sqrMagnitude > (lockMaxDistance * lockMaxDistance))
        {
            DisengageLock();
            return;
        }

        // Check 4: Aim Assist only works while the laser is on the drone.
        // If the laser is not on the drone, Aim Assist must NOT follow the drone!
        bool isLaserOnDrone = IsLaserHittingTarget(currentTarget);
        if (!isLaserOnDrone)
        {
            yawVelocity = 0f;
            pitchVelocity = 0f;

            offTargetTimer += Time.deltaTime;
            if (offTargetTimer >= maxOffTargetDuration)
            {
                DisengageLock();
                return;
            }

            // Do not follow this frame because laser is not on the drone!
            return;
        }

        offTargetTimer = 0f;

        // --- 1. Horizontal Base Yaw Rotation (Smooth Tracing) ---
        // If player gives horizontal input, player input takes priority (AimAssist doesn't force yaw)
        if (!hasHorizontalInput)
        {
            Vector3 flatDir = toTarget;
            flatDir.y = 0f;

            if (flatDir.sqrMagnitude > 0.001f)
            {
                float targetYaw = Quaternion.LookRotation(flatDir.normalized, Vector3.up).eulerAngles.y;
                float currentYaw = canonBase.eulerAngles.y;
                float newYaw = Mathf.SmoothDampAngle(currentYaw, targetYaw, ref yawVelocity, trackingSmoothTime, trackingYawSpeed, Time.deltaTime);
                Vector3 baseEuler = canonBase.eulerAngles;
                canonBase.rotation = Quaternion.Euler(baseEuler.x, newYaw, baseEuler.z);
            }
        }
        else
        {
            yawVelocity = 0f;
        }

        // --- 2. Vertical Barrel Pitch Tilt (Smooth Tracing) ---
        // If player gives vertical input, player input takes priority (AimAssist doesn't force pitch)
        if (!hasVerticalInput)
        {
            Vector3 localDir = canonBase.InverseTransformDirection(toTarget.normalized);
            float horizontalDistance = Mathf.Sqrt(localDir.x * localDir.x + localDir.z * localDir.z);

            // Calculate pitch angle (negative is pitch up in CanonMovement/FinalGame)
            float targetPitch = -Mathf.Atan2(localDir.y, horizontalDistance) * Mathf.Rad2Deg;
            targetPitch = Mathf.Clamp(targetPitch, minVerticalAngle, maxVerticalAngle);

            currentPitch = Mathf.SmoothDamp(currentPitch, targetPitch, ref pitchVelocity, trackingSmoothTime, trackingPitchSpeed, Time.deltaTime);
            currentPitch = Mathf.Clamp(currentPitch, minVerticalAngle, maxVerticalAngle);
            canonRotate.localRotation = initialGunRotation * Quaternion.Euler(currentPitch, 0f, 0f);

            if (finalGame != null)
            {
                finalGame.SyncPitch(currentPitch);
            }
        }
        else
        {
            pitchVelocity = 0f;
        }
    }

    /// <summary>
    /// Disengages the target lock when the player manually steers the cannon/laser away from the drone.
    /// Clears tracking and target, sets cooldowns to prevent snap-back, and stops auto-laser if fire key is not held.
    /// </summary>
    public void DisengageLock()
    {
        if (!isTracking && currentTarget == null) return;

        GameObject abandonedTarget = currentTarget;
        currentTarget = null;
        isTracking = false;
        hadManualInputLastFrame = false;
        offTargetTimer = 0f;
        yawVelocity = 0f;
        pitchVelocity = 0f;

        // Prevent immediate re-locking onto the drone player just abandoned
        disengagedTarget = abandonedTarget;
        disengagedCooldownTimer = disengagedTargetCooldown;
        lockCooldownTimer = 1.0f;

        // If player is not holding fire key (F or Left Click), turn off laser
        if (finalGame != null)
        {
            bool isFireHeld = Input.GetKey(finalGame.fireKey) || (finalGame.allowMouseFire && Input.GetMouseButton(0));
            if (!isFireHeld)
            {
                finalGame.StopLaser();
            }
            finalGame.SyncPitch(currentPitch);
        }

        Debug.Log("<color=yellow>[AimAssist] Manual override: Player steered away from target. Lock disengaged.</color>");
    }

    /// <summary>
    /// Checks whether the cannon aim/laser currently touches or intersects the target drone.
    /// </summary>
    public bool IsLaserHittingTarget(GameObject target)
    {
        if (target == null) return false;
        Transform originTrans = (firePoint != null) ? firePoint : canonRotate;
        if (originTrans == null && canonBase != null) originTrans = canonBase;
        if (originTrans == null) originTrans = transform;

        Vector3 origin = originTrans.position;
        Vector3 dir = originTrans.forward;

        // 1. Raycast check
        RaycastHit[] hits = Physics.RaycastAll(origin, dir, lockMaxDistance, targetLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in hits)
        {
            if (hit.collider == null || IsPartOfCannon(hit.collider)) continue;
            GameObject root = ResolveDroneRoot(hit.collider);
            if (root == target || hit.collider.gameObject == target || hit.collider.transform.IsChildOf(target.transform) || target.transform.IsChildOf(hit.collider.transform)) return true;
            break; // Hit something else in front
        }

        // 2. SphereCast check (with laser beam thickness)
        RaycastHit[] sHits = Physics.SphereCastAll(origin, lockRadius, dir, lockMaxDistance, targetLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(sHits, (a, b) => a.distance.CompareTo(b.distance));
        foreach (var hit in sHits)
        {
            if (hit.collider == null || IsPartOfCannon(hit.collider)) continue;
            GameObject root = ResolveDroneRoot(hit.collider);
            if (root == target || hit.collider.gameObject == target || hit.collider.transform.IsChildOf(target.transform) || target.transform.IsChildOf(hit.collider.transform)) return true;
            break; // Hit something else
        }

        return false;
    }

    /// <summary>
    /// Syncs current pitch angle when player adjusts pitch via manual controls.
    /// </summary>
    public void SyncPitch(float pitch)
    {
        currentPitch = pitch;
    }

    #endregion

    #region Target Destruction & Post-Destruction

    /// <summary>
    /// Rule 3 & 4: Called immediately when the locked drone is destroyed.
    /// Stops tracking, deactivates Aim Assist, and marks target as permanently completed.
    /// </summary>
    public void OnTargetDestroyed()
    {
        GameObject destroyed = currentTarget;
        if (destroyed != null)
        {
            int id = destroyed.GetHashCode();
            completedTargetIds.Add(id);
            completedTargetCount = completedTargetIds.Count;

            Debug.Log($"<color=yellow>[AimAssist] Target {destroyed.name} DESTROYED! Aim Assist deactivated. Target permanently completed.</color>");
        }

        // Immediately stop tracking
        isTracking = false;
        currentTarget = null;
        hadManualInputLastFrame = false;
        offTargetTimer = 0f;
        disengagedTarget = null;
        disengagedCooldownTimer = 0f;
        yawVelocity = 0f;
        pitchVelocity = 0f;

        // Set cooldown so no accidental auto-lock or frame-overlap lock can occur
        lockCooldownTimer = postDestructionLockCooldown;

        // Turn off laser, require fire button release, and sync pitch with manual controller
        if (finalGame != null)
        {
            finalGame.OnTargetDroneDestroyed(destroyed);
            finalGame.SyncPitch(currentPitch);
        }

        // Rule 4: System must NOT automatically acquire another drone
    }

    /// <summary>
    /// Checks if a drone target is alive and active.
    /// </summary>
    private bool IsTargetAlive(GameObject targetObj)
    {
        if (targetObj == null) return false;
        if (!targetObj.activeInHierarchy) return false;

        DroneHealth health = targetObj.GetComponent<DroneHealth>() ?? targetObj.GetComponentInChildren<DroneHealth>() ?? targetObj.GetComponentInParent<DroneHealth>();
        if (health != null && (health.IsDestroyed || health.health <= 0f)) return false;

        return true;
    }

    #endregion

    #region Helpers

    /// <summary>
    /// Resolves the root GameObject of a drone from a collider.
    /// </summary>
    private GameObject ResolveDroneRoot(Collider hitCol)
    {
        if (hitCol == null) return null;

        MonoBehaviour[] comps = hitCol.GetComponentsInParent<MonoBehaviour>(true);
        foreach (var c in comps)
        {
            if (c == null) continue;
            string tName = c.GetType().Name;
            if (tName == "FlightControlSystem" || tName == "DroneHardware" ||
                tName == "DroneBrain" || tName == "DroneNPCFollowTarget" || tName == "DroneHealth")
            {
                return c.gameObject;
            }
        }

        string n = hitCol.name.ToLower();
        string rn = hitCol.transform.root.name.ToLower();
        if (n.Contains("drone") || rn.Contains("drone") || n.Contains("tactical") || rn.Contains("tactical"))
        {
            return hitCol.transform.root.gameObject;
        }

        return null;
    }

    private bool IsPartOfCannon(Collider col)
    {
        if (col == null) return false;
        if (canonBase != null && (col.transform == canonBase || col.transform.IsChildOf(canonBase))) return true;
        if (transform.root != null && col.transform.root == transform.root) return true;
        return false;
    }

    /// <summary>
    /// Collects all active drones from DroneDirector or scene.
    /// </summary>
    private List<GameObject> GetAllActiveDrones()
    {
        List<GameObject> list = new List<GameObject>();

        if (DroneDirector.Instance != null && DroneDirector.Instance.squad != null)
        {
            for (int i = 0; i < DroneDirector.Instance.squad.Count; i++)
            {
                var entry = DroneDirector.Instance.squad[i];
                if (entry != null && entry.droneObject != null && entry.droneObject.activeInHierarchy)
                {
                    list.Add(entry.droneObject);
                }
            }
        }

        // Fallback: search MonoBehaviours in scene if DroneDirector squad is empty
        if (list.Count == 0)
        {
            MonoBehaviour[] allScripts = FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Exclude);
            foreach (var s in allScripts)
            {
                if (s == null) continue;
                string sName = s.GetType().Name;
                if (sName == "FlightControlSystem" || sName == "DroneBrain" || sName == "DroneHealth")
                {
                    if (s.gameObject.activeInHierarchy && !list.Contains(s.gameObject))
                    {
                        list.Add(s.gameObject);
                    }
                }
            }
        }

        return list;
    }

    #endregion
}

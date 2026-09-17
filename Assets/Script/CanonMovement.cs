using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// CanonMovement: Unified Controller combining Cannon Movement, Laser Firing,
/// and Drone Destruction (deactivating via SetActive(false) on laser hit).
/// </summary>
public class CanonMovement : MonoBehaviour
{
    [Header("--- Cannon Movement Setup ---")]
    [Tooltip("Parent base object that rotates horizontally (yaw 360°). If empty, will auto-detect.")]
    public Transform canonBase;

    [Tooltip("Gun barrel that tilts vertically (pitch up/down). If empty, will auto-detect.")]
    public Transform canonRotate;

    [Header("--- Movement Settings ---")]
    [Tooltip("Rotation speed for base yaw (A/D or Left/Right arrows).")]
    public float baseRotationSpeed = 60f;

    [Tooltip("Tilt speed for barrel pitch (W/S or Up/Down arrows).")]
    public float barrelRotationSpeed = 40f;

    [Tooltip("Maximum pitch angle UP (negative degrees).")]
    public float minVerticalAngle = -60f;

    [Tooltip("Maximum pitch angle DOWN (positive degrees).")]
    public float maxVerticalAngle = 10f;

    [Header("--- Laser Setup ---")]
    [Tooltip("Point from where the laser beam originates. If empty, auto-finds 'FirePoint'.")]
    public Transform firePoint;

    [Tooltip("LineRenderer used to render the laser beam. Auto-created if null.")]
    public LineRenderer lineRenderer;

    [Header("--- Laser Settings ---")]
    [Tooltip("Keyboard key to fire the laser.")]
    public KeyCode fireKey = KeyCode.F;

    [Tooltip("Allow firing with Mouse Left-Click in addition to the keyboard key.")]
    public bool allowMouseFire = true;

    [Tooltip("Maximum range of the laser beam.")]
    public float maxDistance = 200f;

    [Tooltip("How long the laser beam stays visible after a single tap (seconds).")]
    public float laserDuration = 0.2f;

    [Tooltip("Visual thickness of the laser line.")]
    public float laserWidth = 0.12f;

    [Tooltip("Color of the laser beam.")]
    public Color laserColor = Color.red;

    [Tooltip("Beam radius for hit detection (helps easily hit fast-moving drones).")]
    public float beamHitRadius = 0.35f;

    [Header("--- Collision & Targeting ---")]
    [Tooltip("Physics layers the laser raycast can hit.")]
    public LayerMask hitLayers = ~0;

    [Header("--- Laser Target Tracking ---")]
    [Tooltip("The specific drone currently targeted and hit by the laser.")]
    [SerializeField] private GameObject currentTargetDrone = null;
    public GameObject CurrentTargetDrone => currentTargetDrone;

    private DroneHealth currentTargetDroneHealth = null;

    private float currentPitch = 0f;
    private Quaternion initialGunRotation;
    private float laserTimer = 0f;
    private float cannonAttackCooldownTimer = 0f;
    private int lastDamagedFrame = -1;

    public float CurrentPitch => currentPitch;
    public bool IsLaserActive => (lineRenderer != null && lineRenderer.enabled) ||
                                 Input.GetKey(fireKey) ||
                                 (allowMouseFire && Input.GetMouseButton(0)) ||
                                 laserTimer > 0f;

    /// <summary>
    /// Clears the currently targeted drone reference and removes its laser targeting state.
    /// </summary>
    public void ClearTargetDrone(GameObject targetToClear = null)
    {
        if (targetToClear == null || currentTargetDrone == targetToClear)
        {
            if (currentTargetDroneHealth != null)
            {
                currentTargetDroneHealth.SetLaserTargeted(false);
                currentTargetDroneHealth = null;
            }
            currentTargetDrone = null;
        }
    }

    /// <summary>
    /// Synchronizes currently targeted drone from AimAssist or external caller.
    /// </summary>
    public void SetTargetDrone(GameObject newTarget)
    {
        UpdateTargetedDrone(newTarget);
    }

    /// <summary>
    /// Called immediately when a drone is destroyed by the laser or health reaches 0.
    /// Wipes all target references in both CanonMovement and AimAssist.
    /// If the player is still holding fire (F or Left Click), the laser remains active.
    /// If the fire key is not held, the laser turns off immediately.
    /// </summary>
    public void OnTargetDroneDestroyed(GameObject destroyedDrone)
    {
        ClearTargetDrone(destroyedDrone);

        AimAssist aim = GetComponent<AimAssist>() ?? GetComponentInParent<AimAssist>() ?? GetComponentInChildren<AimAssist>();
        if (aim != null)
        {
            aim.OnDroneDestroyedOrDisabled(destroyedDrone);
        }

        bool isKeyHeld = Input.GetKey(fireKey) || (allowMouseFire && Input.GetMouseButton(0));

        // If player is not holding fire key (F / Left Mouse), turn off laser
        if (!isKeyHeld)
        {
            StopLaser();
        }
    }

    /// <summary>
    /// Disables redundant legacy scripts and initializes laser rendering.
    /// </summary>
    private void Awake()
    {
        DisableDuplicateScripts();
        InitializeLaser();
    }

    /// <summary>
    /// Auto-detects cannon transforms and ensures CanonHealth is attached.
    /// </summary>
    private void Start()
    {
        InitializeCannonMovement();

        Transform healthTarget = (canonBase != null) ? canonBase : transform.root;
        if (healthTarget.GetComponentInChildren<CanonHealth>() == null && healthTarget.GetComponentInParent<CanonHealth>() == null)
        {
            healthTarget.gameObject.AddComponent<CanonHealth>();
        }
    }

    /// <summary>
    /// Processes manual cannon rotation and laser firing each frame.
    /// </summary>
    private void Update()
    {
        if (cannonAttackCooldownTimer > 0f)
        {
            cannonAttackCooldownTimer -= Time.deltaTime;
        }

        HandleCannonMovement();
        HandleLaserFiring();
    }

    /// <summary>
    /// Shuts down the laser beam and clears target tracking when the cannon is disabled or destroyed.
    /// </summary>
    private void OnDisable()
    {
        StopLaser();
    }

    #region Duplicate Prevention

    /// <summary>
    /// Searches the transform hierarchy and disables obsolete LasserGun or duplicate CanonMovement scripts.
    /// </summary>
    private void DisableDuplicateScripts()
    {
        Transform rootTransform = (canonBase != null) ? canonBase : transform.root;
        MonoBehaviour[] allScripts = rootTransform.GetComponentsInChildren<MonoBehaviour>(true);

        foreach (var s in allScripts)
        {
            if (s == null || s == this) continue;
            string sName = s.GetType().Name;
            if (sName == "LasserGun" || sName == "CanonMovement")
            {
                s.enabled = false;
            }
            else if (sName == "FinalGame" && s != this)
            {
                s.enabled = false;
            }
        }
    }

    #endregion

    #region Initialization

    /// <summary>
    /// Resolves base and barrel transform hierarchies and records initial barrel orientation.
    /// </summary>
    private void InitializeCannonMovement()
    {
        if (canonBase == null)
        {
            if (transform.name.ToLower().Contains("base"))
            {
                canonBase = transform;
            }
            else if (transform.parent != null && transform.parent.name.ToLower().Contains("base"))
            {
                canonBase = transform.parent;
            }
            else if (transform.root != null)
            {
                canonBase = transform.root;
            }
            else
            {
                canonBase = transform;
            }
        }

        if (canonRotate == null)
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

        if (canonRotate != null)
        {
            initialGunRotation = canonRotate.localRotation;
        }
    }

    /// <summary>
    /// Auto-locates the muzzle fire point and configures the LineRenderer component.
    /// </summary>
    private void InitializeLaser()
    {
        if (firePoint == null)
        {
            Transform searchRoot = (canonBase != null) ? canonBase : transform.root;
            Transform[] allChildren = searchRoot.GetComponentsInChildren<Transform>(true);
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

        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }
        }

        ConfigureLineRenderer();
    }

    /// <summary>
    /// Configures LineRenderer widths, materials, colors, and initial visibility state.
    /// </summary>
    private void ConfigureLineRenderer()
    {
        if (lineRenderer == null) return;

        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = laserWidth;
        lineRenderer.endWidth = laserWidth;
        lineRenderer.useWorldSpace = true;

        if (lineRenderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null)
            {
                lineRenderer.material = new Material(shader);
            }
        }

        lineRenderer.startColor = laserColor;
        lineRenderer.endColor = laserColor;
        lineRenderer.enabled = false;
    }

    #endregion

    #region Cannon Movement

    /// <summary>
    /// Reads player horizontal (A/D) and vertical (W/S) input and rotates cannon base and barrel.
    /// </summary>
    private void HandleCannonMovement()
    {
        // Horizontal Base Yaw Rotation (A / D or Left / Right Arrows)
        float horizontalInput = 0f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontalInput += 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontalInput -= 1f;

        // Vertical Barrel Pitch Tilt (W / S or Up / Down Arrows)
        float verticalInput = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) verticalInput += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) verticalInput -= 1f;

        bool hasHorizontalInput = Mathf.Abs(horizontalInput) > 0.01f;
        bool hasVerticalInput = Mathf.Abs(verticalInput) > 0.01f;

        // 1. Horizontal Base Yaw Rotation (A / D)
        if (hasHorizontalInput && canonBase != null)
        {
            canonBase.Rotate(Vector3.up, horizontalInput * baseRotationSpeed * Time.deltaTime, Space.World);
        }

        // 2. Vertical Barrel Pitch Tilt (W / S)
        if (hasVerticalInput && canonRotate != null)
        {
            currentPitch -= verticalInput * barrelRotationSpeed * Time.deltaTime;
            currentPitch = Mathf.Clamp(currentPitch, minVerticalAngle, maxVerticalAngle);
            canonRotate.localRotation = initialGunRotation * Quaternion.Euler(currentPitch, 0f, 0f);
        }
    }

    /// <summary>
    /// Synchronizes pitch angle when manual or external controller sets pitch.
    /// </summary>
    public void SyncPitch(float pitch)
    {
        currentPitch = Mathf.Clamp(pitch, minVerticalAngle, maxVerticalAngle);
        if (canonRotate != null)
        {
            canonRotate.localRotation = initialGunRotation * Quaternion.Euler(currentPitch, 0f, 0f);
        }
    }

    #endregion

    #region Laser & Drone Destruction

    /// <summary>
    /// Checks for user fire key input (F or Left Click) and manages laser beam triggering and duration.
    /// Also checks for explicit unselect (Right Click or custom unselect key).
    /// </summary>
    private void HandleLaserFiring()
    {
        AimAssist aim = GetComponent<AimAssist>() ?? GetComponentInParent<AimAssist>() ?? GetComponentInChildren<AimAssist>();
        if (Input.GetMouseButtonDown(1) || (aim != null && Input.GetKeyDown(aim.explicitUnselectKey)))
        {
            ClearTargetDrone();
            if (aim != null)
            {
                aim.ReleaseCurrentTarget("Explicit unselect input");
            }
        }

        bool isKeyDown = Input.GetKeyDown(fireKey) || (allowMouseFire && Input.GetMouseButtonDown(0));
        bool isKeyHeld = Input.GetKey(fireKey) || (allowMouseFire && Input.GetMouseButton(0));

        if (isKeyHeld || isKeyDown)
        {
            FireLaser();
        }
        else if (laserTimer > 0f)
        {
            laserTimer -= Time.deltaTime;
            FireLaser();
        }
        else
        {
            StopLaser();
        }
    }

    /// <summary>
    /// Fires the laser beam and detects hits on drones or obstacles.
    /// </summary>
    public void FireLaser()
    {
        if (firePoint == null || lineRenderer == null) return;

        Vector3 startPos = firePoint.position;
        Vector3 direction = firePoint.forward;
        Vector3 endPos = startPos + (direction * maxDistance);

        // 1. Perform SphereCastAll with beamHitRadius so the beam has thickness and easily hits fast drones
        RaycastHit[] hits = Physics.SphereCastAll(startPos, beamHitRadius, direction, maxDistance, hitLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        bool hitValidTarget = false;
        GameObject hitDrone = null;

        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;

            // CRITICAL: Ignore all colliders on the cannon itself!
            if (IsPartOfCannon(hit.collider)) continue;

            // Straight collinear laser calculation so beam never bends:
            float hitDist = (hit.distance > 0.05f) ? hit.distance : Vector3.Distance(startPos, hit.point);
            endPos = startPos + (direction * hitDist);
            hitValidTarget = true;

            // Try to hit drone
            if (TryApplyLaserToDrone(hit.collider, out hitDrone))
            {
                break;
            }

            // Hit an obstacle/ground/wall
            break;
        }

        // 2. Fallback: Standard thin RaycastAll if SphereCast didn't catch anything
        if (!hitValidTarget)
        {
            RaycastHit[] rayHits = Physics.RaycastAll(startPos, direction, maxDistance, hitLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(rayHits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var rHit in rayHits)
            {
                if (rHit.collider == null || IsPartOfCannon(rHit.collider)) continue;

                float hitDist = (rHit.distance > 0.05f) ? rHit.distance : Vector3.Distance(startPos, rHit.point);
                endPos = startPos + (direction * hitDist);

                if (TryApplyLaserToDrone(rHit.collider, out hitDrone))
                {
                    break;
                }

                break;
            }
        }

        // Update target tracking: immediately remove laser effect if target changed or was lost
        UpdateTargetedDrone(hitDrone);

        // Draw laser beam
        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, startPos);
        lineRenderer.SetPosition(1, endPos);
    }

    /// <summary>
    /// Immediately disables the laser line renderer and removes laser effects from all drones.
    /// </summary>
    public void StopLaser()
    {
        laserTimer = 0f;
        if (lineRenderer != null && lineRenderer.enabled)
        {
            lineRenderer.enabled = false;
        }

        ClearTargetDrone();

        AimAssist aim = GetComponent<AimAssist>() ?? GetComponentInParent<AimAssist>() ?? GetComponentInChildren<AimAssist>();
        if (aim != null)
        {
            aim.ClearCurrentTargetOnFireStop();
        }

        ClearAllDronesLaserTargeting();
    }

    /// <summary>
    /// Updates the currently targeted drone reference.
    /// If the laser switched to a different drone or lost its target,
    /// the previously targeted drone's laser effect and speed reduction are removed immediately.
    /// </summary>
    private void UpdateTargetedDrone(GameObject newTarget)
    {
        if (currentTargetDrone != newTarget)
        {
            if (currentTargetDroneHealth != null)
            {
                currentTargetDroneHealth.SetLaserTargeted(false);
            }

            currentTargetDrone = newTarget;
            currentTargetDroneHealth = newTarget != null ? (newTarget.GetComponent<DroneHealth>() ?? newTarget.GetComponentInChildren<DroneHealth>()) : null;

            if (currentTargetDroneHealth != null)
            {
                currentTargetDroneHealth.SetLaserTargeted(true);
            }
        }
    }

    /// <summary>
    /// Ensures all drones in the squad have their laser targeted state cleared immediately.
    /// </summary>
    public void ClearAllDronesLaserTargeting()
    {
        if (DroneDirector.Instance != null && DroneDirector.Instance.squad != null)
        {
            for (int i = 0; i < DroneDirector.Instance.squad.Count; i++)
            {
                var member = DroneDirector.Instance.squad[i];
                if (member != null && member.droneObject != null)
                {
                    DroneHealth health = member.droneObject.GetComponent<DroneHealth>() ?? member.droneObject.GetComponentInChildren<DroneHealth>();
                    if (health != null)
                    {
                        health.SetLaserTargeted(false);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Checks if a collider belongs to the cannon itself so the laser never blocks itself.
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

    /// <summary>
    /// Resolves the root GameObject of a drone from a hit collider.
    /// </summary>
    private GameObject ResolveDroneRoot(Collider hitCollider)
    {
        if (hitCollider == null) return null;
        if (IsPartOfCannon(hitCollider)) return null;

        // 1. Search for drone flight components up the hierarchy
        MonoBehaviour[] components = hitCollider.GetComponentsInParent<MonoBehaviour>(true);
        foreach (var comp in components)
        {
            if (comp == null) continue;
            string typeName = comp.GetType().Name;
            if (typeName == "FlightControlSystem" ||
                typeName == "DroneHardware" ||
                typeName == "DroneBrain" ||
                typeName == "DroneNPCFollowTarget")
            {
                return comp.gameObject;
            }
        }

        // 2. Check Rigidbody attached to the drone
        if (hitCollider.attachedRigidbody != null)
        {
            string rbName = hitCollider.attachedRigidbody.name.ToLower();
            if (rbName.Contains("drone") || rbName.Contains("tactical"))
            {
                return hitCollider.attachedRigidbody.gameObject;
            }
        }

        // 3. Check name of hit object or its root
        string nameLower = hitCollider.name.ToLower();
        string rootNameLower = hitCollider.transform.root.name.ToLower();
        if (nameLower.Contains("drone") || rootNameLower.Contains("drone") ||
            nameLower.Contains("tactical") || rootNameLower.Contains("tactical"))
        {
            return hitCollider.transform.root.gameObject;
        }

        return null;
    }

    /// <summary>
    /// Checks if collider belongs to a drone, outputs the resolved drone root, and applies laser damage to its DroneHealth.
    /// </summary>
    private bool TryApplyLaserToDrone(Collider hitCollider, out GameObject hitDrone)
    {
        hitDrone = null;
        GameObject droneRoot = ResolveDroneRoot(hitCollider);

        if (droneRoot != null && droneRoot.activeInHierarchy)
        {
            hitDrone = droneRoot;

            AimAssist aim = GetComponent<AimAssist>() ?? GetComponentInParent<AimAssist>() ?? GetComponentInChildren<AimAssist>();
            if (aim != null && !aim.IsTracking && aim.CanLockTarget(droneRoot))
            {
                aim.TryLockTarget(droneRoot);
            }

            DroneHealth droneHealth = hitCollider.GetComponentInParent<DroneHealth>();
            if (droneHealth == null)
            {
                droneHealth = droneRoot.GetComponent<DroneHealth>() ?? droneRoot.GetComponentInChildren<DroneHealth>();
                if (droneHealth == null)
                {
                    droneHealth = droneRoot.AddComponent<DroneHealth>();
                }
            }

            droneHealth.damageSlowdownMultiplier = 0.70f;
            droneHealth.SetLaserTargeted(true);

            // Centralized Cannon attack timing & damage from DroneDirector
            float attackInterval = (DroneDirector.Instance != null) ? DroneDirector.Instance.timeBetweenCannonAttacks : 0.5f;
            float attackDamage = (DroneDirector.Instance != null) ? DroneDirector.Instance.cannonDamagePerAttack : 25f;

            // Apply damage exactly once per attack interval, guarded against multiple colliders in the same frame
            if (cannonAttackCooldownTimer <= 0f && Time.frameCount != lastDamagedFrame)
            {
                cannonAttackCooldownTimer = attackInterval;
                lastDamagedFrame = Time.frameCount;

                droneHealth.TakeDamage(attackDamage);
                Debug.Log($"<color=yellow>[CanonMovement] Cannon attacked drone ({droneRoot.name}) dealing {attackDamage:F1} damage! Drone Health: {droneHealth.health:F1}/{droneHealth.maxHealth:F1}</color>");
            }

            if (droneHealth.IsDestroyed)
            {
                OnTargetDroneDestroyed(droneRoot);
                hitDrone = null;
                return true;
            }

            return true;
        }

        return false;
    }

    /// <summary>
    /// Detects if the struck object is a drone and applies laser damage. Provided for backwards compatibility.
    /// </summary>
    private bool TryDeactivateDrone(Collider hitCollider)
    {
        return TryApplyLaserToDrone(hitCollider, out _);
    }

    /// <summary>
    /// Notifies DroneDirector that a drone has been destroyed so droneCount is decremented by 1
    /// and the remaining drones dynamically reorganize their formation.
    /// </summary>
    private void CleanUpSquadWaypoint(GameObject droneObject)
    {
        if (DroneDirector.Instance != null)
        {
            DroneDirector.Instance.OnDroneDestroyed(droneObject);
        }
    }

    #endregion
}

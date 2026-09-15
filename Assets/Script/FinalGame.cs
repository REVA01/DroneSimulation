using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// FinalGame: Unified Controller combining Cannon Movement, Laser Firing,
/// and Drone Destruction (deactivating via SetActive(false) on laser hit).
/// </summary>
public class FinalGame : MonoBehaviour
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

    private float currentPitch = 0f;
    private Quaternion initialGunRotation;
    private float laserTimer = 0f;
    private AimAssist aimAssist;

    public float CurrentPitch => currentPitch;

    /// <summary>
    /// Called immediately when a drone is destroyed by the laser or health reaches 0.
    /// If the player is still holding fire (F or Left Click), the laser remains active.
    /// If the fire key is not held, the laser turns off immediately.
    /// Aim Assist always detaches from the destroyed drone.
    /// </summary>
    public void OnTargetDroneDestroyed(GameObject destroyedDrone)
    {
        bool isKeyHeld = Input.GetKey(fireKey) || (allowMouseFire && Input.GetMouseButton(0));

        // If player is not holding fire key (F / Left Mouse), turn off laser
        if (!isKeyHeld)
        {
            StopLaser();
        }

        if (aimAssist != null && aimAssist.IsTracking)
        {
            aimAssist.OnTargetDestroyed();
        }
    }

    private void Awake()
    {
        // 1. Automatically disable any duplicate LasserGun or CanonMovement scripts on this cannon
        // so that duplicate lines and double-speed rotations NEVER happen!
        DisableDuplicateScripts();

        // 2. Setup laser and LineRenderer
        InitializeLaser();
    }

    private void Start()
    {
        InitializeCannonMovement();

        // Cache or auto-add AimAssist
        aimAssist = GetComponent<AimAssist>() ?? GetComponentInParent<AimAssist>() ?? GetComponentInChildren<AimAssist>();
        if (aimAssist == null)
        {
            aimAssist = gameObject.AddComponent<AimAssist>();
        }

        if (aimAssist != null)
        {
            if (aimAssist.canonBase == null) aimAssist.canonBase = canonBase;
            if (aimAssist.canonRotate == null) aimAssist.canonRotate = canonRotate;
            if (aimAssist.firePoint == null) aimAssist.firePoint = firePoint;
            aimAssist.lockOnLaserTouch = true;
            aimAssist.SyncInitialRotation(initialGunRotation);
        }

        // Ensure CanonHealth is attached so the cannon withstands 5 drone attacks
        Transform healthTarget = (canonBase != null) ? canonBase : transform.root;
        if (healthTarget.GetComponentInChildren<CanonHealth>() == null && healthTarget.GetComponentInParent<CanonHealth>() == null)
        {
            healthTarget.gameObject.AddComponent<CanonHealth>();
        }
    }

    private void Update()
    {
        HandleCannonMovement();
        HandleLaserFiring();
    }

    #region Duplicate Prevention

    private void DisableDuplicateScripts()
    {
        // Search root and all children for any old LasserGun or duplicate CanonMovement
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
            // If another FinalGame is on a different part of the same cannon, disable it
            else if (sName == "FinalGame" && s != this)
            {
                s.enabled = false;
            }
        }
    }

    #endregion

    #region Initialization

    private void InitializeCannonMovement()
    {
        // Auto-assign base if not assigned
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

        // Auto-assign barrel if not assigned
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

    private void InitializeLaser()
    {
        // 1. Auto-find FirePoint
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

        // 2. Setup LineRenderer
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

        // 1. Horizontal: Player input takes priority when keys are pressed
        // Otherwise (no input), AimAssist automatically tracks the drone horizontally
        if (hasHorizontalInput && canonBase != null)
        {
            canonBase.Rotate(Vector3.up, horizontalInput * baseRotationSpeed * Time.deltaTime, Space.World);
        }

        // 2. Vertical: Player input takes priority when keys are pressed
        // Otherwise (no input), AimAssist automatically tracks the drone vertically
        if (hasVerticalInput && canonRotate != null)
        {
            currentPitch -= verticalInput * barrelRotationSpeed * Time.deltaTime;
            currentPitch = Mathf.Clamp(currentPitch, minVerticalAngle, maxVerticalAngle);
            canonRotate.localRotation = initialGunRotation * Quaternion.Euler(currentPitch, 0f, 0f);

            if (aimAssist != null)
            {
                aimAssist.SyncPitch(currentPitch);
            }
        }
    }

    /// <summary>
    /// Synchronizes pitch angle from AimAssist when transitioning between auto-tracking and manual control.
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

    private void HandleLaserFiring()
    {
        bool isKeyDown = Input.GetKeyDown(fireKey) || (allowMouseFire && Input.GetMouseButtonDown(0));
        bool isKeyHeld = Input.GetKey(fireKey) || (allowMouseFire && Input.GetMouseButton(0));

        // Firing is NOT connected with Aim Assist: ONLY user click/hold fires the laser
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

        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;

            // CRITICAL: Ignore all colliders on the cannon itself!
            if (IsPartOfCannon(hit.collider)) continue;

            // Straight collinear laser calculation so beam never bends:
            float hitDist = (hit.distance > 0.05f) ? hit.distance : Vector3.Distance(startPos, hit.point);
            endPos = startPos + (direction * hitDist);
            hitValidTarget = true;

            // Try to deactivate drone
            if (TryDeactivateDrone(hit.collider))
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

                if (TryDeactivateDrone(rHit.collider))
                {
                    break;
                }

                break;
            }
        }

        // Draw laser beam
        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, startPos);
        lineRenderer.SetPosition(1, endPos);
    }

    /// <summary>
    /// Immediately disables the laser line renderer.
    /// </summary>
    public void StopLaser()
    {
        laserTimer = 0f;
        if (lineRenderer != null && lineRenderer.enabled)
        {
            lineRenderer.enabled = false;
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
    /// Detects if the struck object is a drone (via components, rigids, or names) and disables it with SetActive(false).
    /// </summary>
    private bool TryDeactivateDrone(Collider hitCollider)
    {
        if (hitCollider == null) return false;
        if (IsPartOfCannon(hitCollider)) return false;

        GameObject droneRoot = null;

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
                droneRoot = comp.gameObject;
                break;
            }
        }

        // 2. Check Rigidbody attached to the drone
        if (droneRoot == null && hitCollider.attachedRigidbody != null)
        {
            string rbName = hitCollider.attachedRigidbody.name.ToLower();
            if (rbName.Contains("drone") || rbName.Contains("tactical"))
            {
                droneRoot = hitCollider.attachedRigidbody.gameObject;
            }
        }

        // 3. Check name of hit object or its root
        if (droneRoot == null)
        {
            string nameLower = hitCollider.name.ToLower();
            string rootNameLower = hitCollider.transform.root.name.ToLower();
            if (nameLower.Contains("drone") || rootNameLower.Contains("drone") ||
                nameLower.Contains("tactical") || rootNameLower.Contains("tactical"))
            {
                droneRoot = hitCollider.transform.root.gameObject;
            }
        }

        // Apply laser damage to DroneHealth
        if (droneRoot != null && droneRoot.activeInHierarchy)
        {
            // If AimAssist is on the cannon, connect/lock to this drone on laser hit
            if (aimAssist != null && !aimAssist.IsTracking && aimAssist.lockOnLaserTouch && !aimAssist.IsInCooldown)
            {
                aimAssist.TryLockTarget(droneRoot);
            }

            DroneHealth droneHealth = hitCollider.GetComponentInParent<DroneHealth>();
            if (droneHealth == null)
            {
                droneHealth = droneRoot.GetComponent<DroneHealth>();
                if (droneHealth == null)
                {
                    droneHealth = droneRoot.AddComponent<DroneHealth>();
                }
            }

            droneHealth.TakeLaserDamage(Time.deltaTime);

            if (droneHealth.IsDestroyed)
            {
                OnTargetDroneDestroyed(droneRoot);
                return true;
            }

            return true;
        }

        return false;
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

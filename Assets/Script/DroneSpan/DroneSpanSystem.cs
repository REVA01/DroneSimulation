using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Controls how drones are spawned into the scene.
/// Simultaneous: all drones spawn at the same time (default).
/// Sequential: drones spawn one by one with a configurable delay between each.
/// </summary>
public enum DroneSpawnMode
{
    /// <summary>All drones are spawned at the same time (original behavior).</summary>
    Simultaneous,

    /// <summary>Drones are spawned one after another with a delay between each.</summary>
    Sequential
}

public class DroneSpanSystem : MonoBehaviour
{
    [Header("Drone Director Connection")]
    [SerializeField] private DroneDirector droneDirector;

    [Header("Drone Spawner Settings")]
    [SerializeField] private GameObject dronePrefab;
    [Range(2, 20)]
    [SerializeField] private int droneCount = 2;
    [SerializeField] private float spawnRadius = 6f;
    [SerializeField] private float spawnHeight = 2.5f;
    [SerializeField] private bool spawnOnStart = true;

    [Header("Spawn Mode")]
    [Tooltip("Simultaneous (default): all drones spawn at once.\nSequential: drones spawn one by one with a delay.")]
    [SerializeField] private DroneSpawnMode spawnMode = DroneSpawnMode.Simultaneous;

    [Tooltip("Seconds to wait between spawning each drone in Sequential mode. Has no effect in Simultaneous mode.")]
    [SerializeField] [Min(0f)] private float sequentialSpawnDelay = 0.5f;

    [Header("Spawn Position Validation")]
    [Tooltip("Minimum distance (metres) between any two drone spawn positions. " +
             "If the ideal circular slot is already occupied the system spirals outward in rings " +
             "to find the nearest free slot. Increase this value when drones stack.")]
    [SerializeField] [Min(0.1f)] private float minSpawnDistance = 2.5f;

    // Public properties for external access
    public DroneDirector DroneDirector
    {
        get => droneDirector;
        set => droneDirector = value;
    }

    public GameObject DronePrefab
    {
        get => dronePrefab;
        set => dronePrefab = value;
    }

    public int DroneCount
    {
        get => droneCount;
        set => droneCount = value;
    }

    public float SpawnRadius
    {
        get => spawnRadius;
        set => spawnRadius = value;
    }

    public float SpawnHeight
    {
        get => spawnHeight;
        set => spawnHeight = value;
    }

    public bool SpawnOnStart
    {
        get => spawnOnStart;
        set => spawnOnStart = value;
    }

    public int LastSpawnedCount
    {
        get => lastSpawnedCount;
        set => lastSpawnedCount = value;
    }

    public DroneSpawnMode SpawnMode
    {
        get => spawnMode;
        set => spawnMode = value;
    }

    public float SequentialSpawnDelay
    {
        get => sequentialSpawnDelay;
        set => sequentialSpawnDelay = Mathf.Max(0f, value);
    }

    public float MinSpawnDistance
    {
        get => minSpawnDistance;
        set => minSpawnDistance = Mathf.Max(0.1f, value);
    }

    /// <summary>True while a sequential spawn coroutine is running.</summary>
    public bool IsSpawning => _spawnCoroutine != null;

    private int lastSpawnedCount = -1;
    private Coroutine _spawnCoroutine = null;

    // Positions already claimed in the current spawn cycle – reset before each full spawn run
    private readonly List<Vector3> _reservedPositions = new List<Vector3>();

    private void Awake()
    {
        EnsureDirectorConnection();
    }

    private void Start()
    {
        EnsureDirectorConnection();

        if (spawnOnStart && droneDirector != null && droneDirector.squad.Count == 0)
        {
            SpawnDrones();
        }
    }

    /// <summary>
    /// Ensures the two-way serialized connection between DroneSpanSystem and DroneDirector.
    /// </summary>
    public void EnsureDirectorConnection()
    {
        if (droneDirector == null)
        {
            droneDirector = GetComponent<DroneDirector>() ?? DroneDirector.Instance ?? FindAnyObjectByType<DroneDirector>();
        }

        if (droneDirector != null && droneDirector.DroneSpanSystem == null)
        {
            droneDirector.DroneSpanSystem = this;
        }
    }

    /// <summary>
    /// Entry point: clears any running spawn cycle and starts a new one according to the current SpawnMode.
    /// Prevents duplicate spawns by always clearing first.
    /// </summary>
    [ContextMenu("Spawn Drones")]
    public void SpawnDrones()
    {
        EnsureDirectorConnection();

        if (droneDirector == null)
        {
            Debug.LogWarning("[DroneSpanSystem] Cannot spawn drones: DroneDirector reference is missing!");
            return;
        }

        // Stop any in-progress sequential spawn before restarting
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }

        // Always clear previous squad so we never get duplicate drones
        ClearDrones();

        // Reset position reservations for the new cycle
        _reservedPositions.Clear();

        // Resolve template once so both paths share the same validation
        GameObject template = ResolveTemplate();
        if (template == null) return;

        // Record counts before spawning
        lastSpawnedCount = droneCount;
        if (droneDirector != null)
            droneDirector.LastSpawnedCount = droneCount;

        if (spawnMode == DroneSpawnMode.Sequential)
        {
            _spawnCoroutine = StartCoroutine(SpawnSequentially(template));
        }
        else
        {
            SpawnAllAtOnce(template);
        }
    }

    /// <summary>
    /// Destroys all spawned squad drones and waypoints, resets the spawn counter,
    /// and stops any running sequential spawn coroutine.
    /// </summary>
    [ContextMenu("Clear Drones")]
    public void ClearDrones()
    {
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }

        EnsureDirectorConnection();
        if (droneDirector == null) return;

        for (int i = droneDirector.squad.Count - 1; i >= 0; i--)
        {
            if (droneDirector.squad[i] != null)
            {
                if (droneDirector.squad[i].tacticalWaypoint != null)
                {
                    if (Application.isPlaying) Destroy(droneDirector.squad[i].tacticalWaypoint.gameObject);
                    else DestroyImmediate(droneDirector.squad[i].tacticalWaypoint.gameObject);
                }

                if (droneDirector.squad[i].droneObject != null)
                {
                    if (Application.isPlaying) Destroy(droneDirector.squad[i].droneObject);
                    else DestroyImmediate(droneDirector.squad[i].droneObject);
                }
            }
        }

        droneDirector.squad.Clear();
        lastSpawnedCount = 0;
        if (droneDirector != null)
            droneDirector.LastSpawnedCount = 0;
    }

    // ── Spawn Implementations ─────────────────────────────────────────────────

    /// <summary>Mode 2 – Simultaneous (default): spawns every drone in a single frame.</summary>
    private void SpawnAllAtOnce(GameObject template)
    {
        Vector3 centerPos = droneDirector.transform.position;

        for (int i = 0; i < droneCount; i++)
        {
            Vector3 spawnPos = FindValidSpawnPosition(i, centerPos);
            SpawnSingleDrone(template, i, spawnPos);
        }

        droneDirector.WireIgnoredColliders();
        Debug.Log($"[DroneSpanSystem] Spawned {droneCount} drones simultaneously.");
    }

    /// <summary>Mode 1 – Sequential: spawns drones one by one with sequentialSpawnDelay seconds between each.</summary>
    private IEnumerator SpawnSequentially(GameObject template)
    {
        Vector3 centerPos = droneDirector.transform.position;

        for (int i = 0; i < droneCount; i++)
        {
            Vector3 spawnPos = FindValidSpawnPosition(i, centerPos);
            SpawnSingleDrone(template, i, spawnPos);
            Debug.Log($"[DroneSpanSystem] Spawned drone {i + 1}/{droneCount} (sequential).");

            // Re-wire colliders after each drone so obstacle avoidance stays up to date
            droneDirector.WireIgnoredColliders();

            // Wait before spawning the next one (no wait after the last drone)
            if (i < droneCount - 1 && sequentialSpawnDelay > 0f)
            {
                yield return new WaitForSeconds(sequentialSpawnDelay);
            }
        }

        _spawnCoroutine = null;
        Debug.Log($"[DroneSpanSystem] Sequential spawn complete. {droneCount} drones spawned.");
    }

    // ── Shared per-drone setup ────────────────────────────────────────────────

    /// <summary>
    /// Instantiates and fully configures a single drone at the supplied (pre-validated) world position.
    /// </summary>
    private void SpawnSingleDrone(GameObject template, int index, Vector3 spawnPos)
    {
        GameObject droneObj = Instantiate(template, spawnPos, Quaternion.identity);
        droneObj.SetActive(true);

        // Dedicated tactical waypoint parented to the director transform
        GameObject waypointObj = new GameObject($"TacticalWaypoint_{index + 1}");
        waypointObj.transform.position = spawnPos;
        waypointObj.transform.SetParent(droneDirector.transform);

        // Navigation
        DroneNPCFollowTarget followTarget = droneObj.GetComponent<DroneNPCFollowTarget>();
        if (followTarget == null) followTarget = droneObj.AddComponent<DroneNPCFollowTarget>();
        followTarget.enabled = true;
        followTarget.targetBox = waypointObj.transform;
        followTarget.stopDistance = 0.5f;
        followTarget.moveSpeed = droneDirector.cruiseSpeed;
        followTarget.requireTargetLock = false;

        // Disable manual flight systems
        FlightControlSystem fcs = droneObj.GetComponent<FlightControlSystem>();
        if (fcs != null) fcs.enabled = false;

        DroneHardware dh = droneObj.GetComponent<DroneHardware>();
        if (dh != null) dh.enabled = false;

        DroneInputs inputs = droneObj.GetComponent<DroneInputs>();
        if (inputs != null) inputs.isAIControlled = true;

        // AI brain
        DroneBrain brain = droneObj.GetComponent<DroneBrain>();
        if (brain == null) brain = droneObj.AddComponent<DroneBrain>();

        // Health – 30% laser slowdown, clear targeting state
        DroneHealth health = droneObj.GetComponent<DroneHealth>();
        if (health == null) health = droneObj.AddComponent<DroneHealth>();
        health.damageSlowdownMultiplier = 0.70f;
        health.SetLaserTargeted(false);

        // Role assignment
        DroneTacticalRole assignedRole = (index == 0) ? DroneTacticalRole.Distractor : DroneTacticalRole.Flanker;
        droneObj.name = $"TacticalDrone_{index + 1}_{assignedRole}";

        DroneDirector.DroneSquadMember member = new DroneDirector.DroneSquadMember
        {
            droneObject = droneObj,
            followTarget = followTarget,
            tacticalWaypoint = waypointObj.transform,
            brain = brain,
            role = assignedRole,
            droneIndex = index,
            hasArrivedAtFinalSlot = false,
            arrivalTimestamp = 0f,
            initialized = false
        };

        brain.Initialize(droneDirector, member);
        droneDirector.squad.Add(member);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Resolves and returns the drone template to clone.
    /// Falls back to any DroneHardware found in the scene.
    /// Hides active scene objects so they act only as master templates.
    /// </summary>
    private GameObject ResolveTemplate()
    {
        GameObject template = dronePrefab;

        if (template == null)
        {
            DroneHardware existing = FindAnyObjectByType<DroneHardware>(FindObjectsInactive.Include);
            if (existing != null) template = existing.gameObject;
        }

        if (template == null)
        {
            Debug.LogWarning("[DroneSpanSystem] No drone prefab or template found in scene!");
            return null;
        }

        if (template.scene.name != null && template.activeSelf)
        {
            template.SetActive(false);
        }

        return template;
    }

    // ── Position Validation ───────────────────────────────────────────────────

    /// <summary>
    /// Returns a world-space spawn position for drone <paramref name="index"/> that is at least
    /// <see cref="minSpawnDistance"/> metres away from every already-reserved position.
    ///
    /// Strategy:
    ///   1. Try the ideal circular slot on the base ring (radius = spawnRadius).
    ///   2. If that slot is too close to a reserved position, try candidate slots on a slightly
    ///      larger ring, evenly spread around the full circle.
    ///   3. Keep expanding the ring radius by one step until a free slot is found.
    ///   4. If no free slot is found within <see cref="maxSearchRings"/> rings, fall back to
    ///      placing the drone at the ideal position with a small random offset so it is at
    ///      least visible rather than perfectly stacked.
    ///
    /// The found position is recorded in <see cref="_reservedPositions"/> immediately so the next
    /// drone in the same cycle respects it (critical for simultaneous spawning).
    /// </summary>
    private Vector3 FindValidSpawnPosition(int index, Vector3 centerPos)
    {
        // How many candidate directions to test on each fallback ring
        const int candidatesPerRing = 16;
        // How many rings to try before giving up and using the fallback
        const int maxSearchRings = 20;
        // How much to grow the search radius on each ring step
        float ringStep = Mathf.Max(minSpawnDistance, 1f);

        // ── Step 1: try the ideal circular slot ─────────────────────────────
        float idealAngle = (index / (float)Mathf.Max(droneCount, 1)) * Mathf.PI * 2f;
        Vector3 idealOffset = new Vector3(Mathf.Cos(idealAngle), 0f, Mathf.Sin(idealAngle)) * spawnRadius;
        Vector3 idealPos = centerPos + idealOffset + Vector3.up * spawnHeight;

        if (!IsPositionTooClose(idealPos))
        {
            _reservedPositions.Add(idealPos);
            return idealPos;
        }

        // ── Step 2: spiral outward in rings ─────────────────────────────────
        for (int ring = 1; ring <= maxSearchRings; ring++)
        {
            float searchRadius = spawnRadius + ring * ringStep;

            for (int c = 0; c < candidatesPerRing; c++)
            {
                float candidateAngle = (c / (float)candidatesPerRing) * Mathf.PI * 2f;
                Vector3 candidateOffset = new Vector3(
                    Mathf.Cos(candidateAngle),
                    0f,
                    Mathf.Sin(candidateAngle)) * searchRadius;
                Vector3 candidatePos = centerPos + candidateOffset + Vector3.up * spawnHeight;

                if (!IsPositionTooClose(candidatePos))
                {
                    _reservedPositions.Add(candidatePos);
                    Debug.Log($"[DroneSpanSystem] Drone {index + 1}: ideal slot occupied, using fallback ring {ring} candidate {c}.");
                    return candidatePos;
                }
            }
        }

        // ── Step 3: last-resort – tiny random offset to avoid perfect stacking
        Vector3 fallbackPos = idealPos + new Vector3(
            Random.Range(-minSpawnDistance, minSpawnDistance),
            0f,
            Random.Range(-minSpawnDistance, minSpawnDistance));

        _reservedPositions.Add(fallbackPos);
        Debug.LogWarning($"[DroneSpanSystem] Drone {index + 1}: could not find a free slot within {maxSearchRings} rings. " +
                         "Using randomised fallback. Consider increasing spawnRadius or reducing droneCount.");
        return fallbackPos;
    }

    /// <summary>
    /// Returns true if <paramref name="candidate"/> is closer than <see cref="minSpawnDistance"/>
    /// to any position already in <see cref="_reservedPositions"/> (horizontal plane only so that
    /// height differences at the same XZ location are also caught).
    /// </summary>
    private bool IsPositionTooClose(Vector3 candidate)
    {
        float minSqr = minSpawnDistance * minSpawnDistance;

        for (int i = 0; i < _reservedPositions.Count; i++)
        {
            // Compare on the XZ plane only (all drones spawn at the same height)
            Vector3 diff = candidate - _reservedPositions[i];
            diff.y = 0f;
            if (diff.sqrMagnitude < minSqr)
                return true;
        }
        return false;
    }
}

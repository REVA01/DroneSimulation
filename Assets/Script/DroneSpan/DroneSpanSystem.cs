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

    [Header("Spawn Tracking & Lifecycle (Current Cycle)")]
    [Tooltip("Total number of unique drones planned to spawn during this spawn cycle (set from Drone Count).")]
    [SerializeField] private int totalPlannedSpawns = 0;

    [Tooltip("Total unique drones spawned so far in the current cycle (Read Only).")]
    [SerializeField] private int totalDronesSpawned = 0;

    [Tooltip("Number of unspawned drones remaining to be spawned in the current cycle (Read Only).")]
    [SerializeField] private int remainingSpawns = 0;

    [Tooltip("Number of currently alive and active drones in the squad (Read Only).")]
    [SerializeField] private int currentlyAlive = 0;

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

    // Auto-replacement is permanently disabled; properties kept for API compatibility
    public bool AutoRespawn
    {
        get => false;
        set { }
    }

    public float RespawnDelay
    {
        get => 0f;
        set { }
    }

    public int MaxTotalSpawns
    {
        get => totalPlannedSpawns;
        set { }
    }

    public int TotalPlannedSpawns => totalPlannedSpawns;
    public int TotalDronesSpawned => totalDronesSpawned;
    public int RemainingSpawns => Mathf.Max(0, totalPlannedSpawns - totalDronesSpawned);
    public int CurrentlyAlive => droneDirector != null ? droneDirector.squad.Count : 0;
    public int ActiveDroneCount => CurrentlyAlive;

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

    private void Update()
    {
        UpdateTrackingTelemetry();
    }

    /// <summary>
    /// Synchronizes live telemetry properties for Inspector display.
    /// </summary>
    public void UpdateTrackingTelemetry()
    {
        currentlyAlive = droneDirector != null ? droneDirector.squad.Count : 0;
        remainingSpawns = Mathf.Max(0, totalPlannedSpawns - totalDronesSpawned);
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
    /// Synchronizes _reservedPositions with the current live positions of all active squad drones,
    /// purging any dead or destroyed drone positions.
    /// </summary>
    public void SyncReservedPositions()
    {
        _reservedPositions.Clear();
        if (droneDirector == null) return;

        for (int i = 0; i < droneDirector.squad.Count; i++)
        {
            DroneDirector.DroneSquadMember member = droneDirector.squad[i];
            if (member != null && member.droneObject != null && member.droneObject.activeInHierarchy)
            {
                _reservedPositions.Add(member.droneObject.transform.position);
            }
        }
    }

    /// <summary>
    /// Returns the lowest available drone index not currently used by any active squad member.
    /// </summary>
    private int GetNextAvailableIndex()
    {
        if (droneDirector == null || droneDirector.squad.Count == 0) return 0;
        HashSet<int> used = new HashSet<int>();
        for (int i = 0; i < droneDirector.squad.Count; i++)
        {
            if (droneDirector.squad[i] != null)
            {
                used.Add(droneDirector.squad[i].droneIndex);
            }
        }
        for (int i = 0; i < 100; i++)
        {
            if (!used.Contains(i)) return i;
        }
        return droneDirector.squad.Count;
    }

    /// <summary>
    /// Entry point: clears any running spawn cycle and starts a new one according to the current SpawnMode.
    /// Resets the session spawn counter.
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

        // Stop running coroutines
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }

        // Always clear previous squad so we start a clean cycle
        ClearDrones();

        // Initialize cycle tracking
        totalPlannedSpawns = droneCount;
        totalDronesSpawned = 0;
        UpdateTrackingTelemetry();

        // Resolve template once
        GameObject template = ResolveTemplate();
        if (template == null) return;

        // Record counts before spawning
        lastSpawnedCount = droneCount;
        if (droneDirector != null)
            droneDirector.LastSpawnedCount = droneCount;

        if (spawnMode == DroneSpawnMode.Sequential)
        {
            _spawnCoroutine = StartCoroutine(SpawnSequentiallyRoutine(template));
        }
        else
        {
            SpawnAllAtOnce(template);
        }
    }

    /// <summary>
    /// Spawns only the remaining unspawned drones required to reach totalPlannedSpawns in the current cycle.
    /// Never spawns replacements for destroyed drones.
    /// </summary>
    public void SpawnMissingDrones()
    {
        EnsureDirectorConnection();
        if (droneDirector == null) return;

        int unspawned = totalPlannedSpawns - totalDronesSpawned;
        if (unspawned <= 0) return;

        GameObject template = ResolveTemplate();
        if (template == null) return;

        SyncReservedPositions();

        if (spawnMode == DroneSpawnMode.Sequential)
        {
            if (_spawnCoroutine == null)
            {
                _spawnCoroutine = StartCoroutine(SpawnSequentiallyRoutine(template));
            }
        }
        else
        {
            Vector3 centerPos = droneDirector.transform.position;
            for (int i = 0; i < unspawned; i++)
            {
                int nextIndex = GetNextAvailableIndex();
                Vector3 spawnPos = FindValidSpawnPosition(nextIndex, centerPos);
                SpawnSingleDrone(template, nextIndex, spawnPos);
            }
            droneDirector.WireIgnoredColliders();
            UpdateTrackingTelemetry();
            Debug.Log($"[DroneSpanSystem] Spawned {unspawned} remaining unspawned drones. Total spawned: {totalDronesSpawned}/{totalPlannedSpawns}. Active squad: {CurrentlyAlive}");
        }
    }

    /// <summary>
    /// Adjusts totalPlannedSpawns to match droneCount when updated via Inspector.
    /// If count increased, spawns only the remaining unspawned drones.
    /// If count decreased below active squad, trims excess active drones.
    /// </summary>
    public void SyncToTargetCount()
    {
        EnsureDirectorConnection();
        if (droneDirector == null) return;

        if (droneCount > totalPlannedSpawns)
        {
            totalPlannedSpawns = droneCount;
            UpdateTrackingTelemetry();
            SpawnMissingDrones();
        }
        else if (droneCount < totalPlannedSpawns)
        {
            totalPlannedSpawns = droneCount;
            int active = droneDirector.squad.Count;
            if (active > droneCount)
            {
                for (int i = active - 1; i >= droneCount; i--)
                {
                    var member = droneDirector.squad[i];
                    if (member != null)
                    {
                        if (member.tacticalWaypoint != null) Destroy(member.tacticalWaypoint.gameObject);
                        if (member.droneObject != null) Destroy(member.droneObject);
                    }
                    droneDirector.squad.RemoveAt(i);
                }
                droneDirector.WireIgnoredColliders();
            }
            UpdateTrackingTelemetry();
        }
    }

    /// <summary>
    /// Notified by DroneDirector whenever an active squad drone is destroyed.
    /// Cleans up position reservations and updates live telemetry.
    /// Under no circumstances does this spawn a replacement or respawn the destroyed drone.
    /// If a sequential spawn coroutine is currently waiting/running, it continues unaffected
    /// until totalDronesSpawned == totalPlannedSpawns.
    /// </summary>
    public void OnDroneDestroyed(GameObject destroyedDrone)
    {
        SyncReservedPositions();
        UpdateTrackingTelemetry();
    }

    /// <summary>
    /// Destroys all spawned squad drones and waypoints, resets spawn counters,
    /// and stops any running spawn coroutines.
    /// </summary>
    [ContextMenu("Clear Drones")]
    public void ClearDrones()
    {
        if (_spawnCoroutine != null)
        {
            StopCoroutine(_spawnCoroutine);
            _spawnCoroutine = null;
        }

        _reservedPositions.Clear();

        EnsureDirectorConnection();
        if (droneDirector != null)
        {
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
            droneDirector.LastSpawnedCount = 0;
        }

        totalPlannedSpawns = 0;
        totalDronesSpawned = 0;
        remainingSpawns = 0;
        currentlyAlive = 0;
        lastSpawnedCount = 0;
    }

    // ── Spawn Implementations ─────────────────────────────────────────────────

    /// <summary>Mode 2 – Simultaneous (default): spawns every remaining planned drone in a single frame.</summary>
    private void SpawnAllAtOnce(GameObject template)
    {
        Vector3 centerPos = droneDirector.transform.position;
        SyncReservedPositions();

        int toSpawn = totalPlannedSpawns - totalDronesSpawned;
        for (int i = 0; i < toSpawn; i++)
        {
            int nextIndex = GetNextAvailableIndex();
            Vector3 spawnPos = FindValidSpawnPosition(nextIndex, centerPos);
            SpawnSingleDrone(template, nextIndex, spawnPos);
        }

        droneDirector.WireIgnoredColliders();
        UpdateTrackingTelemetry();
        Debug.Log($"[DroneSpanSystem] Spawned {totalDronesSpawned}/{totalPlannedSpawns} drones simultaneously. Active squad: {CurrentlyAlive}");
    }

    /// <summary>Mode 1 – Sequential: spawns drones one by one until totalDronesSpawned reaches totalPlannedSpawns.</summary>
    private IEnumerator SpawnSequentiallyRoutine(GameObject template)
    {
        Vector3 centerPos = droneDirector.transform.position;

        while (droneDirector != null && totalDronesSpawned < totalPlannedSpawns)
        {
            SyncReservedPositions();

            int nextIndex = GetNextAvailableIndex();
            Vector3 spawnPos = FindValidSpawnPosition(nextIndex, centerPos);
            SpawnSingleDrone(template, nextIndex, spawnPos);
            UpdateTrackingTelemetry();
            Debug.Log($"[DroneSpanSystem] Spawned drone ({totalDronesSpawned}/{totalPlannedSpawns}) sequentially. Active squad: {CurrentlyAlive}");

            droneDirector.WireIgnoredColliders();

            if (totalDronesSpawned < totalPlannedSpawns && sequentialSpawnDelay > 0f)
            {
                yield return new WaitForSeconds(sequentialSpawnDelay);
            }
        }

        _spawnCoroutine = null;
        UpdateTrackingTelemetry();
        Debug.Log($"[DroneSpanSystem] Sequential spawn complete. Total spawned: {totalDronesSpawned}/{totalPlannedSpawns}. Active squad: {CurrentlyAlive}");
    }

    // ── Shared per-drone setup ────────────────────────────────────────────────

    /// <summary>
    /// Instantiates and fully configures a single drone at the supplied (pre-validated) world position via DroneDirector.
    /// </summary>
    private void SpawnSingleDrone(GameObject template, int index, Vector3 spawnPos)
    {
        totalDronesSpawned++;
        GameObject droneObj = Instantiate(template, spawnPos, Quaternion.identity);
        droneObj.SetActive(true);

        droneDirector.ConfigureDrone(droneObj, spawnPos, index);
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

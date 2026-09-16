using System;
using System.Collections.Generic;
using UnityEngine;

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

    private int lastSpawnedCount = -1;

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
    /// Spawns squad drone instances in a circular formation around the director and configures their components.
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

        ClearDrones();

        GameObject template = dronePrefab;
        if (template == null)
        {
            DroneHardware existing = FindAnyObjectByType<DroneHardware>(FindObjectsInactive.Include);
            if (existing != null) template = existing.gameObject;
        }

        if (template == null)
        {
            Debug.LogWarning("[DroneSpanSystem] No drone prefab or template found in scene!");
            return;
        }

        // If template is an active scene object, hide it so it only serves as an intact master template
        if (template.scene.name != null && template.activeSelf)
        {
            template.SetActive(false);
        }

        lastSpawnedCount = droneCount;
        if (droneDirector != null)
        {
            droneDirector.LastSpawnedCount = droneCount;
        }

        Vector3 centerPos = droneDirector.transform.position;

        for (int i = 0; i < droneCount; i++)
        {
            float angle = (i / (float)droneCount) * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spawnRadius;
            Vector3 spawnPos = centerPos + offset + Vector3.up * spawnHeight;

            GameObject droneObj = Instantiate(template, spawnPos, Quaternion.identity);
            droneObj.SetActive(true);

            GameObject waypointObj = new GameObject($"TacticalWaypoint_{i + 1}");
            waypointObj.transform.position = spawnPos;
            waypointObj.transform.SetParent(droneDirector.transform);

            DroneNPCFollowTarget followTarget = droneObj.GetComponent<DroneNPCFollowTarget>();
            if (followTarget == null)
            {
                followTarget = droneObj.AddComponent<DroneNPCFollowTarget>();
            }

            followTarget.enabled = true;
            followTarget.targetBox = waypointObj.transform;
            followTarget.stopDistance = 0.5f;
            followTarget.moveSpeed = droneDirector.cruiseSpeed;
            followTarget.requireTargetLock = false;

            FlightControlSystem fcs = droneObj.GetComponent<FlightControlSystem>();
            if (fcs != null) fcs.enabled = false;

            DroneHardware dh = droneObj.GetComponent<DroneHardware>();
            if (dh != null) dh.enabled = false;

            DroneInputs inputs = droneObj.GetComponent<DroneInputs>();
            if (inputs != null) inputs.isAIControlled = true;

            DroneBrain brain = droneObj.GetComponent<DroneBrain>();
            if (brain == null)
            {
                brain = droneObj.AddComponent<DroneBrain>();
            }

            DroneHealth health = droneObj.GetComponent<DroneHealth>();
            if (health == null)
            {
                health = droneObj.AddComponent<DroneHealth>();
            }
            health.damageSlowdownMultiplier = 0.70f;
            health.SetLaserTargeted(false);

            DroneTacticalRole assignedRole = (i == 0) ? DroneTacticalRole.Distractor : DroneTacticalRole.Flanker;
            droneObj.name = $"TacticalDrone_{i + 1}_{assignedRole}";

            DroneDirector.DroneSquadMember member = new DroneDirector.DroneSquadMember
            {
                droneObject = droneObj,
                followTarget = followTarget,
                tacticalWaypoint = waypointObj.transform,
                brain = brain,
                role = assignedRole,
                droneIndex = i,
                hasArrivedAtFinalSlot = false,
                arrivalTimestamp = 0f,
                initialized = false
            };

            brain.Initialize(droneDirector, member);
            droneDirector.squad.Add(member);
        }

        droneDirector.WireIgnoredColliders();
    }

    /// <summary>
    /// Destroys all spawned squad drones and waypoints managed by DroneDirector.
    /// </summary>
    [ContextMenu("Clear Drones")]
    public void ClearDrones()
    {
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
        {
            droneDirector.LastSpawnedCount = 0;
        }
    }
}

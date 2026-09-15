using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

public enum CombatPhase
{
    FollowTarget,
    SearchAndSpread,
    Surround,
    DistractAndFlank,
    CoordinatedAttack,
    DisengageAndReposition,
    PostCombatTraveling,
    PostCombatStabilizing,
    MissionComplete
}

public enum FinalFormationType
{
    VFormation,
    ParallelFormation
}

public enum DroneTacticalRole
{
    Distractor,
    Flanker,
    PressureDrone,
    AttackDrone,
    RepositioningDrone
}

public enum TacticalAttackPattern
{
    ConvergingPincer,
    FeintAndFlank,
    HighLowStrike,
    BlitzAssault
}

public enum TacticalFormationProfile
{
    LeftRightFlank,
    FrontRearSplit,
    WideTwoSided,
    CrossDiagonal,
    RearSidePressure,
    AsymmetricPincer,
    HighLowBracket,
    VantageSpread,
    EchelonRight,
    EchelonLeft,
    OverwatchSplit,
    DiamondPerimeter
}

public class DroneBrain : MonoBehaviour
{
    [Header("Brain Status")]
    public DroneDirector director;
    public DroneDirector.DroneSquadMember member;
    public float distanceToWaypoint;
    public float distanceToCentroid;
    public float currentFlightSpeed;
    public bool isLaggingBehind;
    public bool isConnectedWithSquad = true;

    // Initializes references to the director and squad member.
    public void Initialize(DroneDirector directorInstance, DroneDirector.DroneSquadMember squadMember)
    {
        director = directorInstance;
        member = squadMember;
    }

    // Regulates adaptive flight speed based on distance and wingman positions.
    public void UpdateBrain(float baseSpeed, float maxSpeed, float acceleration, float dt)
    {
        if (director == null || member == null || member.followTarget == null || member.tacticalWaypoint == null)
            return;

        Vector3 myPos = transform.position;
        distanceToWaypoint = Vector3.Distance(myPos, member.tacticalWaypoint.position);

        Vector3 squadCentroid = director.GetSquadCentroid();
        distanceToCentroid = Vector3.Distance(myPos, squadCentroid);

        isLaggingBehind = distanceToWaypoint > 3.0f;

        float targetSpeed = baseSpeed;
        if (distanceToWaypoint > 1.5f)
        {
            float distanceScale = Mathf.Clamp(distanceToWaypoint / 3.0f, 1.0f, maxSpeed / Mathf.Max(baseSpeed, 0.1f));
            targetSpeed = Mathf.Min(baseSpeed * distanceScale, maxSpeed);
        }
        else if (distanceToWaypoint < director.arrivalSlowdownDistance && director.currentPhase != CombatPhase.CoordinatedAttack)
        {
            float t = Mathf.Clamp01(distanceToWaypoint / Mathf.Max(director.arrivalSlowdownDistance, 0.01f));
            targetSpeed = Mathf.Lerp(director.minCombatSpeed, baseSpeed, t);
        }

        bool peerIsLagging = false;
        for (int i = 0; i < director.squad.Count; i++)
        {
            DroneDirector.DroneSquadMember peer = director.squad[i];
            if (peer == null || peer == member || peer.droneObject == null || !peer.droneObject.activeInHierarchy || peer.tacticalWaypoint == null)
                continue;

            float peerDist = Vector3.Distance(peer.droneObject.transform.position, peer.tacticalWaypoint.position);
            if (peerDist > distanceToWaypoint + 3.0f)
            {
                peerIsLagging = true;
                break;
            }
        }

        if (peerIsLagging && distanceToWaypoint < 1.0f && director.currentPhase == CombatPhase.FollowTarget)
        {
            targetSpeed = baseSpeed * 0.75f;
        }

        // Apply slowdown if this drone is currently taking damage
        DroneHealth droneHealth = member.droneObject != null ? member.droneObject.GetComponent<DroneHealth>() : null;
        if (droneHealth != null && droneHealth.IsTakingDamage)
        {
            targetSpeed *= droneHealth.damageSlowdownMultiplier;
        }

        currentFlightSpeed = Mathf.MoveTowards(member.followTarget.moveSpeed, targetSpeed, acceleration * dt);
        member.followTarget.moveSpeed = currentFlightSpeed;
        isConnectedWithSquad = distanceToCentroid < 25.0f;
    }

    // Returns the distance to the closest peer squad drone.
    public float GetDistanceToNearestPeer()
    {
        if (director == null) return 0f;
        float minDist = float.MaxValue;
        Vector3 myPos = transform.position;

        for (int i = 0; i < director.squad.Count; i++)
        {
            DroneDirector.DroneSquadMember peer = director.squad[i];
            if (peer == null || peer == member || peer.droneObject == null) continue;

            float d = Vector3.Distance(myPos, peer.droneObject.transform.position);
            if (d < minDist) minDist = d;
        }

        return minDist == float.MaxValue ? 0f : minDist;
    }
}

public class DroneDirector : MonoBehaviour
{
    [Header("Target")]
    public Transform target;
    public string targetTag = "";

    [Header("Distance-Based Following & Combat")]
    public float engagementDistance = 8.5f;
    public float followDistance = 6.0f;
    public float followHeight = 1.8f;
    public float followFormationSpacing = 4.0f;
    public float followSpeed = 6.5f;
    public float maxFollowSpeed = 11.0f;
    public float followAcceleration = 7.0f;
    public FinalFormationType followFormation = FinalFormationType.VFormation;
    public bool randomizeFollowFormation = true;

    [HideInInspector] public bool chaseMode = true;
    [HideInInspector] public float reachDistance { get => followDistance; set => followDistance = value; }
    [HideInInspector] public float startSpeed { get => followSpeed; set => followSpeed = value; }
    [HideInInspector] public float maxSpeed { get => maxFollowSpeed; set => maxFollowSpeed = value; }
    [HideInInspector] public float acceleration { get => followAcceleration; set => followAcceleration = value; }
    [HideInInspector] public float obstacleCheckDistance = 4.0f;

    public static DroneDirector Instance { get; private set; }

    [Header("Combat State Machine (Read Only)")]
    public CombatPhase currentPhase = CombatPhase.SearchAndSpread;
    public TacticalFormationProfile currentCombatFormation = TacticalFormationProfile.LeftRightFlank;
    public FinalFormationType selectedFinalFormation = FinalFormationType.VFormation;
    public float phaseTimer = 0f;

    [Header("Formation Cycling")]
    [Range(2, 8)]
    public int formationsBeforeAttack = 4;
    public float surroundFormationDuration = 3.5f;
    public int currentFormationStep = 0;

    [Header("Smooth Trajectory Settings")]
    public float formationTransitionSpeed = 120f;
    public float radialTransitionSpeed = 8.0f;
    public float verticalTransitionSpeed = 5.0f;

    [Header("Combat Timings")]
    public float spreadDuration = 3.0f;
    public float distractDuration = 5.0f;
    public float attackDuration = 4.0f;
    public float disengageDuration = 2.5f;

    [Header("Tactical Speeds & Smooth Acceleration")]
    public float cruiseSpeed = 5.5f;
    public float attackSpeed = 10.0f;
    public float disengageSpeed = 7.5f;
    public float strikeDistance = 2.5f;
    public float arrivalSlowdownDistance = 0.8f;
    public float minCombatSpeed = 3.5f;

    [Header("Randomized Attack Intelligence (Read Only)")]
    public TacticalAttackPattern currentAttackPattern = TacticalAttackPattern.ConvergingPincer;

    [Header("Area Control & Separation")]
    public float minDroneSeparation = 5.5f;
    public float separationRepulsionStrength = 2.0f;

    [Header("Post-Combat Victory Sequence")]
    public float victoryFormationSpacing = 4.2f;
    public float victoryFormationHeight = 2.5f;
    public float formationArrivalTolerance = 0.35f;
    public float formationSyncTimeTolerance = 0.5f;
    public float cinematicHoldDuration = 4.0f;
    public float victoryTravelTimeout = 2.0f;

    [Header("Synchronized Travel Speeds")]
    public float minTravelSpeed = 4.5f;
    public float maxTravelSpeed = 9.5f;

    [Header("Mission Status")]
    public bool isMissionComplete = false;
    public UnityEvent OnMissionComplete;

    [Header("Obstacle Awareness")]
    public LayerMask obstacleLayers = -1;

    [Header("Drone Spawner")]
    public GameObject dronePrefab;

    [Range(2, 6)]
    public int droneCount = 2;
    public float spawnRadius = 6f;
    public float spawnHeight = 2.5f;
    public bool spawnOnStart = true;

    [Header("Active Squad Data (Read Only)")]
    public List<DroneSquadMember> squad = new List<DroneSquadMember>();

    private int lastSpawnedCount = -1;
    private int formationCycleCount = 0;
    private TacticalFormationProfile lastFormationProfile;
    private bool distractorRoleToggle = false;
    private bool targetEliminated = false;
    private bool hadActiveTarget = false;
    private Vector3 lastKnownTargetPos;
    private Vector3 lastKnownTargetForward = Vector3.forward;
    private Vector3 victoryCenter;
    private Vector3 victoryForward = Vector3.forward;

    private float attackAngleA;
    private float attackAngleB;
    private float attackRadiusA;
    private float attackRadiusB;
    private float attackAltitudeA;
    private float attackAltitudeB;
    private float egressAngleA;
    private float egressAngleB;

    [System.Serializable]
    public class DroneSquadMember
    {
        public GameObject droneObject;
        public DroneNPCFollowTarget followTarget;
        public Transform tacticalWaypoint;
        public DroneBrain brain;
        public DroneTacticalRole role;
        public int droneIndex;

        [HideInInspector] public float currentAngleDeg;
        [HideInInspector] public float currentRadius;
        [HideInInspector] public float currentAltitude;
        [HideInInspector] public bool initialized;

        [HideInInspector] public bool hasArrivedAtFinalSlot;
        [HideInInspector] public float arrivalTimestamp;
        [HideInInspector] public int assignedFormationSlotIndex = -1;
    }

    // Initializes the singleton instance.
    private void Awake()
    {
        Instance = this;
    }

    // Finds the initial target and initializes squad drones.
    private void Start()
    {
        FindTargetSafely();

        if (target != null && target.gameObject.activeInHierarchy)
        {
            hadActiveTarget = true;
            lastKnownTargetPos = target.position;
            lastKnownTargetForward = target.forward;
        }

        if (spawnOnStart)
        {
            SpawnDrones();
        }

        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null && squad[i].droneObject != null)
            {
                if (squad[i].brain == null)
                {
                    DroneBrain brain = squad[i].droneObject.GetComponent<DroneBrain>();
                    if (brain == null) brain = squad[i].droneObject.AddComponent<DroneBrain>();
                    squad[i].brain = brain;
                }
                squad[i].brain.Initialize(this, squad[i]);
            }

            if (squad[i] != null && squad[i].followTarget != null)
            {
                squad[i].followTarget.stopDistance = 0.25f;
            }
        }

        float initialDist = (target != null) ? Vector3.Distance(transform.position, target.position) : 50f;
        if (initialDist > engagementDistance)
        {
            EnterPhase(CombatPhase.FollowTarget);
        }
        else
        {
            PickRandomTacticalAction();
        }
    }

    // Updates target tracking, combat state, and squad formation flight each frame.
    private void Update()
    {
        if (target == null && !targetEliminated)
        {
            FindTargetSafely();
        }

        if (droneCount != lastSpawnedCount && droneCount > 0)
        {
            SpawnDrones();
        }

        bool targetIsActive = (target != null && target.gameObject.activeInHierarchy);

        if (targetIsActive)
        {
            hadActiveTarget = true;
            lastKnownTargetPos = target.position;
            lastKnownTargetForward = target.forward;

            UpdateCombatStateMachine();
            UpdateTacticalWaypoints(lastKnownTargetPos, lastKnownTargetForward);
            ApplyInterDroneSeparation();
            UpdateSquadFlightSpeeds();
        }
        else
        {
            if (hadActiveTarget && !targetEliminated)
            {
                if (target != null)
                {
                    lastKnownTargetPos = target.position;
                    lastKnownTargetForward = target.forward;
                }
                targetEliminated = true;
                SetupVictorySequence();
            }

            if (targetEliminated)
            {
                UpdateCombatStateMachine();

                if (currentPhase == CombatPhase.PostCombatTraveling)
                {
                    UpdateVictoryWaypoints();
                    UpdateSynchronizedTravelSpeeds();
                    CheckSynchronizedArrival();
                }
                else if (currentPhase == CombatPhase.PostCombatStabilizing || currentPhase == CombatPhase.MissionComplete)
                {
                    HoldVictoryStabilization();
                }
            }
        }
    }

    // Computes the average world position of all active squad drones.
    public Vector3 GetSquadCentroid()
    {
        Vector3 sum = Vector3.zero;
        int count = 0;
        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null && squad[i].droneObject != null && squad[i].droneObject.activeInHierarchy)
            {
                sum += squad[i].droneObject.transform.position;
                count++;
            }
        }
        return count > 0 ? sum / count : transform.position;
    }

    // Randomly selects an attack, surround, or flank combat action.
    public void PickRandomTacticalAction()
    {
        float roll = UnityEngine.Random.value;

        if (roll < 0.40f)
        {
            EnterPhase(CombatPhase.CoordinatedAttack);
        }
        else if (roll < 0.75f)
        {
            SelectNewFormationProfile();
            AssignDynamicRoles();
            EnterPhase(CombatPhase.Surround);
        }
        else
        {
            AssignDynamicRoles();
            EnterPhase(CombatPhase.DistractAndFlank);
        }
    }

    // Evaluates target distance and updates combat state transitions.
    private void UpdateCombatStateMachine()
    {
        phaseTimer -= Time.deltaTime;

        if (currentPhase == CombatPhase.PostCombatTraveling ||
            currentPhase == CombatPhase.PostCombatStabilizing ||
            currentPhase == CombatPhase.MissionComplete ||
            targetEliminated)
        {
            if (currentPhase == CombatPhase.PostCombatTraveling && phaseTimer <= 0f)
            {
                EnterPhase(CombatPhase.PostCombatStabilizing);
            }
            else if (currentPhase == CombatPhase.PostCombatStabilizing && phaseTimer <= 0f)
            {
                EnterPhase(CombatPhase.MissionComplete);
            }
            return;
        }

        float distToTarget = Vector3.Distance(GetSquadCentroid(), lastKnownTargetPos);
        float exitThreshold = (currentPhase == CombatPhase.FollowTarget) ? engagementDistance : (engagementDistance + 1.5f);

        if (distToTarget > exitThreshold)
        {
            if (currentPhase != CombatPhase.FollowTarget)
            {
                EnterPhase(CombatPhase.FollowTarget);
            }

            if (randomizeFollowFormation && phaseTimer <= 0f)
            {
                SelectRandomFollowFormation();
                phaseTimer = 7.0f;
            }
            return;
        }

        if (currentPhase == CombatPhase.FollowTarget)
        {
            PickRandomTacticalAction();
            return;
        }

        if (currentPhase == CombatPhase.CoordinatedAttack || currentPhase == CombatPhase.DistractAndFlank)
        {
            CheckPincerStrikeProximity();
        }

        if (phaseTimer <= 0f && !targetEliminated)
        {
            PickRandomTacticalAction();
        }
    }

    // Transitions the squad into a new combat or post-combat phase.
    public void EnterPhase(CombatPhase newPhase)
    {
        currentPhase = newPhase;

        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null && squad[i].followTarget != null)
                squad[i].followTarget.ResetAvoidance();
        }

        switch (newPhase)
        {
            case CombatPhase.FollowTarget:
                phaseTimer = 7.0f;
                if (randomizeFollowFormation)
                {
                    SelectRandomFollowFormation();
                }
                else
                {
                    AssignOptimalFollowSlots();
                }
                for (int i = 0; i < squad.Count; i++)
                {
                    if (squad[i] != null)
                    {
                        squad[i].role = DroneTacticalRole.AttackDrone;
                        if (squad[i].droneObject != null)
                            squad[i].droneObject.name = $"TacticalDrone_{i + 1}_Follow";
                    }
                }
                SetSquadSpeed(followSpeed);
                break;

            case CombatPhase.SearchAndSpread:
                phaseTimer = spreadDuration;
                currentFormationStep = 0;
                SelectNewFormationProfile();
                AssignDynamicRoles();
                SetSquadSpeed(cruiseSpeed);
                break;

            case CombatPhase.Surround:
                phaseTimer = surroundFormationDuration;
                SetSquadSpeed(cruiseSpeed);
                break;

            case CombatPhase.DistractAndFlank:
                phaseTimer = distractDuration;
                AssignDynamicRoles();
                SetSquadSpeed(cruiseSpeed);
                break;

            case CombatPhase.CoordinatedAttack:
                phaseTimer = attackDuration;
                SetupRandomizedAttackPattern();
                SetSquadSpeed(attackSpeed);
                break;

            case CombatPhase.DisengageAndReposition:
                phaseTimer = disengageDuration;
                egressAngleA = UnityEngine.Random.Range(0f, 360f);
                egressAngleB = egressAngleA + UnityEngine.Random.Range(140f, 220f);
                for (int i = 0; i < squad.Count; i++)
                {
                    if (squad[i] != null)
                    {
                        squad[i].role = DroneTacticalRole.RepositioningDrone;
                        if (squad[i].droneObject != null)
                            squad[i].droneObject.name = $"TacticalDrone_{i + 1}_{squad[i].role}";
                    }
                }
                SetSquadSpeed(disengageSpeed);
                break;

            case CombatPhase.PostCombatTraveling:
                phaseTimer = victoryTravelTimeout;
                break;

            case CombatPhase.PostCombatStabilizing:
                phaseTimer = cinematicHoldDuration;
                SetSquadSpeed(0f);
                break;

            case CombatPhase.MissionComplete:
                isMissionComplete = true;
                SetSquadSpeed(0f);
                OnMissionComplete?.Invoke();
                break;
        }
    }

    // Configures randomized approach angles and altitudes for attack runs.
    private void SetupRandomizedAttackPattern()
    {
        TacticalAttackPattern[] patterns = (TacticalAttackPattern[])Enum.GetValues(typeof(TacticalAttackPattern));
        currentAttackPattern = patterns[UnityEngine.Random.Range(0, patterns.Length)];

        float primaryAngle = UnityEngine.Random.Range(0f, 360f);
        bool swapRoles = (UnityEngine.Random.value > 0.5f);

        switch (currentAttackPattern)
        {
            case TacticalAttackPattern.ConvergingPincer:
                float pincerSpread = UnityEngine.Random.Range(140f, 180f);
                attackAngleA = primaryAngle;
                attackAngleB = primaryAngle + pincerSpread;
                attackRadiusA = strikeDistance * 0.75f;
                attackRadiusB = strikeDistance * 0.75f;
                attackAltitudeA = 1.6f;
                attackAltitudeB = 2.1f;
                break;

            case TacticalAttackPattern.FeintAndFlank:
                attackAngleA = primaryAngle;
                attackAngleB = primaryAngle + UnityEngine.Random.Range(150f, 180f);
                attackRadiusA = 5.5f;
                attackRadiusB = strikeDistance * 0.75f;
                attackAltitudeA = 2.2f;
                attackAltitudeB = 2.6f;
                break;

            case TacticalAttackPattern.HighLowStrike:
                attackAngleA = primaryAngle;
                attackAngleB = primaryAngle + UnityEngine.Random.Range(100f, 150f);
                attackRadiusA = strikeDistance * 0.75f;
                attackRadiusB = strikeDistance * 0.75f;
                attackAltitudeA = 4.5f;
                attackAltitudeB = 1.3f;
                break;

            case TacticalAttackPattern.BlitzAssault:
            default:
                attackAngleA = primaryAngle - UnityEngine.Random.Range(35f, 65f);
                attackAngleB = primaryAngle + UnityEngine.Random.Range(35f, 65f);
                attackRadiusA = strikeDistance * 0.7f;
                attackRadiusB = strikeDistance * 0.7f;
                attackAltitudeA = 1.7f;
                attackAltitudeB = 1.9f;
                break;
        }

        if (swapRoles)
        {
            float tempAngle = attackAngleA; attackAngleA = attackAngleB; attackAngleB = tempAngle;
            float tempRadius = attackRadiusA; attackRadiusA = attackRadiusB; attackRadiusB = tempRadius;
            float tempAlt = attackAltitudeA; attackAltitudeA = attackAltitudeB; attackAltitudeB = tempAlt;
        }

        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] == null) continue;
            if (currentAttackPattern == TacticalAttackPattern.FeintAndFlank)
            {
                squad[i].role = (i == 0 ? (!swapRoles ? DroneTacticalRole.Distractor : DroneTacticalRole.Flanker) : (!swapRoles ? DroneTacticalRole.Flanker : DroneTacticalRole.Distractor));
            }
            else
            {
                squad[i].role = (i == 0 ? DroneTacticalRole.AttackDrone : DroneTacticalRole.PressureDrone);
            }

            if (squad[i].droneObject != null)
            {
                squad[i].droneObject.name = $"TacticalDrone_{i + 1}_{squad[i].role}";
            }
        }
    }

    // Sets the flight speed for all squad members.
    private void SetSquadSpeed(float speed)
    {
        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null && squad[i].followTarget != null)
            {
                squad[i].followTarget.moveSpeed = speed;
                if (squad[i].brain != null) squad[i].brain.currentFlightSpeed = speed;
            }
        }
    }

    // Regulates flight speeds based on phase and distance to waypoints.
    private void UpdateSquadFlightSpeeds()
    {
        float baseSpeed = cruiseSpeed;
        float maxSpeed = maxFollowSpeed;
        float accel = 10.0f;

        if (currentPhase == CombatPhase.FollowTarget)
        {
            baseSpeed = followSpeed;
            maxSpeed = maxFollowSpeed;
            accel = followAcceleration;
        }
        else if (currentPhase == CombatPhase.CoordinatedAttack)
        {
            baseSpeed = attackSpeed;
            maxSpeed = attackSpeed * 1.3f;
        }
        else if (currentPhase == CombatPhase.DisengageAndReposition)
        {
            baseSpeed = disengageSpeed;
            maxSpeed = maxFollowSpeed;
        }

        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null || member.droneObject == null || member.followTarget == null || member.tacticalWaypoint == null)
                continue;

            if (member.brain == null)
            {
                member.brain = member.droneObject.GetComponent<DroneBrain>();
                if (member.brain == null) member.brain = member.droneObject.AddComponent<DroneBrain>();
                member.brain.Initialize(this, member);
            }

            member.brain.UpdateBrain(baseSpeed, maxSpeed, accel, Time.deltaTime);
        }
    }

    // Repels drones away from each other if they violate minimum separation.
    private void ApplyInterDroneSeparation()
    {
        if (squad.Count < 2 || currentPhase == CombatPhase.FollowTarget) return;

        for (int i = 0; i < squad.Count; i++)
        {
            for (int j = i + 1; j < squad.Count; j++)
            {
                DroneSquadMember mA = squad[i];
                DroneSquadMember mB = squad[j];
                if (mA == null || mB == null || 
                    mA.droneObject == null || mB.droneObject == null ||
                    !mA.droneObject.activeInHierarchy || !mB.droneObject.activeInHierarchy) continue;

                Vector3 posA = mA.droneObject.transform.position;
                Vector3 posB = mB.droneObject.transform.position;
                Vector3 diff = posA - posB;
                diff.y = 0f;
                float dist = diff.magnitude;

                if (dist < minDroneSeparation && dist > 0.001f)
                {
                    Vector3 repelDir = diff / dist;
                    float overlap = (minDroneSeparation - dist) * 0.5f;
                    Vector3 push = repelDir * overlap * (separationRepulsionStrength * Time.deltaTime);

                    if (mA.tacticalWaypoint != null) mA.tacticalWaypoint.position += push;
                    if (mB.tacticalWaypoint != null) mB.tacticalWaypoint.position -= push;
                }
            }
        }
    }

    // Detects if any attacking drone has reached strike distance to the target.
    private void CheckPincerStrikeProximity()
    {
        if (target == null || !target.gameObject.activeInHierarchy) return;

        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null && squad[i].droneObject != null && squad[i].droneObject.activeInHierarchy)
            {
                float dist = Vector3.Distance(squad[i].droneObject.transform.position, target.position);
                if (dist <= strikeDistance)
                {
                    ExecuteStrike(squad[i].droneObject.transform, target);
                    break;
                }
            }
        }
    }

    // Executes an attack strike on the target (cannon).
    private void ExecuteStrike(Transform attacker, Transform victim)
    {
        lastKnownTargetPos = victim.position;
        lastKnownTargetForward = victim.forward;

        // Check if victim (cannon) has CanonHealth component
        CanonHealth canonHealth = victim.GetComponentInParent<CanonHealth>();
        if (canonHealth == null) canonHealth = victim.GetComponentInChildren<CanonHealth>();

        if (canonHealth != null)
        {
            canonHealth.TakeDamage(1);

            if (!canonHealth.IsDestroyed)
            {
                // Cannon survived! Drones disengage, retreat, and regroup for another attack run
                EnterPhase(CombatPhase.DisengageAndReposition);
                return;
            }
        }

        // Cannon destroyed after 5 attacks (or if no CanonHealth is attached)
        targetEliminated = true;
        victim.gameObject.SetActive(false);
        SetupVictorySequence();
    }

    // Initializes post-combat formation slots and travel speeds.
    private void SetupVictorySequence()
    {
        SelectFinalFormation();

        victoryForward = lastKnownTargetForward;
        if (victoryForward.sqrMagnitude < 0.001f) victoryForward = Vector3.forward;
        victoryForward.y = 0f;
        victoryForward.Normalize();

        victoryCenter = lastKnownTargetPos + Vector3.up * victoryFormationHeight - victoryForward * 1.5f;
        phaseTimer = victoryTravelTimeout;

        AssignOptimalFormationSlots();

        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null)
            {
                squad[i].hasArrivedAtFinalSlot = false;
                squad[i].arrivalTimestamp = 0f;
                if (squad[i].followTarget != null)
                {
                    squad[i].followTarget.stopDistance = 0.15f;
                }
            }
        }

        EnterPhase(CombatPhase.PostCombatTraveling);
    }

    // Selects final formation type based on odd or even drone count.
    private void SelectFinalFormation()
    {
        if (squad.Count % 2 == 0)
        {
            selectedFinalFormation = FinalFormationType.ParallelFormation;
        }
        else
        {
            selectedFinalFormation = FinalFormationType.VFormation;
        }
    }

    // Assigns drones to their closest victory formation slots.
    private void AssignOptimalFormationSlots()
    {
        if (squad.Count == 0) return;

        if (squad.Count == 2)
        {
            Vector3 pos0 = squad[0].droneObject != null ? squad[0].droneObject.transform.position : transform.position;
            Vector3 pos1 = squad[1].droneObject != null ? squad[1].droneObject.transform.position : transform.position;

            Vector3 slot0 = CalculateFinalFormationSlot(0, victoryCenter, victoryForward);
            Vector3 slot1 = CalculateFinalFormationSlot(1, victoryCenter, victoryForward);

            float costDirect = Vector3.Distance(pos0, slot0) + Vector3.Distance(pos1, slot1);
            float costSwapped = Vector3.Distance(pos0, slot1) + Vector3.Distance(pos1, slot0);

            if (costSwapped < costDirect)
            {
                squad[0].assignedFormationSlotIndex = 1;
                squad[1].assignedFormationSlotIndex = 0;
            }
            else
            {
                squad[0].assignedFormationSlotIndex = 0;
                squad[1].assignedFormationSlotIndex = 1;
            }
        }
        else
        {
            List<int> availableSlots = new List<int>();
            for (int j = 0; j < squad.Count; j++) availableSlots.Add(j);

            for (int i = 0; i < squad.Count; i++)
            {
                Vector3 dronePos = squad[i].droneObject != null ? squad[i].droneObject.transform.position : transform.position;
                int bestSlot = availableSlots[0];
                float bestDist = float.MaxValue;

                for (int k = 0; k < availableSlots.Count; k++)
                {
                    int slotIdx = availableSlots[k];
                    float d = Vector3.Distance(dronePos, CalculateFinalFormationSlot(slotIdx, victoryCenter, victoryForward));
                    if (d < bestDist)
                    {
                        bestDist = d;
                        bestSlot = slotIdx;
                    }
                }

                squad[i].assignedFormationSlotIndex = bestSlot;
                availableSlots.Remove(bestSlot);
            }
        }
    }

    // Computes the world position of a drone's assigned final formation slot.
    public Vector3 GetAssignedFormationSlot(int droneIndex)
    {
        if (droneIndex < 0 || droneIndex >= squad.Count) return victoryCenter;
        int slotIdx = squad[droneIndex].assignedFormationSlotIndex;
        if (slotIdx < 0) slotIdx = droneIndex;
        return CalculateFinalFormationSlot(slotIdx, victoryCenter, victoryForward);
    }

    // Updates tactical waypoints to assigned victory formation positions.
    private void UpdateVictoryWaypoints()
    {
        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null || member.tacticalWaypoint == null) continue;

            Vector3 slot = GetAssignedFormationSlot(i);
            member.tacticalWaypoint.position = slot;
        }
    }

    // Computes formation slot offsets for V and Parallel victory formations.
    public Vector3 CalculateFinalFormationSlot(int slotIndex, Vector3 center, Vector3 forward)
    {
        Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
        if (right.sqrMagnitude < 0.001f) right = Vector3.right;
        float s = victoryFormationSpacing;
        int total = squad.Count;

        Vector3 slotPos;

        switch (selectedFinalFormation)
        {
            case FinalFormationType.VFormation:
                if (slotIndex == 0)
                {
                    slotPos = center + forward * (s * 0.4f);
                }
                else
                {
                    int pairIndex = (slotIndex + 1) / 2;
                    float side = (slotIndex % 2 == 1) ? -1f : 1f;
                    slotPos = center - forward * (s * 0.3f * pairIndex) + right * (side * s * 0.45f * pairIndex);
                }
                break;

            case FinalFormationType.ParallelFormation:
            default:
                float halfSpan = (total - 1) * 0.5f;
                float offset = slotIndex - halfSpan;
                slotPos = center + right * (offset * s * 0.5f);
                break;
        }

        return ClampToNavigableSpace(center, slotPos);
    }

    // Synchronizes drone speeds so all drones reach victory slots together.
    private void UpdateSynchronizedTravelSpeeds()
    {
        if (squad.Count == 0) return;

        float targetDuration = 1.25f;

        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null || member.followTarget == null || member.droneObject == null) continue;

            Vector3 slot = GetAssignedFormationSlot(i);
            float dist = Vector3.Distance(member.droneObject.transform.position, slot);

            if (member.hasArrivedAtFinalSlot || dist <= formationArrivalTolerance)
            {
                member.hasArrivedAtFinalSlot = true;
                member.followTarget.moveSpeed = 0f;
                if (member.brain != null) member.brain.currentFlightSpeed = 0f;
                continue;
            }

            float requiredSpeed = dist / targetDuration;
            float assignedSpeed = Mathf.Clamp(requiredSpeed, minTravelSpeed, maxTravelSpeed);

            if (dist < 0.6f)
            {
                assignedSpeed = Mathf.Lerp(2.5f, assignedSpeed, dist / 0.6f);
            }

            member.followTarget.moveSpeed = assignedSpeed;
            if (member.brain != null) member.brain.currentFlightSpeed = assignedSpeed;
        }
    }

    // Checks if all drones have arrived at their victory slots.
    private void CheckSynchronizedArrival()
    {
        if (squad.Count == 0) return;

        bool allArrived = true;

        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null || member.droneObject == null)
                continue;

            Vector3 slot = GetAssignedFormationSlot(i);
            float distToSlot = Vector3.Distance(member.droneObject.transform.position, slot);

            if (distToSlot <= formationArrivalTolerance)
            {
                if (!member.hasArrivedAtFinalSlot)
                {
                    member.hasArrivedAtFinalSlot = true;
                    member.arrivalTimestamp = Time.time;
                }
            }
            else
            {
                allArrived = false;
            }
        }

        if (allArrived || phaseTimer <= 0f)
        {
            EnterPhase(CombatPhase.PostCombatStabilizing);
        }
    }

    // Holds drones in stable hover at their final formation slots.
    private void HoldVictoryStabilization()
    {
        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null || member.droneObject == null) continue;

            Vector3 slot = GetAssignedFormationSlot(i);
            if (member.tacticalWaypoint != null)
            {
                member.tacticalWaypoint.position = slot;
            }

            if (member.followTarget != null)
            {
                member.followTarget.moveSpeed = 0f;
            }

            if (member.brain != null)
            {
                member.brain.currentFlightSpeed = 0f;
            }

            member.droneObject.transform.position = Vector3.MoveTowards(
                member.droneObject.transform.position,
                slot,
                Time.deltaTime * 5.0f
            );

            Quaternion targetRot = Quaternion.LookRotation(victoryForward, Vector3.up);
            member.droneObject.transform.rotation = Quaternion.Slerp(
                member.droneObject.transform.rotation,
                targetRot,
                Time.deltaTime * 6.0f
            );

            DroneInputs inputs = member.droneObject.GetComponent<DroneInputs>();
            if (inputs != null)
            {
                inputs.SetAIInputs(0f, 0f, 0f, 0f);
            }

            Rigidbody rb = member.droneObject.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = Vector3.MoveTowards(rb.linearVelocity, Vector3.zero, Time.deltaTime * 12f);
                rb.angularVelocity = Vector3.MoveTowards(rb.angularVelocity, Vector3.zero, Time.deltaTime * 12f);
            }
        }
    }

    // Selects a follow formation based on drone count.
    public void SelectRandomFollowFormation()
    {
        if (squad.Count % 2 == 0)
        {
            followFormation = FinalFormationType.ParallelFormation;
        }
        else
        {
            followFormation = FinalFormationType.VFormation;
        }

        AssignOptimalFollowSlots();
    }

    // Assigns follow formation slots based on left-to-right lateral ordering.
    public void AssignOptimalFollowSlots()
    {
        if (squad.Count == 0) return;
        if (squad.Count == 1)
        {
            if (squad[0] != null) squad[0].assignedFormationSlotIndex = 0;
            return;
        }

        Vector3 squadCentroid = GetSquadCentroid();
        Vector3 toTarget = lastKnownTargetPos - squadCentroid;
        toTarget.y = 0f;
        Vector3 followForward = toTarget.sqrMagnitude > 0.01f ? toTarget.normalized : transform.forward;
        Vector3 followRight = Vector3.Cross(Vector3.up, followForward).normalized;

        List<int> sortedIndices = new List<int>();
        for (int i = 0; i < squad.Count; i++) sortedIndices.Add(i);

        sortedIndices.Sort((a, b) =>
        {
            Vector3 posA = squad[a].droneObject != null ? squad[a].droneObject.transform.position : transform.position;
            Vector3 posB = squad[b].droneObject != null ? squad[b].droneObject.transform.position : transform.position;
            float dotA = Vector3.Dot(posA - squadCentroid, followRight);
            float dotB = Vector3.Dot(posB - squadCentroid, followRight);
            return dotA.CompareTo(dotB);
        });

        for (int rank = 0; rank < sortedIndices.Count; rank++)
        {
            squad[sortedIndices[rank]].assignedFormationSlotIndex = rank;
        }
    }

    // Computes formation slot offsets for follow pursuit.
    public Vector3 CalculateFollowFormationSlot(int slotIndex, Vector3 center, Vector3 forward, Vector3 right)
    {
        float s = followFormationSpacing;
        int total = squad.Count;
        Vector3 slotPos;

        switch (followFormation)
        {
            case FinalFormationType.VFormation:
                if (slotIndex == 0)
                {
                    slotPos = center + forward * (s * 0.35f);
                }
                else
                {
                    int pairIndex = (slotIndex + 1) / 2;
                    float side = (slotIndex % 2 == 1) ? -1f : 1f;
                    slotPos = center - forward * (s * 0.30f * pairIndex) + right * (side * s * 0.45f * pairIndex);
                }
                break;

            case FinalFormationType.ParallelFormation:
            default:
                float halfSpan = (total - 1) * 0.5f;
                float offset = slotIndex - halfSpan;
                slotPos = center + right * (offset * s * 0.5f);
                break;
        }

        return slotPos;
    }

    // Positions follow waypoints trailing behind the target in formation.
    private void UpdateFollowWaypoints(Vector3 targetCenter, Vector3 forwardDir)
    {
        Vector3 squadCentroid = GetSquadCentroid();
        Vector3 toTarget = targetCenter - squadCentroid;
        toTarget.y = 0f;

        Vector3 followForward;
        if (toTarget.sqrMagnitude > 0.01f)
        {
            followForward = toTarget.normalized;
        }
        else if (forwardDir.sqrMagnitude > 0.01f)
        {
            followForward = forwardDir;
            followForward.y = 0f;
            followForward.Normalize();
        }
        else
        {
            followForward = Vector3.forward;
        }

        Vector3 followRight = Vector3.Cross(Vector3.up, followForward).normalized;

        Vector3 formationCenter = targetCenter - followForward * followDistance;
        formationCenter.y = targetCenter.y + followHeight;

        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null || member.tacticalWaypoint == null) continue;

            int slotIdx = member.assignedFormationSlotIndex;
            if (slotIdx < 0) slotIdx = i;

            Vector3 slotPos = CalculateFollowFormationSlot(slotIdx, formationCenter, followForward, followRight);
            slotPos = ClampToNavigableSpace(targetCenter, slotPos);
            member.tacticalWaypoint.position = slotPos;
        }
    }

    // Updates dynamic waypoints around the target for the current combat phase.
    private void UpdateTacticalWaypoints(Vector3 targetCenter, Vector3 forwardDir)
    {
        if (currentPhase == CombatPhase.FollowTarget)
        {
            UpdateFollowWaypoints(targetCenter, forwardDir);
            return;
        }

        Vector3 rightDir = Vector3.Cross(Vector3.up, forwardDir).normalized;
        if (rightDir.sqrMagnitude < 0.001f) rightDir = Vector3.right;
        Vector3 upDir = Vector3.up;

        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null || member.tacticalWaypoint == null) continue;

            float targetAngleDeg;
            float targetRadius;
            float targetAltitude;

            if (currentPhase == CombatPhase.DisengageAndReposition)
            {
                float baseAngle = (i == 0) ? egressAngleA : egressAngleB;
                targetAngleDeg = baseAngle + (i >= 2 ? i * 40f : 0f);
                targetRadius = 14f;
                targetAltitude = 4.0f + (i * 0.5f);
            }
            else if (currentPhase == CombatPhase.CoordinatedAttack)
            {
                if (i == 0)
                {
                    targetAngleDeg = attackAngleA;
                    targetRadius = attackRadiusA;
                    targetAltitude = attackAltitudeA;
                }
                else if (i == 1)
                {
                    targetAngleDeg = attackAngleB;
                    targetRadius = attackRadiusB;
                    targetAltitude = attackAltitudeB;
                }
                else
                {
                    targetAngleDeg = attackAngleA + (i * 90f);
                    targetRadius = strikeDistance * 0.8f;
                    targetAltitude = 1.6f + (i * 0.4f);
                }
            }
            else if (currentPhase == CombatPhase.DistractAndFlank)
            {
                if (member.role == DroneTacticalRole.Distractor)
                {
                    float feintOffset = Mathf.Sin(Time.time * 2.5f) * 25f;
                    targetAngleDeg = 0f + feintOffset;
                    targetRadius = 5.5f;
                    targetAltitude = 2.2f;
                }
                else
                {
                    float flankOffset = (i > 1) ? (i * 35f) : 0f;
                    targetAngleDeg = 160f + flankOffset;
                    targetRadius = 8.5f;
                    targetAltitude = 2.8f + (i * 0.4f);
                }
            }
            else
            {
                GetFormationSlotParameters(i, out targetAngleDeg, out targetRadius, out targetAltitude);
            }

            if (!member.initialized)
            {
                member.currentAngleDeg = targetAngleDeg;
                member.currentRadius = targetRadius;
                member.currentAltitude = targetAltitude;
                member.initialized = true;
            }

            float angularSpeed = (currentPhase == CombatPhase.CoordinatedAttack) ? formationTransitionSpeed * 2.0f : formationTransitionSpeed;
            float radialSpeed = (currentPhase == CombatPhase.CoordinatedAttack) ? radialTransitionSpeed * 2.0f : radialTransitionSpeed;
            float altSpeed = verticalTransitionSpeed;

            member.currentAngleDeg = Mathf.MoveTowardsAngle(member.currentAngleDeg, targetAngleDeg, angularSpeed * Time.deltaTime);
            member.currentRadius = Mathf.MoveTowards(member.currentRadius, targetRadius, radialSpeed * Time.deltaTime);
            member.currentAltitude = Mathf.MoveTowards(member.currentAltitude, targetAltitude, altSpeed * Time.deltaTime);

            float sway = Mathf.Sin(Time.time * 1.5f + member.droneIndex * 2f) * 0.35f;
            float altSway = Mathf.Cos(Time.time * 1.2f + member.droneIndex) * 0.2f;

            Quaternion rot = Quaternion.AngleAxis(member.currentAngleDeg + sway, upDir);
            Vector3 slotPosition = targetCenter + (rot * forwardDir) * member.currentRadius + upDir * (member.currentAltitude + altSway);
            slotPosition = ClampToNavigableSpace(targetCenter, slotPosition);

            member.tacticalWaypoint.position = slotPosition;
        }
    }

    // Returns angular and radial parameters for tactical encirclement profiles.
    private void GetFormationSlotParameters(int index, out float angleDeg, out float radius, out float altitude)
    {
        angleDeg = 0f;
        radius = 9f;
        altitude = 2.5f;

        switch (currentCombatFormation)
        {
            case TacticalFormationProfile.LeftRightFlank:
                angleDeg = (index == 0) ? -70f : 110f;
                radius = (index == 0) ? 8f : 12f;
                altitude = (index == 0) ? 2.5f : 3.5f;
                break;

            case TacticalFormationProfile.FrontRearSplit:
                angleDeg = (index == 0) ? 20f : -160f;
                radius = (index == 0) ? 7f : 13f;
                altitude = (index == 0) ? 2f : 4f;
                break;

            case TacticalFormationProfile.WideTwoSided:
                angleDeg = (index == 0) ? -90f : 90f;
                radius = 13f;
                altitude = 3f;
                break;

            case TacticalFormationProfile.CrossDiagonal:
                angleDeg = (index == 0) ? -45f : 135f;
                radius = (index == 0) ? 8.5f : 11.5f;
                altitude = 2.8f;
                break;

            case TacticalFormationProfile.RearSidePressure:
                angleDeg = (index == 0) ? -130f : 80f;
                radius = (index == 0) ? 9f : 10.5f;
                altitude = 3f;
                break;

            case TacticalFormationProfile.AsymmetricPincer:
                angleDeg = (index == 0) ? 35f : -125f;
                radius = (index == 0) ? 6f : 14f;
                altitude = (index == 0) ? 2f : 4.5f;
                break;

            case TacticalFormationProfile.HighLowBracket:
                angleDeg = (index == 0) ? -60f : 60f;
                radius = (index == 0) ? 10f : 8f;
                altitude = (index == 0) ? 5.5f : 1.5f;
                break;

            case TacticalFormationProfile.VantageSpread:
                angleDeg = (index == 0) ? -50f : 50f;
                radius = (index == 0) ? 10f : 12f;
                altitude = 3f;
                break;

            case TacticalFormationProfile.EchelonRight:
                angleDeg = (index == 0) ? 30f : 80f;
                radius = (index == 0) ? 7.5f : 12.5f;
                altitude = (index == 0) ? 2.2f : 3.8f;
                break;

            case TacticalFormationProfile.EchelonLeft:
                angleDeg = (index == 0) ? -30f : -80f;
                radius = (index == 0) ? 7.5f : 12.5f;
                altitude = (index == 0) ? 2.2f : 3.8f;
                break;

            case TacticalFormationProfile.OverwatchSplit:
                angleDeg = (index == 0) ? 15f : -150f;
                radius = (index == 0) ? 6.5f : 14f;
                altitude = (index == 0) ? 1.8f : 6.0f;
                break;

            case TacticalFormationProfile.DiamondPerimeter:
            default:
                angleDeg = (index == 0) ? -45f : 45f;
                radius = 9f;
                altitude = (index == 0) ? 2.5f : 3.5f;
                break;
        }

        if (index >= 2)
        {
            angleDeg += (index - 1) * 75f;
            radius += (index % 2 == 0 ? 2f : -1.5f);
            altitude += (index * 0.7f);
        }
    }

    // Clamps waypoint positions against environmental obstacles.
    private Vector3 ClampToNavigableSpace(Vector3 origin, Vector3 desiredPos)
    {
        Vector3 dir = desiredPos - origin;
        float dist = dir.magnitude;
        if (dist > 0.01f)
        {
            if (Physics.Raycast(origin, dir.normalized, out RaycastHit hit, dist, obstacleLayers, QueryTriggerInteraction.Ignore))
            {
                if (hit.collider != null &&
                    hit.collider.GetComponentInParent<FlightControlSystem>() == null &&
                    hit.collider.GetComponentInParent<DroneNPCFollowTarget>() == null &&
                    (target == null || hit.collider.transform.root != target.root))
                {
                    return hit.point - dir.normalized * 1.5f;
                }
            }
        }
        return desiredPos;
    }

    // Advances to the next tactical encirclement formation profile.
    private void SelectNewFormationProfile()
    {
        TacticalFormationProfile[] profiles = (TacticalFormationProfile[])Enum.GetValues(typeof(TacticalFormationProfile));
        formationCycleCount++;

        TacticalFormationProfile nextProfile = profiles[formationCycleCount % profiles.Length];
        if (nextProfile == lastFormationProfile && profiles.Length > 1)
        {
            formationCycleCount++;
            nextProfile = profiles[formationCycleCount % profiles.Length];
        }

        lastFormationProfile = nextProfile;
        currentCombatFormation = nextProfile;
    }

    // Alternates distractor and flanker roles between squad drones.
    private void AssignDynamicRoles()
    {
        distractorRoleToggle = !distractorRoleToggle;
        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] == null) continue;

            bool isDistractor = (i == 0) ? !distractorRoleToggle : distractorRoleToggle;
            squad[i].role = isDistractor ? DroneTacticalRole.Distractor : DroneTacticalRole.Flanker;
            if (squad[i].droneObject != null)
            {
                squad[i].droneObject.name = $"TacticalDrone_{i + 1}_{squad[i].role}";
            }
        }
    }

    // Locates the target GameObject by name or tag.
    public void FindTargetSafely()
    {
        if (target != null && target.gameObject.activeInHierarchy)
            return;

        GameObject found = GameObject.Find("TargetCube");
        if (found == null) found = GameObject.Find("Target1");
        if (found == null) found = GameObject.Find("Target");

        if (found != null && found.activeInHierarchy)
        {
            target = found.transform;
            return;
        }

        if (!string.IsNullOrEmpty(targetTag) && targetTag != "Untagged")
        {
            try
            {
                found = GameObject.FindGameObjectWithTag(targetTag);
                if (found != null && found.activeInHierarchy)
                {
                    target = found.transform;
                }
            }
            catch (Exception)
            {
            }
        }
    }

    // Spawns squad drone instances and configures their components.
    [ContextMenu("Spawn Drones")]
    public void SpawnDrones()
    {
        ClearDrones();

        GameObject template = dronePrefab;
        if (template == null)
        {
            DroneHardware existing = FindAnyObjectByType<DroneHardware>();
            if (existing != null) template = existing.gameObject;
        }

        if (template == null)
        {
            return;
        }

        lastSpawnedCount = droneCount;
        Vector3 centerPos = transform.position;

        for (int i = 0; i < droneCount; i++)
        {
            float angle = (i / (float)droneCount) * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * spawnRadius;
            Vector3 spawnPos = centerPos + offset + Vector3.up * spawnHeight;

            GameObject droneObj;
            if (template.scene.name != null && i == 0 && template.activeInHierarchy)
            {
                droneObj = template;
                droneObj.transform.position = spawnPos;
            }
            else
            {
                droneObj = Instantiate(template, spawnPos, Quaternion.identity);
            }

            GameObject waypointObj = new GameObject($"TacticalWaypoint_{i + 1}");
            waypointObj.transform.position = spawnPos;
            waypointObj.transform.SetParent(transform);

            DroneNPCFollowTarget followTarget = droneObj.GetComponent<DroneNPCFollowTarget>();
            if (followTarget == null)
            {
                followTarget = droneObj.AddComponent<DroneNPCFollowTarget>();
            }

            followTarget.enabled = true;
            followTarget.targetBox = waypointObj.transform;
            followTarget.stopDistance = 0.5f;
            followTarget.moveSpeed = cruiseSpeed;
            followTarget.requireTargetLock = false;

            FlightControlSystem fcs = droneObj.GetComponent<FlightControlSystem>();
            if (fcs != null) fcs.enabled = true;

            DroneHardware dh = droneObj.GetComponent<DroneHardware>();
            if (dh != null) dh.enabled = true;

            DroneInputs inputs = droneObj.GetComponent<DroneInputs>();
            if (inputs != null) inputs.isAIControlled = true;

            DroneBrain brain = droneObj.GetComponent<DroneBrain>();
            if (brain == null)
            {
                brain = droneObj.AddComponent<DroneBrain>();
            }

            DroneTacticalRole assignedRole = (i == 0) ? DroneTacticalRole.Distractor : DroneTacticalRole.Flanker;
            droneObj.name = $"TacticalDrone_{i + 1}_{assignedRole}";

            DroneSquadMember member = new DroneSquadMember
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

            brain.Initialize(this, member);
            squad.Add(member);
        }

        WireIgnoredColliders();
    }

    // Configures obstacle avoidance on each drone to ignore squadmates and target.
    private void WireIgnoredColliders()
    {
        List<Collider> targetCols = new List<Collider>();
        if (target != null)
            targetCols.AddRange(target.GetComponentsInChildren<Collider>());

        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] == null || squad[i].followTarget == null || squad[i].droneObject == null) continue;

            squad[i].followTarget.ignoredColliders.Clear();

            for (int j = 0; j < squad.Count; j++)
            {
                if (squad[j] == null || squad[j].droneObject == null) continue;
                squad[i].followTarget.ignoredColliders.AddRange(
                    squad[j].droneObject.GetComponentsInChildren<Collider>()
                );
            }

            squad[i].followTarget.ignoredColliders.AddRange(targetCols);
        }
    }

    // Destroys all spawned squad drones and waypoints.
    [ContextMenu("Clear Drones")]
    public void ClearDrones()
    {
        for (int i = squad.Count - 1; i >= 0; i--)
        {
            if (squad[i] != null)
            {
                if (squad[i].tacticalWaypoint != null)
                {
                    if (Application.isPlaying) Destroy(squad[i].tacticalWaypoint.gameObject);
                    else DestroyImmediate(squad[i].tacticalWaypoint.gameObject);
                }

                if (squad[i].droneObject != null)
                {
                    if (Application.isPlaying) Destroy(squad[i].droneObject);
                    else DestroyImmediate(squad[i].droneObject);
                }
            }
        }
        squad.Clear();
    }

    /// <summary>
    /// Called when a squad drone is destroyed.
    /// Decrements droneCount by 1, cleans up waypoints, and reorganizes the remaining squad.
    /// </summary>
    public void OnDroneDestroyed(GameObject destroyedDrone)
    {
        if (destroyedDrone == null) return;

        bool removed = false;
        for (int i = squad.Count - 1; i >= 0; i--)
        {
            if (squad[i] == null)
            {
                squad.RemoveAt(i);
                continue;
            }

            bool isMatch = (squad[i].droneObject == destroyedDrone) ||
                           (squad[i].droneObject == null) ||
                           (!squad[i].droneObject.activeInHierarchy) ||
                           (destroyedDrone.transform.IsChildOf(squad[i].droneObject.transform)) ||
                           (squad[i].droneObject.transform.IsChildOf(destroyedDrone.transform));

            if (isMatch)
            {
                if (squad[i].tacticalWaypoint != null)
                {
                    if (Application.isPlaying) Destroy(squad[i].tacticalWaypoint.gameObject);
                    else DestroyImmediate(squad[i].tacticalWaypoint.gameObject);
                }

                squad.RemoveAt(i);
                removed = true;
            }
        }

        if (!removed) return;

        // Decrement drone count and update lastSpawnedCount so Update() doesn't respawn
        droneCount = squad.Count;
        lastSpawnedCount = droneCount;

        // Re-index remaining squad members
        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null)
            {
                squad[i].droneIndex = i;
                squad[i].assignedFormationSlotIndex = -1;
            }
        }

        // Re-wire obstacle avoidance for surviving drones
        WireIgnoredColliders();

        if (squad.Count == 0)
        {
            Debug.Log("<color=green>[DroneDirector] ALL DRONES ELIMINATED! Victory!</color>");
            return;
        }

        // Re-assign roles and formation slots for the surviving drones
        if (currentPhase == CombatPhase.FollowTarget)
        {
            AssignOptimalFollowSlots();
        }
        else
        {
            AssignDynamicRoles();
        }

        Debug.Log($"<color=cyan>[DroneDirector] Drone destroyed! droneCount is now: {droneCount}. Active squad size: {squad.Count}</color>");
    }
}

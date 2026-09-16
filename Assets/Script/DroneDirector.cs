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

public enum DroneAIArchetype
{
    AggressiveChaser,  // Closes in quickly, dives at high speed, short cooldown, tight combat distance
    RangedHarasser,    // Maintains stand-off distance (8-12m), fires ranged bursts or darts in for quick probes
    CirclerOrbiter,    // Circles/orbits target at varied radius/altitude, providing constant distraction
    Flanker,           // Positions at target's sides/rear (70° - 150°), attacks from unexpected angles
    Opportunist        // Repositions between high/low vantage points, strikes when target is distracted
}

public enum DroneAIState
{
    FollowFormation,   // Target is beyond engagement distance
    Approaching,       // Closing in towards individual combat distance
    Circling,          // Orbiting target at individual radius/speed
    Flanking,          // Maneuvering around target flank/rear
    Repositioning,     // Changing vantage position / elevation
    Attacking,         // Actively closing in / diving to execute strike
    Retreating,        // Disengaging to safe distance after attack/damage
    PostCombat         // Target eliminated, victory sequence
}

[System.Serializable]
public class DronePersonalityProfile
{
    public DroneAIArchetype archetype;
    [Range(0f, 1f)] public float aggression = 0.5f;
    public float preferredDistance = 7f;
    public float strikeDistance = 2.5f;
    public float approachSpeed = 6f;
    public float attackSpeed = 10f;
    public float decisionInterval = 0.5f;
    public float reactionTime = 0.2f;
    public float attackCooldown = 4.5f;
    public float orbitRadius = 8f;
    public float orbitSpeed = 35f; // signed: positive = CW, negative = CCW
    public float preferredAltitude = 2.5f;
    public float flankAngleOffset = 90f;
    public float retreatDistance = 10f;
    public float retreatDuration = 2.5f;
    public bool canRangedAttack = false;
    public float rangedAttackDistance = 14f;

    // Relative multipliers sampled at creation to maintain personality variation when master director settings change
    public float speedMultiplier = 1.0f;
    public float distanceMultiplier = 1.0f;
    public float cooldownMultiplier = 1.0f;
    public float reactionMultiplier = 1.0f;
    public float altitudeOffset = 0f;
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

    [Header("Individual AI Behavior (Runtime Generated)")]
    public DronePersonalityProfile personality;
    public DroneAIState currentState = DroneAIState.Approaching;
    public bool isAttacking = false;
    public float currentAttackCooldown = 0f;
    public float retreatTimer = 0f;

    private float _decisionTimer = 0f;
    private float _currentOrbitAngleDeg = 0f;
    private float _swayPhase = 0f;
    private float _attackTimeoutTimer = 0f;
    private LineRenderer _tracerLine = null;
    private float _tracerTimer = 0f;

    // Initializes references to the director and squad member.
    public void Initialize(DroneDirector directorInstance, DroneDirector.DroneSquadMember squadMember)
    {
        director = directorInstance;
        member = squadMember;

        if (director != null)
        {
            GeneratePersonality();
        }

        // Stagger initial timers so drones don't think on the exact same frame
        _decisionTimer = UnityEngine.Random.Range(0.05f, personality != null ? personality.decisionInterval : 0.5f);
        _currentOrbitAngleDeg = UnityEngine.Random.Range(0f, 360f);
        _swayPhase = UnityEngine.Random.Range(0f, 100f);

        if (personality != null && member != null && member.droneObject != null)
        {
            member.droneObject.name = $"TacticalDrone_{member.droneIndex + 1}_{personality.archetype}";
        }
    }

    /// <summary>
    /// Recalculates individual drone profile values based on central DroneDirector parameters while preserving personality variance.
    /// </summary>
    public void SyncFromDirector(DroneDirector directorInstance)
    {
        if (directorInstance == null) return;
        director = directorInstance;
        if (personality == null) return;

        personality.approachSpeed = director.approachSpeed * personality.speedMultiplier;
        personality.attackSpeed = director.attackSpeed * personality.speedMultiplier;
        personality.strikeDistance = director.attackDistance;
        personality.decisionInterval = Mathf.Max(0.1f, director.decisionInterval * personality.reactionMultiplier);
        personality.reactionTime = Mathf.Max(0.05f, director.reactionTime * personality.reactionMultiplier);
        personality.attackCooldown = Mathf.Max(1.0f, director.attackCooldown * personality.cooldownMultiplier);
        personality.orbitRadius = Mathf.Max(3.0f, director.orbitRadius * personality.distanceMultiplier);
        personality.preferredAltitude = Mathf.Clamp(director.hoverHeight + personality.altitudeOffset, director.minAltitude, director.maxAltitude);
        personality.retreatDistance = director.repositionDistance * personality.distanceMultiplier;
        personality.rangedAttackDistance = director.maxAttackRange;
        personality.canRangedAttack = director.allowRangedVisuals && (director.maxAttackRange > director.attackDistance);
    }

    /// <summary>
    /// Generates a completely unique personality profile with controlled randomness based on director settings.
    /// </summary>
    public void GeneratePersonality()
    {
        if (director == null) return;

        personality = new DronePersonalityProfile();

        // 1. Pick Archetype based on director weights
        int totalWeight = director.weightAggressiveChaser + director.weightRangedHarasser +
                          director.weightCirclerOrbiter + director.weightFlanker + director.weightOpportunist;
        if (totalWeight <= 0) totalWeight = 100;

        int roll = UnityEngine.Random.Range(0, totalWeight);
        if (roll < director.weightAggressiveChaser)
        {
            personality.archetype = DroneAIArchetype.AggressiveChaser;
        }
        else if (roll < director.weightAggressiveChaser + director.weightRangedHarasser)
        {
            personality.archetype = DroneAIArchetype.RangedHarasser;
        }
        else if (roll < director.weightAggressiveChaser + director.weightRangedHarasser + director.weightCirclerOrbiter)
        {
            personality.archetype = DroneAIArchetype.CirclerOrbiter;
        }
        else if (roll < director.weightAggressiveChaser + director.weightRangedHarasser + director.weightCirclerOrbiter + director.weightFlanker)
        {
            personality.archetype = DroneAIArchetype.Flanker;
        }
        else
        {
            personality.archetype = DroneAIArchetype.Opportunist;
        }

        // 2. Sample relative multipliers for individual personality variety
        float minSpeedM = Mathf.Min(director.speedMultiplierRange.x, director.speedMultiplierRange.y);
        float maxSpeedM = Mathf.Max(director.speedMultiplierRange.x, director.speedMultiplierRange.y);
        personality.speedMultiplier = UnityEngine.Random.Range(minSpeedM, maxSpeedM);

        float minDistM = Mathf.Min(director.distanceMultiplierRange.x, director.distanceMultiplierRange.y);
        float maxDistM = Mathf.Max(director.distanceMultiplierRange.x, director.distanceMultiplierRange.y);
        personality.distanceMultiplier = UnityEngine.Random.Range(minDistM, maxDistM);

        float minCdM = Mathf.Min(director.cooldownMultiplierRange.x, director.cooldownMultiplierRange.y);
        float maxCdM = Mathf.Max(director.cooldownMultiplierRange.x, director.cooldownMultiplierRange.y);
        personality.cooldownMultiplier = UnityEngine.Random.Range(minCdM, maxCdM);

        float minReactM = Mathf.Min(director.reactionTimeRange.x, director.reactionTimeRange.y) / Mathf.Max(director.reactionTime, 0.01f);
        float maxReactM = Mathf.Max(director.reactionTimeRange.x, director.reactionTimeRange.y) / Mathf.Max(director.reactionTime, 0.01f);
        personality.reactionMultiplier = UnityEngine.Random.Range(minReactM, maxReactM);

        float minAltOff = Mathf.Min(director.altitudeOffsetRange.x, director.altitudeOffsetRange.y);
        float maxAltOff = Mathf.Max(director.altitudeOffsetRange.x, director.altitudeOffsetRange.y);
        personality.altitudeOffset = UnityEngine.Random.Range(minAltOff, maxAltOff);

        float minAgg = Mathf.Min(director.aggressionRange.x, director.aggressionRange.y);
        float maxAgg = Mathf.Max(director.aggressionRange.x, director.aggressionRange.y);
        personality.aggression = UnityEngine.Random.Range(minAgg, maxAgg);

        float minOrbS = Mathf.Min(director.orbitSpeedRange.x, director.orbitSpeedRange.y);
        float maxOrbS = Mathf.Max(director.orbitSpeedRange.x, director.orbitSpeedRange.y);
        float orbitDir = (UnityEngine.Random.value > 0.5f) ? 1f : -1f;
        personality.orbitSpeed = UnityEngine.Random.Range(minOrbS, maxOrbS) * orbitDir;

        float minRet = Mathf.Min(director.retreatDurationRange.x, director.retreatDurationRange.y);
        float maxRet = Mathf.Max(director.retreatDurationRange.x, director.retreatDurationRange.y);
        personality.retreatDuration = UnityEngine.Random.Range(minRet, maxRet);

        personality.flankAngleOffset = (UnityEngine.Random.value > 0.5f ? 1f : -1f) * UnityEngine.Random.Range(70f, 135f);

        // Apply director base values scaled by individual multipliers
        SyncFromDirector(director);

        // 3. Archetype-specific bias & initial state
        switch (personality.archetype)
        {
            case DroneAIArchetype.AggressiveChaser:
                personality.aggression = Mathf.Clamp01(personality.aggression + 0.2f);
                personality.preferredDistance = Mathf.Max(3.0f, personality.preferredDistance - 2f);
                personality.attackCooldown = Mathf.Max(1.5f, personality.attackCooldown - 1.0f);
                personality.attackSpeed = Mathf.Max(personality.attackSpeed, director.attackSpeed * 1.1f);
                currentState = DroneAIState.Approaching;
                break;

            case DroneAIArchetype.RangedHarasser:
                personality.preferredDistance = Mathf.Max(8.0f, personality.preferredDistance + 1.5f);
                personality.aggression = Mathf.Clamp(personality.aggression * 0.75f, 0.2f, 0.7f);
                currentState = DroneAIState.Approaching;
                break;

            case DroneAIArchetype.CirclerOrbiter:
                personality.preferredDistance = personality.orbitRadius;
                currentState = DroneAIState.Circling;
                break;

            case DroneAIArchetype.Flanker:
                personality.preferredDistance = Mathf.Clamp(personality.preferredDistance, 6.5f, 10f);
                currentState = DroneAIState.Flanking;
                break;

            case DroneAIArchetype.Opportunist:
                personality.preferredAltitude = UnityEngine.Random.value > 0.5f ? director.maxAltitude - 1f : director.minAltitude + 0.5f;
                currentState = DroneAIState.Repositioning;
                break;
        }
    }

    /// <summary>
    /// Executes the autonomous individual AI per-frame tick: updates timers, makes tactical decisions,
    /// tracks attacks, calculates dynamic 3D waypoints, and smoothly adjusts speed.
    /// </summary>
    public void TickAutonomousAI(Transform target, Vector3 targetCenter, Vector3 forwardDir, float dt)
    {
        if (director == null || member == null || member.followTarget == null || member.tacticalWaypoint == null)
            return;

        if (currentState == DroneAIState.PostCombat)
            return;

        if (personality == null)
        {
            GeneratePersonality();
        }

        Vector3 myPos = transform.position;
        distanceToWaypoint = Vector3.Distance(myPos, member.tacticalWaypoint.position);
        distanceToCentroid = Vector3.Distance(myPos, director.GetSquadCentroid());
        isConnectedWithSquad = distanceToCentroid < 30.0f;
        isLaggingBehind = distanceToWaypoint > 3.5f;

        // Update internal cooldowns
        if (currentAttackCooldown > 0f) currentAttackCooldown -= dt;
        if (_decisionTimer > 0f) _decisionTimer -= dt;

        if (_tracerTimer > 0f)
        {
            _tracerTimer -= dt;
            if (_tracerTimer <= 0f && _tracerLine != null)
            {
                _tracerLine.enabled = false;
            }
        }

        // 1. Tactical decision evaluation
        if (_decisionTimer <= 0f)
        {
            _decisionTimer = personality.decisionInterval + UnityEngine.Random.Range(-0.08f, 0.08f);
            EvaluateTacticalDecision(target);
        }

        // 2. Attack tracking & proximity check
        if (currentState == DroneAIState.Attacking)
        {
            isAttacking = true;
            _attackTimeoutTimer += dt;

            float distToTarget = Vector3.Distance(myPos, targetCenter);
            if (distToTarget <= personality.strikeDistance)
            {
                director.ExecuteDroneStrike(this, target);
                return;
            }

            // Safety timeout: if blocked or taking too long, abort attack and disengage
            if (_attackTimeoutTimer > 6.0f)
            {
                director.ReleaseAttackToken(this);
                OnStrikeDelivered();
                return;
            }
        }
        else
        {
            isAttacking = false;
            _attackTimeoutTimer = 0f;
        }

        // 3. Retreat timer handling
        if (currentState == DroneAIState.Retreating)
        {
            retreatTimer -= dt;
            if (retreatTimer <= 0f)
            {
                switch (personality.archetype)
                {
                    case DroneAIArchetype.CirclerOrbiter:
                        currentState = DroneAIState.Circling;
                        break;
                    case DroneAIArchetype.Flanker:
                        currentState = DroneAIState.Flanking;
                        break;
                    case DroneAIArchetype.Opportunist:
                        currentState = DroneAIState.Repositioning;
                        break;
                    default:
                        currentState = DroneAIState.Approaching;
                        break;
                }
            }
        }

        // 4. Compute unique dynamic waypoint position
        _currentOrbitAngleDeg += personality.orbitSpeed * dt;

        float sway = Mathf.Sin(Time.time * 1.6f + _swayPhase) * 0.35f;
        float altSway = Mathf.Cos(Time.time * 1.3f + _swayPhase * 0.8f) * 0.25f;

        Vector3 desiredWaypointPos = targetCenter;
        float targetFlightSpeed = personality.approachSpeed;

        switch (currentState)
        {
            case DroneAIState.Attacking:
                desiredWaypointPos = targetCenter + Vector3.up * 0.4f;
                targetFlightSpeed = personality.attackSpeed;
                break;

            case DroneAIState.Retreating:
                Vector3 awayDir = (myPos - targetCenter);
                awayDir.y = 0f;
                if (awayDir.sqrMagnitude < 0.01f) awayDir = -forwardDir;
                awayDir.Normalize();

                desiredWaypointPos = targetCenter + awayDir * personality.retreatDistance + Vector3.up * (personality.preferredAltitude + 2.0f);
                targetFlightSpeed = personality.approachSpeed * 1.15f;
                break;

            case DroneAIState.Circling:
                Quaternion orbitRot = Quaternion.AngleAxis(_currentOrbitAngleDeg + sway * 15f, Vector3.up);
                Vector3 orbitOffset = (orbitRot * forwardDir) * personality.orbitRadius;
                desiredWaypointPos = targetCenter + orbitOffset + Vector3.up * (personality.preferredAltitude + altSway);
                targetFlightSpeed = personality.approachSpeed;
                break;

            case DroneAIState.Flanking:
                Quaternion flankRot = Quaternion.AngleAxis(personality.flankAngleOffset + sway * 10f, Vector3.up);
                Vector3 flankOffset = (flankRot * forwardDir) * personality.preferredDistance;
                desiredWaypointPos = targetCenter + flankOffset + Vector3.up * (personality.preferredAltitude + altSway);
                targetFlightSpeed = personality.approachSpeed;
                break;

            case DroneAIState.Repositioning:
                Quaternion repRot = Quaternion.AngleAxis(_currentOrbitAngleDeg * 0.5f, Vector3.up);
                Vector3 repOffset = (repRot * forwardDir) * personality.preferredDistance;
                desiredWaypointPos = targetCenter + repOffset + Vector3.up * (personality.preferredAltitude + altSway * 1.5f);
                targetFlightSpeed = personality.approachSpeed * 1.1f;
                break;

            case DroneAIState.Approaching:
            default:
                float angle = _currentOrbitAngleDeg + (member.droneIndex * 45f);
                Quaternion appRot = Quaternion.AngleAxis(angle, Vector3.up);
                Vector3 appOffset = (appRot * forwardDir) * personality.preferredDistance;
                desiredWaypointPos = targetCenter + appOffset + Vector3.up * (personality.preferredAltitude + altSway);
                targetFlightSpeed = personality.approachSpeed;
                break;
        }

        // Clamp against environmental obstacles
        desiredWaypointPos = director.ClampToNavigableSpace(targetCenter, desiredWaypointPos);
        member.tacticalWaypoint.position = desiredWaypointPos;

        // Smooth flight speed adjustment
        currentFlightSpeed = Mathf.MoveTowards(member.followTarget.moveSpeed, targetFlightSpeed, 10.0f * dt);
        member.followTarget.moveSpeed = currentFlightSpeed;
    }

    private void EvaluateTacticalDecision(Transform target)
    {
        if (target == null || currentState == DroneAIState.Attacking || currentState == DroneAIState.Retreating)
            return;

        bool canAttack = (currentAttackCooldown <= 0f);

        switch (personality.archetype)
        {
            case DroneAIArchetype.AggressiveChaser:
                if (canAttack)
                {
                    if (director.TryAcquireAttackToken(this))
                    {
                        StartAttack();
                    }
                    else
                    {
                        currentState = DroneAIState.Approaching;
                    }
                }
                else
                {
                    currentState = DroneAIState.Approaching;
                }
                break;

            case DroneAIArchetype.RangedHarasser:
                if (personality.canRangedAttack && canAttack)
                {
                    float dist = Vector3.Distance(transform.position, target.position);
                    if (dist <= personality.rangedAttackDistance)
                    {
                        FireRangedBurst(target);
                        currentAttackCooldown = director.rangedAttackCooldown + UnityEngine.Random.Range(-0.4f, 0.4f);
                    }
                }
                else if (canAttack && UnityEngine.Random.value < (personality.aggression * 0.4f))
                {
                    if (director.TryAcquireAttackToken(this))
                    {
                        StartAttack();
                    }
                }
                break;

            case DroneAIArchetype.CirclerOrbiter:
                currentState = DroneAIState.Circling;
                if (canAttack && UnityEngine.Random.value < (personality.aggression * 0.35f))
                {
                    if (director.TryAcquireAttackToken(this))
                    {
                        StartAttack();
                    }
                }
                break;

            case DroneAIArchetype.Flanker:
                currentState = DroneAIState.Flanking;
                if (canAttack && UnityEngine.Random.value < (personality.aggression * 0.5f))
                {
                    if (director.TryAcquireAttackToken(this))
                    {
                        StartAttack();
                    }
                }
                break;

            case DroneAIArchetype.Opportunist:
                if (canAttack && UnityEngine.Random.value < (personality.aggression * 0.6f))
                {
                    if (director.TryAcquireAttackToken(this))
                    {
                        StartAttack();
                    }
                    else
                    {
                        currentState = DroneAIState.Repositioning;
                    }
                }
                else
                {
                    if (UnityEngine.Random.value < 0.3f)
                    {
                        personality.preferredAltitude = UnityEngine.Random.Range(director.altitudeRange.x, director.altitudeRange.y + 1.5f);
                    }
                    currentState = DroneAIState.Repositioning;
                }
                break;
        }
    }

    private void StartAttack()
    {
        currentState = DroneAIState.Attacking;
        _attackTimeoutTimer = 0f;
    }

    /// <summary>
    /// Called when this drone successfully executes a strike on the target.
    /// Only this drone enters retreat on its individual cooldown; other drones remain active.
    /// </summary>
    public void OnStrikeDelivered()
    {
        currentState = DroneAIState.Retreating;
        retreatTimer = personality != null ? personality.retreatDuration : 2.5f;
        currentAttackCooldown = personality != null ? personality.attackCooldown : 4.5f;
    }

    public void CancelAttack()
    {
        if (director != null)
        {
            director.ReleaseAttackToken(this);
        }
        currentState = DroneAIState.Retreating;
        retreatTimer = 1.5f;
    }

    private void FireRangedBurst(Transform target)
    {
        if (target == null) return;

        Vector3 startPos = transform.position;
        Vector3 targetPos = target.position;

        if (_tracerLine == null)
        {
            _tracerLine = gameObject.GetComponent<LineRenderer>();
            if (_tracerLine == null)
            {
                _tracerLine = gameObject.AddComponent<LineRenderer>();
            }
            _tracerLine.positionCount = 2;
            _tracerLine.startWidth = 0.08f;
            _tracerLine.endWidth = 0.08f;
            _tracerLine.useWorldSpace = true;
            if (_tracerLine.sharedMaterial == null)
            {
                Shader s = Shader.Find("Sprites/Default");
                if (s != null) _tracerLine.material = new Material(s);
            }
            _tracerLine.startColor = new Color(1f, 0.5f, 0.1f, 0.9f);
            _tracerLine.endColor = new Color(1f, 0.8f, 0.2f, 0.9f);
        }

        _tracerLine.enabled = true;
        _tracerLine.SetPosition(0, startPos);
        _tracerLine.SetPosition(1, targetPos);
        _tracerTimer = 0.15f;

        CanonHealth canonHealth = target.GetComponentInParent<CanonHealth>() ?? target.GetComponentInChildren<CanonHealth>();
        if (canonHealth != null)
        {
            canonHealth.TakeDamage(1);
            if (canonHealth.IsDestroyed)
            {
                if (director != null)
                {
                    director.ExecuteDroneStrike(this, target);
                }
            }
        }
    }

    // Regulates adaptive flight speed based on distance and wingman positions (legacy fallback).
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
    public static DroneDirector Instance { get; private set; }

    // =========================================================================
    // 1. FLIGHT
    // =========================================================================
    [Header("Flight")]
    [Tooltip("Maximum horizontal forward flight speed limit enforced by FCS.")]
    public float maxForwardSpeed = 25.0f;
    [Tooltip("Maximum horizontal strafe/side flight speed limit enforced by FCS.")]
    public float maxSideSpeed = 20.0f;
    [Tooltip("Hard clamp on drone pitch and roll tilt angle in degrees.")]
    public float maxTiltAngle = 30.0f;
    [Tooltip("Maximum upward motor force capacity per motor.")]
    public float maxMotorForce = 10.0f;
    [Tooltip("Drone physical mass in kilograms.")]
    public float droneMass = 1.0f;
    [Tooltip("Rigidbody linear damping / drag to resist unwanted air drift.")]
    public float linearDamping = 0.2f;
    [Tooltip("Rigidbody angular damping to eliminate rotational oscillations.")]
    public float angularDamping = 1.0f;

    // =========================================================================
    // 2. MOVEMENT
    // =========================================================================
    [Header("Movement")]
    [Tooltip("Standard cruise speed for formation flight and general cruising.")]
    public float cruiseSpeed = 6.0f;
    [Tooltip("Approach speed when navigating toward the target or closing distance.")]
    public float approachSpeed = 8.0f;
    [Tooltip("High-speed sprint when actively diving or executing an attack run.")]
    public float attackSpeed = 12.0f;
    [Tooltip("Speed when disengaging, retreating, or repositioning away from target.")]
    public float disengageSpeed = 8.0f;
    [Tooltip("Acceleration rate for smooth speed transitions.")]
    public float acceleration = 8.0f;
    [Tooltip("Distance to target/waypoint where the drone halts and holds hover.")]
    public float stopDistance = 1.5f;
    [Tooltip("Distance from waypoint where arrival slowdown and deceleration begins.")]
    public float brakingDistance = 3.0f;

    // =========================================================================
    // 3. ROTATION
    // =========================================================================
    [Header("Rotation")]
    [Tooltip("Target alignment and yaw rotation speed in radians/second.")]
    public float turnSpeed = 6.0f;
    [Tooltip("Maximum yaw angular rate limit in degrees/second.")]
    public float maxYawRate = 90.0f;
    [Tooltip("Maximum pitch angular rate limit in degrees/second.")]
    public float maxPitchRate = 100.0f;
    [Tooltip("Maximum roll angular rate limit in degrees/second.")]
    public float maxRollRate = 100.0f;
    [Tooltip("Maximum aerodynamic banking roll angle into turns in degrees.")]
    public float maxBankAngle = 35.0f;
    [Tooltip("Banking sensitivity multiplier based on turn sharpness.")]
    public float bankAmount = 1.2f;
    [Tooltip("Smoothing factor for steering direction changes.")]
    public float steeringSmoothing = 8.0f;

    // =========================================================================
    // 4. ALTITUDE
    // =========================================================================
    [Header("Altitude")]
    [Tooltip("Standard cruising and hover altitude offset above target position.")]
    public float hoverHeight = 2.5f;
    [Tooltip("Lower flight ceiling / minimum ground clearance altitude.")]
    public float minAltitude = 1.0f;
    [Tooltip("Upper flight ceiling clamp.")]
    public float maxAltitude = 15.0f;
    [Tooltip("Maximum vertical ascent and descent rate in meters/second.")]
    public float maxClimbSpeed = 6.0f;

    // =========================================================================
    // 5. TARGETING
    // =========================================================================
    [Header("Targeting")]
    [Tooltip("Transform of the primary target (player, vehicle, objective).")]
    public Transform target;
    [Tooltip("Tag used to automatically locate the target if unassigned.")]
    public string targetTag = "";
    [Tooltip("Maximum detection range for tracking targets.")]
    public float detectionRange = 50.0f;
    [Tooltip("Distance threshold where combat maneuvers and tactical behavior activate.")]
    public float engagementDistance = 10.0f;
    [Tooltip("Interval (seconds) at which target switching conditions are evaluated.")]
    public float targetSwitchingInterval = 5.0f;
    [Tooltip("Aim accuracy factor [0 to 1] influencing lead and targeting precision.")]
    [Range(0f, 1f)]
    public float aimAccuracy = 0.9f;

    // =========================================================================
    // 6. ATTACK
    // =========================================================================
    [Header("Attack")]
    [Tooltip("Contact or strike initiation distance for melee dive attacks.")]
    public float attackDistance = 2.5f;
    [Tooltip("Minimum standoff distance before attack initiation.")]
    public float minAttackRange = 2.0f;
    [Tooltip("Maximum range for ranged attacks and harassment.")]
    public float maxAttackRange = 14.0f;
    [Tooltip("Safe standoff distance drones retreat to after delivering an attack.")]
    public float repositionDistance = 8.0f;
    [Tooltip("Radius of orbit circle for circling and distraction maneuvers.")]
    public float orbitRadius = 8.0f;
    [Tooltip("Base cooldown in seconds between attacks for an individual drone.")]
    public float attackCooldown = 4.5f;
    [Tooltip("Minimum delay in seconds between any two drone strikes squad-wide.")]
    [Min(0f)]
    public float globalAttackCooldown = 0.8f;
    [Tooltip("Maximum number of drones allowed to actively strike simultaneously.")]
    [Range(1, 4)]
    public int maxConcurrentAttackers = 1;
    [Tooltip("Burst fire rate / cooldown for ranged visual attacks.")]
    public float fireRate = 1.0f;
    [Tooltip("Base aggression level [0 to 1] influencing attack probability.")]
    [Range(0f, 1f)]
    public float aggression = 0.6f;
    [Tooltip("Enable ranged laser tracer visual effects during combat.")]
    public bool allowRangedVisuals = true;

    // =========================================================================
    // 7. PID / FLIGHT CONTROL
    // =========================================================================
    [Header("PID / Flight Control")]
    [Tooltip("Proportional gain for horizontal velocity controller.")]
    public float velocityKp = 1.5f;
    [Tooltip("Integral gain for horizontal velocity controller.")]
    public float velocityKi = 0.05f;
    [Tooltip("Derivative gain for horizontal velocity controller.")]
    public float velocityKd = 0.25f;
    [Tooltip("Proportional gain for pitch attitude angle controller.")]
    public float pitchAngleKp = 4.0f;
    [Tooltip("Proportional gain for roll attitude angle controller.")]
    public float rollAngleKp = 4.0f;
    [Tooltip("Proportional gain for angular rate controllers (pitch and roll).")]
    public float rateKp = 0.8f;
    [Tooltip("Derivative gain for angular rate controllers (pitch and roll).")]
    public float rateKd = 0.05f;
    [Tooltip("Proportional gain for yaw rotation rate controller.")]
    public float yawRateKp = 2.5f;
    [Tooltip("Proportional gain for vertical altitude controller.")]
    public float altitudeKp = 3.0f;

    // =========================================================================
    // 8. AI BEHAVIOR & ROLES
    // =========================================================================
    [Header("AI Behavior & Roles")]
    [Tooltip("Enable independent AI personality profiles and autonomous decision making.")]
    public bool enableAutonomousIndividualAI = true;
    [Tooltip("Base AI reaction latency in seconds.")]
    public float reactionTime = 0.2f;
    [Tooltip("Evaluation interval between tactical decision reassessments.")]
    public float decisionInterval = 0.5f;
    [Tooltip("Minimum spacing between squad drones to prevent crowding.")]
    public float minDroneSeparation = 5.0f;
    [Tooltip("Strength of repulsive steering force applied between nearby drones.")]
    public float separationRepulsionStrength = 2.0f;
    [Tooltip("Relative spawn weight for Aggressive Chaser archetype.")]
    [Range(0, 100)] public int weightAggressiveChaser = 25;
    [Tooltip("Relative spawn weight for Ranged Harasser archetype.")]
    [Range(0, 100)] public int weightRangedHarasser = 25;
    [Tooltip("Relative spawn weight for Circler Orbiter archetype.")]
    [Range(0, 100)] public int weightCirclerOrbiter = 25;
    [Tooltip("Relative spawn weight for Flanker archetype.")]
    [Range(0, 100)] public int weightFlanker = 15;
    [Tooltip("Relative spawn weight for Opportunist archetype.")]
    [Range(0, 100)] public int weightOpportunist = 10;

    // =========================================================================
    // 9. RANDOMIZATION RANGES
    // =========================================================================
    [Header("Randomization Ranges")]
    [Tooltip("Sampling range for individual drone aggression.")]
    public Vector2 aggressionRange = new Vector2(0.25f, 0.95f);
    [Tooltip("Sampling range for individual drone speed multiplier.")]
    public Vector2 speedMultiplierRange = new Vector2(0.85f, 1.25f);
    [Tooltip("Sampling range for individual drone standoff distance multiplier.")]
    public Vector2 distanceMultiplierRange = new Vector2(0.8f, 1.2f);
    [Tooltip("Sampling range for individual drone attack cooldown multiplier.")]
    public Vector2 cooldownMultiplierRange = new Vector2(0.75f, 1.3f);
    [Tooltip("Sampling range for individual drone reaction latency in seconds.")]
    public Vector2 reactionTimeRange = new Vector2(0.1f, 0.35f);
    [Tooltip("Sampling range for individual drone altitude offset from hoverHeight.")]
    public Vector2 altitudeOffsetRange = new Vector2(-0.8f, 1.2f);
    [Tooltip("Sampling range for individual drone orbit speed in degrees/second.")]
    public Vector2 orbitSpeedRange = new Vector2(25.0f, 50.0f);
    [Tooltip("Sampling range for retreat duration in seconds after an attack.")]
    public Vector2 retreatDurationRange = new Vector2(2.0f, 3.2f);

    // =========================================================================
    // 10. FINAL FORMATION
    // =========================================================================
    [Header("Final Formation")]
    [Tooltip("Whether to automatically select Parallel for even counts and V-Formation for odd counts.")]
    public bool autoSelectFormationByCount = true;

    [Tooltip("Currently active final formation type (auto-selected by drone count or set manually).")]
    public FinalFormationType finalFormationType = FinalFormationType.VFormation;

    [Tooltip("Spacing between adjacent drone slots in the final formation (meters). Used for parallel spacing and baseline V spacing.")]
    public float formationSpacing = 4.0f;

    [Tooltip("Full opening angle of the V formation in degrees (e.g. 60° to 90°).")]
    [Range(30f, 150f)]
    public float vAngle = 70.0f;

    [Tooltip("Longitudinal/forward spacing between successive wing rows in the V formation (meters).")]
    public float vForwardSpacing = 2.5f;

    [Tooltip("Lateral/side spacing between successive wing drones in the V formation (meters).")]
    public float vSideSpacing = 3.0f;

    [Tooltip("Symmetrical heading deflection angle for wing drones relative to formation forward (degrees). Set to 0 for all drones facing straight forward.")]
    [Range(-30f, 30f)]
    public float wingHeadingAngle = 0.0f;

    [Tooltip("Target altitude of the final formation above the ground/target (meters).")]
    public float formationHeight = 3.0f;

    [Tooltip("Yaw rotation angle offset (degrees) for the final formation orientation.")]
    [Range(-180f, 180f)]
    public float formationRotation = 0f;

    [Tooltip("Strict minimum safe ground clearance in meters. Formation slots and drones will never be placed below this altitude above terrain.")]
    public float minGroundClearance = 2.0f;

    [Tooltip("LayerMask used to check ground and terrain under formation slots and drones.")]
    public LayerMask groundCheckLayers = ~0;

    [Tooltip("Maximum allowed distance from assigned slot to consider position complete (meters).")]
    [Range(0.1f, 1.0f)]
    public float positionTolerance = 0.35f;

    [Tooltip("Maximum allowed heading/yaw error (degrees) to consider rotation complete.")]
    [Range(1.0f, 15.0f)]
    public float rotationTolerance = 4.0f;

    [Tooltip("Cruise flight speed when transitioning into the final formation (m/s).")]
    [Range(3.0f, 15.0f)]
    public float formationSpeed = 8.0f;

    [Tooltip("Distance from slot at which the drone smoothly decelerates into its final position (meters).")]
    [Range(1.0f, 6.0f)]
    public float formationDecelerationDistance = 3.0f;

    [Tooltip("Multiplier for yaw alignment responsiveness when snapping to the formation heading.")]
    [Range(0.5f, 3.0f)]
    public float finalAlignmentStrength = 1.6f;

    [Tooltip("Safety timeout (seconds) for drones to travel to final slots before forcing stabilization.")]
    public float formationTimeout = 12.0f;

    [Tooltip("Hold duration (seconds) after all drones complete formation before mission completion.")]
    public float cinematicHoldDuration = 4.0f;

    public float minimumAltitude { get => minGroundClearance; set => minGroundClearance = value; }
    public float formationBrakingDistance { get => formationDecelerationDistance; set => formationDecelerationDistance = value; }

    // =========================================================================
    // 11. DEBUG
    // =========================================================================
    [Header("Debug")]
    public CombatPhase currentPhase = CombatPhase.SearchAndSpread;
    public TacticalFormationProfile currentCombatFormation = TacticalFormationProfile.LeftRightFlank;
    public FinalFormationType selectedFinalFormation { get => finalFormationType; set => finalFormationType = value; }
    public float phaseTimer = 0f;
    public List<DroneSquadMember> squad = new List<DroneSquadMember>();

    // ── Internal / Backward Compatibility State ──────────────────────────────
    [HideInInspector] public float followDistance = 6.0f;
    [HideInInspector] public float followHeight = 1.8f;
    [HideInInspector] public float followFormationSpacing = 4.0f;
    [HideInInspector] public float followSpeed = 6.5f;
    [HideInInspector] public float maxFollowSpeed = 11.0f;
    [HideInInspector] public float followAcceleration = 7.0f;
    [HideInInspector] public FinalFormationType followFormation = FinalFormationType.VFormation;
    [HideInInspector] public bool randomizeFollowFormation = true;
    [HideInInspector] public bool chaseMode = true;
    [HideInInspector] public float reachDistance { get => followDistance; set => followDistance = value; }
    [HideInInspector] public float startSpeed { get => followSpeed; set => followSpeed = value; }
    [HideInInspector] public float maxSpeed { get => maxFollowSpeed; set => maxFollowSpeed = value; }
    [HideInInspector] public float obstacleCheckDistance = 4.0f;
    [HideInInspector] public int formationsBeforeAttack = 4;
    [HideInInspector] public float surroundFormationDuration = 3.5f;
    [HideInInspector] public int currentFormationStep = 0;
    [HideInInspector] public float formationTransitionSpeed = 120f;
    [HideInInspector] public float radialTransitionSpeed = 8.0f;
    [HideInInspector] public float verticalTransitionSpeed = 5.0f;
    [HideInInspector] public float spreadDuration = 3.0f;
    [HideInInspector] public float distractDuration = 5.0f;
    [HideInInspector] public float attackDuration = 4.0f;
    [HideInInspector] public float disengageDuration = 2.5f;
    [HideInInspector] public float strikeDistance { get => attackDistance; set => attackDistance = value; }
    [HideInInspector] public float arrivalSlowdownDistance { get => brakingDistance; set => brakingDistance = value; }
    [HideInInspector] public float minCombatSpeed = 3.5f;
    [HideInInspector] public TacticalAttackPattern currentAttackPattern = TacticalAttackPattern.ConvergingPincer;
    [HideInInspector] public float victoryFormationSpacing { get => formationSpacing; set => formationSpacing = value; }
    [HideInInspector] public float victoryFormationHeight { get => formationHeight; set => formationHeight = value; }
    [HideInInspector] public float formationArrivalTolerance { get => positionTolerance; set => positionTolerance = value; }
    [HideInInspector] public float formationSyncTimeTolerance = 0.5f;
    [HideInInspector] public float victoryTravelTimeout { get => formationTimeout; set => formationTimeout = value; }
    [HideInInspector] public float minTravelSpeed = 4.5f;
    [HideInInspector] public float maxTravelSpeed = 9.5f;
    [HideInInspector] public bool isMissionComplete = false;
    [HideInInspector] public UnityEvent OnMissionComplete;
    [HideInInspector] public LayerMask obstacleLayers = -1;

    [SerializeField, HideInInspector] private DroneSpanSystem droneSpanSystem;
    public DroneSpanSystem DroneSpanSystem { get => droneSpanSystem; set => droneSpanSystem = value; }

    public int droneCount
    {
        get => droneSpanSystem != null ? droneSpanSystem.DroneCount : squad.Count;
        set
        {
            if (droneSpanSystem != null) droneSpanSystem.DroneCount = value;
            lastSpawnedCount = value;
        }
    }

    [HideInInspector] public float rangedAttackRange { get => maxAttackRange; set => maxAttackRange = value; }
    [HideInInspector] public float rangedAttackCooldown { get => fireRate; set => fireRate = value; }
    [HideInInspector] public Vector2 preferredDistanceRange { get => new Vector2(minAttackRange, repositionDistance); set {} }
    [HideInInspector] public Vector2 strikeDistanceRange { get => new Vector2(attackDistance * 0.8f, attackDistance * 1.1f); set {} }
    [HideInInspector] public Vector2 approachSpeedRange { get => new Vector2(approachSpeed * speedMultiplierRange.x, approachSpeed * speedMultiplierRange.y); set {} }
    [HideInInspector] public Vector2 attackSpeedRange { get => new Vector2(attackSpeed * speedMultiplierRange.x, attackSpeed * speedMultiplierRange.y); set {} }
    [HideInInspector] public Vector2 decisionIntervalRange { get => new Vector2(decisionInterval * 0.6f, decisionInterval * 1.4f); set {} }
    [HideInInspector] public Vector2 attackCooldownRange { get => new Vector2(attackCooldown * cooldownMultiplierRange.x, attackCooldown * cooldownMultiplierRange.y); set {} }
    [HideInInspector] public Vector2 orbitRadiusRange { get => new Vector2(orbitRadius * distanceMultiplierRange.x, orbitRadius * distanceMultiplierRange.y); set {} }
    [HideInInspector] public Vector2 altitudeRange { get => new Vector2(minAltitude, maxAltitude); set { minAltitude = value.x; maxAltitude = value.y; } }

    public int LastSpawnedCount { get => lastSpawnedCount; set => lastSpawnedCount = value; }
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

    private readonly HashSet<DroneBrain> _activeAttackers = new HashSet<DroneBrain>();
    private float _lastGlobalStrikeTime = -10f;

    /// <summary>
    /// Attempts to acquire an attack authorization token for the requesting drone.
    /// Limits concurrent attackers to maxConcurrentAttackers and enforces globalAttackCooldown.
    /// </summary>
    public bool TryAcquireAttackToken(DroneBrain brain)
    {
        if (brain == null) return false;
        if (_activeAttackers.Contains(brain)) return true;
        if (Time.time - _lastGlobalStrikeTime < globalAttackCooldown) return false;
        if (_activeAttackers.Count < maxConcurrentAttackers)
        {
            _activeAttackers.Add(brain);
            return true;
        }
        return false;
    }

    /// <summary>
    /// Releases the attack token held by the drone so other squadmates can attack.
    /// </summary>
    public void ReleaseAttackToken(DroneBrain brain)
    {
        if (brain != null)
        {
            _activeAttackers.Remove(brain);
        }
    }

    public void RecordGlobalStrike()
    {
        _lastGlobalStrikeTime = Time.time;
    }

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
        EnsureSpanSystemConnection();
    }

    /// <summary>
    /// Ensures the two-way serialized connection between DroneDirector and DroneSpanSystem.
    /// </summary>
    public void EnsureSpanSystemConnection()
    {
        if (droneSpanSystem == null)
        {
            droneSpanSystem = GetComponent<DroneSpanSystem>() ?? FindAnyObjectByType<DroneSpanSystem>();
        }

        if (droneSpanSystem != null && droneSpanSystem.DroneDirector == null)
        {
            droneSpanSystem.DroneDirector = this;
        }
    }

    /// <summary>
    /// Fully equips an autonomous drone with required navigation, input, FCS, hardware, brain, and health components,
    /// parenting a dedicated tactical waypoint to DroneDirector and syncing all settings.
    /// </summary>
    public DroneSquadMember ConfigureDrone(GameObject droneObj, Vector3 spawnPos, int index)
    {
        if (droneObj == null) return null;

        // Dedicated tactical waypoint parented to the director transform
        GameObject waypointObj = new GameObject($"TacticalWaypoint_{index + 1}");
        waypointObj.transform.position = spawnPos;
        waypointObj.transform.SetParent(transform);

        // Navigation
        DroneNPCFollowTarget followTarget = droneObj.GetComponent<DroneNPCFollowTarget>();
        if (followTarget == null) followTarget = droneObj.AddComponent<DroneNPCFollowTarget>();
        followTarget.enabled = true;
        followTarget.targetBox = waypointObj.transform;
        followTarget.stopDistance = stopDistance;
        followTarget.moveSpeed = cruiseSpeed;
        followTarget.requireTargetLock = false;

        // Enable autonomous flight control systems
        DroneInputs inputs = droneObj.GetComponent<DroneInputs>();
        if (inputs == null) inputs = droneObj.AddComponent<DroneInputs>();
        inputs.isAIControlled = true;

        FlightControlSystem fcs = droneObj.GetComponent<FlightControlSystem>();
        if (fcs == null) fcs = droneObj.AddComponent<FlightControlSystem>();
        fcs.enabled = true;

        DroneHardware dh = droneObj.GetComponent<DroneHardware>();
        if (dh == null) dh = droneObj.AddComponent<DroneHardware>();
        dh.enabled = true;

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

        DroneSquadMember member = new DroneSquadMember
        {
            droneObject = droneObj,
            followTarget = followTarget,
            tacticalWaypoint = waypointObj.transform,
            brain = brain,
            role = assignedRole,
            droneIndex = index,
            assignedFormationSlotIndex = index,
            hasArrivedAtFinalSlot = false,
            arrivalTimestamp = 0f,
            initialized = false
        };

        brain.Initialize(this, member);
        squad.Add(member);

        ApplyDirectorSettingsToDrone(member);

        if (targetEliminated || currentPhase == CombatPhase.PostCombatTraveling || currentPhase == CombatPhase.PostCombatStabilizing)
        {
            if (brain != null) brain.currentState = DroneAIState.PostCombat;

            GetFinalFormationSlot(index, out Vector3 slotPos, out Vector3 slotHeading);
            if (waypointObj != null) waypointObj.transform.position = slotPos;
            if (followTarget != null)
            {
                followTarget.EnterFormationMode(
                    slotPos,
                    slotHeading,
                    formationSpeed,
                    formationDecelerationDistance,
                    positionTolerance,
                    rotationTolerance,
                    finalAlignmentStrength,
                    minGroundClearance,
                    groundCheckLayers
                );
            }
            UpdateVictoryWaypoints();
        }

        return member;
    }

    /// <summary>
    /// Applies authoritative DroneDirector configuration to an individual drone's navigation, FCS, hardware, and AI brain.
    /// </summary>
    public void ApplyDirectorSettingsToDrone(DroneSquadMember member)
    {
        if (member == null || member.droneObject == null) return;

        // 1. Navigation & Avoidance
        if (member.followTarget != null)
        {
            member.followTarget.SyncFromDirector(this);
            if (member.followTarget.isFormationMode)
            {
                member.followTarget.UpdateFormationParameters(
                    formationSpeed,
                    formationDecelerationDistance,
                    positionTolerance,
                    rotationTolerance,
                    finalAlignmentStrength,
                    minGroundClearance,
                    groundCheckLayers
                );
            }
        }

        // 2. Flight Control System (FCS) & Cascading PID Controllers
        FlightControlSystem fcs = member.droneObject.GetComponent<FlightControlSystem>();
        if (fcs != null)
        {
            fcs.ApplyFlightSettings(
                maxForwardSpeed, maxSideSpeed, maxTiltAngle,
                maxYawRate, maxPitchRate, maxRollRate,
                maxMotorForce, maxClimbSpeed,
                velocityKp, velocityKi, velocityKd,
                pitchAngleKp, rollAngleKp,
                rateKp, rateKd,
                yawRateKp, altitudeKp
            );
        }

        // 3. Drone Hardware & Rigidbody physics
        DroneHardware dh = member.droneObject.GetComponent<DroneHardware>();
        if (dh != null)
        {
            dh.ApplyHardwareSettings(droneMass, linearDamping, angularDamping);
        }

        // 4. Tactical AI Brain & Personality
        if (member.brain != null)
        {
            member.brain.SyncFromDirector(this);
        }
    }

    /// <summary>
    /// Broadcasts authoritative settings from DroneDirector to all active drones in the squad.
    /// </summary>
    public void ApplyDirectorSettingsToSquad()
    {
        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null && squad[i].droneObject != null)
            {
                ApplyDirectorSettingsToDrone(squad[i]);
            }
        }

        if (targetEliminated || currentPhase == CombatPhase.PostCombatTraveling || currentPhase == CombatPhase.PostCombatStabilizing)
        {
            UpdateVictoryWaypoints();
        }
    }

    private float _lastSettingsSyncTime = 0f;
    private int _lastSettingsHash = 0;

    private int ComputeSettingsHash()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + maxForwardSpeed.GetHashCode();
            hash = hash * 31 + maxSideSpeed.GetHashCode();
            hash = hash * 31 + maxTiltAngle.GetHashCode();
            hash = hash * 31 + maxMotorForce.GetHashCode();
            hash = hash * 31 + droneMass.GetHashCode();
            hash = hash * 31 + linearDamping.GetHashCode();
            hash = hash * 31 + angularDamping.GetHashCode();
            hash = hash * 31 + cruiseSpeed.GetHashCode();
            hash = hash * 31 + approachSpeed.GetHashCode();
            hash = hash * 31 + attackSpeed.GetHashCode();
            hash = hash * 31 + disengageSpeed.GetHashCode();
            hash = hash * 31 + stopDistance.GetHashCode();
            hash = hash * 31 + brakingDistance.GetHashCode();
            hash = hash * 31 + turnSpeed.GetHashCode();
            hash = hash * 31 + maxYawRate.GetHashCode();
            hash = hash * 31 + maxPitchRate.GetHashCode();
            hash = hash * 31 + maxRollRate.GetHashCode();
            hash = hash * 31 + maxBankAngle.GetHashCode();
            hash = hash * 31 + bankAmount.GetHashCode();
            hash = hash * 31 + steeringSmoothing.GetHashCode();
            hash = hash * 31 + hoverHeight.GetHashCode();
            hash = hash * 31 + minAltitude.GetHashCode();
            hash = hash * 31 + maxAltitude.GetHashCode();
            hash = hash * 31 + maxClimbSpeed.GetHashCode();
            hash = hash * 31 + attackDistance.GetHashCode();
            hash = hash * 31 + repositionDistance.GetHashCode();
            hash = hash * 31 + orbitRadius.GetHashCode();
            hash = hash * 31 + attackCooldown.GetHashCode();
            hash = hash * 31 + velocityKp.GetHashCode();
            hash = hash * 31 + velocityKi.GetHashCode();
            hash = hash * 31 + velocityKd.GetHashCode();
            hash = hash * 31 + pitchAngleKp.GetHashCode();
            hash = hash * 31 + rollAngleKp.GetHashCode();
            hash = hash * 31 + rateKp.GetHashCode();
            hash = hash * 31 + rateKd.GetHashCode();
            hash = hash * 31 + yawRateKp.GetHashCode();
            hash = hash * 31 + altitudeKp.GetHashCode();
            hash = hash * 31 + autoSelectFormationByCount.GetHashCode();
            hash = hash * 31 + finalFormationType.GetHashCode();
            hash = hash * 31 + formationSpacing.GetHashCode();
            hash = hash * 31 + vAngle.GetHashCode();
            hash = hash * 31 + vForwardSpacing.GetHashCode();
            hash = hash * 31 + vSideSpacing.GetHashCode();
            hash = hash * 31 + wingHeadingAngle.GetHashCode();
            hash = hash * 31 + formationHeight.GetHashCode();
            hash = hash * 31 + formationRotation.GetHashCode();
            hash = hash * 31 + minGroundClearance.GetHashCode();
            hash = hash * 31 + positionTolerance.GetHashCode();
            hash = hash * 31 + rotationTolerance.GetHashCode();
            hash = hash * 31 + formationSpeed.GetHashCode();
            hash = hash * 31 + formationDecelerationDistance.GetHashCode();
            hash = hash * 31 + finalAlignmentStrength.GetHashCode();
            return hash;
        }
    }

    private void OnValidate()
    {
        if (vAngle > 0f && formationSpacing > 0f)
        {
            float halfAngleRad = (vAngle * 0.5f) * Mathf.Deg2Rad;
            if (vSideSpacing <= 0.01f) vSideSpacing = formationSpacing * Mathf.Sin(halfAngleRad);
            if (vForwardSpacing <= 0.01f) vForwardSpacing = formationSpacing * Mathf.Cos(halfAngleRad);
        }

        if (Application.isPlaying && squad != null && squad.Count > 0)
        {
            ApplyDirectorSettingsToSquad();
        }
    }

    /// <summary>
    /// Detects runtime Inspector changes and automatically syncs them across all squad drones.
    /// </summary>
    public void CheckAndApplySettingChanges()
    {
        if (Time.time - _lastSettingsSyncTime < 0.1f) return;
        _lastSettingsSyncTime = Time.time;

        int currentHash = ComputeSettingsHash();
        if (currentHash != _lastSettingsHash)
        {
            _lastSettingsHash = currentHash;
            ApplyDirectorSettingsToSquad();
        }
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

        EnsureSpanSystemConnection();

        if (droneSpanSystem != null)
        {
            if (droneSpanSystem.SpawnOnStart && squad.Count == 0)
            {
                droneSpanSystem.SpawnDrones();
            }
            lastSpawnedCount = droneSpanSystem.DroneCount;
        }
        else if (squad.Count == 0)
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

            if (squad[i] != null && squad[i].droneObject != null)
            {
                DroneHealth health = squad[i].droneObject.GetComponent<DroneHealth>();
                if (health == null) health = squad[i].droneObject.AddComponent<DroneHealth>();
                health.damageSlowdownMultiplier = 0.70f;
                health.SetLaserTargeted(false);
            }
        }

        ApplyDirectorSettingsToSquad();

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
        CheckAndApplySettingChanges();

        if (target == null && !targetEliminated)
        {
            FindTargetSafely();
        }

        int targetCount = droneSpanSystem != null ? droneSpanSystem.DroneCount : squad.Count;
        if (targetCount != lastSpawnedCount && targetCount > 0)
        {
            lastSpawnedCount = targetCount;
            if (droneSpanSystem != null)
            {
                droneSpanSystem.SyncToTargetCount();
            }
            else
            {
                SpawnDrones();
            }
        }

        bool targetIsActive = (target != null && target.gameObject.activeInHierarchy);

        if (targetIsActive)
        {
            hadActiveTarget = true;
            lastKnownTargetPos = target.position;
            lastKnownTargetForward = target.forward;

            if (enableAutonomousIndividualAI)
            {
                UpdateAutonomousCombat();
            }
            else
            {
                UpdateCombatStateMachine();
                UpdateTacticalWaypoints(lastKnownTargetPos, lastKnownTargetForward);
                ApplyInterDroneSeparation();
                UpdateSquadFlightSpeeds();
            }
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

    /// <summary>
    /// Executes the individual autonomous AI for each drone in the squad:
    /// - Follows target in formation if beyond engagementDistance.
    /// - Within engagementDistance, runs each drone's unique AI state machine, dynamic waypoints, and attack decisions.
    /// </summary>
    private void UpdateAutonomousCombat()
    {
        float distToTarget = Vector3.Distance(GetSquadCentroid(), lastKnownTargetPos);
        float exitThreshold = (currentPhase == CombatPhase.FollowTarget) ? engagementDistance : (engagementDistance + 1.5f);

        if (distToTarget > exitThreshold)
        {
            if (currentPhase != CombatPhase.FollowTarget)
            {
                EnterPhase(CombatPhase.FollowTarget);
            }

            UpdateFollowWaypoints(lastKnownTargetPos, lastKnownTargetForward);
            UpdateSquadFlightSpeeds();

            for (int i = 0; i < squad.Count; i++)
            {
                if (squad[i] != null && squad[i].brain != null)
                {
                    squad[i].brain.currentState = DroneAIState.FollowFormation;
                }
            }
            return;
        }

        // Within combat engagement range: tick each autonomous drone
        currentPhase = CombatPhase.CoordinatedAttack;

        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null || member.droneObject == null || !member.droneObject.activeInHierarchy)
                continue;

            if (member.brain == null)
            {
                member.brain = member.droneObject.GetComponent<DroneBrain>();
                if (member.brain == null) member.brain = member.droneObject.AddComponent<DroneBrain>();
                member.brain.Initialize(this, member);
            }

            member.brain.TickAutonomousAI(target, lastKnownTargetPos, lastKnownTargetForward, Time.deltaTime);
        }

        ApplyInterDroneSeparation();
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

    /// <summary>
    /// Executes an autonomous individual drone strike on the target.
    /// Only this attacking drone enters retreat on its individual cooldown; other drones remain active.
    /// Releases the attack token so another ready drone can strike next.
    /// </summary>
    public void ExecuteDroneStrike(DroneBrain attacker, Transform victim)
    {
        if (victim == null || targetEliminated) return;

        RecordGlobalStrike();

        lastKnownTargetPos = victim.position;
        lastKnownTargetForward = victim.forward;

        CanonHealth canonHealth = victim.GetComponentInParent<CanonHealth>();
        if (canonHealth == null) canonHealth = victim.GetComponentInChildren<CanonHealth>();

        if (canonHealth != null)
        {
            canonHealth.TakeDamage(1);

            if (!canonHealth.IsDestroyed)
            {
                // Cannon survived! Release attack token and order attacker to retreat individually
                if (attacker != null)
                {
                    ReleaseAttackToken(attacker);
                    attacker.OnStrikeDelivered();
                }
                return;
            }
        }

        // Cannon destroyed!
        targetEliminated = true;
        victim.gameObject.SetActive(false);
        SetupVictorySequence();
    }

    /// <summary>
    /// Context menu shortcut to trigger the final victory formation in Play Mode for easy verification.
    /// </summary>
    [ContextMenu("Trigger Final Formation")]
    public void TriggerFinalFormation()
    {
        targetEliminated = true;
        SetupVictorySequence();
    }

    /// <summary>
    /// Returns the number of currently active, non-null squad drones.
    /// </summary>
    public int GetActiveSquadCount()
    {
        int count = 0;
        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null && squad[i].droneObject != null && squad[i].droneObject.activeInHierarchy)
            {
                count++;
            }
        }
        return count > 0 ? count : squad.Count;
    }

    /// <summary>
    /// Returns ParallelFormation for even drone counts and VFormation for odd drone counts.
    /// </summary>
    public FinalFormationType DetermineFormationTypeForCount(int count)
    {
        return (count % 2 == 0) ? FinalFormationType.ParallelFormation : FinalFormationType.VFormation;
    }

    // Selects final formation type based on odd or even active drone count.
    private void SelectFinalFormation()
    {
        if (autoSelectFormationByCount)
        {
            finalFormationType = DetermineFormationTypeForCount(GetActiveSquadCount());
        }
    }

    // Initializes post-combat formation slots, deterministic mappings, and arrival parameters.
    private void SetupVictorySequence()
    {
        SelectFinalFormation();

        _activeAttackers.Clear();
        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null && squad[i].brain != null)
            {
                squad[i].brain.currentState = DroneAIState.PostCombat;
            }
        }

        victoryForward = (lastKnownTargetForward.sqrMagnitude > 0.001f) ? lastKnownTargetForward : Vector3.forward;
        victoryForward.y = 0f;
        victoryForward.Normalize();

        Vector3 formationForward = Quaternion.AngleAxis(formationRotation, Vector3.up) * victoryForward;
        formationForward.y = 0f;
        formationForward.Normalize();

        victoryCenter = lastKnownTargetPos + Vector3.up * formationHeight - formationForward * 1.5f;
        phaseTimer = formationTimeout;

        int totalDrones = Mathf.Max(GetActiveSquadCount(), 1);

        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null) continue;

            // Deterministic slot mapping: drone index i always receives slot i
            member.assignedFormationSlotIndex = i;
            member.hasArrivedAtFinalSlot = false;
            member.arrivalTimestamp = 0f;

            GetFinalFormationSlot(i, totalDrones, out Vector3 slotPos, out Vector3 slotHeading);

            if (member.tacticalWaypoint != null)
            {
                member.tacticalWaypoint.position = slotPos;
            }

            if (member.followTarget != null)
            {
                member.followTarget.EnterFormationMode(
                    slotPos,
                    slotHeading,
                    formationSpeed,
                    formationDecelerationDistance,
                    positionTolerance,
                    rotationTolerance,
                    finalAlignmentStrength,
                    minGroundClearance,
                    groundCheckLayers
                );
            }
        }

        EnterPhase(CombatPhase.PostCombatTraveling);
    }

    /// <summary>
    /// Computes the deterministic world-space slot position and target heading for a squad member.
    /// </summary>
    public void GetFinalFormationSlot(int slotIndex, out Vector3 slotPosition, out Vector3 slotHeading)
    {
        GetFinalFormationSlot(slotIndex, Mathf.Max(GetActiveSquadCount(), 1), out slotPosition, out slotHeading);
    }

    /// <summary>
    /// Computes the deterministic world-space slot position and target heading based on squad count:
    /// - Even count: Symmetrical Parallel line formation with equal spacing.
    /// - Odd count: Centered V-shaped formation with lead drone at apex (slot 0) and symmetrical wings.
    /// </summary>
    public void GetFinalFormationSlot(int slotIndex, int totalDrones, out Vector3 slotPosition, out Vector3 slotHeading)
    {
        FinalFormationType activeType = autoSelectFormationByCount
            ? DetermineFormationTypeForCount(totalDrones)
            : finalFormationType;

        Vector3 baseForward = (victoryForward.sqrMagnitude > 0.001f) ? victoryForward : Vector3.forward;
        baseForward.y = 0f;
        baseForward.Normalize();

        Vector3 formationForward = Quaternion.AngleAxis(formationRotation, Vector3.up) * baseForward;
        formationForward.y = 0f;
        formationForward.Normalize();
        Vector3 formationRight = Vector3.Cross(Vector3.up, formationForward).normalized;

        slotHeading = formationForward;
        Vector3 center = victoryCenter;
        Vector3 slotOffset = Vector3.zero;

        switch (activeType)
        {
            case FinalFormationType.ParallelFormation:
                // Even count: Symmetrical line abreast perpendicular to formationForward
                float halfSpan = (totalDrones - 1) * 0.5f;
                float lateralOffset = (slotIndex - halfSpan) * formationSpacing;
                slotOffset = formationRight * lateralOffset;
                slotHeading = formationForward;
                break;

            case FinalFormationType.VFormation:
            default:
                // Odd count: Symmetrical V formation with slot 0 as apex leader
                if (totalDrones == 1 || slotIndex == 0)
                {
                    slotOffset = Vector3.zero;
                    slotHeading = formationForward;
                }
                else
                {
                    // Alternating symmetrical wings:
                    // Odd indices (1, 3, 5...) form the Left Wing (side = -1)
                    // Even indices (2, 4, 6...) form the Right Wing (side = +1)
                    int wingPair = (slotIndex + 1) / 2;
                    float side = (slotIndex % 2 == 1) ? -1f : 1f;

                    float latStep = vSideSpacing;
                    float fwdStep = vForwardSpacing;
                    if (latStep <= 0.01f || fwdStep <= 0.01f)
                    {
                        float halfAngleRad = (vAngle * 0.5f) * Mathf.Deg2Rad;
                        latStep = formationSpacing * Mathf.Sin(halfAngleRad);
                        fwdStep = formationSpacing * Mathf.Cos(halfAngleRad);
                    }

                    slotOffset = formationRight * (side * wingPair * latStep) - formationForward * (wingPair * fwdStep);

                    // Symmetrical wing heading deflection if wingHeadingAngle != 0
                    if (Mathf.Abs(wingHeadingAngle) > 0.01f)
                    {
                        slotHeading = Quaternion.AngleAxis(side * wingHeadingAngle, Vector3.up) * formationForward;
                    }
                    else
                    {
                        slotHeading = formationForward;
                    }
                }
                break;
        }

        Vector3 rawSlot = center + slotOffset;

        // Strict minimum safe ground clearance: ensures slot can never be below minGroundClearance above terrain
        if (minGroundClearance > 0.01f)
        {
            Vector3 groundRayStart = new Vector3(rawSlot.x, rawSlot.y + 50f, rawSlot.z);
            if (Physics.Raycast(groundRayStart, Vector3.down, out RaycastHit groundHit, 100f, groundCheckLayers, QueryTriggerInteraction.Ignore))
            {
                if (groundHit.collider != null &&
                    groundHit.collider.GetComponentInParent<FlightControlSystem>() == null &&
                    groundHit.collider.GetComponentInParent<DroneNPCFollowTarget>() == null &&
                    (target == null || groundHit.collider.transform.root != target.root))
                {
                    float minSafeY = groundHit.point.y + minGroundClearance;
                    if (rawSlot.y < minSafeY)
                    {
                        rawSlot.y = minSafeY;
                    }
                }
            }
        }

        slotPosition = ClampToNavigableSpace(center, rawSlot);
    }

    /// <summary>
    /// Computes formation slot world position (legacy overload for backward compatibility).
    /// </summary>
    public Vector3 CalculateFinalFormationSlot(int slotIndex, Vector3 center, Vector3 forward)
    {
        GetFinalFormationSlot(slotIndex, out Vector3 slotPosition, out _);
        return slotPosition;
    }

    // Computes the world position of a drone's assigned final formation slot.
    public Vector3 GetAssignedFormationSlot(int droneIndex)
    {
        if (droneIndex < 0 || droneIndex >= squad.Count) return victoryCenter;
        int slotIdx = squad[droneIndex].assignedFormationSlotIndex;
        if (slotIdx < 0) slotIdx = droneIndex;
        GetFinalFormationSlot(slotIdx, out Vector3 slotPos, out _);
        return slotPos;
    }

    // Updates tactical waypoints and follow targets to assigned victory formation positions and headings.
    private void UpdateVictoryWaypoints()
    {
        int totalDrones = Mathf.Max(GetActiveSquadCount(), 1);
        if (autoSelectFormationByCount)
        {
            finalFormationType = DetermineFormationTypeForCount(totalDrones);
        }

        Vector3 baseForward = (victoryForward.sqrMagnitude > 0.001f) ? victoryForward : Vector3.forward;
        baseForward.y = 0f;
        baseForward.Normalize();
        Vector3 formationForward = Quaternion.AngleAxis(formationRotation, Vector3.up) * baseForward;
        formationForward.y = 0f;
        formationForward.Normalize();

        victoryCenter = lastKnownTargetPos + Vector3.up * formationHeight - formationForward * 1.5f;

        // Check ground clearance across the entire formation footprint to preserve planar shape if terrain rises
        if (minGroundClearance > 0.01f)
        {
            float maxRequiredElevation = victoryCenter.y;
            Vector3 formationRight = Vector3.Cross(Vector3.up, formationForward).normalized;

            for (int i = 0; i < squad.Count; i++)
            {
                Vector3 samplePos = victoryCenter;
                if (finalFormationType == FinalFormationType.ParallelFormation)
                {
                    float halfSpan = (totalDrones - 1) * 0.5f;
                    samplePos += formationRight * ((i - halfSpan) * formationSpacing);
                }
                else
                {
                    if (i > 0)
                    {
                        int wingPair = (i + 1) / 2;
                        float side = (i % 2 == 1) ? -1f : 1f;
                        float latStep = (vSideSpacing > 0.01f) ? vSideSpacing : formationSpacing * Mathf.Sin(vAngle * 0.5f * Mathf.Deg2Rad);
                        float fwdStep = (vForwardSpacing > 0.01f) ? vForwardSpacing : formationSpacing * Mathf.Cos(vAngle * 0.5f * Mathf.Deg2Rad);
                        samplePos += formationRight * (side * wingPair * latStep) - formationForward * (wingPair * fwdStep);
                    }
                }

                Vector3 rayStart = new Vector3(samplePos.x, victoryCenter.y + 50f, samplePos.z);
                if (Physics.Raycast(rayStart, Vector3.down, out RaycastHit hit, 100f, groundCheckLayers, QueryTriggerInteraction.Ignore))
                {
                    if (hit.collider != null &&
                        hit.collider.GetComponentInParent<FlightControlSystem>() == null &&
                        hit.collider.GetComponentInParent<DroneNPCFollowTarget>() == null &&
                        (target == null || hit.collider.transform.root != target.root))
                    {
                        float safeY = hit.point.y + minGroundClearance;
                        if (safeY > maxRequiredElevation)
                        {
                            maxRequiredElevation = safeY;
                        }
                    }
                }
            }

            if (maxRequiredElevation > victoryCenter.y)
            {
                victoryCenter.y = maxRequiredElevation;
            }
        }

        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null) continue;

            GetFinalFormationSlot(i, totalDrones, out Vector3 slotPos, out Vector3 slotHeading);

            if (member.tacticalWaypoint != null)
            {
                member.tacticalWaypoint.position = slotPos;
            }

            if (member.followTarget != null)
            {
                member.followTarget.UpdateFormationTarget(slotPos, slotHeading);
                member.followTarget.UpdateFormationParameters(
                    formationSpeed,
                    formationDecelerationDistance,
                    positionTolerance,
                    rotationTolerance,
                    finalAlignmentStrength,
                    minGroundClearance,
                    groundCheckLayers
                );
            }
        }
    }

    // Synchronizes drone formation completion status.
    private void UpdateSynchronizedTravelSpeeds()
    {
        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null || member.droneObject == null) continue;

            if (member.followTarget != null && member.followTarget.isFormationComplete)
            {
                if (!member.hasArrivedAtFinalSlot)
                {
                    member.hasArrivedAtFinalSlot = true;
                    member.arrivalTimestamp = Time.time;
                }
            }
        }
    }

    // Checks if all drones have reached and stabilized in their final formation slots.
    private void CheckSynchronizedArrival()
    {
        if (squad.Count == 0) return;

        bool allComplete = true;

        for (int i = 0; i < squad.Count; i++)
        {
            DroneSquadMember member = squad[i];
            if (member == null || member.droneObject == null) continue;

            bool isComplete = (member.followTarget != null && member.followTarget.isFormationComplete);
            if (isComplete)
            {
                if (!member.hasArrivedAtFinalSlot)
                {
                    member.hasArrivedAtFinalSlot = true;
                    member.arrivalTimestamp = Time.time;
                }
            }
            else
            {
                allComplete = false;
            }
        }

        if (allComplete || phaseTimer <= 0f)
        {
            EnterPhase(CombatPhase.PostCombatStabilizing);
            Debug.Log($"<color=green>[DroneDirector] Final formation settled! Active squad: {squad.Count} in {finalFormationType}</color>");
        }
    }

    // Holds drones in stable hover at their final formation slots while maintaining active formation orientation.
    private void HoldVictoryStabilization()
    {
        UpdateVictoryWaypoints();
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
    public Vector3 ClampToNavigableSpace(Vector3 origin, Vector3 desiredPos)
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
        EnsureSpanSystemConnection();
        if (droneSpanSystem != null)
        {
            droneSpanSystem.SpawnDrones();
            lastSpawnedCount = droneSpanSystem.DroneCount;
            return;
        }

        ClearDrones();

        GameObject template = null;
        DroneHardware existing = FindAnyObjectByType<DroneHardware>(FindObjectsInactive.Include);
        if (existing != null) template = existing.gameObject;

        if (template == null)
        {
            return;
        }

        // If template is an active scene object, hide it so it only serves as an intact master template
        if (template.scene.name != null && template.activeSelf)
        {
            template.SetActive(false);
        }

        float radius = 6f;
        float height = 2.5f;
        int count = 2;
        lastSpawnedCount = count;
        Vector3 centerPos = transform.position;

        for (int i = 0; i < count; i++)
        {
            float angle = (i / (float)count) * Mathf.PI * 2f;
            Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
            Vector3 spawnPos = centerPos + offset + Vector3.up * height;

            GameObject droneObj = Instantiate(template, spawnPos, Quaternion.identity);
            droneObj.SetActive(true);

            ConfigureDrone(droneObj, spawnPos, i);
        }

        WireIgnoredColliders();
    }

    // Configures obstacle avoidance on each drone to ignore squadmates and target.
    public void WireIgnoredColliders()
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
        _activeAttackers.Clear();
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
                if (squad[i].brain != null)
                {
                    ReleaseAttackToken(squad[i].brain);
                }

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

        // Clear target references in AimAssist and CanonMovement
        AimAssist aim = FindAnyObjectByType<AimAssist>();
        if (aim != null)
        {
            aim.OnDroneDestroyedOrDisabled(destroyedDrone);
        }
        CanonMovement canon = FindAnyObjectByType<CanonMovement>();
        if (canon != null)
        {
            canon.OnTargetDroneDestroyed(destroyedDrone);
        }

        // Destroy the dead drone GameObject cleanly so it doesn't linger in hierarchy
        if (Application.isPlaying) Destroy(destroyedDrone);
        else DestroyImmediate(destroyedDrone);

        // Re-index remaining squad members
        for (int i = 0; i < squad.Count; i++)
        {
            if (squad[i] != null)
            {
                squad[i].droneIndex = i;
                squad[i].assignedFormationSlotIndex = i;
            }
        }

        if (targetEliminated || currentPhase == CombatPhase.PostCombatTraveling || currentPhase == CombatPhase.PostCombatStabilizing)
        {
            UpdateVictoryWaypoints();
        }

        // Re-wire obstacle avoidance for surviving drones
        WireIgnoredColliders();

        // Notify DroneSpanSystem to trigger replacement spawning
        if (droneSpanSystem != null)
        {
            droneSpanSystem.OnDroneDestroyed(destroyedDrone);
        }

        if (squad.Count == 0)
        {
            bool hasMoreToSpawn = droneSpanSystem != null && droneSpanSystem.AutoRespawn &&
                                  (droneSpanSystem.MaxTotalSpawns == 0 || droneSpanSystem.TotalDronesSpawned < droneSpanSystem.MaxTotalSpawns);

            if (!hasMoreToSpawn)
            {
                Debug.Log("<color=green>[DroneDirector] ALL DRONES ELIMINATED! Victory!</color>");
            }
            else
            {
                Debug.Log("<color=yellow>[DroneDirector] Squad eliminated, but AutoRespawn is active. Replacement wave incoming!</color>");
            }
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

        Debug.Log($"<color=cyan>[DroneDirector] Drone destroyed! Active squad size: {squad.Count}/{droneCount}</color>");
    }
}

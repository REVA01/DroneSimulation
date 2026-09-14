using System.Collections.Generic;
using UnityEngine;

public class DroneNPCFollowTarget : MonoBehaviour
{
    [Header("Target")]
    public Transform targetBox;

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

    private readonly RaycastHit[] _hitBuffer = new RaycastHit[16];
    private Vector3 _currentMoveDir;
    private Vector3 _committedAvoidDir;
    private float _avoidCommitTimer;
    private bool _isAvoiding;

    // Steers the drone toward its target with obstacle avoidance.
    private void Update()
    {
        if (targetBox == null)
            return;

        Vector3 toTarget = targetBox.position - transform.position;
        float distance = toTarget.magnitude;

        if (distance <= stopDistance)
            return;

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
            return;

        if (_currentMoveDir.sqrMagnitude < 0.001f)
        {
            _currentMoveDir = safeDir;
        }
        else
        {
            float blendSpeed = waypointBehind ? steeringSmoothSpeed * 3f : steeringSmoothSpeed;
            _currentMoveDir = Vector3.Slerp(_currentMoveDir, safeDir, blendSpeed * Time.deltaTime).normalized;
        }

        float moveAmount = Mathf.Min(moveSpeed * Time.deltaTime, distance - stopDistance);
        transform.Translate(_currentMoveDir * moveAmount, Space.World);

        ApplyFlightRotation(_currentMoveDir);
    }

    // Resets active avoidance commitment and timer.
    public void ResetAvoidance()
    {
        _isAvoiding = false;
        _avoidCommitTimer = 0f;
        _committedAvoidDir = Vector3.zero;
    }

    // Rotates the drone toward movement direction with aerodynamic banking.
    private void ApplyFlightRotation(Vector3 direction)
    {
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

    // Determines an unobstructed steering direction using raycasts.
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

    // Checks if an obstacle raycast hits valid environmental geometry.
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

    // Generates alternative avoidance vectors across yaw and pitch angles.
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
}
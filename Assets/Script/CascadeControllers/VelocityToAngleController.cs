using UnityEngine;

/// <summary>
/// Top-level outer cascaded flight controller converting horizontal velocity commands into target tilt angles with active braking.
/// </summary>
public class VelocityToAngleController
{
    private readonly PIDController rollPID;
    private readonly PIDController pitchPID;
    private float maxTilt;
    private readonly float maxTiltRate;
    private readonly float brakingDeadband;
    private readonly float maxBrakingSpeed;
    private Vector2 previousAngles;
    private bool hasPreviousAngles;

    public Vector2 LastTargetAngles => hasPreviousAngles ? previousAngles : Vector2.zero;

    /// <summary>
    /// Initializes velocity-to-attitude PID controllers, maximum tilt bounds, and deceleration parameters.
    /// </summary>
    /// <param name="rollKp">Roll velocity proportional gain.</param>
    /// <param name="rollKi">Roll velocity integral gain.</param>
    /// <param name="rollKd">Roll velocity derivative gain.</param>
    /// <param name="pitchKp">Pitch velocity proportional gain.</param>
    /// <param name="pitchKi">Pitch velocity integral gain.</param>
    /// <param name="pitchKd">Pitch velocity derivative gain.</param>
    /// <param name="integralLimit">Integrator saturation limit.</param>
    /// <param name="outputLimit">Maximum angle command limit.</param>
    /// <param name="maxTilt">Hard maximum tilt angle in degrees.</param>
    /// <param name="maxTiltRate">Maximum rate of tilt angle change in deg/s.</param>
    /// <param name="brakingDeadband">Speed threshold below which active counter-tilt transitions to level hover.</param>
    /// <param name="maxBrakingSpeed">Speed at which maximum counter-tilt deceleration is applied.</param>
    public VelocityToAngleController(
        float rollKp,
        float rollKi,
        float rollKd,
        float pitchKp,
        float pitchKi,
        float pitchKd,
        float integralLimit,
        float outputLimit,
        float maxTilt,
        float maxTiltRate = 450f,
        float brakingDeadband = 0.04f,
        float maxBrakingSpeed = 5.0f)
    {
        rollPID = new PIDController(rollKp, rollKi, rollKd, integralLimit, outputLimit);
        pitchPID = new PIDController(pitchKp, pitchKi, pitchKd, integralLimit, outputLimit);

        this.maxTilt = maxTilt;
        this.maxTiltRate = maxTiltRate;
        this.brakingDeadband = brakingDeadband;
        this.maxBrakingSpeed = Mathf.Max(0.1f, maxBrakingSpeed);
    }

    /// <summary>
    /// Computes target roll and pitch angles required to track demanded horizontal velocity.
    /// Applies active aerodynamic counter-tilt braking when user stick commands are released.
    /// </summary>
    /// <param name="targetVelocity">Demanded lateral and forward velocities (m/s).</param>
    /// <param name="currentVelocity">Current lateral and forward velocities (m/s).</param>
    /// <param name="dt">Physics timestep in seconds.</param>
    /// <returns>Vector2 containing demanded (rollAngle, pitchAngle) in degrees.</returns>
    public Vector2 GetTargetAngles(Vector2 targetVelocity, Vector2 currentVelocity, float dt)
    {
        if (dt <= 0f)
            return hasPreviousAngles ? previousAngles : Vector2.zero;

        float rollAngle;
        bool rollReleased = Mathf.Abs(targetVelocity.x) < 0.01f;

        if (rollReleased)
        {
            rollPID.Reset();
            float vx = currentVelocity.x;

            if (Mathf.Abs(vx) <= brakingDeadband)
            {
                rollAngle = 0f;
            }
            else
            {
                float speedFactor = Mathf.Clamp(vx / maxBrakingSpeed, -1f, 1f);
                rollAngle = -speedFactor * maxTilt;
            }
        }
        else
        {
            float rollError = targetVelocity.x - currentVelocity.x;
            rollAngle = rollPID.Compute(rollError, dt);
        }

        float pitchAngle;
        bool pitchReleased = Mathf.Abs(targetVelocity.y) < 0.01f;

        if (pitchReleased)
        {
            pitchPID.Reset();
            float vy = currentVelocity.y;

            if (Mathf.Abs(vy) <= brakingDeadband)
            {
                pitchAngle = 0f;
            }
            else
            {
                float speedFactor = Mathf.Clamp(vy / maxBrakingSpeed, -1f, 1f);
                pitchAngle = -speedFactor * maxTilt;
            }
        }
        else
        {
            float pitchError = targetVelocity.y - currentVelocity.y;
            pitchAngle = pitchPID.Compute(pitchError, dt);
        }

        rollAngle = Mathf.Clamp(rollAngle, -maxTilt, maxTilt);
        pitchAngle = Mathf.Clamp(pitchAngle, -maxTilt, maxTilt);

        Vector2 targetAngles = new Vector2(rollAngle, pitchAngle);

        if (hasPreviousAngles)
        {
            float maxDelta = maxTiltRate * dt;
            targetAngles = Vector2.MoveTowards(previousAngles, targetAngles, maxDelta);
        }

        previousAngles = targetAngles;
        hasPreviousAngles = true;

        return targetAngles;
    }

    /// <summary>
    /// Updates PID gains, limits, and maximum tilt angle dynamically at runtime.
    /// </summary>
    public void UpdateGains(
        float rollKp, float rollKi, float rollKd,
        float pitchKp, float pitchKi, float pitchKd,
        float integralLimit, float outputLimit,
        float maxTilt)
    {
        rollPID.SetGains(rollKp, rollKi, rollKd, integralLimit, outputLimit);
        pitchPID.SetGains(pitchKp, pitchKi, pitchKd, integralLimit, outputLimit);
        this.maxTilt = maxTilt;
    }

    /// <summary>
    /// Resets PID internal states and stored angle history.
    /// </summary>
    public void Reset()
    {
        rollPID.Reset();
        pitchPID.Reset();
        previousAngles = Vector2.zero;
        hasPreviousAngles = false;
    }
}
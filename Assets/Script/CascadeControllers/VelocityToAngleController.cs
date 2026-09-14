using UnityEngine;

public class VelocityToAngleController
{
    private readonly PIDController rollPID;
    private readonly PIDController pitchPID;
    private readonly float maxTilt;
    private readonly float maxTiltRate;
    private readonly float brakingDeadband;
    private readonly float maxBrakingSpeed;
    private Vector2 previousAngles;
    private bool hasPreviousAngles;

    // Initializes horizontal velocity-to-angle PID controllers and limits.
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

    // Computes target tilt angles from horizontal velocity errors.
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

    // Resets the velocity PID controllers and angle history.
    public void Reset()
    {
        rollPID.Reset();
        pitchPID.Reset();
        previousAngles = Vector2.zero;
        hasPreviousAngles = false;
    }
}
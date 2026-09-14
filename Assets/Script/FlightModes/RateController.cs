using UnityEngine;

public class RateController
{
    private readonly PIDController pitchPID;
    private readonly PIDController rollPID;
    private readonly PIDController yawPID;

    // Initializes angular rate PID controllers for pitch, roll, and yaw.
    public RateController(
        float pitchKp,
        float pitchKi,
        float pitchKd,
        float rollKp,
        float rollKi,
        float rollKd,
        float yawKp,
        float yawKi,
        float yawKd,
        float integralLimit,
        float pitchOutputLimit,
        float rollOutputLimit,
        float yawOutputLimit)
    {
        pitchPID = new PIDController(pitchKp, pitchKi, pitchKd, integralLimit, pitchOutputLimit);
        rollPID = new PIDController(rollKp, rollKi, rollKd, integralLimit, rollOutputLimit);
        yawPID = new PIDController(yawKp, yawKi, yawKd, integralLimit, yawOutputLimit);
    }

    // Computes motor rate corrections from angular rate errors.
    public Vector3 ComputeCorrections(Vector3 targetRate, Vector3 currentRate, float dt)
    {
        return new Vector3(
            pitchPID.Compute(targetRate.x - currentRate.x, dt),
            yawPID.Compute(targetRate.y - currentRate.y, dt),
            rollPID.Compute(targetRate.z - currentRate.z, dt)
        );
    }

    // Resets the rate PID controllers.
    public void Reset()
    {
        pitchPID.Reset();
        rollPID.Reset();
        yawPID.Reset();
    }
}
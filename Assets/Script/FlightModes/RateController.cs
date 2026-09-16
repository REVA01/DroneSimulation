using UnityEngine;

/// <summary>
/// Innermost flight control loop calculating motor rate corrections from 3-axis angular velocity errors.
/// </summary>
public class RateController
{
    private readonly PIDController pitchPID;
    private readonly PIDController rollPID;
    private readonly PIDController yawPID;

    /// <summary>
    /// Initializes rate PID controllers for pitch, roll, and yaw axes.
    /// </summary>
    /// <param name="pitchKp">Pitch rate proportional gain.</param>
    /// <param name="pitchKi">Pitch rate integral gain.</param>
    /// <param name="pitchKd">Pitch rate derivative gain.</param>
    /// <param name="rollKp">Roll rate proportional gain.</param>
    /// <param name="rollKi">Roll rate integral gain.</param>
    /// <param name="rollKd">Roll rate derivative gain.</param>
    /// <param name="yawKp">Yaw rate proportional gain.</param>
    /// <param name="yawKi">Yaw rate integral gain.</param>
    /// <param name="yawKd">Yaw rate derivative gain.</param>
    /// <param name="integralLimit">Integrator saturation limit for all 3 axes.</param>
    /// <param name="pitchOutputLimit">Maximum pitch rate correction output.</param>
    /// <param name="rollOutputLimit">Maximum roll rate correction output.</param>
    /// <param name="yawOutputLimit">Maximum yaw rate correction output.</param>
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

    /// <summary>
    /// Calculates differential motor torque corrections required to align angular velocities with demanded setpoints.
    /// </summary>
    /// <param name="targetRate">Demanded rates vector (x=pitch, y=yaw, z=roll) in deg/s.</param>
    /// <param name="currentRate">Current angular velocities vector (x=pitch, y=yaw, z=roll) in deg/s.</param>
    /// <param name="dt">Physics timestep in seconds.</param>
    /// <returns>Vector3 containing (pitch, yaw, roll) motor torque mixing commands.</returns>
    public Vector3 ComputeCorrections(Vector3 targetRate, Vector3 currentRate, float dt)
    {
        return new Vector3(
            pitchPID.Compute(targetRate.x - currentRate.x, dt),
            yawPID.Compute(targetRate.y - currentRate.y, dt),
            rollPID.Compute(targetRate.z - currentRate.z, dt)
        );
    }

    /// <summary>
    /// Clears rate PID integrator memory across all three axes.
    /// </summary>
    public void Reset()
    {
        pitchPID.Reset();
        rollPID.Reset();
        yawPID.Reset();
    }
}
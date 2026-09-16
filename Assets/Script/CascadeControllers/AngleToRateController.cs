using UnityEngine;

/// <summary>
/// Intermediate cascaded flight controller converting target pitch/roll angles into demanded angular rates.
/// </summary>
public class AngleToRateController
{
    private readonly PIDController pitchPID;
    private readonly PIDController rollPID;
    private readonly float levelRecoveryMultiplier = 2.0f;

    /// <summary>
    /// Initializes pitch and roll angle controllers with specific PID parameters.
    /// </summary>
    /// <param name="pitchKp">Pitch proportional gain.</param>
    /// <param name="pitchKi">Pitch integral gain.</param>
    /// <param name="pitchKd">Pitch derivative gain.</param>
    /// <param name="rollKp">Roll proportional gain.</param>
    /// <param name="rollKi">Roll integral gain.</param>
    /// <param name="rollKd">Roll derivative gain.</param>
    /// <param name="integralLimit">Integrator saturation limit.</param>
    /// <param name="outputLimit">Maximum angular rate demand output (deg/s).</param>
    /// <param name="maxTilt">Maximum allowable tilt angle in degrees.</param>
    public AngleToRateController(
        float pitchKp,
        float pitchKi,
        float pitchKd,
        float rollKp,
        float rollKi,
        float rollKd,
        float integralLimit,
        float outputLimit,
        float maxTilt = 30f)
    {
        pitchPID = new PIDController(pitchKp, pitchKi, pitchKd, integralLimit, outputLimit);
        rollPID = new PIDController(rollKp, rollKi, rollKd, integralLimit, outputLimit);
    }

    /// <summary>
    /// Computes commanded angular rates (deg/s) based on target attitude and current orientation.
    /// </summary>
    /// <param name="targetAngles">Target roll (x) and pitch (y) angles in degrees.</param>
    /// <param name="currentAngles">Current normalized Euler angles (pitch=x, yaw=y, roll=z).</param>
    /// <param name="dt">Physics timestep in seconds.</param>
    /// <returns>Vector2 containing (pitchRate, rollRate) commands in deg/s.</returns>
    public Vector2 GetTargetRates(Vector2 targetAngles, Vector3 currentAngles, float dt)
    {
        float pitchError = targetAngles.y - currentAngles.x;
        float pitchRate = pitchPID.Compute(pitchError, dt);

        float rollError = -targetAngles.x - currentAngles.z;
        float rollRate = rollPID.Compute(rollError, dt);

        // Boost recovery damping when near level attitude to reduce oscillation
        if (Mathf.Abs(targetAngles.y) < 5f)
        {
            pitchRate *= levelRecoveryMultiplier;
        }

        if (Mathf.Abs(targetAngles.x) < 5f)
        {
            rollRate *= levelRecoveryMultiplier;
        }

        pitchRate = Mathf.Clamp(pitchRate, -100f, 100f);
        rollRate = Mathf.Clamp(rollRate, -100f, 100f);

        return new Vector2(pitchRate, rollRate);
    }

    /// <summary>
    /// Resets the pitch and roll angle PID state history.
    /// </summary>
    public void Reset()
    {
        pitchPID.Reset();
        rollPID.Reset();
    }
}
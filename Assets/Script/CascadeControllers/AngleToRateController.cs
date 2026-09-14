using UnityEngine;

public class AngleToRateController
{
    private readonly PIDController pitchPID;
    private readonly PIDController rollPID;
    private readonly float levelRecoveryMultiplier = 2.0f;

    // Initializes pitch and roll angle-to-rate PID controllers.
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

    // Computes target angular rates from angle errors.
    public Vector2 GetTargetRates(Vector2 targetAngles, Vector3 currentAngles, float dt)
    {
        float pitchError = targetAngles.y - currentAngles.x;
        float pitchRate = pitchPID.Compute(pitchError, dt);

        float rollError = -targetAngles.x - currentAngles.z;
        float rollRate = rollPID.Compute(rollError, dt);

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

    // Resets the angle PID controllers.
    public void Reset()
    {
        pitchPID.Reset();
        rollPID.Reset();
    }
}
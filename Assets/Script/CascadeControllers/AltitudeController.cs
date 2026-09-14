using UnityEngine;

public class AltitudeController
{
    private readonly PIDController altitudePID;
    private readonly float maxClimbSpeed;
    private readonly float hoverForce;
    private readonly float maxTotalThrust;

    // Initializes PID parameters, climb speed, and thrust limits.
    public AltitudeController(
        float kp,
        float ki,
        float kd,
        float integralLimit,
        float outputLimit,
        float maxClimbSpeed,
        float hoverForce,
        float maxTotalThrust)
    {
        altitudePID = new PIDController(kp, ki, kd, integralLimit, outputLimit);
        this.maxClimbSpeed = maxClimbSpeed;
        this.hoverForce = hoverForce;
        this.maxTotalThrust = Mathf.Max(maxTotalThrust, 0.0001f);
    }

    // Computes normalized throttle output from vertical velocity error and tilt.
    public float GetThrottle(float inputThrottle, float currentVelocityY, float upDot, float dt)
    {
        float targetVelocityY = inputThrottle * maxClimbSpeed;
        float error = targetVelocityY - currentVelocityY;
        float correctionForce = altitudePID.Compute(error, dt);

        float safeUpDot = Mathf.Clamp(upDot, 0.35f, 1f);
        float compensatedHoverForce = hoverForce / safeUpDot;
        float requestedTotalForce = compensatedHoverForce + correctionForce;

        return Mathf.Clamp01(requestedTotalForce / maxTotalThrust);
    }

    // Resets the altitude PID controller state.
    public void Reset()
    {
        altitudePID.Reset();
    }
}

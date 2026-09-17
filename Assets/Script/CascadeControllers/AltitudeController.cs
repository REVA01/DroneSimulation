using UnityEngine;

/// <summary>
/// Controls drone vertical climb/descent rate using a dedicated altitude PID controller and tilt-compensated hover force.
/// </summary>
public class AltitudeController
{
    private readonly PIDController altitudePID;
    private float maxClimbSpeed;
    private float hoverForce;
    private float maxTotalThrust;

    /// <summary>
    /// Initializes altitude controller gains, velocity limits, and thrust capabilities.
    /// </summary>
    /// <param name="kp">Proportional gain coefficient.</param>
    /// <param name="ki">Integral gain coefficient.</param>
    /// <param name="kd">Derivative gain coefficient.</param>
    /// <param name="integralLimit">Integrator saturation limit.</param>
    /// <param name="outputLimit">Maximum correction force output.</param>
    /// <param name="maxClimbSpeed">Maximum target climb/descent velocity in m/s.</param>
    /// <param name="hoverForce">Nominal force in Newtons required to hold hover.</param>
    /// <param name="maxTotalThrust">Maximum combined thrust capacity of all motors.</param>
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
        this.maxClimbSpeed = Mathf.Max(0.1f, maxClimbSpeed);
        this.hoverForce = hoverForce;
        this.maxTotalThrust = Mathf.Max(maxTotalThrust, 0.0001f);
    }

    /// <summary>
    /// Calculates normalized collective throttle [0, 1] required to reach target vertical velocity with attitude tilt compensation.
    /// </summary>
    /// <param name="inputThrottle">Normalized climb input [-1, 1].</param>
    /// <param name="currentVelocityY">Current vertical speed in world space (m/s).</param>
    /// <param name="upDot">Dot product between drone local up and world up (cos of tilt angle).</param>
    /// <param name="dt">Physics timestep in seconds.</param>
    /// <returns>Normalized throttle value between 0 and 1.</returns>
    public float GetThrottle(float inputThrottle, float currentVelocityY, float upDot, float dt)
    {
        if (float.IsNaN(inputThrottle) || float.IsInfinity(inputThrottle)) inputThrottle = 0f;
        if (float.IsNaN(currentVelocityY) || float.IsInfinity(currentVelocityY)) currentVelocityY = 0f;
        if (float.IsNaN(upDot) || float.IsInfinity(upDot)) upDot = 1f;
        if (dt <= 0f || float.IsNaN(dt)) dt = 0.02f;

        float targetVelocityY = inputThrottle * maxClimbSpeed;
        float error = targetVelocityY - currentVelocityY;
        float correctionForce = altitudePID.Compute(error, dt);

        // Compensate hover force as the drone tilts to prevent altitude loss during translation
        float safeUpDot = Mathf.Clamp(upDot, 0.35f, 1f);
        float compensatedHoverForce = hoverForce / safeUpDot;
        float requestedTotalForce = compensatedHoverForce + correctionForce;

        return Mathf.Clamp01(requestedTotalForce / maxTotalThrust);
    }

    /// <summary>
    /// Updates altitude PID gains, climb speed, and thrust limits dynamically at runtime.
    /// </summary>
    public void UpdateGains(
        float kp, float ki, float kd,
        float integralLimit, float outputLimit,
        float maxClimbSpeed, float hoverForce, float maxTotalThrust)
    {
        altitudePID.SetGains(kp, ki, kd, integralLimit, outputLimit);
        this.maxClimbSpeed = Mathf.Max(0.1f, maxClimbSpeed);
        this.hoverForce = hoverForce;
        this.maxTotalThrust = Mathf.Max(maxTotalThrust, 0.0001f);
    }

    /// <summary>
    /// Resets the internal altitude PID integrator and error memory.
    /// </summary>
    public void Reset()
    {
        altitudePID.Reset();
    }
}

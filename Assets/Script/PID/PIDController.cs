using UnityEngine;

/// <summary>
/// General-purpose discrete PID controller with derivative low-pass filtering and anti-windup clamping.
/// </summary>
public class PIDController
{
    private float kp;
    private float ki;
    private float kd;
    private float integralLimit;
    private float outputLimit;

    private const float DerivativeFilterAlpha = 0.15f;
    private const float MaxDt = 0.05f;

    private float integral;
    private float previousError;
    private float filteredDerivative;
    private bool hasPreviousError;

    /// <summary>
    /// Constructs a PIDController instance with specific gains and saturation bounds.
    /// </summary>
    /// <param name="kp">Proportional gain coefficient.</param>
    /// <param name="ki">Integral gain coefficient.</param>
    /// <param name="kd">Derivative gain coefficient.</param>
    /// <param name="integralLimit">Maximum absolute accumulation limit for the integrator.</param>
    /// <param name="outputLimit">Maximum absolute command output saturation limit.</param>
    public PIDController(float kp, float ki, float kd, float integralLimit, float outputLimit)
    {
        this.kp = kp;
        this.ki = ki;
        this.kd = kd;
        this.integralLimit = integralLimit;
        this.outputLimit = outputLimit;
    }

    /// <summary>
    /// Updates PID gains and limits dynamically at runtime for live tuning.
    /// </summary>
    public void SetGains(float kp, float ki, float kd, float integralLimit = -1f, float outputLimit = -1f)
    {
        this.kp = kp;
        this.ki = ki;
        this.kd = kd;
        if (integralLimit >= 0f) this.integralLimit = integralLimit;
        if (outputLimit >= 0f) this.outputLimit = outputLimit;
    }

    /// <summary>
    /// Computes the control output based on current tracking error and timestep.
    /// </summary>
    /// <param name="error">Difference between setpoint target and current state.</param>
    /// <param name="dt">Time step delta in seconds.</param>
    /// <returns>Clamped control actuation command.</returns>
    public float Compute(float error, float dt)
    {
        if (float.IsNaN(error) || float.IsInfinity(error))
        {
            return 0f;
        }

        if (float.IsNaN(dt) || dt <= 0f)
        {
            return Mathf.Clamp(kp * error, -outputLimit, outputLimit);
        }

        dt = Mathf.Min(dt, MaxDt);

        float pTerm = kp * error;

        // Integral accumulation with saturation clamp
        integral += error * dt;
        integral = Mathf.Clamp(integral, -integralLimit, integralLimit);
        float iTerm = ki * integral;

        // Filtered derivative calculation
        float rawDerivative = hasPreviousError ? (error - previousError) / dt : 0f;
        filteredDerivative = Mathf.Lerp(filteredDerivative, rawDerivative, DerivativeFilterAlpha);
        float dTerm = kd * filteredDerivative;

        previousError = error;
        hasPreviousError = true;

        float output = pTerm + iTerm + dTerm;
        float clampedOutput = Mathf.Clamp(output, -outputLimit, outputLimit);

        // Anti-windup clamping: only adjust integrator if integral gain is active
        if (!Mathf.Approximately(output, clampedOutput) && Mathf.Abs(ki) > 0.0001f)
        {
            bool sameSign = (output > 0f && error > 0f) || (output < 0f && error < 0f);
            if (sameSign)
            {
                integral -= error * dt;
            }
        }

        return clampedOutput;
    }

    /// <summary>
    /// Clears internal integrator accumulation and derivative state history.
    /// </summary>
    public void Reset()
    {
        integral = 0f;
        previousError = 0f;
        filteredDerivative = 0f;
        hasPreviousError = false;
    }
}
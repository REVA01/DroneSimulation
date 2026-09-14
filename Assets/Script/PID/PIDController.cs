using UnityEngine;

public class PIDController
{
    private readonly float kp;
    private readonly float ki;
    private readonly float kd;
    private readonly float integralLimit;
    private readonly float outputLimit;

    private const float DerivativeFilterAlpha = 0.15f;
    private const float MaxDt = 0.05f;

    private float integral;
    private float previousError;
    private float filteredDerivative;
    private bool hasPreviousError;

    // Initializes PID gains and saturation limits.
    public PIDController(float kp, float ki, float kd, float integralLimit, float outputLimit)
    {
        this.kp = kp;
        this.ki = ki;
        this.kd = kd;
        this.integralLimit = integralLimit;
        this.outputLimit = outputLimit;
    }

    // Computes the filtered PID output for a given error and time delta.
    public float Compute(float error, float dt)
    {
        if (dt <= 0f)
        {
            return Mathf.Clamp(kp * error, -outputLimit, outputLimit);
        }

        dt = Mathf.Min(dt, MaxDt);

        float pTerm = kp * error;

        integral += error * dt;
        integral = Mathf.Clamp(integral, -integralLimit, integralLimit);
        float iTerm = ki * integral;

        float rawDerivative = hasPreviousError ? (error - previousError) / dt : 0f;
        filteredDerivative = Mathf.Lerp(filteredDerivative, rawDerivative, DerivativeFilterAlpha);
        float dTerm = kd * filteredDerivative;

        previousError = error;
        hasPreviousError = true;

        float output = pTerm + iTerm + dTerm;
        float clampedOutput = Mathf.Clamp(output, -outputLimit, outputLimit);

        if (!Mathf.Approximately(output, clampedOutput))
        {
            bool sameSign = (output > 0f && error > 0f) || (output < 0f && error < 0f);
            if (sameSign)
            {
                integral -= error * dt;
            }
        }

        return clampedOutput;
    }

    // Resets the integral and derivative state.
    public void Reset()
    {
        integral = 0f;
        previousError = 0f;
        filteredDerivative = 0f;
        hasPreviousError = false;
    }
}
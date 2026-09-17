using UnityEngine;

/// <summary>
/// Captures and exposes normalized flight control axes (Pitch, Roll, Yaw, Throttle).
/// Supports manual keyboard polling as well as external AI programmatic overrides.
/// </summary>
public class DroneInputs : MonoBehaviour
{
    [field: SerializeField]
    public float Pitch { get; private set; }

    [field: SerializeField]
    public float Roll { get; private set; }

    [field: SerializeField]
    public float Yaw { get; private set; }

    [field: SerializeField]
    public float Throttle { get; private set; }

    public Vector2 Cyclic => new Vector2(Roll, Pitch);

    [Header("Throttle Configuration")]
    [Tooltip("Normal climb rate multiplier.")]
    public float normalThrottle = 1f;

    [Tooltip("Boosted climb rate multiplier when holding Shift.")]
    public float boostThrottle = 1f;

    [Header("AI Override")]
    [Tooltip("If true, manual keyboard input is ignored and SetAIInputs must be used.")]
    public bool isAIControlled = false;

    [Header("Axis Inversion")]
    public bool invertPitch = false;
    public bool invertRoll = false;
    public bool invertYaw = false;

    /// <summary>
    /// Reads manual keyboard inputs each frame when not under AI control.
    /// </summary>
    private void Update()
    {
        if (isAIControlled)
            return;

        try
        {
            Pitch = 0f;
            if (Input.GetKey(KeyCode.W)) Pitch += 1f;
            if (Input.GetKey(KeyCode.S)) Pitch -= 1f;

            Roll = 0f;
            if (Input.GetKey(KeyCode.D)) Roll += 1f;
            if (Input.GetKey(KeyCode.A)) Roll -= 1f;

            Yaw = 0f;
            if (Input.GetKey(KeyCode.E)) Yaw += 1f;
            if (Input.GetKey(KeyCode.Q)) Yaw -= 1f;

            Throttle = 0f;
            if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
            {
                Throttle = boostThrottle;
            }
            else if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
            {
                Throttle = -normalThrottle;
            }

            if (invertPitch) Pitch = -Pitch;
            if (invertRoll) Roll = -Roll;
            if (invertYaw) Yaw = -Yaw;

            Pitch = Mathf.Clamp(Pitch, -1f, 1f);
            Roll = Mathf.Clamp(Roll, -1f, 1f);
            Yaw = Mathf.Clamp(Yaw, -1f, 1f);
            Throttle = Mathf.Clamp(Throttle, -1f, 1f);
        }
        catch (System.Exception)
        {
            // Safeguard against missing input device exceptions in headless/test environments
        }
    }

    /// <summary>
    /// Programmatically sets normalized control inputs from an autonomous controller or AI.
    /// </summary>
    /// <param name="pitch">Normalized pitch axis [-1, 1].</param>
    /// <param name="roll">Normalized roll axis [-1, 1].</param>
    /// <param name="yaw">Normalized yaw axis [-1, 1].</param>
    /// <param name="throttle">Normalized throttle axis [-1, 1].</param>
    public void SetAIInputs(float pitch, float roll, float yaw, float throttle)
    {
        isAIControlled = true;
        Pitch = Mathf.Clamp(pitch, -1f, 1f);
        Roll = Mathf.Clamp(roll, -1f, 1f);
        Yaw = Mathf.Clamp(yaw, -1f, 1f);
        Throttle = Mathf.Clamp(throttle, -1f, 1f);
    }

    /// <summary>
    /// Resets all input axes back to neutral zero.
    /// </summary>
    public void ResetInputs()
    {
        Pitch = 0f;
        Roll = 0f;
        Yaw = 0f;
        Throttle = 0f;
    }
}
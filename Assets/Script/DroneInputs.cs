using UnityEngine;

public class DroneInputs : MonoBehaviour
{
    public float Pitch { get; private set; }
    public float Roll { get; private set; }
    public float Yaw { get; private set; }
    public float Throttle { get; private set; }

    public Vector2 Cyclic => new Vector2(Roll, Pitch);

    public float normalThrottle = 1f;
    public float boostThrottle = 1f;

    [Header("AI Override")]
    public bool isAIControlled = false;

    [Header("Sign Conventions")]
    public bool invertPitch = false;
    public bool invertRoll = false;
    public bool invertYaw = false;

    // Reads keyboard inputs when not under AI control.
    private void Update()
    {
        if (isAIControlled)
            return;

        Pitch = 0f;
        if (Input.GetKey(KeyCode.W)) Pitch = 1f;
        if (Input.GetKey(KeyCode.S)) Pitch = -1f;

        Roll = 0f;
        if (Input.GetKey(KeyCode.A)) Roll = -1f;
        if (Input.GetKey(KeyCode.D)) Roll = 1f;

        Yaw = 0f;
        if (Input.GetKey(KeyCode.Q)) Yaw = -1f;
        if (Input.GetKey(KeyCode.E)) Yaw = 1f;

        Throttle = 0f;
        if (Input.GetKey(KeyCode.LeftShift) || Input.GetKey(KeyCode.RightShift))
        {
            Throttle = boostThrottle;
        }
        if (Input.GetKey(KeyCode.LeftControl) || Input.GetKey(KeyCode.RightControl))
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

    // Sets control inputs directly from an external AI controller.
    public void SetAIInputs(float pitch, float roll, float yaw, float throttle)
    {
        Pitch = Mathf.Clamp(pitch, -1f, 1f);
        Roll = Mathf.Clamp(roll, -1f, 1f);
        Yaw = Mathf.Clamp(yaw, -1f, 1f);
        Throttle = Mathf.Clamp(throttle, -1f, 1f);
    }
}
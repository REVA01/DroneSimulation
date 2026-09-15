using UnityEngine;

public class CanonMovement : MonoBehaviour
{
    [Header("Cannon References")]
    [Tooltip("Parent base object (CanonBase) that rotates 360 degrees.")]
    public Transform canonBase;
    [Tooltip("Gun barrel that tilts up and down.")]
    public Transform canonRotate;

    [Header("Movement Settings")]
    public float baseRotationSpeed = 60f;   // A / D rotation speed (360°)
    public float barrelRotationSpeed = 40f; // W / S tilt speed
    public float minVerticalAngle = -60f;   // Max pitch UP
    public float maxVerticalAngle = 10f;    // Max pitch DOWN

    private float currentPitch = 0f;
    private Quaternion initialGunRotation;

    private void Start()
    {
        if (canonRotate != null)
        {
            initialGunRotation = canonRotate.localRotation;
        }

        // Auto-assign parent base if not set in Inspector
        if (canonBase == null)
        {
            if (canonRotate != null && canonRotate.parent != null)
            {
                canonBase = canonRotate.parent;
            }
            else if (transform.parent != null)
            {
                canonBase = transform.parent;
            }
        }
    }

    private void Update()
    {
        HandleMovement();
    }

    private void HandleMovement()
    {
        // A / D: Rotate parent base 360 degrees freely
        float horizontalInput = 0f;
        if (Input.GetKey(KeyCode.D) || Input.GetKey(KeyCode.RightArrow)) horizontalInput += 1f;
        if (Input.GetKey(KeyCode.A) || Input.GetKey(KeyCode.LeftArrow)) horizontalInput -= 1f;

        if (canonBase != null && Mathf.Abs(horizontalInput) > 0.01f)
        {
            canonBase.Rotate(Vector3.up, horizontalInput * baseRotationSpeed * Time.deltaTime, Space.World);
        }

        // W / S: Tilt gun barrel up and down
        float verticalInput = 0f;
        if (Input.GetKey(KeyCode.W) || Input.GetKey(KeyCode.UpArrow)) verticalInput += 1f;
        if (Input.GetKey(KeyCode.S) || Input.GetKey(KeyCode.DownArrow)) verticalInput -= 1f;

        if (canonRotate != null)
        {
            currentPitch -= verticalInput * barrelRotationSpeed * Time.deltaTime;
            currentPitch = Mathf.Clamp(currentPitch, minVerticalAngle, maxVerticalAngle);

            canonRotate.localRotation = initialGunRotation * Quaternion.Euler(currentPitch, 0f, 0f);
        }
    }
}

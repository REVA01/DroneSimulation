using UnityEngine;

[RequireComponent(typeof(Rigidbody))]
public class DroneHardware : MonoBehaviour
{
    [Header("Drone Physics")]
    public float mass = 1f;
    public float torqueFactor = 0.02f;

    [Range(0f, 1f)]
    public float linearDamping = 0.2f;

    [Header("Motor References")]
    public Transform frontLeft;
    public Transform frontRight;
    public Transform backLeft;
    public Transform backRight;

    [Header("Runtime Motor Forces")]
    public float FLForce;
    public float FRForce;
    public float BLForce;
    public float BRForce;

    private Rigidbody rb;

    // Initializes Rigidbody mass and linear damping.
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();
        rb.mass = mass;
        rb.linearDamping = linearDamping;
    }

    // Applies motor thrust forces and yaw torque to the Rigidbody.
    public void ApplyMotorForces(float fl, float fr, float bl, float br)
    {
        FLForce = fl;
        FRForce = fr;
        BLForce = bl;
        BRForce = br;

        if (frontLeft == null || frontRight == null || backLeft == null || backRight == null)
        {
            return;
        }

        Vector3 forceDirection = transform.up;

        rb.AddForceAtPosition(forceDirection * FLForce, frontLeft.position, ForceMode.Force);
        rb.AddForceAtPosition(forceDirection * FRForce, frontRight.position, ForceMode.Force);
        rb.AddForceAtPosition(forceDirection * BLForce, backLeft.position, ForceMode.Force);
        rb.AddForceAtPosition(forceDirection * BRForce, backRight.position, ForceMode.Force);

        float yawTorque = (-FLForce * torqueFactor)
            + (FRForce * torqueFactor)
            + (BLForce * torqueFactor)
            - (BRForce * torqueFactor);

        rb.AddRelativeTorque(Vector3.up * yawTorque, ForceMode.Force);
    }
}

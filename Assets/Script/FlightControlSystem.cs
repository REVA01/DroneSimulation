using UnityEngine;
using Drone.Runtime.FlightModes;

[RequireComponent(typeof(Rigidbody))]
[RequireComponent(typeof(DroneHardware))]
[RequireComponent(typeof(DroneInputs))]
public class FlightControlSystem : MonoBehaviour
{
    [Header("References")]
    public DroneHardware droneHardware;
    public DroneInputs droneInputs;

    [Header("Flight Limits")]
    [Range(5f, 45f)]
    public float maxTiltAngle = 30f;
    public float maxForwardSpeed = 30f;
    public float maxSideSpeed = 25f;
    public float maxYawRate = 90f;
    public float maxPitchRate = 100f;
    public float maxRollRate = 100f;

    [Header("Motor")]
    public float maxMotorForce = 10f;

    [Header("Velocity PID")]
    public float velocityKp = 1f;
    public float velocityKi = 0f;
    public float velocityKd = 0f;
    public float velocityIntegralLimit = 10f;
    public float velocityOutputLimit = 30f;

    [Header("Pitch Angle PID")]
    public float pitchAngleKp = 3f;
    public float pitchAngleKi = 0f;
    public float pitchAngleKd = 0f;
    public float pitchAngleIntegralLimit = 10f;
    public float pitchAngleOutputLimit = 60f;

    [Header("Roll Angle PID")]
    public float rollAngleKp = 3f;
    public float rollAngleKi = 0f;
    public float rollAngleKd = 0.5f;
    public float rollAngleIntegralLimit = 10f;
    public float rollAngleOutputLimit = 60f;

    [Header("Pitch Rate PID")]
    public float pitchRateKp = 0.05f;
    public float pitchRateKi = 0f;
    public float pitchRateKd = 0.0001f;
    public float pitchRateIntegralLimit = 1f;
    public float pitchRateOutputLimit = 5f;

    [Header("Roll Rate PID")]
    public float rollRateKp = 0.05f;
    public float rollRateKi = 0f;
    public float rollRateKd = 0.0001f;
    public float rollRateIntegralLimit = 1f;
    public float rollRateOutputLimit = 5f;

    [Header("Yaw Rate PID")]
    public float yawRateKp = 0.05f;
    public float yawRateKi = 0f;
    public float yawRateKd = 0.0001f;
    public float yawRateIntegralLimit = 1f;
    public float yawRateOutputLimit = 3f;

    [Header("Altitude PID")]
    public float altitudeKp = 2f;
    public float altitudeKi = 0f;
    public float altitudeKd = 0f;
    public float altitudeIntegralLimit = 10f;
    public float altitudeOutputLimit = 0.5f;

    [Header("Altitude")]
    public float maxClimbSpeed = 8f;

    [Header("Runtime Debug")]
    public float hoverForce;
    public float hoverThrottle;
    public float targetForwardVelocity;
    public float targetSideVelocity;
    public float currentForwardVelocity;
    public float currentSideVelocity;
    public float targetPitchAngle;
    public float targetRollAngle;
    public float targetPitchRate;
    public float targetRollRate;
    public float pitchCorrection;
    public float rollCorrection;
    public float yawCorrection;
    public float finalThrottle;

    private Rigidbody rb;
    private VelocityToAngleController velocityController;
    private AngleToRateController angleController;
    private RateController rateController;
    private AltitudeController altitudeController;
    private VelocityMode velocityMode;

    /// <summary>
    /// Initializes hardware references, physics mass, hover calibration, and cascading controllers.
    /// </summary>
    private void Awake()
    {
        rb = GetComponent<Rigidbody>();

        if (droneHardware == null)
            droneHardware = GetComponent<DroneHardware>();

        if (droneInputs == null)
            droneInputs = GetComponent<DroneInputs>();

        if (droneHardware != null && rb != null)
        {
            rb.mass = droneHardware.mass;
            rb.isKinematic = false;
            rb.useGravity = true;
        }

        RecalculateHoverParameters();

        velocityController = new VelocityToAngleController(
            velocityKp, velocityKi, velocityKd,
            velocityKp, velocityKi, velocityKd,
            velocityIntegralLimit, velocityOutputLimit,
            maxTiltAngle
        );

        angleController = new AngleToRateController(
            pitchAngleKp, pitchAngleKi, pitchAngleKd,
            rollAngleKp, rollAngleKi, rollAngleKd,
            Mathf.Max(pitchAngleIntegralLimit, rollAngleIntegralLimit),
            Mathf.Max(pitchAngleOutputLimit, rollAngleOutputLimit),
            maxTiltAngle
        );

        rateController = new RateController(
            pitchRateKp, pitchRateKi, pitchRateKd,
            rollRateKp, rollRateKi, rollRateKd,
            yawRateKp, yawRateKi, yawRateKd,
            Mathf.Max(pitchRateIntegralLimit, Mathf.Max(rollRateIntegralLimit, yawRateIntegralLimit)),
            pitchRateOutputLimit, rollRateOutputLimit, yawRateOutputLimit
        );

        altitudeController = new AltitudeController(
            altitudeKp, altitudeKi, altitudeKd,
            altitudeIntegralLimit, altitudeOutputLimit,
            maxClimbSpeed, hoverForce, maxMotorForce * 4f
        );

        velocityMode = new VelocityMode(
            velocityController, angleController, altitudeController,
            maxForwardSpeed, maxSideSpeed, maxYawRate
        );
    }

    /// <summary>
    /// Applies authoritative flight parameters and PID gains centrally from DroneDirector.
    /// </summary>
    public void ApplyFlightSettings(
        float maxForwardSpeed, float maxSideSpeed, float maxTiltAngle,
        float maxYawRate, float maxPitchRate, float maxRollRate,
        float maxMotorForce, float maxClimbSpeed,
        float velocityKp, float velocityKi, float velocityKd,
        float pitchAngleKp, float rollAngleKp,
        float rateKp, float rateKd,
        float yawRateKp, float altitudeKp)
    {
        this.maxForwardSpeed = maxForwardSpeed;
        this.maxSideSpeed = maxSideSpeed;
        this.maxTiltAngle = maxTiltAngle;
        this.maxYawRate = maxYawRate;
        this.maxPitchRate = maxPitchRate;
        this.maxRollRate = maxRollRate;
        this.maxMotorForce = maxMotorForce;
        this.maxClimbSpeed = maxClimbSpeed;

        this.velocityKp = velocityKp;
        this.velocityKi = velocityKi;
        this.velocityKd = velocityKd;

        this.pitchAngleKp = pitchAngleKp;
        this.rollAngleKp = rollAngleKp;

        this.pitchRateKp = rateKp;
        this.rollRateKp = rateKp;
        this.pitchRateKd = rateKd;
        this.rollRateKd = rateKd;

        this.yawRateKp = yawRateKp;
        this.altitudeKp = altitudeKp;

        RecalculateHoverParameters();
        SyncControllerParameters();
    }

    /// <summary>
    /// Synchronizes public Inspector / runtime GUI tuning parameters to active controller instances.
    /// </summary>
    public void SyncControllerParameters()
    {
        if (velocityController != null)
        {
            velocityController.UpdateGains(
                velocityKp, velocityKi, velocityKd,
                velocityKp, velocityKi, velocityKd,
                velocityIntegralLimit, velocityOutputLimit,
                maxTiltAngle
            );
        }

        if (angleController != null)
        {
            angleController.UpdateGains(
                pitchAngleKp, pitchAngleKi, pitchAngleKd,
                rollAngleKp, rollAngleKi, rollAngleKd,
                Mathf.Max(pitchAngleIntegralLimit, rollAngleIntegralLimit),
                Mathf.Max(pitchAngleOutputLimit, rollAngleOutputLimit)
            );
        }

        if (rateController != null)
        {
            rateController.UpdateGains(
                pitchRateKp, pitchRateKi, pitchRateKd,
                rollRateKp, rollRateKi, rollRateKd,
                yawRateKp, yawRateKi, yawRateKd,
                Mathf.Max(pitchRateIntegralLimit, Mathf.Max(rollRateIntegralLimit, yawRateIntegralLimit)),
                pitchRateOutputLimit, rollRateOutputLimit, yawRateOutputLimit
            );
        }

        if (altitudeController != null)
        {
            altitudeController.UpdateGains(
                altitudeKp, altitudeKi, altitudeKd,
                altitudeIntegralLimit, altitudeOutputLimit,
                maxClimbSpeed, hoverForce, maxMotorForce * 4f
            );
        }
    }

    /// <summary>
    /// Recalculates hover force and normalized throttle required to balance gravity.
    /// </summary>
    public void RecalculateHoverParameters()
    {
        float droneMass = (rb != null) ? rb.mass : 1f;
        hoverForce = droneMass * Physics.gravity.magnitude;
        hoverThrottle = Mathf.Clamp01(hoverForce / Mathf.Max(maxMotorForce * 4f, 0.001f));

        if (altitudeController != null)
        {
            altitudeController.UpdateGains(
                altitudeKp, altitudeKi, altitudeKd,
                altitudeIntegralLimit, altitudeOutputLimit,
                maxClimbSpeed, hoverForce, maxMotorForce * 4f
            );
        }
    }

    /// <summary>
    /// Updates flight state, executes cascading PID controllers, and applies motor forces each physics step.
    /// </summary>
    private void FixedUpdate()
    {
        if (droneHardware == null || droneInputs == null || velocityMode == null || rb == null)
            return;

        SyncControllerParameters();

        float dt = Time.fixedDeltaTime;

        Vector3 localVelocity = transform.InverseTransformDirection(rb.linearVelocity);
        Vector3 localAngularVelocity = transform.InverseTransformDirection(rb.angularVelocity) * Mathf.Rad2Deg;
        Vector3 rotation = NormalizeAngles(transform.eulerAngles);

        DroneState state = new DroneState
        {
            Rotation = rotation,
            AngularVelocity = localAngularVelocity,
            Velocity = localVelocity,
            VerticalVelocity = rb.linearVelocity.y,
            UpDot = Vector3.Dot(transform.up, Vector3.up)
        };

        currentSideVelocity = localVelocity.x;
        currentForwardVelocity = localVelocity.z;
        targetSideVelocity = droneInputs.Roll * maxSideSpeed;
        targetForwardVelocity = droneInputs.Pitch * maxForwardSpeed;

        FlightControlOutput output = velocityMode.Calculate(droneInputs, state, dt);

        finalThrottle = output.Throttle;
        targetRollAngle = output.TargetAngles.x;
        targetPitchAngle = output.TargetAngles.y;
        targetPitchRate = output.TargetRate.x;
        targetRollRate = output.TargetRate.z;

        Vector3 corrections = rateController.ComputeCorrections(output.TargetRate, state.AngularVelocity, dt);

        pitchCorrection = corrections.x;
        yawCorrection = corrections.y;
        rollCorrection = corrections.z;

        ApplyMotorMixing(finalThrottle, corrections);
    }

    /// <summary>
    /// Distributes total throttle and attitude rate corrections across 4 quadcopter motors with saturation scaling.
    /// </summary>
    /// <param name="throttle">Normalized throttle value [0, 1].</param>
    /// <param name="correction">Angular rate correction vector (pitch, yaw, roll).</param>
    private void ApplyMotorMixing(float throttle, Vector3 correction)
    {
        float baseMotorForce = throttle * maxMotorForce;

        float pitch = correction.x;
        float yaw = correction.y;
        float roll = correction.z;

        float fl = -pitch - roll - yaw;
        float fr = -pitch + roll + yaw;
        float bl = pitch - roll + yaw;
        float br = pitch + roll - yaw;

        float minMix = Mathf.Min(fl, Mathf.Min(fr, Mathf.Min(bl, br)));

        if (baseMotorForce + minMix < 0f)
        {
            float requiredCorrection = -minMix;
            if (requiredCorrection > 0.0001f)
            {
                float scale = Mathf.Clamp01(baseMotorForce / requiredCorrection);
                pitch *= scale;
                roll *= scale;
                yaw *= scale;

                fl = -pitch - roll - yaw;
                fr = -pitch + roll + yaw;
                bl = pitch - roll + yaw;
                br = pitch + roll - yaw;
            }
        }

        fl = Mathf.Clamp(fl + baseMotorForce, 0f, maxMotorForce);
        fr = Mathf.Clamp(fr + baseMotorForce, 0f, maxMotorForce);
        bl = Mathf.Clamp(bl + baseMotorForce, 0f, maxMotorForce);
        br = Mathf.Clamp(br + baseMotorForce, 0f, maxMotorForce);

        droneHardware.ApplyMotorForces(fl, fr, bl, br);
    }

    /// <summary>
    /// Resets all cascaded PID loops (velocity, attitude, angular rates, altitude) to initial state.
    /// </summary>
    public void ResetControllers()
    {
        if (velocityMode != null)
            velocityMode.Reset();

        if (rateController != null)
            rateController.Reset();

        RecalculateHoverParameters();
    }

    /// <summary>
    /// Wraps Euler angle values into the signed [-180, 180] degree range for continuous attitude control.
    /// </summary>
    /// <param name="angles">Euler angles in degrees [0, 360].</param>
    /// <returns>Signed Euler angles in degrees [-180, 180].</returns>
    private Vector3 NormalizeAngles(Vector3 angles)
    {
        if (angles.x > 180f) angles.x -= 360f;
        if (angles.y > 180f) angles.y -= 360f;
        if (angles.z > 180f) angles.z -= 360f;
        return angles;
    }

    /// <summary>
    /// Cleans up controller states when the flight control component is disabled.
    /// </summary>
    private void OnDisable()
    {
        ResetControllers();
    }
}

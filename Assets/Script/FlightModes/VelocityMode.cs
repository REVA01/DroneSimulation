using UnityEngine;

namespace Drone.Runtime.FlightModes
{
    /// <summary>
    /// Implements autonomous horizontal velocity cruise flight mode combining velocity, attitude, and altitude cascading controllers.
    /// </summary>
    public class VelocityMode : IFlightMode
    {
        private readonly VelocityToAngleController velocityController;
        private readonly AngleToRateController angleController;
        private readonly AltitudeController altitudeController;
        private readonly float maxForwardSpeed;
        private readonly float maxSideSpeed;
        private readonly float maxYawRate;

        public string ModeName => "VELOCITY CRUISE";
        public MixingStrategy Mixing => MixingStrategy.PrioritizeThrottle;

        /// <summary>
        /// Constructs a VelocityMode instance linking the required cascading controllers and kinematic speed limits.
        /// </summary>
        /// <param name="velocityController">Outer velocity-to-angle controller.</param>
        /// <param name="angleController">Intermediate angle-to-rate controller.</param>
        /// <param name="altitudeController">Vertical climb/hover controller.</param>
        /// <param name="maxForwardSpeed">Maximum forward flight speed in m/s.</param>
        /// <param name="maxSideSpeed">Maximum lateral strafe flight speed in m/s.</param>
        /// <param name="maxYawRate">Maximum yaw rotation rate in deg/s.</param>
        public VelocityMode(
            VelocityToAngleController velocityController,
            AngleToRateController angleController,
            AltitudeController altitudeController,
            float maxForwardSpeed,
            float maxSideSpeed,
            float maxYawRate)
        {
            this.velocityController = velocityController;
            this.angleController = angleController;
            this.altitudeController = altitudeController;
            this.maxForwardSpeed = maxForwardSpeed;
            this.maxSideSpeed = maxSideSpeed;
            this.maxYawRate = maxYawRate;
        }

        /// <summary>
        /// Computes 3-axis target angular rates and throttle command for the current timestep.
        /// </summary>
        /// <param name="inputs">Current normalized control stick inputs.</param>
        /// <param name="state">Current physical drone kinematics state.</param>
        /// <param name="dt">Physics timestep in seconds.</param>
        /// <returns>FlightControlOutput containing demanded rates and collective throttle.</returns>
        public FlightControlOutput Calculate(DroneInputs inputs, DroneState state, float dt)
        {
            float targetSideVelocity = inputs.Roll * maxSideSpeed;
            float targetForwardVelocity = inputs.Pitch * maxForwardSpeed;
            Vector2 targetVelocity = new Vector2(targetSideVelocity, targetForwardVelocity);

            Vector2 currentVelocity = new Vector2(state.Velocity.x, state.Velocity.z);
            Vector2 targetAngles = velocityController.GetTargetAngles(targetVelocity, currentVelocity, dt);
            Vector2 targetRates = angleController.GetTargetRates(targetAngles, state.Rotation, dt);

            float throttle = altitudeController.GetThrottle(
                inputs.Throttle,
                state.VerticalVelocity,
                state.UpDot,
                dt
            );

            return new FlightControlOutput
            {
                TargetRate = new Vector3(
                    targetRates.x,
                    inputs.Yaw * maxYawRate,
                    targetRates.y
                ),
                Throttle = throttle
            };
        }

        /// <summary>
        /// Resets all internal cascade controller states (integrators, derivative memory).
        /// </summary>
        public void Reset()
        {
            velocityController.Reset();
            angleController.Reset();
            altitudeController.Reset();
        }
    }
}

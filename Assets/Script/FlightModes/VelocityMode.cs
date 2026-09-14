using UnityEngine;

namespace Drone.Runtime.FlightModes
{
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

        // Initializes the velocity flight mode with cascading controllers and limits.
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

        // Calculates flight control rates and throttle from user inputs and drone state.
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

        // Resets the cascading controllers.
        public void Reset()
        {
            velocityController.Reset();
            angleController.Reset();
            altitudeController.Reset();
        }
    }
}

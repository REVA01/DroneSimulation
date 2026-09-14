using UnityEngine;

namespace Drone.Runtime.FlightModes
{
    public enum MixingStrategy
    {
        PrioritizeThrottle,
        PrioritizeAttitude
    }

    [System.Serializable]
    public struct DroneState
    {
        public Vector3 Rotation;
        public Vector3 AngularVelocity;
        public Vector3 Velocity;
        public float VerticalVelocity;
        public float UpDot;
    }

    [System.Serializable]
    public struct FlightControlOutput
    {
        public Vector3 TargetRate;
        public float Throttle;
    }

    public interface IFlightMode
    {
        // Calculates target angular rates and throttle for this flight mode.
        FlightControlOutput Calculate(DroneInputs inputs, DroneState state, float dt);

        // Resets the internal state of the flight mode.
        void Reset();

        string ModeName { get; }
        MixingStrategy Mixing { get; }
    }
}

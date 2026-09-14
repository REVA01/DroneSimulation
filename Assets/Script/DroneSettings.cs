using UnityEngine;

namespace Drone
{
    [CreateAssetMenu(fileName = "DroneSettings", menuName = "Drone/Drone Settings")]
    public class DroneSettings : ScriptableObject
    {
        [Header("Movement Limits")]
        public float maxTiltAngle = 30f;
        public float maxYawSpeed = 120f;
        public float maxClimbSpeed = 5f;
        public float maxHorizontalSpeed = 10f;

        [Header("Motor Settings")]
        [Range(0f, 1f)]
        public float idleThrottle = 0.05f;
        public float thrustToWeightRatio = 2f;
    }
}
using UnityEngine;

namespace Drone.Runtime.PID
{
    [CreateAssetMenu(fileName = "PIDConfig", menuName = "Scriptable Objects/PIDConfig")]
    public class PIDConfig : ScriptableObject
    {
        [Header("Gains")]
        public float Kp = 1f;
        public float Ki = 0f;
        public float Kd = 0.5f;

        [Header("Limits")]
        public float maxOutput = 10f;
        public float integralSaturation = 10f;
    }
}
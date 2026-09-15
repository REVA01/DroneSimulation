using UnityEngine;

/// <summary>
/// DroneHealth: 200 Health Points system.
/// Takes 200 damage over 2.0 seconds of continuous laser exposure (100 damage/second).
/// Calculated by damage points (not just simple raw timer).
/// Drone significantly slows down while taking damage.
/// Deactivates with SetActive(false) when health reaches 0 and reorganizes squad.
/// </summary>
public class DroneHealth : MonoBehaviour
{
    [Header("Health Settings")]
    [Tooltip("Health of the drone. Can be controlled directly in the Inspector. When <= 0, drone is destroyed.")]
    public float health = 200f;

    [Tooltip("Maximum health of the drone.")]
    public float maxHealth = 200f;

    [Tooltip("Laser damage dealt per second.")]
    public float laserDamagePerSecond = 100f;

    /// <summary>
    /// Backwards-compatible alias for health so external code or references continue to work seamlessly.
    /// </summary>
    public float currentHealth
    {
        get => health;
        set => health = value;
    }

    [Header("Damage Slowdown Settings")]
    [Tooltip("Speed multiplier applied to the drone while receiving damage (e.g. 0.525 = 50% faster than previous 0.35).")]
    [Range(0.05f, 1f)]
    public float damageSlowdownMultiplier = 0.8f;

    [Tooltip("How long in seconds the slowdown effect persists after the last damage hit.")]
    public float slowdownRecoveryTime = 0.15f;

    [Header("Health Recovery Settings")]
    [Tooltip("If true, health slowly recovers if the player takes the laser off the drone.")]
    public bool recoverWhenNotHit = true;

    [Tooltip("Seconds after laser loses contact before health begins recovering.")]
    public float recoveryDelay = 1.0f;

    [Tooltip("Health points recovered per second when not taking laser damage.")]
    public float recoveryRate = 20f;

    private float timeSinceLastHit = 0f;
    private bool isTakingDamage = false;
    private bool isDestroyed = false;

    public bool IsDestroyed => isDestroyed;
    public bool IsTakingDamage => isTakingDamage;

    private void Awake()
    {
        // If maxHealth wasn't customized beyond default or health was set in Inspector, sync maxHealth
        if (maxHealth <= 0f || (Mathf.Approximately(maxHealth, 200f) && !Mathf.Approximately(health, 200f)))
        {
            maxHealth = health;
        }
        else if (health > maxHealth)
        {
            maxHealth = health;
        }
    }

    private void Update()
    {
        timeSinceLastHit += Time.deltaTime;

        // Clear taking damage flag when laser is no longer hitting the drone
        if (timeSinceLastHit >= slowdownRecoveryTime)
        {
            isTakingDamage = false;
        }

        // If health was manually set to <= 0 in Inspector or depleted
        if (health <= 0f && !isDestroyed)
        {
            health = 0f;
            DestroyDrone();
            return;
        }

        // Slowly recover health if laser is not currently hitting this drone
        if (recoverWhenNotHit && timeSinceLastHit > recoveryDelay && health < maxHealth && !isDestroyed)
        {
            health = Mathf.Min(maxHealth, health + recoveryRate * Time.deltaTime);
        }
    }

    /// <summary>
    /// Called every frame the laser is pointed at this drone.
    /// Calculates damage based on laserDamagePerSecond * deltaTime.
    /// </summary>
    /// <param name="deltaTime">Time.deltaTime from the laser firing frame.</param>
    public void TakeLaserDamage(float deltaTime)
    {
        if (isDestroyed) return;

        float damageThisFrame = laserDamagePerSecond * deltaTime;
        TakeDamage(damageThisFrame);
    }

    /// <summary>
    /// Applies damage points to the drone, triggers slowdown, and checks for destruction.
    /// </summary>
    /// <param name="damageAmount">Damage points to deduct.</param>
    public void TakeDamage(float damageAmount)
    {
        if (isDestroyed) return;

        timeSinceLastHit = 0f;
        isTakingDamage = true;
        health -= damageAmount;

        if (health <= 0f)
        {
            health = 0f;
            DestroyDrone();
        }
    }

    private void DestroyDrone()
    {
        if (isDestroyed) return;
        isDestroyed = true;
        isTakingDamage = false;

        Debug.Log($"<color=red>[DroneHealth] DRONE DESTROYED ({gameObject.name})! Health depleted.</color>");

        // Immediately notify AimAssist to leave this target
        AimAssist assist = FindAnyObjectByType<AimAssist>();
        if (assist != null && assist.CurrentTarget != null)
        {
            if (assist.CurrentTarget == gameObject ||
                gameObject.transform.IsChildOf(assist.CurrentTarget.transform) ||
                assist.CurrentTarget.transform.IsChildOf(gameObject.transform))
            {
                assist.OnTargetDestroyed();
            }
        }

        // Immediately notify FinalGame so laser shuts off and leaves the destroyed drone
        FinalGame fg = FindAnyObjectByType<FinalGame>();
        if (fg != null)
        {
            fg.OnTargetDroneDestroyed(gameObject);
        }

        // Deactivate drone GameObject
        gameObject.SetActive(false);

        // Notify DroneDirector to decrement droneCount by 1 and re-organize the squad
        if (DroneDirector.Instance != null)
        {
            DroneDirector.Instance.OnDroneDestroyed(gameObject);
        }
    }

    public void ResetHealth()
    {
        isDestroyed = false;
        isTakingDamage = false;
        health = maxHealth;
        timeSinceLastHit = 0f;
    }
}

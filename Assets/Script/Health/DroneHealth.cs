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
    [Tooltip("Speed multiplier applied to the drone while receiving damage (0.70 = 30% speed reduction).")]
    [Range(0.05f, 1f)]
    public float damageSlowdownMultiplier = 0.70f;

    [Tooltip("How long in seconds the slowdown effect persists after the last damage hit.")]
    public float slowdownRecoveryTime = 0.15f;

    [Header("Health Recovery Settings")]
    [Tooltip("If true, health slowly recovers if the player takes the laser off the drone.")]
    public bool recoverWhenNotHit = true;

    [Tooltip("Seconds after laser loses contact before health begins recovering.")]
    public float recoveryDelay = 1.0f;

    [Tooltip("Health points recovered per second when not taking laser damage.")]
    public float recoveryRate = 20f;

    [Header("Laser Targeting State")]
    [Tooltip("True exclusively when this exact drone is actively targeted by the laser.")]
    [SerializeField] private bool isLaserTargeted = false;

    private float timeSinceLastHit = 0f;
    private bool isTakingDamage = false;
    private bool isDestroyed = false;
    private CanonMovement cachedCanonMovement;

    public bool IsDestroyed => isDestroyed;
    public bool IsTakingDamage => isTakingDamage;
    public bool IsTargetedByLaser => isLaserTargeted && !isDestroyed;

    /// <summary>
    /// Explicitly sets or clears the laser targeting state on this specific drone.
    /// When targeted is false, the speed reduction is removed immediately.
    /// </summary>
    public void SetLaserTargeted(bool targeted)
    {
        if (isDestroyed && targeted) return;

        isLaserTargeted = targeted;

        if (targeted)
        {
            timeSinceLastHit = 0f;
            isTakingDamage = true;
        }
        else
        {
            isTakingDamage = false;
        }
    }

    /// <summary>
    /// Synchronizes health limits and caches system references.
    /// </summary>
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

        cachedCanonMovement = FindAnyObjectByType<CanonMovement>();
        damageSlowdownMultiplier = 0.70f;
    }

    /// <summary>
    /// Synchronizes drone maximum health capacity with central DroneDirector setting.
    /// </summary>
    private void Start()
    {
        if (DroneDirector.Instance != null && DroneDirector.Instance.droneMaxHealth > 0f)
        {
            maxHealth = DroneDirector.Instance.droneMaxHealth;
            health = maxHealth;
        }
    }

    /// <summary>
    /// Synchronizes maximum health capacity dynamically with DroneDirector settings.
    /// </summary>
    public void SyncMaxHealth(float newMaxHealth)
    {
        if (newMaxHealth <= 0f) return;
        float ratio = maxHealth > 0f ? health / maxHealth : 1.0f;
        maxHealth = newMaxHealth;
        health = Mathf.Clamp(newMaxHealth * ratio, 1f, maxHealth);
    }

    /// <summary>
    /// Tracks laser damage cooldowns, damage slowdown expiration, and health regeneration when unhit.
    /// </summary>
    private void Update()
    {
        timeSinceLastHit += Time.deltaTime;

        // Immediately clear targeting and remove slowdown if laser stopped hitting this drone
        if (timeSinceLastHit > 0.05f)
        {
            if (isLaserTargeted || isTakingDamage)
            {
                SetLaserTargeted(false);
            }
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

        SetLaserTargeted(true);
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

    /// <summary>
    /// Executes drone destruction sequence, notifies targeting systems, deactivates GameObject, and alerts squad director.
    /// </summary>
    private void DestroyDrone()
    {
        if (isDestroyed) return;
        isDestroyed = true;
        isTakingDamage = false;
        isLaserTargeted = false;

        Debug.Log($"<color=red>[DroneHealth] DRONE DESTROYED ({gameObject.name})! Health depleted.</color>");

        // Immediately notify CanonMovement and AimAssist so laser leaves the destroyed drone permanently
        if (cachedCanonMovement == null) cachedCanonMovement = FindAnyObjectByType<CanonMovement>();
        if (cachedCanonMovement != null)
        {
            cachedCanonMovement.OnTargetDroneDestroyed(gameObject);
        }

        AimAssist aim = FindAnyObjectByType<AimAssist>();
        if (aim != null)
        {
            aim.OnDroneDestroyedOrDisabled(gameObject);
        }

        // Deactivate drone GameObject
        gameObject.SetActive(false);

        // Notify DroneDirector to decrement droneCount by 1 and re-organize the squad
        if (DroneDirector.Instance != null)
        {
            DroneDirector.Instance.OnDroneDestroyed(gameObject);
        }
    }

    /// <summary>
    /// Restores full health and clears damage state flags.
    /// </summary>
    public void ResetHealth()
    {
        isDestroyed = false;
        isTakingDamage = false;
        isLaserTargeted = false;
        health = maxHealth;
        timeSinceLastHit = 0f;
    }
}

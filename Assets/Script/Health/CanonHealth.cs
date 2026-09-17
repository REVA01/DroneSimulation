using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks cannon durability and attack tolerance.
/// Configurable durability capacity centralized through DroneDirector.
/// </summary>
public class CanonHealth : MonoBehaviour
{
    [Header("Cannon Health")]
    [Tooltip("Total health capacity of the cannon.")]
    public float maxHealth = 100f;

    [Tooltip("Current remaining health of the cannon.")]
    public float currentHealth = 100f;

    [Header("Hit Cooldown")]
    [Tooltip("Minimum time (seconds) between registered attacks to prevent multi-hit glitches.")]
    public float hitCooldown = 0.1f;

    [Header("Events")]
    public UnityEvent OnDamaged;
    public UnityEvent OnDestroyed;

    private float lastHitTime = -10f;
    private int lastHitFrame = -1;
    private bool isDestroyed = false;

    public bool IsDestroyed => isDestroyed;

    /// <summary>
    /// Initializes current health to maximum health capacity.
    /// </summary>
    private void Awake()
    {
        currentHealth = maxHealth;
    }

    /// <summary>
    /// Synchronizes maximum health from central DroneDirector if available.
    /// </summary>
    private void Start()
    {
        if (DroneDirector.Instance != null && DroneDirector.Instance.cannonMaxHealth > 0f)
        {
            maxHealth = DroneDirector.Instance.cannonMaxHealth;
            currentHealth = maxHealth;
        }
    }

    /// <summary>
    /// Synchronizes maximum health capacity dynamically with DroneDirector settings.
    /// </summary>
    public void SyncMaxHealth(float newMaxHealth)
    {
        if (newMaxHealth <= 0f) return;
        float ratio = maxHealth > 0f ? currentHealth / maxHealth : 1.0f;
        maxHealth = newMaxHealth;
        currentHealth = Mathf.Clamp(newMaxHealth * ratio, 1f, maxHealth);
    }

    /// <summary>
    /// Deals discrete attack strike damage to the cannon.
    /// Debounces same-frame multi-collider hits while allowing fast independent drone strikes.
    /// </summary>
    /// <param name="damage">Number of health points to deduct.</param>
    /// <returns>True if damage was successfully registered, false if on cooldown or already destroyed.</returns>
    public bool TakeDamage(float damage = 10f)
    {
        if (isDestroyed) return false;

        // Debounce only same-frame hits from multiple colliders on the same impact
        if (Time.frameCount == lastHitFrame && Time.time - lastHitTime < 0.001f)
        {
            return false;
        }

        lastHitTime = Time.time;
        lastHitFrame = Time.frameCount;
        currentHealth = Mathf.Max(0f, currentHealth - damage);

        Debug.Log($"<color=orange>[CanonHealth] Cannon attacked! Took {damage:F1} damage. Health remaining: {currentHealth:F1}/{maxHealth:F1}</color>");

        OnDamaged?.Invoke();

        if (currentHealth <= 0f)
        {
            currentHealth = 0f;
            DestroyCanon();
            return true;
        }

        return true;
    }

    /// <summary>
    /// Overload for backwards compatibility with integer damage calls.
    /// </summary>
    public bool TakeDamage(int damage)
    {
        return TakeDamage((float)damage);
    }

    /// <summary>
    /// Deactivates the cannon upon reaching zero health and invokes defeat events.
    /// </summary>
    private void DestroyCanon()
    {
        if (isDestroyed) return;
        isDestroyed = true;

        Debug.Log("<color=red>[CanonHealth] CANNON DESTROYED! Defeat!</color>");

        OnDestroyed?.Invoke();

        // Deactivate the Cannon GameObject
        gameObject.SetActive(false);
    }

    /// <summary>
    /// Resets cannon health and clears cooldown timers.
    /// </summary>
    public void ResetHealth()
    {
        isDestroyed = false;
        currentHealth = maxHealth;
        lastHitTime = -10f;
    }
}

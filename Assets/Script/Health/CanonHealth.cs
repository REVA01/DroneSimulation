using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tracks cannon durability and attack tolerance.
/// Withstands 5 distinct drone attacks before triggering destruction and defeat sequence.
/// </summary>
public class CanonHealth : MonoBehaviour
{
    [Header("Cannon Health")]
    [Tooltip("Total attacks from drones needed to destroy the cannon.")]
    public int maxHealth = 5;

    [Tooltip("Current remaining health (attacks remaining).")]
    public int currentHealth = 5;

    [Header("Hit Cooldown")]
    [Tooltip("Minimum time (seconds) between registered attacks to prevent multi-hit glitches.")]
    public float hitCooldown = 1.0f;

    [Header("Events")]
    public UnityEvent OnDamaged;
    public UnityEvent OnDestroyed;

    private float lastHitTime = -10f;
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
    /// Deals discrete attack strike damage to the cannon.
    /// Enforces hit cooldown to prevent multiple overlapping hits in a single physics frame.
    /// </summary>
    /// <param name="damage">Number of health points / attacks to deduct.</param>
    /// <returns>True if damage was successfully registered, false if on cooldown or already destroyed.</returns>
    public bool TakeDamage(int damage = 1)
    {
        if (isDestroyed) return false;

        if (Time.time - lastHitTime < hitCooldown)
        {
            return false;
        }

        lastHitTime = Time.time;
        currentHealth -= damage;

        Debug.Log($"<color=orange>[CanonHealth] Cannon attacked! Health remaining: {currentHealth}/{maxHealth}</color>");

        OnDamaged?.Invoke();

        if (currentHealth <= 0)
        {
            currentHealth = 0;
            DestroyCanon();
            return true;
        }

        return true;
    }

    /// <summary>
    /// Deactivates the cannon upon reaching zero health and invokes defeat events.
    /// </summary>
    private void DestroyCanon()
    {
        if (isDestroyed) return;
        isDestroyed = true;

        Debug.Log("<color=red>[CanonHealth] CANNON DESTROYED after 5 drone attacks! Defeat!</color>");

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

using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// CanonHealth: Tracks Cannon health. Can withstand 5 drone attacks before being destroyed.
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

    private void Awake()
    {
        currentHealth = maxHealth;
    }

    /// <summary>
    /// Deals 1 attack damage to the cannon.
    /// Returns true if damage was applied, or false if on cooldown or already destroyed.
    /// </summary>
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

    private void DestroyCanon()
    {
        if (isDestroyed) return;
        isDestroyed = true;

        Debug.Log("<color=red>[CanonHealth] CANNON DESTROYED after 5 drone attacks! Defeat!</color>");

        OnDestroyed?.Invoke();

        // Deactivate the Cannon GameObject
        gameObject.SetActive(false);
    }

    public void ResetHealth()
    {
        isDestroyed = false;
        currentHealth = maxHealth;
        lastHitTime = -10f;
    }
}

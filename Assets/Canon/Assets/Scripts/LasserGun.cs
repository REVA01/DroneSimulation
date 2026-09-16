using System;
using UnityEngine;

public class LasserGun : MonoBehaviour
{
    [Header("Laser Setup")]
    public Transform firePoint;
    public LineRenderer lineRenderer;

    [Header("Laser Settings")]
    public KeyCode fireKey = KeyCode.F;
    public float maxDistance = 200f;
    public float laserDuration = 0.2f;
    public float laserWidth = 0.1f;
    public Color laserColor = Color.red;
    public float beamHitRadius = 0.35f;

    [Header("Layer Mask")]
    public LayerMask hitLayers = ~0;

    private float laserTimer = 0f;

    private void Awake()
    {
        // If FinalGame is also attached to the Cannon, disable this LasserGun
        // so that TWO lines are NEVER fired!
        MonoBehaviour[] allScripts = transform.root.GetComponentsInChildren<MonoBehaviour>(true);
        foreach (var s in allScripts)
        {
            if (s != null && (s.GetType().Name == "FinalGame" || s.GetType().Name == "CanonMovement") && s.enabled)
            {
                enabled = false;
                return;
            }
        }

        // Auto-find FirePoint
        if (firePoint == null)
        {
            Transform[] allChildren = transform.root.GetComponentsInChildren<Transform>(true);
            foreach (var child in allChildren)
            {
                string lower = child.name.ToLower();
                if (lower == "firepoint" || lower.Contains("muzzle"))
                {
                    firePoint = child;
                    break;
                }
            }

            if (firePoint == null) firePoint = transform;
        }

        // Setup LineRenderer
        if (lineRenderer == null)
        {
            lineRenderer = GetComponent<LineRenderer>();
            if (lineRenderer == null)
            {
                lineRenderer = gameObject.AddComponent<LineRenderer>();
            }
        }

        ConfigureLineRenderer();
    }

    private void ConfigureLineRenderer()
    {
        if (lineRenderer == null) return;

        lineRenderer.positionCount = 2;
        lineRenderer.startWidth = laserWidth;
        lineRenderer.endWidth = laserWidth;
        lineRenderer.useWorldSpace = true;

        if (lineRenderer.sharedMaterial == null)
        {
            Shader shader = Shader.Find("Sprites/Default");
            if (shader != null) lineRenderer.material = new Material(shader);
        }

        lineRenderer.startColor = laserColor;
        lineRenderer.endColor = laserColor;
        lineRenderer.enabled = false;
    }

    private void Update()
    {
        if (Input.GetKeyDown(fireKey) || Input.GetMouseButtonDown(0))
        {
            laserTimer = laserDuration;
            FireLaser();
        }
        else if (Input.GetKey(fireKey) || Input.GetMouseButton(0))
        {
            FireLaser();
        }
        else if (laserTimer > 0f)
        {
            laserTimer -= Time.deltaTime;
            FireLaser();
        }
        else
        {
            if (lineRenderer != null && lineRenderer.enabled)
            {
                lineRenderer.enabled = false;
            }
        }
    }

    private void FireLaser()
    {
        if (firePoint == null || lineRenderer == null) return;

        Vector3 startPos = firePoint.position;
        Vector3 direction = firePoint.forward;
        Vector3 endPos = startPos + (direction * maxDistance);

        // SphereCast to easily detect drones
        RaycastHit[] hits = Physics.SphereCastAll(startPos, beamHitRadius, direction, maxDistance, hitLayers, QueryTriggerInteraction.Ignore);
        System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

        bool hitValid = false;

        foreach (var hit in hits)
        {
            if (hit.collider == null) continue;
            if (hit.collider.transform.root == transform.root) continue; // Ignore self

            if (TryDeactivateDrone(hit.collider))
            {
                endPos = hit.point;
                hitValid = true;
                break;
            }

            endPos = hit.point;
            hitValid = true;
            break;
        }

        if (!hitValid)
        {
            RaycastHit[] rayHits = Physics.RaycastAll(startPos, direction, maxDistance, hitLayers, QueryTriggerInteraction.Ignore);
            System.Array.Sort(rayHits, (a, b) => a.distance.CompareTo(b.distance));

            foreach (var rHit in rayHits)
            {
                if (rHit.collider == null || rHit.collider.transform.root == transform.root) continue;

                if (TryDeactivateDrone(rHit.collider))
                {
                    endPos = rHit.point;
                    break;
                }

                endPos = rHit.point;
                break;
            }
        }

        lineRenderer.enabled = true;
        lineRenderer.SetPosition(0, startPos);
        lineRenderer.SetPosition(1, endPos);
    }

    private bool TryDeactivateDrone(Collider hitCollider)
    {
        if (hitCollider == null) return false;
        if (hitCollider.transform.root == transform.root) return false;

        GameObject droneRoot = null;

        MonoBehaviour[] components = hitCollider.GetComponentsInParent<MonoBehaviour>(true);
        foreach (var comp in components)
        {
            if (comp == null) continue;
            string typeName = comp.GetType().Name;
            if (typeName == "FlightControlSystem" || typeName == "DroneHardware" ||
                typeName == "DroneBrain" || typeName == "DroneNPCFollowTarget")
            {
                droneRoot = comp.gameObject;
                break;
            }
        }

        if (droneRoot == null && (hitCollider.name.ToLower().Contains("drone") || hitCollider.transform.root.name.ToLower().Contains("drone")))
        {
            droneRoot = hitCollider.transform.root.gameObject;
        }

        if (droneRoot != null && droneRoot.activeInHierarchy)
        {
            DroneHealth droneHealth = hitCollider.GetComponentInParent<DroneHealth>();
            if (droneHealth == null)
            {
                droneHealth = droneRoot.GetComponent<DroneHealth>();
                if (droneHealth == null)
                {
                    droneHealth = droneRoot.AddComponent<DroneHealth>();
                }
            }

            droneHealth.TakeLaserDamage(Time.deltaTime);
            return true;
        }

        return false;
    }
}

using UnityEngine;

public class DisappearOnProximity : MonoBehaviour
{
    [Header("Detection Settings")]
    public float triggerDistance = 0.5f;
    public Transform playerTransform;

    [Header("Optional Effects")]
    public bool useScaleAnimation = true;
    public float shrinkSpeed = 5f;

    private bool _triggered = false;

    void Start()
    {
        // Auto-find Main Camera if nothing is dragged in
        if (playerTransform == null)
        {
            if (Camera.main != null)
                playerTransform = Camera.main.transform;
            else
                Debug.LogError("[DisappearOnProximity] No playerTransform set and no Main Camera found!");
        }
    }

    void Update()
    {
        if (_triggered || playerTransform == null) return;

        // XZ only — ignores Y height difference so walking NEAR the tile triggers it
        // regardless of your headset height vs tile height
        Vector2 tileXZ   = new Vector2(transform.position.x, transform.position.z);
        Vector2 playerXZ = new Vector2(playerTransform.position.x, playerTransform.position.z);
        float dist = Vector2.Distance(tileXZ, playerXZ);

        Debug.Log("Distance to tile: " + dist); // ADD THIS LINE
        
        if (dist <= triggerDistance)
        {
            _triggered = true;

            if (useScaleAnimation)
                StartCoroutine(ShrinkAndDestroy());
            else
                Destroy(gameObject);
        }
    }

    private System.Collections.IEnumerator ShrinkAndDestroy()
    {
        while (transform.localScale.magnitude > 0.05f)
        {
            transform.localScale = Vector3.Lerp(
                transform.localScale,
                Vector3.zero,
                Time.deltaTime * shrinkSpeed
            );
            yield return null;
        }
        Destroy(gameObject);
    }

    void OnDrawGizmosSelected()
    {
        Gizmos.color = Color.cyan;
        Gizmos.DrawWireSphere(transform.position, triggerDistance);
    }
}
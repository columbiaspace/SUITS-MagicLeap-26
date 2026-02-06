using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public class BreadcrumbTrail : MonoBehaviour
{
    [Header("References")]
    public Transform playerCamera;   // XR Camera
    public GameObject tilePrefab;

    [Header("Settings")]
    public float distanceBetweenTiles = 0.6f; // ~2 feet in meters
    public float groundY = 0f;                // adjust if needed

    private Vector3 lastTilePosition;

    void Start()
    {
        // Spawn the first tile immediately
        Vector3 startPos = GetGroundPosition(playerCamera.position);
        SpawnTile(startPos);
        lastTilePosition = startPos;
    }

    void Update()
    {
        Vector3 currentPos = GetGroundPosition(playerCamera.position);

        float distance = Vector3.Distance(lastTilePosition, currentPos);

        if (distance >= distanceBetweenTiles)
        {
            SpawnTile(currentPos);
            lastTilePosition = currentPos;
        }
    }

    Vector3 GetGroundPosition(Vector3 headPos)
{
    Ray ray = new Ray(headPos + Vector3.up, Vector3.down);

    if (Physics.Raycast(ray, out RaycastHit hit, 10f))
    {
        return hit.point;
    }

    return headPos;
}

    void SpawnTile(Vector3 position)
    {
        Instantiate(tilePrefab, position, Quaternion.identity);
    }
}


using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

public class TileManager : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private Transform xrOrigin;              // XR Origin transform
    [SerializeField] private ARAnchorManager anchorManager;   // On XR Origin
    [SerializeField] private GameObject tilePrefab;

    private const float TILE_SIZE = 0.6096f; // 2 feet in meters

    private HashSet<Vector2Int> spawnedTiles = new HashSet<Vector2Int>();

    void Update()
    {
        if (xrOrigin == null || anchorManager == null) return;

        Vector3 playerPos = xrOrigin.position;

        Vector2Int currentGrid = WorldToGrid(playerPos);

        SpawnSurroundingTiles(currentGrid);
    }

    Vector2Int WorldToGrid(Vector3 position)
    {
        int x = Mathf.FloorToInt(position.x / TILE_SIZE);
        int z = Mathf.FloorToInt(position.z / TILE_SIZE);
        return new Vector2Int(x, z);
    }

    void SpawnSurroundingTiles(Vector2Int center)
    {
        for (int x = -1; x <= 1; x++)
        {
            for (int z = -1; z <= 1; z++)
            {
                TrySpawnTile(center + new Vector2Int(x, z));
            }
        }
    }

    void TrySpawnTile(Vector2Int grid)
    {
        if (spawnedTiles.Contains(grid)) return;

        Vector3 spawnPosition = new Vector3(
            grid.x * TILE_SIZE,
            0f,
            grid.y * TILE_SIZE
        );

        // Create an anchor at this world position
        ARAnchor anchor = anchorManager.AddAnchor(new Pose(spawnPosition, Quaternion.identity));

        if (anchor == null)
        {
            Debug.LogWarning("Failed to create anchor.");
            return;
        }

        // Spawn tile as child of anchor (world locked)
        Instantiate(tilePrefab, anchor.transform);

        spawnedTiles.Add(grid);
    }
}

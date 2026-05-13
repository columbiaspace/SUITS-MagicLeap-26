using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Owns the lunar-mesh-derived nav grid that everyone else queries. Builds a per-cell walkability
/// score and average height once at <see cref="Start"/>, then exposes lookup APIs and TSS-position
/// → grid-cell conversion (<see cref="PosToGrid"/>) used by <see cref="TileManager"/> and
/// <see cref="NavPathfinder"/>.
///
/// Registers in <see cref="OnEnable"/> rather than Awake so <see cref="Instance"/> is set when the
/// GameObject becomes active. Inactive objects never receive Awake/OnEnable at load, which would
/// leave <see cref="Instance"/> null until enabled. Execution order -500 puts grid construction
/// before TileManager's -600 Awake bootstrap and before any consumer's Start.
/// </summary>
[DefaultExecutionOrder(-500)]
public class TerrainAnalyzer : MonoBehaviour
{
    [Header("Terrain Source")]
    [Tooltip("Assign the Mesh sub-asset directly (expand the .obj in the Project panel to find it).")]
    [SerializeField] private Mesh terrainMesh;
    [Tooltip("Alternative: drag any scene GameObject that has a MeshFilter containing the lunar mesh. " +
             "Easier than finding the Mesh sub-asset inside the .obj file.")]
    [SerializeField] private MeshFilter meshFilterSource;

    [Header("Grid Settings")]
    [SerializeField] private float tileSize = 0.6096f;

    [Header("Position → Mesh Calibration")]
    [Tooltip("TSS X value that maps to mesh X = 0 (mesh centre). " +
             "Default = centre of the minimap TSS X range: (mapMinX + mapMaxX) / 2 = (-5765 + -5545) / 2 = -5655.")]
    [SerializeField] private float posOffsetX = -5655f;
    [Tooltip("TSS Y value that maps to mesh Z = 0 (mesh centre). " +
             "Default = centre of the minimap TSS Y range: (mapMinY + mapMaxY) / 2 = (-10075 + -9940) / 2 = -10007.5.")]
    [SerializeField] private float posOffsetY = -10007.5f;
    [Tooltip("TSS units per mesh unit along X. " +
             "Default = (mapMaxX - mapMinX) / mesh_X_extent = 220 / 54.638 ≈ 4.027. " +
             "Increase if path appears compressed east-west; decrease if stretched.")]
    [SerializeField] private float posScaleX = 4.027f;
    [Tooltip("TSS units per mesh unit along Z. " +
             "Default = (mapMaxY - mapMinY) / mesh_Z_extent = 135 / 56.735 ≈ 2.380. " +
             "Increase if path appears compressed north-south; decrease if stretched.")]
    [SerializeField] private float posScaleY = 2.380f;

    [Header("Walkability Thresholds")]
    [Tooltip("Higher = allow steeper triangles before maxing difficulty.")]
    [SerializeField] private float maxSlopeDegrees = 40f;
    [Tooltip("Higher = more internal height variation allowed inside one cell.")]
    [SerializeField] private float maxHeightRange = 0.45f;
    [Range(0f, 1f)]
    [SerializeField] private float slopeBlendFactor = 0.6f;
    [Tooltip("Cells with difficulty strictly below this count as walkable. Higher = more walkable cells.")]
    [SerializeField] private float impassableThreshold = 0.82f;

    public static TerrainAnalyzer Instance { get; private set; }
    public bool IsReady { get; private set; }

    private Dictionary<Vector2Int, float> _walkabilityGrid = new Dictionary<Vector2Int, float>();
    private Dictionary<Vector2Int, float> _heightGrid = new Dictionary<Vector2Int, float>();
    private HashSet<Vector2Int> _walkableSet = new HashSet<Vector2Int>();

    private float _objMinX, _objMaxX, _objMinZ, _objMaxZ;

    private struct CellTerrain
    {
        public float maxSlope;
        public float minY;
        public float maxY;
    }

    /// <summary>
    /// Every cell that has any data (walkable or not). Currently only used for diagnostics — the
    /// editor debug overlay and any caller that wants to enumerate the full footprint of the grid.
    /// </summary>
    public IEnumerable<Vector2Int> AllCells => _walkabilityGrid.Keys;

    /// <summary>
    /// The set A* operates over. <see cref="NavPathfinder.FindPath"/> consumes this directly, and
    /// <see cref="NavGridUtilities.SnapToWalkable"/> uses it to round non-walkable EVA/LTV positions
    /// onto the nearest navigable cell. Backed by the live <c>_walkableSet</c>; callers must not
    /// mutate.
    /// </summary>
    public HashSet<Vector2Int> WalkableSet => _walkableSet;

    /// <summary>
    /// Runtime mesh injection used by <see cref="TileManager.Awake"/> when it auto-creates a
    /// TerrainAnalyzer for a scene that doesn't already have one. Must be called before the
    /// component's <see cref="Start"/> runs (which is what triggers <see cref="BuildGrid"/>).
    /// </summary>
    public void SetTerrainMesh(Mesh mesh)
    {
        terrainMesh = mesh;
    }

    /// <summary>
    /// Singleton claim point: first instance to enable wins, duplicates are removed. Runs in
    /// OnEnable rather than Awake so disabled-at-load objects can still register on later
    /// activation. Marks the GameObject DontDestroyOnLoad so the analyzer survives scene swaps.
    /// </summary>
    private void OnEnable()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this);
            return;
        }

        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    /// <summary>
    /// Releases the singleton slot when the active analyzer is disabled, so a freshly enabled
    /// instance can take over without leaking the reference to a stale (disabled) component.
    /// </summary>
    private void OnDisable()
    {
        if (Instance == this)
        {
            Instance = null;
        }
    }

    /// <summary>
    /// Triggers the one-shot grid build. <see cref="IsReady"/> is flipped at the end of
    /// <see cref="BuildGrid"/>; until then <see cref="TileManager.Update"/> short-circuits with a
    /// "TerrainAnalyzer not ready" status.
    /// </summary>
    private void Start()
    {
        BuildGrid();
    }

    // --- Public queries ---

    /// <summary>
    /// Per-cell traversal difficulty in [0, 1] where 0 is flat/easy and 1 is impassable. Returns
    /// -1 for cells that have no data (no triangles touched them). <see cref="NavPathfinder.FindPath"/>
    /// folds this into the edge cost as <c>(1 + weight)</c> so steep/bumpy detours score worse
    /// than flat ones. Negative results are treated as zero by the pathfinder.
    /// </summary>
    public float GetWeight(Vector2Int gridCell)
    {
        return _walkabilityGrid.TryGetValue(gridCell, out float weight) ? weight : -1f;
    }

    /// <summary>
    /// Average mesh-frame Y for the cell (mean of the lowest and highest vertex seen across all
    /// triangles whose centroid landed in it). Currently unused by the pathfinder/TileManager but
    /// kept around for callers that want to drape geometry on the terrain instead of using the
    /// fixed +0.05 m offset in <see cref="NavGridUtilities.CellToLocalTilePos"/>.
    /// </summary>
    public float GetHeight(Vector2Int gridCell)
    {
        return _heightGrid.TryGetValue(gridCell, out float h) ? h : 0f;
    }

    /// <summary>
    /// True iff any triangle's centroid landed in this cell during <see cref="BuildGrid"/>.
    /// The editor debug overlay uses this to color "no-mesh" cells differently from blocked cells.
    /// </summary>
    public bool HasData(Vector2Int gridCell) => _walkabilityGrid.ContainsKey(gridCell);

    /// <summary>
    /// Cheap membership check against the same set <see cref="WalkableSet"/> exposes. Convenience
    /// for the editor debug overlay; the pathfinder uses the set directly to avoid the property
    /// indirection on hot paths.
    /// </summary>
    public bool IsWalkable(Vector2Int gridCell) => _walkableSet.Contains(gridCell);

    // --- Coordinate conversions ---

    /// <summary>
    /// Quantizes a position already in the mesh frame onto the tile grid (<c>floor(coord / tileSize)</c>).
    /// Used internally by <see cref="PosToGrid"/> after calibration, and by
    /// <see cref="TriangleCentroidCell"/> while bucketing mesh triangles into cells. The X
    /// argument is mesh X, the second argument is mesh Z (Y is unused for grid keys; the grid is
    /// a top-down projection).
    /// </summary>
    public Vector2Int ObjToGrid(float objX, float objZ)
    {
        int gx = Mathf.FloorToInt(objX / tileSize);
        int gz = Mathf.FloorToInt(objZ / tileSize);
        return new Vector2Int(gx, gz);
    }

    /// <summary>
    /// Single entry point for "TSS-frame X/Y" → grid cell. Applies <c>posOffsetX/Y</c> +
    /// <c>posScaleX/Y</c> calibration to bring source units into the mesh frame, then defers to
    /// <see cref="ObjToGrid"/> for the floor-to-cell step. Both live TSS data
    /// (<see cref="TileManager.GetImuGridPosition"/>, <see cref="TileManager.GetLtvGridPosition"/>)
    /// and the dummy override fields go through this method, so calibration applies uniformly.
    /// With defaults (offset=0, scale=1) it's equivalent to <see cref="ObjToGrid"/>.
    /// </summary>
    public Vector2Int PosToGrid(float posX, float posY)
    {
        float safeScaleX = Mathf.Approximately(posScaleX, 0f) ? 1f : posScaleX;
        float safeScaleY = Mathf.Approximately(posScaleY, 0f) ? 1f : posScaleY;

        float meshX = (posX - posOffsetX) / safeScaleX;
        float meshZ = (posY - posOffsetY) / safeScaleY;
        return ObjToGrid(meshX, meshZ);
    }

    /// <summary>
    /// Inverse of <see cref="PosToGrid"/>: converts a grid cell back to its TSS-frame center
    /// position by reversing the tileSize quantization and the posOffset/posScale calibration.
    /// Used by the minimap to place A* path segments at the correct fractional position on the
    /// map image. Returns the cell center (cell + 0.5) * tileSize in mesh frame, then
    /// un-calibrates to TSS frame.
    /// </summary>
    public Vector2 GridToTssPos(Vector2Int cell)
    {
        float safeScaleX = Mathf.Approximately(posScaleX, 0f) ? 1f : posScaleX;
        float safeScaleY = Mathf.Approximately(posScaleY, 0f) ? 1f : posScaleY;

        float meshX = (cell.x + 0.5f) * tileSize;
        float meshZ = (cell.y + 0.5f) * tileSize;

        return new Vector2(
            meshX * safeScaleX + posOffsetX,
            meshZ * safeScaleY + posOffsetY
        );
    }

    // --- Grid construction pipeline ---

    /// <summary>
    /// One-shot grid build, called from <see cref="Start"/>:
    /// validate mesh → record overall OBJ bounds → bucket triangles into cells with
    /// slope/height stats → classify cells as walkable or not → flip <see cref="IsReady"/>.
    /// Logs a one-line summary so it's easy to confirm the analyzer woke up with sensible data.
    /// </summary>
    private void BuildGrid()
    {
        if (!ValidateMesh(out Vector3[] verts, out int[] tris))
            return;

        ComputeBounds(verts);

        var cellTerrain = AccumulateTriangleSlopesAndHeights(verts, tris);
        ClassifyCells(cellTerrain);

        IsReady = true;
    }

    /// <summary>
    /// Pulls vertices and triangle indices off the assigned terrain mesh and rejects empty meshes
    /// up front. Returning false leaves <see cref="IsReady"/> false, which keeps
    /// <see cref="TileManager"/> in its "not ready" status until a valid mesh is attached.
    /// </summary>
    private bool ValidateMesh(out Vector3[] verts, out int[] tris)
    {
        verts = null;
        tris = null;

        // Prefer the direct Mesh field; fall back to the MeshFilter on a scene object.
        Mesh mesh = terrainMesh;
        if (mesh == null && meshFilterSource != null)
            mesh = meshFilterSource.sharedMesh;

        if (mesh == null)
        {
            Debug.LogWarning("[TerrainAnalyzer] No mesh assigned. " +
                "Assign the Mesh sub-asset (expand the .obj in the Project panel) to 'Terrain Mesh', " +
                "OR drag a scene object with a MeshFilter into 'Mesh Filter Source'.");
            return false;
        }

        verts = mesh.vertices;
        tris  = mesh.triangles;

        if (verts.Length == 0 || tris.Length == 0)
        {
            Debug.LogWarning("[TerrainAnalyzer] Assigned mesh has no vertices or triangles.");
            return false;
        }

        return true;
    }

    /// <summary>
    /// First pass of the grid build: for each triangle, find the cell its centroid lives in and
    /// fold this triangle's slope and Y-extent into that cell's running stats. Output is
    /// "raw" per-cell terrain data — slope/height numbers, not yet normalized into a 0-1 score.
    /// <see cref="ClassifyCells"/> consumes the result. Note: bucketing by centroid means a
    /// triangle that straddles cell borders only contributes to one cell — fine at this tile
    /// resolution but worth knowing if you ever shrink <c>tileSize</c>.
    /// </summary>
    private Dictionary<Vector2Int, CellTerrain> AccumulateTriangleSlopesAndHeights(
        Vector3[] verts, int[] tris)
    {
        var cells = new Dictionary<Vector2Int, CellTerrain>();

        for (int i = 0; i < tris.Length; i += 3)
        {
            Vector3 a = verts[tris[i]];
            Vector3 b = verts[tris[i + 1]];
            Vector3 c = verts[tris[i + 2]];

            float slopeDeg = TriangleSlopeDegrees(a, b, c);
            Vector2Int cell = TriangleCentroidCell(a, b, c);
            float triMinY = Mathf.Min(a.y, Mathf.Min(b.y, c.y));
            float triMaxY = Mathf.Max(a.y, Mathf.Max(b.y, c.y));

            if (cells.TryGetValue(cell, out CellTerrain existing))
            {
                existing.maxSlope = Mathf.Max(existing.maxSlope, slopeDeg);
                existing.minY = Mathf.Min(existing.minY, triMinY);
                existing.maxY = Mathf.Max(existing.maxY, triMaxY);
                cells[cell] = existing;
            }
            else
            {
                cells[cell] = new CellTerrain
                {
                    maxSlope = slopeDeg,
                    minY = triMinY,
                    maxY = triMaxY
                };
            }
        }

        return cells;
    }

    /// <summary>
    /// Angle in degrees between the triangle face normal and world-up. 0° = perfectly flat,
    /// 90° = vertical wall. Folded into the per-cell <c>maxSlope</c> stat by
    /// <see cref="AccumulateTriangleSlopesAndHeights"/> and later normalized against
    /// <c>maxSlopeDegrees</c> in <see cref="BlendedDifficultyWeight"/>.
    /// </summary>
    private static float TriangleSlopeDegrees(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 normal = Vector3.Cross(b - a, c - a).normalized;
        return Vector3.Angle(normal, Vector3.up);
    }

    /// <summary>
    /// Top-down grid cell containing the triangle's centroid. Used by
    /// <see cref="AccumulateTriangleSlopesAndHeights"/> to decide which cell each triangle
    /// contributes to.
    /// </summary>
    private Vector2Int TriangleCentroidCell(Vector3 a, Vector3 b, Vector3 c)
    {
        Vector3 centroid = (a + b + c) / 3f;
        return ObjToGrid(centroid.x, centroid.z);
    }

    /// <summary>
    /// Second pass of the grid build: turns raw per-cell slope/height stats into the persistent
    /// <c>_walkabilityGrid</c> (0-1 difficulty weight), <c>_heightGrid</c> (mean Y), and
    /// <c>_walkableSet</c> (cells under <c>impassableThreshold</c>). Everything downstream —
    /// <see cref="GetWeight"/>, <see cref="WalkableSet"/>, the pathfinder — reads from these.
    /// </summary>
    private void ClassifyCells(Dictionary<Vector2Int, CellTerrain> cellTerrain)
    {
        foreach (var kvp in cellTerrain)
        {
            Vector2Int cell = kvp.Key;
            CellTerrain terrain = kvp.Value;

            float weight = BlendedDifficultyWeight(terrain.maxSlope, terrain.maxY - terrain.minY);
            _walkabilityGrid[cell] = weight;
            _heightGrid[cell] = (terrain.minY + terrain.maxY) * 0.5f;

            if (weight < impassableThreshold)
                _walkableSet.Add(cell);
        }
    }

    /// <summary>
    /// Combines normalized slope (relative to <c>maxSlopeDegrees</c>) and normalized height
    /// variation (relative to <c>maxHeightRange</c>) into a single 0-1 difficulty score, mixed
    /// by <c>slopeBlendFactor</c>. Result feeds <see cref="ClassifyCells"/> and the per-cell
    /// weight A* uses for edge cost. Higher score = harder to traverse; ≥
    /// <c>impassableThreshold</c> = excluded from the walkable set entirely.
    /// </summary>
    private float BlendedDifficultyWeight(float slopeDeg, float heightRange)
    {
        float slopeWeight = Mathf.Clamp01(slopeDeg / maxSlopeDegrees);
        float heightWeight = Mathf.Clamp01(heightRange / maxHeightRange);
        return Mathf.Clamp01(slopeWeight * slopeBlendFactor + heightWeight * (1f - slopeBlendFactor));
    }

    /// <summary>
    /// Computes the OBJ-frame XZ bounding box of the terrain mesh. The numbers are only used in
    /// the <see cref="BuildGrid"/> log line for sanity-checking that the mesh ended up in the
    /// expected coordinate range — they don't affect pathfinding.
    /// </summary>
    private void ComputeBounds(Vector3[] verts)
    {
        _objMinX = float.MaxValue;
        _objMaxX = float.MinValue;
        _objMinZ = float.MaxValue;
        _objMaxZ = float.MinValue;

        for (int i = 0; i < verts.Length; i++)
        {
            if (verts[i].x < _objMinX) _objMinX = verts[i].x;
            if (verts[i].x > _objMaxX) _objMaxX = verts[i].x;
            if (verts[i].z < _objMinZ) _objMinZ = verts[i].z;
            if (verts[i].z > _objMaxZ) _objMaxZ = verts[i].z;
        }
    }
}

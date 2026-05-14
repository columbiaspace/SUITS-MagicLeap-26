using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Image-based nav-grid builder. Reads a keepout Texture2D that covers the same geographic
/// area as the minimap and builds a 2-D walkability grid where:
///
///   • Red pixels  (R &gt; redThreshold, G &amp; B &lt; greenBlueMax)  → blocked, never entered by A*
///   • Dark pixels (non-red)                                   → high traversal cost
///   • Light pixels (non-red)                                  → low traversal cost (nearly free)
///
/// Coordinate conventions:
///   • Unity's Texture2D.GetPixel(x, y) has y = 0 at the BOTTOM of the image.
///   • Standard PNGs store rows top-to-bottom (row 0 = top of the image), but Unity's
///     importer flips Y so that GetPixel(0, 0) is the bottom-left corner as displayed.
///   • "Top of image = north" therefore aligns naturally: pixel y = 0 → mapMinY (south),
///     pixel y = height-1 → mapMaxY (north).  No manual Y-flip is needed.
///
/// The public interface (PosToGrid, GridToTssPos, GetWeight, WalkableSet, Instance, IsReady)
/// is intentionally identical to the old mesh-based version so NavPathfinder and
/// ARMinimapErica require only minimal changes.
/// </summary>
[DefaultExecutionOrder(-500)]
public class TerrainAnalyzer : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    [Header("Keepout Image")]
    [Tooltip("Texture2D covering the same geographic area as the minimap.\n" +
             "Red pixels (R > redThreshold, G & B < greenBlueMax) are impassable.\n" +
             "Darker non-red pixels cost more to traverse.\n" +
             "IMPORTANT: enable Read/Write in the texture's Import Settings.")]
    [SerializeField] private Texture2D keepoutImage;

    [Header("Grid Resolution")]
    [Tooltip("Edge length of one grid cell in image pixels (each axis). " +
             "4 → one cell per 4×4-pixel block. Smaller = more precise A*, slower build.")]
    [SerializeField] private int cellPixelSize = 4;

    [Header("Map Bounds  (must match ARMinimapErica)")]
    [SerializeField] private float mapMinX = -5765f;
    [SerializeField] private float mapMaxX = -5545f;
    [SerializeField] private float mapMinY = -10075f;
    [SerializeField] private float mapMaxY = -9940f;

    [Header("Red Keepout Detection")]
    [Tooltip("A pixel is 'red' (blocked) when its R channel exceeds this.")]
    [SerializeField] private float redThreshold  = 0.6f;
    [Tooltip("A pixel is 'red' (blocked) when both G and B channels are below this.")]
    [SerializeField] private float greenBlueMax  = 0.4f;

    [Header("Darkness → Cost Mapping")]
    [Tooltip("Maximum traversal cost assigned to a pure-black non-red pixel. " +
             "White non-red pixels get cost 0.  Keep below impassableThreshold.")]
    [SerializeField] private float maxDarknessCost    = 0.75f;
    [Tooltip("Cells whose cost equals or exceeds this value are excluded from A*. " +
             "Must be > maxDarknessCost so dark-but-non-red cells remain walkable.")]
    [SerializeField] private float impassableThreshold = 0.9f;

    // -------------------------------------------------------------------------
    // Singleton
    // -------------------------------------------------------------------------

    public static TerrainAnalyzer Instance { get; private set; }

    /// <summary>True once BuildGrid has successfully completed.</summary>
    public bool IsReady { get; private set; }

    /// <summary>The impassable threshold used during grid construction — exposed so the
    /// minimap overlay can shade cells with a consistent colour scale.</summary>
    public float ImpassableThreshold => impassableThreshold;

    // -------------------------------------------------------------------------
    // Grid data
    // -------------------------------------------------------------------------

    private int _gridW, _gridH;

    // Walkable cells: cost in [0, maxDarknessCost].  A* only visits these.
    private readonly Dictionary<Vector2Int, float> _costGrid    = new Dictionary<Vector2Int, float>();
    private readonly HashSet<Vector2Int>            _walkableSet = new HashSet<Vector2Int>();
    // Blocked cells (red pixels): excluded from A*, coloured red in the overlay.
    private readonly HashSet<Vector2Int>            _blockedSet  = new HashSet<Vector2Int>();

    // -------------------------------------------------------------------------
    // Public grid queries
    // -------------------------------------------------------------------------

    /// <summary>All walkable cells. NavPathfinder.FindPath uses this set directly.</summary>
    public HashSet<Vector2Int> WalkableSet => _walkableSet;

    /// <summary>All blocked (red) cells. Used by the minimap debug overlay.</summary>
    public IEnumerable<Vector2Int> BlockedCells => _blockedSet;

    /// <summary>All walkable cells (same as WalkableSet, exposed as IEnumerable for the overlay).</summary>
    public IEnumerable<Vector2Int> AllCells => _costGrid.Keys;

    /// <summary>
    /// Traversal cost for <paramref name="cell"/> in [0, maxDarknessCost], or -1 if the cell
    /// has no data.  NavPathfinder folds this into edge cost as (1 + weight).
    /// </summary>
    public float GetWeight(Vector2Int cell) =>
        _costGrid.TryGetValue(cell, out float cost) ? cost : -1f;

    /// <summary>True if the cell is in the walkable set.</summary>
    public bool IsWalkable(Vector2Int cell) => _walkableSet.Contains(cell);

    /// <summary>
    /// Axis-aligned bounding box of the grid. Because every image cell is analysed,
    /// this is always (0, 0) → (_gridW-1, _gridH-1).
    /// </summary>
    public void GetGridBounds(out Vector2Int minCell, out Vector2Int maxCell)
    {
        minCell = Vector2Int.zero;
        maxCell = new Vector2Int(Mathf.Max(0, _gridW - 1), Mathf.Max(0, _gridH - 1));
    }

    // -------------------------------------------------------------------------
    // Coordinate conversion  (TSS ↔ grid cell)
    // -------------------------------------------------------------------------

    /// <summary>
    /// Converts a TSS world position (metres) into a grid cell index.
    /// Each cell covers <c>cellPixelSize × cellPixelSize</c> pixels of the keepout image,
    /// so this is a two-step transform: TSS → normalised → pixel → cell.
    /// </summary>
    public Vector2Int PosToGrid(float tssX, float tssY)
    {
        if (keepoutImage == null) return Vector2Int.zero;
        float normX = Mathf.Clamp01((tssX - mapMinX) / (mapMaxX - mapMinX));
        float normY = Mathf.Clamp01((tssY - mapMinY) / (mapMaxY - mapMinY));
        int px = Mathf.Clamp(Mathf.FloorToInt(normX * keepoutImage.width),  0, keepoutImage.width  - 1);
        int py = Mathf.Clamp(Mathf.FloorToInt(normY * keepoutImage.height), 0, keepoutImage.height - 1);
        return new Vector2Int(px / cellPixelSize, py / cellPixelSize);
    }

    /// <summary>
    /// Converts a grid cell back to the TSS position at its centre. Inverse of
    /// <see cref="PosToGrid"/> (modulo the quantisation introduced by <c>cellPixelSize</c>).
    /// Used by ARMinimapErica to map A* path cells onto the minimap.
    /// </summary>
    public Vector2 GridToTssPos(Vector2Int cell)
    {
        if (keepoutImage == null) return Vector2.zero;
        float pixCX = (cell.x + 0.5f) * cellPixelSize;
        float pixCY = (cell.y + 0.5f) * cellPixelSize;
        float tssX  = mapMinX + (pixCX / keepoutImage.width)  * (mapMaxX - mapMinX);
        float tssY  = mapMinY + (pixCY / keepoutImage.height) * (mapMaxY - mapMinY);
        return new Vector2(tssX, tssY);
    }

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    // Called by Unity when the component is added or Reset is chosen in the Inspector.
    // Sets calibration fields to the correct project defaults so they are never left at 0.
    private void Reset()
    {
        mapMinX = -5765f; mapMaxX = -5545f;
        mapMinY = -10075f; mapMaxY = -9940f;
        cellPixelSize      = 4;
        redThreshold       = 0.6f;
        greenBlueMax       = 0.4f;
        maxDarknessCost    = 0.75f;
        impassableThreshold = 0.9f;
    }

    private void OnEnable()
    {
        if (Instance != null && Instance != this) { Destroy(this); return; }
        Instance = this;
        DontDestroyOnLoad(gameObject);
    }

    private void OnDisable()
    {
        if (Instance == this) Instance = null;
    }

    private void Start()
    {
        BuildGrid();
    }

    // -------------------------------------------------------------------------
    // Grid construction
    // -------------------------------------------------------------------------

    private void BuildGrid()
    {
        _costGrid.Clear();
        _walkableSet.Clear();
        _blockedSet.Clear();
        IsReady = false;

        if (keepoutImage == null)
        {
            Debug.LogError("[TerrainAnalyzer] No keepout image assigned. " +
                           "Drag keepout.png into the 'Keepout Image' field and enable " +
                           "Read/Write in its Import Settings.");
            return;
        }

        // Verify the texture is CPU-readable (Read/Write must be enabled in Import Settings).
        try { keepoutImage.GetPixel(0, 0); }
        catch (UnityException)
        {
            Debug.LogError($"[TerrainAnalyzer] '{keepoutImage.name}' is not CPU-readable. " +
                           "Select the texture in the Project panel → Inspector → " +
                           "tick 'Read/Write Enabled' → Apply.");
            return;
        }

        _gridW = Mathf.CeilToInt((float)keepoutImage.width  / cellPixelSize);
        _gridH = Mathf.CeilToInt((float)keepoutImage.height / cellPixelSize);

        for (int gy = 0; gy < _gridH; gy++)
            for (int gx = 0; gx < _gridW; gx++)
                AnalyseCell(gx, gy);

        IsReady = true;
        LogDiagnostics();
    }

    /// <summary>
    /// Samples all pixels in the <c>cellPixelSize × cellPixelSize</c> block for cell
    /// (gx, gy).  A single red pixel makes the whole cell impassable.  Otherwise the
    /// average brightness determines the cost (darker → higher cost).
    /// </summary>
    private void AnalyseCell(int gx, int gy)
    {
        int pxStart = gx * cellPixelSize;
        int pyStart = gy * cellPixelSize;
        int pxEnd   = Mathf.Min(pxStart + cellPixelSize, keepoutImage.width);
        int pyEnd   = Mathf.Min(pyStart + cellPixelSize, keepoutImage.height);

        bool  hasRed      = false;
        float brightness  = 0f;
        int   samples     = 0;

        for (int py = pyStart; py < pyEnd && !hasRed; py++)
        {
            for (int px = pxStart; px < pxEnd && !hasRed; px++)
            {
                Color c = keepoutImage.GetPixel(px, py);
                if (IsRed(c))
                    hasRed = true;
                else
                {
                    brightness += (c.r + c.g + c.b) / 3f;
                    samples++;
                }
            }
        }

        var cell = new Vector2Int(gx, gy);

        if (hasRed)
        {
            _blockedSet.Add(cell);
            // Not added to _costGrid or _walkableSet — A* will not visit this cell.
        }
        else
        {
            float avg  = samples > 0 ? brightness / samples : 1f;
            // White (avg=1) → cost 0 (free).  Black (avg=0) → cost maxDarknessCost.
            float cost = (1f - avg) * maxDarknessCost;
            _costGrid[cell]  = cost;
            _walkableSet.Add(cell);
        }
    }

    private bool IsRed(Color c) =>
        c.r > redThreshold && c.g < greenBlueMax && c.b < greenBlueMax;

    // -------------------------------------------------------------------------
    // Diagnostics
    // -------------------------------------------------------------------------

    /// <summary>
    /// Prints a summary of the current grid state to the Unity Console.
    /// Also available via the Inspector context-menu (⋮ → Log Diagnostics).
    /// </summary>
    [ContextMenu("Log Diagnostics")]
    public void LogDiagnostics()
    {
        int   total   = _gridW * _gridH;
        float walkPct = total > 0 ? 100f * _walkableSet.Count / total : 0f;
        float blkPct  = total > 0 ? 100f * _blockedSet.Count  / total : 0f;

        string imgInfo = keepoutImage != null
            ? $"{keepoutImage.name}  ({keepoutImage.width}×{keepoutImage.height} px)"
            : "NONE";

        Debug.Log(
            $"[TerrainAnalyzer] ── Diagnostics ──────────────────────────────────\n" +
            $"  IsReady         : {IsReady}\n" +
            $"  Keepout image   : {imgInfo}\n" +
            $"  Cell pixel size : {cellPixelSize} px  →  grid {_gridW}×{_gridH} = {total} cells\n" +
            $"  Walkable cells  : {_walkableSet.Count} ({walkPct:F1}%)\n" +
            $"  Blocked cells   : {_blockedSet.Count}  ({blkPct:F1}%)  ← red keepout pixels\n" +
            $"  Map bounds      : X [{mapMinX}, {mapMaxX}]  Y [{mapMinY}, {mapMaxY}]\n" +
            $"  Red detection   : R > {redThreshold}  AND  G,B < {greenBlueMax}\n" +
            $"  Cost mapping    : white→0  black→{maxDarknessCost:F2}  blocked if ≥{impassableThreshold}\n" +
            $"  ────────────────────────────────────────────────────────────────"
        );
    }
}

using MockRoom.Core.Geometry;
using MockRoom.Core.Units;

namespace MockRoom.Core.Spatial;

/// <summary>
/// A uniform grid over the room floor that marks which cells are occupied by
/// items or door swings. Cell-center sampling keeps the math simple and handles
/// overlapping footprints correctly (a cell counts once no matter how many things
/// cover it). The grid also drives the blue free-space overlay in the views.
/// </summary>
public sealed class OccupancyGrid
{
    private readonly bool[] _cells;

    public OccupancyGrid(double widthMeters, double lengthMeters, double cellSizeMeters)
    {
        if (cellSizeMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(cellSizeMeters));

        CellSize = cellSizeMeters;
        Columns = Math.Max(1, (int)Math.Round(widthMeters / cellSizeMeters));
        Rows = Math.Max(1, (int)Math.Round(lengthMeters / cellSizeMeters));
        _cells = new bool[Columns * Rows];
    }

    public int Columns { get; }
    public int Rows { get; }
    public double CellSize { get; }

    public int CellCount => Columns * Rows;
    public double CellAreaSquareMeters => CellSize * CellSize;

    public bool IsOccupied(int col, int row) => _cells[Index(col, row)];

    /// <summary>The world-space center of a cell, in meters.</summary>
    public Vec2 CellCenter(int col, int row)
        => new((col + 0.5) * CellSize, (row + 0.5) * CellSize);

    public int OccupiedCellCount
    {
        get
        {
            var count = 0;
            foreach (var c in _cells)
                if (c) count++;
            return count;
        }
    }

    public int FreeCellCount => CellCount - OccupiedCellCount;

    /// <summary>
    /// Marks every cell whose center lies inside the region as occupied. Generic over
    /// the region type so footprint rectangles and door swing arcs are both marked
    /// without boxing the value type (keeps the path NativeAOT-clean).
    /// </summary>
    public void Mark<T>(T region) where T : IFloorRegion
    {
        var (minX, minY, maxX, maxY) = region.Bounds();

        var colStart = Math.Max(0, (int)Math.Floor(minX / CellSize));
        var colEnd = Math.Min(Columns - 1, (int)Math.Ceiling(maxX / CellSize));
        var rowStart = Math.Max(0, (int)Math.Floor(minY / CellSize));
        var rowEnd = Math.Min(Rows - 1, (int)Math.Ceiling(maxY / CellSize));

        for (var row = rowStart; row <= rowEnd; row++)
        {
            for (var col = colStart; col <= colEnd; col++)
            {
                if (region.Contains(CellCenter(col, row)))
                    _cells[Index(col, row)] = true;
            }
        }
    }

    public Area OccupiedArea => Units.Area.FromSquareMeters(OccupiedCellCount * CellAreaSquareMeters);

    /// <summary>
    /// The free (unoccupied) cells collapsed into maximal horizontal runs, one row at
    /// a time, as <c>(Row, ColStart, ColEnd)</c> with <c>ColEnd</c> exclusive. Lets the
    /// views paint the blue free-floor overlay with a handful of rectangles instead of
    /// one per cell.
    /// </summary>
    public List<(int Row, int ColStart, int ColEnd)> FreeRuns()
    {
        var runs = new List<(int, int, int)>();
        for (var row = 0; row < Rows; row++)
        {
            var col = 0;
            while (col < Columns)
            {
                if (IsOccupied(col, row))
                {
                    col++;
                    continue;
                }

                var start = col;
                while (col < Columns && !IsOccupied(col, row))
                    col++;
                runs.Add((row, start, col));
            }
        }

        return runs;
    }

    private int Index(int col, int row)
    {
        if ((uint)col >= (uint)Columns || (uint)row >= (uint)Rows)
            throw new ArgumentOutOfRangeException($"cell ({col},{row}) outside grid {Columns}x{Rows}");
        return row * Columns + col;
    }
}

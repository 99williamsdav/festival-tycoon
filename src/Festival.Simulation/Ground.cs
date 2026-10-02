using System.Text.Json;

namespace Festival.Simulation;

/// <summary>
/// How worn the field's grass is, cell by cell: only cells with any wear, in ascending flat-index order
/// (<c>Z * Width + X</c>), with the wear at each.
/// </summary>
public sealed record GroundSnapshot(int Version, int[] Cells, int[] Wear);

public static class GroundRules
{
    /// <summary>Every this many ticks, each person on the field wears the grass under their feet by one.</summary>
    public const int FootfallEveryTicks = 40;
    /// <summary>Wear at which a cell has gone to bare earth; the ground shows nothing more after this.</summary>
    public const int BareEarthWear = 160;
    public const int MaximumWear = 1_000;
    /// <summary>The field inside the hedges, in grid cells: world -32 m to +32 m on both axes.</summary>
    public const int FieldFirstCell = 64, FieldLastCell = 191;

    public static bool InField(GridCell cell) =>
        cell.X is >= FieldFirstCell and <= FieldLastCell && cell.Z is >= FieldFirstCell and <= FieldLastCell;
}

public sealed partial class GameSession
{
    // Dense by flat cell index while an edition exists; saved sparsely.
    private int[]? _groundWear;

    /// <summary>Rises whenever any cell's wear changes, so a view can tell when to redraw.</summary>
    public long GroundVersion { get; private set; }

    public GroundSnapshot? CaptureGround()
    {
        if (_groundWear is not { } wear) return null;
        var cells = new List<int>(); var amounts = new List<int>();
        for (var i = 0; i < wear.Length; i++)
            if (wear[i] > 0) { cells.Add(i); amounts.Add(wear[i]); }
        return new(1, cells.ToArray(), amounts.ToArray());
    }

    internal string? GroundCanonicalJson => CaptureGround() is { } ground ? JsonSerializer.Serialize(ground) : null;

    /// <summary>How worn a cell is, 0 for fresh grass up to <see cref="GroundRules.MaximumWear"/>.</summary>
    public int GroundWearAt(GridCell cell) => _groundWear is { } wear && GroundRules.InField(cell) ? wear[cell.Z * TraversalGrid.Width + cell.X] : 0;

    private void ResetGround()
    {
        _groundWear = new int[TraversalGrid.Width * TraversalGrid.Depth];
        GroundVersion++;
    }

    private void RestoreGround(GroundSnapshot? snapshot)
    {
        if (snapshot is null) { _groundWear = null; return; }
        _groundWear = new int[TraversalGrid.Width * TraversalGrid.Depth];
        for (var i = 0; i < snapshot.Cells.Length; i++) _groundWear[snapshot.Cells[i]] = snapshot.Wear[i];
        GroundVersion++;
    }

    /// <summary>
    /// Feet wear the grass: while the festival is on, everyone on the field wears the cell they stand on. Standing
    /// crowds and queues wear deepest; walking routes wear wherever people pass most.
    /// </summary>
    private void AdvanceGround()
    {
        if (_groundWear is not { } wear || _preparation?.Status is not (PreparationStatus.Running or PreparationStatus.Departing) ||
            CurrentTick % GroundRules.FootfallEveryTicks != 0) return;
        var changed = false;
        foreach (var agent in _navigationAgents.Values)
        {
            var cell = TraversalGrid.WorldToCell(agent.XMillimetres, agent.ZMillimetres);
            if (!GroundRules.InField(cell)) continue;
            ref var amount = ref wear[cell.Z * TraversalGrid.Width + cell.X];
            if (amount < GroundRules.MaximumWear) { amount++; changed = true; }
        }
        if (changed) GroundVersion++;
    }

    private static string? ValidatePersistedGround(SessionPersistenceSnapshot s)
    {
        if (s.Ground is null) return s.Preparation?.Plan is null ? null : "Current Build save requires ground state.";
        var g = s.Ground;
        if (g.Version != 1 || g.Cells is null || g.Wear is null || g.Cells.Length != g.Wear.Length || s.Preparation is not { } prep)
            return "Ground state shape invalid.";
        if (prep.Status == PreparationStatus.Preparing && g.Cells.Length != 0) return "Draft/retry must clear ground wear.";
        for (var i = 0; i < g.Cells.Length; i++)
        {
            var index = g.Cells[i];
            if (i > 0 && index <= g.Cells[i - 1]) return "Ground cells must be in ascending order without repeats.";
            if (index < 0 || index >= TraversalGrid.Width * TraversalGrid.Depth ||
                !GroundRules.InField(new GridCell(index % TraversalGrid.Width, index / TraversalGrid.Width)))
                return "Ground wear lies outside the field.";
            if (g.Wear[i] is < 1 or > GroundRules.MaximumWear) return "Ground wear out of range.";
        }
        return null;
    }
}

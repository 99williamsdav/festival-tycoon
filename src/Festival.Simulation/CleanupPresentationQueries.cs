namespace Festival.Simulation;

public sealed partial class GameSession
{
    public CleanupSweep? CaptureCleanupSweep(ulong workerId) => _litter?.Sweeps.SingleOrDefault(s => s.WorkerId == workerId);
    public WastePiece? CaptureWastePiece(string id) => CaptureWaste(id);
    public (int XMillimetres, int ZMillimetres)? CaptureCleanupFooting(ulong workerId) =>
        _navigationAgents.TryGetValue(new(workerId), out var nav) ? (nav.XMillimetres, nav.ZMillimetres) : null;
}

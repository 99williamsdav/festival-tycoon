namespace Festival.Simulation;

/// <summary>Measured fixed barn geometry. Visual translucency never changes this ground or rain cover.</summary>
public static class BarnLayout
{
    public const int OriginZ = -23_000;
    public const int GeneratorX = 7_000, GeneratorZ = -13_500;
    public static (int X, int Z) Local(GridCell cell)
    {
        var p = TraversalGrid.CellCentre(cell); return (p.XMillimetres, p.ZMillimetres - OriginZ);
    }
    public static GridCell Cell(int x, int localZ) => TraversalGrid.WorldToCell(x, OriginZ + localZ);
    public static bool Interior(int x, int z) => x is >= -9_542 and <= 9_542 && z - OriginZ is >= -5_573 and <= 5_573;
    public static bool Covered(GridCell cell) { var p = TraversalGrid.CellCentre(cell); return Interior(p.XMillimetres, p.ZMillimetres); }
    public static bool Deck(GridCell cell) { var p = Local(cell); return p.X is >= 4_500 and <= 7_750 && p.Z is >= -4_250 and <= 1_250; }
    public static bool Stairs(GridCell cell) { var p = Local(cell); return p.X is >= 6_500 and < 7_000 && p.Z is >= 1_500 and <= 3_250; }
    /// <summary>Continuous measured stair clearance, including separation offsets and hands-on steps.
    /// The .38 m upright pick capsule clears the high right rail; feet stay on the tread tops.
    /// Reject a crossing segment instead of clamping or teleporting its actor.</summary>
    public static bool StairSweepClear(int fromX,int fromZ,int toX,int toZ) =>
        !CrossesStairBox(fromX,fromZ,toX,toZ,6_000,6_650,1_485,3_385) &&
        !CrossesStairBox(fromX,fromZ,toX,toZ,7_100,8_000,1_255,3_585);
    public static bool MovementSweepClear(int fromX,int fromZ,int toX,int toZ,bool stageActive = true) =>
        (!stageActive || StairSweepClear(fromX,fromZ,toX,toZ)) &&
        !CrossesStairBox(fromX,fromZ,toX,toZ,-11_000,-8_270,5_193,6_850) &&
        !CrossesStairBox(fromX,fromZ,toX,toZ,-7_050,-3_500,5_193,6_850) &&
        !CrossesStairBox(fromX,fromZ,toX,toZ,3_500,11_000,5_193,6_850);
    // The coordinates below are local to the fixed Barn, while movement supplies world millimetres.
    private static bool CrossesStairBox(int fromX,int fromZ,int toX,int toZ,int minX,int maxX,int minZ,int maxZ)
    {
        var enter=0.0;var leave=1.0;
        bool Clip(int start,int end,int low,int high)
        {
            var delta=end-start;if(delta==0)return start>=low&&start<=high;
            var a=(double)(low-start)/delta;var b=(double)(high-start)/delta;
            enter=Math.Max(enter,Math.Min(a,b));leave=Math.Min(leave,Math.Max(a,b));return enter<=leave;
        }
        return Clip(fromX,toX,minX,maxX)&&Clip(fromZ-OriginZ,toZ-OriginZ,minZ,maxZ);
    }
    public static bool Circulation(GridCell cell)
    {
        var p = Local(cell);
        return p.X is >= -9_300 and <= 9_300 && p.Z is >= 3_500 and <= 7_250 ||
            p.X is >= 6_250 and <= 8_000 && p.Z is >= 1_000 and <= 4_800;
    }
    public static bool Reserved(GridCell cell) { var p = Local(cell); return p.X is >= -9_500 and <= 9_500 && p.Z is >= -5_750 and <= 7_250 || GeneratorReserve(cell); }
    // Existing hire-set bounds x -1.73..2.061, z ±.73 m, plus moving capsule and separation clearance.
    public static bool Generator(GridCell cell) { var p = TraversalGrid.CellCentre(cell); return p.XMillimetres >= GeneratorX-2_250 && p.XMillimetres <= GeneratorX+2_750 && p.ZMillimetres >= GeneratorZ-1_250 && p.ZMillimetres <= GeneratorZ+1_250; }
    public static bool GeneratorReserve(GridCell cell) { var p = TraversalGrid.CellCentre(cell); return p.XMillimetres >= GeneratorX-2_750 && p.XMillimetres <= GeneratorX+3_250 && p.ZMillimetres >= GeneratorZ-1_750 && p.ZMillimetres <= GeneratorZ+1_750; }
    public static IEnumerable<GridCell> Coverage()
    {
        for (var z = 70; z <= 93; z++) for (var x = 108; x <= 147; x++)
        { var cell = new GridCell(x, z); if (Covered(cell)) yield return cell; }
    }
    /// <summary>Keep the old padded perimeter solid; open measured floor and the two front passages only.</summary>
    public static IEnumerable<(GridCell Cell, bool Walkable)> Terrain(bool stageActive = true)
    {
        var lower = TraversalGrid.WorldToCell(-11_000, -30_000); var upper = TraversalGrid.WorldToCell(11_000, -16_000);
        for (var z = lower.Z; z <= upper.Z; z++) for (var x = lower.X; x <= upper.X; x++)
        {
            var cell = new GridCell(x,z); var p = Local(cell);
            // Include a full moving/pick capsule margin against the measured inner walls, not just centre-point clearance.
            var floor = p.X is >= -9_000 and <= 9_000 && p.Z is >= -5_000 and <= 5_000;
            // Three exit columns allow opposing walkers to yield. Continuous movement retains the .38 m frame margin.
            var door = p.Z is >= 5_250 and <= 7_250 && (p.X is >= -3_500 and <= 3_500 || p.X is >= -8_250 and <= -7_250);
            var stageFootprint = p.X is >= 4_000 and <= 8_250 && p.Z is >= -4_750 and <= 1_500;
            var stairSides = p.X is >= 6_000 and <= 8_000 && p.Z is >= 1_500 and <= 3_250;
            yield return (cell, (floor || door) && (!stageActive || !stageFootprint && !stairSides || Deck(cell) || Stairs(cell)));
        }
    }
    public static IEnumerable<(GridCell Cell, bool Walkable)> StageCells()
    {
        // The fixed stage is already in Terrain; the fixture additionally has a real outside generator footprint.
        foreach (var item in Terrain()) yield return item;
        var low = TraversalGrid.WorldToCell(GeneratorX-2_250,GeneratorZ-1_250);
        var high = TraversalGrid.WorldToCell(GeneratorX+2_750,GeneratorZ+1_250);
        for(var z=low.Z;z<=high.Z;z++) for(var x=low.X;x<=high.X;x++) { var cell = new GridCell(x,z); if(Generator(cell)) yield return (cell,false); }
    }
    public static readonly FestivalStage Stage = new()
    {
        Id = "stage.barn", Name = "Barn Stage", ShortName = "Barn Stage",
        // Tier 2 keeps the existing second-stage slot timings, roster and economics.
        SlotStarts = [9_000,20_600,32_800], SlotEnds = [17_400,29_000,41_200],
        PerformerMarks = [Cell(5_700,-250),Cell(5_700,-2_750),Cell(7_050,-1_500)],
        AccessCells = [Cell(6_750,3_750),Cell(7_250,3_750),Cell(6_750,4_750)],
        StairCells = [Cell(6_750,2_750),Cell(6_750,2_250),Cell(6_750,1_750)],
        Deck = Deck, Stairs = Stairs, Cells = StageCells,
        Reserve = Reserved, ReserveBounds = (108,148,70,104), Access = cell => Deck(cell)||Stairs(cell)||Circulation(cell),
        BackstagePlace = i => Cell(3_250,-4_750+i*1_000),
        ArrivalStart = i => new(52+i%2*2,112+i*3),
        ListeningPlaces = Listening(), AudienceBounds = FestivalStage.BoundsOf(Listening()),
        Facing = (-1,0), Front = Cell(2_250,0).X, Sightline = Cell(0,-1_000).Z,
        NearGround = (112,135,74,89), PlaceLimits = FestivalStage.BoundsOf(Listening()),
        MixingPlace = Cell(-7_750,-1_250), Placement = new(6_200,-24_500,-90,900)
    };
    private static GridCell[] Listening() =>
        (from x in Enumerable.Range(0,10) from z in Enumerable.Range(0,6)
         select Cell(2_250-x*1_000,-3_250+z*1_000)).ToArray();
}

public sealed partial class GameSession
{
    private IReadOnlyList<FestivalStage>? _diagnosticStages;
    /// <summary>Not a campaign unlock or persisted mode. Only the opt-in controlled fixture sets this.</summary>
    public bool BarnDiagnostic => _diagnosticStages is not null;
    public int BarnOccupants => _navigationAgents.Values.Count(nav =>
        BarnLayout.Interior(nav.XMillimetres,nav.ZMillimetres) &&
        PersonIn(PersonView.Roster,nav.Id.Value) is { Departed: false } person &&
        (person.Role != ProtectedPersonRole.Guest || person.Admitted));
    internal static GameSession CreateBarnDiagnostic(ulong seed)
    {
        var session = CreateBuildCampaign(seed,25,FestivalStanding.Established,pondStageTrial:true);
        session._diagnosticStages = [FestivalStages.Main,BarnLayout.Stage];
        session._programme = session._programme! with { Stages = [session._programme.Stages[0],session._programme.Stages[1] with {StageId=BarnLayout.Stage.Id}] };
        session._stageGenerators = [NewPondGenerator() with {StageId=BarnLayout.Stage.Id,XMillimetres=BarnLayout.GeneratorX,ZMillimetres=BarnLayout.GeneratorZ,Response="Controlled Barn fixture generator"}];
        return session;
    }
}

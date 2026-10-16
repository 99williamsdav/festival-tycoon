namespace Festival.Simulation;

/// <summary>Approved static tractor B, grounded at world (0,0,-20.9), front +Z.</summary>
public static class BarnTractorLayout
{
    public const int WorldZ=-20_900;
    public const int MinX=-1_127,MaxX=1_127,MinZ=WorldZ-1_860,MaxZ=WorldZ+2_190;
    // The shipped upright actor capsule is .38 m. Inflate the full wheel/ballast envelope once;
    // the optional .15 m artist margin is not added on top of this body clearance.
    public const int BodyClearanceMillimetres=380;
    public static IEnumerable<GridCell> SolidCells()
    {
        var low=TraversalGrid.WorldToCell(MinX-BodyClearanceMillimetres,MinZ-BodyClearanceMillimetres);
        var high=TraversalGrid.WorldToCell(MaxX+BodyClearanceMillimetres,MaxZ+BodyClearanceMillimetres);
        for(var z=low.Z;z<=high.Z;z++)for(var x=low.X;x<=high.X;x++)yield return new(x,z);
    }
    public static bool SweepClear(int fromX,int fromZ,int toX,int toZ)
    {
        var enter=0.0;var leave=1.0;
        bool Clip(int from,int to,int low,int high)
        {
            var delta=to-from;if(delta==0)return from>=low&&from<=high;
            var a=(double)(low-from)/delta;var b=(double)(high-from)/delta;
            enter=Math.Max(enter,Math.Min(a,b));leave=Math.Min(leave,Math.Max(a,b));return enter<=leave;
        }
        return !(Clip(fromX,toX,MinX-BodyClearanceMillimetres,MaxX+BodyClearanceMillimetres)&&
            Clip(fromZ,toZ,MinZ-BodyClearanceMillimetres,MaxZ+BodyClearanceMillimetres));
    }
}

public sealed partial class GameSession
{
    public const int CurrentBarnGeometryVersion=1;
    /// <summary>Zero retains pre-tractor saved geometry and immutable historical fixtures exactly.</summary>
    public int BarnGeometryVersion {get;private set;}
    public bool BarnTractorPresent=>BarnGeometryVersion==CurrentBarnGeometryVersion&&!BarnStageOpen;
    /// <summary>Explicit production new-game boundary; never upgrades a loaded/live simulation.</summary>
    public GameSession WithCurrentBarnGeometry()
    {
        if(BarnGeometryVersion==CurrentBarnGeometryVersion)return this;
        if(_preparation is not {Status:PreparationStatus.Preparing} p||p.Plan?.Committed==true||
            CurrentTick!=0||_traversalGrid is not null||_navigationAgents.Count!=0)
            throw new InvalidOperationException("Current barn geometry requires a fresh, unstarted preparation.");
        if(ValidateBuildLayout(p.BuildPlacements,Stages,_equipment,p.WaterTowerOwned,p.Tier,IsClassicCampaign,CurrentBarnGeometryVersion) is {} issue)
            throw new InvalidOperationException(issue);
        BarnGeometryVersion=CurrentBarnGeometryVersion;
        lock(LayoutFitLock)_lastLayoutFit=null;
        return this;
    }
    private static string? ValidateBarnGeometry(SessionPersistenceSnapshot s)
    {
        if(s.BarnGeometryVersion is <0 or >CurrentBarnGeometryVersion||s.BarnGeometryVersion>0&&s.Preparation is null)
            return "Barn geometry revision invalid.";
        if(s.BarnGeometryVersion==0||s.TraversalGrid is null)return null;
        var tractor=!SavedStages(s).Any(stage=>stage.Id==FestivalStages.BarnId);
        var cells=s.TraversalGrid.Cells.ToDictionary(c=>new GridCell(c.X,c.Z));
        if(BarnTractorLayout.SolidCells().Any(cell=>!cells.TryGetValue(cell,out var value)||value.IsWalkable==tractor))
            return "Barn tractor geometry does not match its active venue state.";
        return null;
    }
}

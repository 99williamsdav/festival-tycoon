namespace Festival.Simulation;

public sealed partial class GameSession
{
    private static ImmersionVendor NewLooseVendor(ImmersionVendor vendor)=>vendor with { QueueCells=[ImmersionServiceCell(vendor)] };
    private static GridCell[] VendorQueueCells(ImmersionVendor vendor,ImmersionSnapshot immersion)=>vendor.QueueCells ??
        Enumerable.Range(0,Math.Min(10,immersion.People.Count(person=>person.VendorId==vendor.Id)+1)).Select(index=>ImmersionQueueCell(vendor,index)).ToArray();
    // Legacy vendors without saved geometry size their queue from current shoppers.
    private GridCell[] VendorQueueCells(ImmersionVendor vendor)=>vendor.QueueCells ?? VendorQueueCells(vendor,ImmersionView!);
    /// <summary>The food and drink vendors, in their stable order.</summary>
    public IReadOnlyList<ImmersionVendor> CaptureVendors() => Vendors;
    public IReadOnlyList<GridCell> CaptureImmersionQueueCells(string vendorId)=>_immersion is null?[]:VendorQueueCells(Vendors.Single(vendor=>vendor.Id==vendorId)).ToArray();
    private GridCell[] ImmersionQueueCorridor(string? except=null)=>_immersion is null?[]:Vendors.Where(vendor=>vendor.Id!=except)
        .SelectMany(vendor=>LooseQueueGeometry.Corridor(VendorQueueCells(vendor)))
        .Concat(ToiletQueueCorridor()).ToArray();
    /// <summary>
    /// Where a queue may grow now: its fixed ground, and audience ground only where nobody's watching from. A queue never
    /// grows within two cells of a listener's place or of someone standing listening, so a busy crowd keeps its ground.
    /// </summary>
    private Func<GridCell,bool> QueueGrowthAllowed()
    {
        HashSet<GridCell>? inUse=null;
        return cell=>QueueGroundAllowed(cell,_preparation) && (!InAudienceArea(cell) || !(inUse??=AudienceGroundInUse()).Contains(cell));
    }
    private HashSet<GridCell> AudienceGroundInUse()
    {
        var used=new HashSet<GridCell>();
        if(_livePerformance is null)return used;
        void Around(GridCell centre){for(var dx=-2;dx<=2;dx++)for(var dz=-2;dz<=2;dz++)used.Add(new(centre.X+dx,centre.Z+dz));}
        foreach(var listener in _livePerformance.Listeners)
        {
            if(PersonIn(PersonView.Roster,listener.AgentId) is not { Departed:false })continue;
            if(listener.Place is { } place)Around(place);
            if(_navigationAgents.TryGetValue(new(listener.AgentId),out var nav) && nav.IntentId?.StartsWith("performance.listen",StringComparison.Ordinal)==true)
                Around(TraversalGrid.WorldToCell(nav.XMillimetres,nav.ZMillimetres));
        }
        return used;
    }
    /// <summary>Every queue cell (with its corridor), for listeners choosing where to stand.</summary>
    private HashSet<GridCell> AllQueueGround()=>WaterPoints().SelectMany(point=>LooseQueueGeometry.Corridor(CaptureWaterQueueCells(point.Id)))
        .Concat(ImmersionQueueCorridor()).ToHashSet();
    // Queues always keep off the stage approach and backstage; audience ground is decided as they grow.
    private static bool QueueGroundAllowed(GridCell cell,PreparationSnapshot? prep)=>!Backstage.StageReserve(cell) && !Backstage.Area(cell) &&
        !(Math.Abs(cell.X-ResponsePost(prep,ResponseRole.Medic).Cell.X)<=3 && Math.Abs(cell.Z-ResponsePost(prep,ResponseRole.Medic).Cell.Z)<=3) &&
        (StewardPostPlacement(prep) is not { } steward || !(Math.Abs(cell.X-steward.Cell.X)<=2 && Math.Abs(cell.Z-steward.Cell.Z)<=2)) &&
        (FirstAidPlacement(prep) is null || !new[]{ResponsePostHome(prep,ResponseRole.Medic),ResponsePostHome(prep,ResponseRole.Medic,true)}.Contains(cell)) &&
        (StewardPostPlacement(prep) is null || !new[]{ResponsePostHome(prep,ResponseRole.Steward),ResponsePostHome(prep,ResponseRole.Steward,true)}.Contains(cell)) &&
        !(Math.Abs(cell.X-MedicalRestCell.X)<=1&&Math.Abs(cell.Z-MedicalRestCell.Z)<=1) &&
        !(Math.Abs(cell.X-ResponsePostHome(prep,ResponseRole.Medic).X)<=1&&Math.Abs(cell.Z-ResponsePostHome(prep,ResponseRole.Medic).Z)<=1);
    private void GrowImmersionQueues()
    {
        if(_immersion is null)return;
        foreach(var vendor in Vendors.ToArray())
        {
            if(vendor.QueueCells is null)continue; // exact legacy occupied and approaching geometry
            var wanted=Math.Min(10,PeopleIn(PersonView.Consumption).Count(person=>person.VendorId==vendor.Id)+1);
            var cells=vendor.QueueCells.Take(wanted).ToList();if(cells.Count==0)cells.Add(ImmersionServiceCell(vendor));
            if(cells.Count<wanted)
            {
                // Other queues' corridors only matter when this queue has to grow.
                var reserved=WaterPoints().SelectMany(point=>LooseQueueGeometry.Corridor(CaptureWaterQueueCells(point.Id))).Concat(ImmersionQueueCorridor(vendor.Id)).ToArray();
                var allowed=QueueGrowthAllowed();
                while(cells.Count<wanted)
                {
                    var next=LooseQueueGeometry.Extend("vendor."+vendor.Id,cells,RotateWaterOffset(new(0,1),vendor.QuarterTurns),_traversalGrid!,allowed,reserved);
                    if(next is null)break;cells.Add(next.Value);
                }
            }
            if(!cells.SequenceEqual(vendor.QueueCells))SetImmersionVendor(vendor with { QueueCells=cells.ToArray() });
        }
    }
}

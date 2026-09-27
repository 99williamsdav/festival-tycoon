namespace Festival.Simulation;

public sealed partial class GameSession
{
    private static ImmersionVendor NewLooseVendor(ImmersionVendor vendor)=>vendor with { QueueCells=[ImmersionServiceCell(vendor)] };
    private static GridCell[] VendorQueueCells(ImmersionVendor vendor,ImmersionSnapshot immersion)=>vendor.QueueCells ??
        Enumerable.Range(0,Math.Min(10,immersion.People.Count(person=>person.VendorId==vendor.Id)+1)).Select(index=>ImmersionQueueCell(vendor,index)).ToArray();
    public IReadOnlyList<GridCell> CaptureImmersionQueueCells(string vendorId)=>_immersion is null?[]:VendorQueueCells(_immersion.Vendors.Single(vendor=>vendor.Id==vendorId),_immersion).ToArray();
    private GridCell[] ImmersionQueueCorridor(string? except=null)=>_immersion is null?[]:_immersion.Vendors.Where(vendor=>vendor.Id!=except).SelectMany(vendor=>LooseQueueGeometry.Corridor(VendorQueueCells(vendor,_immersion))).ToArray();
    private static bool QueueGroundAllowed(GridCell cell,PreparationSnapshot? prep)=>!(cell.X is >=90 and <=101 && cell.Z is >=139 and <=160) &&
        !(Math.Abs(cell.X-ResponsePost(prep,ResponseRole.Medic).Cell.X)<=3 && Math.Abs(cell.Z-ResponsePost(prep,ResponseRole.Medic).Cell.Z)<=3) &&
        (prep?.StewardPostPlacement is null || !(Math.Abs(cell.X-prep.StewardPostPlacement.Cell.X)<=2 && Math.Abs(cell.Z-prep.StewardPostPlacement.Cell.Z)<=2)) &&
        (prep?.FirstAidPlacement is null || !new[]{ResponsePostHome(prep,ResponseRole.Medic),ResponsePostHome(prep,ResponseRole.Medic,true)}.Contains(cell)) &&
        (prep?.StewardPostPlacement is null || !new[]{ResponsePostHome(prep,ResponseRole.Steward),ResponsePostHome(prep,ResponseRole.Steward,true)}.Contains(cell)) &&
        !(Math.Abs(cell.X-MedicalRestCell.X)<=1&&Math.Abs(cell.Z-MedicalRestCell.Z)<=1) &&
        !(Math.Abs(cell.X-ResponsePostHome(prep,ResponseRole.Medic).X)<=1&&Math.Abs(cell.Z-ResponsePostHome(prep,ResponseRole.Medic).Z)<=1);
    private void GrowImmersionQueues()
    {
        if(_immersion is null)return;
        foreach(var vendor in _immersion.Vendors.ToArray())
        {
            if(vendor.QueueCells is null)continue; // exact legacy occupied and approaching geometry
            var wanted=Math.Min(10,_immersion.People.Count(person=>person.VendorId==vendor.Id)+1);
            var cells=vendor.QueueCells.Take(wanted).ToList();if(cells.Count==0)cells.Add(ImmersionServiceCell(vendor));
            var reserved=WaterPoints().SelectMany(point=>LooseQueueGeometry.Corridor(CaptureWaterQueueCells(point.Id))).Concat(ImmersionQueueCorridor(vendor.Id)).ToArray();
            while(cells.Count<wanted)
            {
                var next=LooseQueueGeometry.Extend("vendor."+vendor.Id,cells,RotateWaterOffset(new(0,1),vendor.QuarterTurns),_traversalGrid!,cell=>QueueGroundAllowed(cell,_preparation),reserved);
                if(next is null)break;cells.Add(next.Value);
            }
            SetImmersionVendor(vendor with { QueueCells=cells.ToArray() });
        }
    }
}

namespace Festival.Simulation;

/// <summary>Deterministic, bounded slot geometry. Existing prefixes never move when a tail grows.</summary>
internal static class LooseQueueGeometry
{
    private static readonly int[] Sides = [0,0,0,1,1,1,1,0,0,0,-1,-1,-1,-1,0,0,1,1,1,2,2,1,1,0];
    internal static GridCell[] Corridor(IReadOnlyList<GridCell> cells)
    {
        var result=new HashSet<GridCell>(cells);
        for(var index=1;index<cells.Count;index++)
        {
            var from=TraversalGrid.CellCentre(cells[index-1]);var to=TraversalGrid.CellCentre(cells[index]);
            var steps=Math.Max(Math.Abs(to.XMillimetres-from.XMillimetres),Math.Abs(to.ZMillimetres-from.ZMillimetres))/250;
            for(var step=0;step<=steps;step++)result.Add(TraversalGrid.WorldToCell(from.XMillimetres+(to.XMillimetres-from.XMillimetres)*step/Math.Max(1,steps),from.ZMillimetres+(to.ZMillimetres-from.ZMillimetres)*step/Math.Max(1,steps)));
        }
        return result.ToArray();
    }
    internal static GridCell? Extend(string key,IReadOnlyList<GridCell> cells,GridCell forward,TraversalGrid grid,Func<GridCell,bool> allowed,IReadOnlyCollection<GridCell> reserved)
    {
        uint hash=2166136261;foreach(var character in key)hash=(hash^character)*16777619;
        var side=new GridCell(forward.Z,-forward.X);var tail=cells[^1];var origin=cells[0];
        var index=cells.Count;var desired=Sides[Math.Min(Sides.Length-1,index+(int)(hash%3))]*(hash%2==0?1:-1);
        var lateral=(tail.X-origin.X)*side.X+(tail.Z-origin.Z)*side.Z;
        var drift=Math.Clamp(desired-lateral,-1,1);var gap=(hash+(uint)index*17)%5==0?3:2;
        var previous=index>1?cells[^2]:new GridCell(tail.X-forward.X,tail.Z-forward.Z);
        var heading=new GridCell(Math.Sign(tail.X-previous.X),Math.Sign(tail.Z-previous.Z));
        var deltas=new[] { new GridCell(forward.X*gap+side.X*drift,forward.Z*gap+side.Z*drift),
            new GridCell(heading.X*2,heading.Z*2),new GridCell(forward.X*2,forward.Z*2),
            new GridCell(forward.X*2+side.X,forward.Z*2+side.Z),new GridCell(forward.X*2-side.X,forward.Z*2-side.Z),
            new GridCell(forward.X*2+side.X*2,forward.Z*2+side.Z*2),new GridCell(forward.X*2-side.X*2,forward.Z*2-side.Z*2),
            new GridCell(side.X*2,side.Z*2),new GridCell(-side.X*2,-side.Z*2) }.Distinct();
        var earlier=Corridor(cells.Take(Math.Max(0,index-1)).ToArray());
        foreach(var delta in deltas)
        {
            if(delta.X*forward.X+delta.Z*forward.Z<0 || delta.X*heading.X+delta.Z*heading.Z<0)continue;
            var candidate=new GridCell(tail.X+delta.X,tail.Z+delta.Z);var segment=Corridor([tail,candidate]);
            if(segment.Any(cell=>!grid.Contains(cell)||!grid.Get(cell).IsWalkable||!allowed(cell)||reserved.Any(other=>Math.Abs(other.X-cell.X)<=1&&Math.Abs(other.Z-cell.Z)<=1)) ||
                segment.Skip(1).Any(cell=>earlier.Any(other=>Math.Abs(other.X-cell.X)<=1&&Math.Abs(other.Z-cell.Z)<=1)))continue;
            var from=TraversalGrid.CellCentre(tail);var to=TraversalGrid.CellCentre(candidate);
            if(!TraversalSweep.IsWalkable(grid,from.XMillimetres,from.ZMillimetres,to.XMillimetres,to.ZMillimetres))continue;
            return candidate;
        }
        return null;
    }
    internal static bool Valid(IReadOnlyList<GridCell> cells,GridCell forward,TraversalGrid grid,IReadOnlyCollection<GridCell> reserved)
    {
        if(cells.Distinct().Count()!=cells.Count||cells.Any(cell=>!grid.Contains(cell)||!grid.Get(cell).IsWalkable||reserved.Any(other=>Math.Abs(other.X-cell.X)<=1&&Math.Abs(other.Z-cell.Z)<=1)))return false;
        for(var index=1;index<cells.Count;index++)
        {
            var previous=cells[index-1];var cell=cells[index];var dx=cell.X-previous.X;var dz=cell.Z-previous.Z;
            if(Math.Max(Math.Abs(dx),Math.Abs(dz)) is <2 or >3 || dx*forward.X+dz*forward.Z<0)return false;
            if(index>1 && dx*(previous.X-cells[index-2].X)+dz*(previous.Z-cells[index-2].Z)<0)return false;
            var earlier=Corridor(cells.Take(index-1).ToArray());var segment=Corridor([previous,cell]);
            if(segment.Any(part=>reserved.Any(other=>Math.Abs(other.X-part.X)<=1&&Math.Abs(other.Z-part.Z)<=1)) ||
                segment.Skip(1).Any(part=>earlier.Any(other=>Math.Abs(other.X-part.X)<=1&&Math.Abs(other.Z-part.Z)<=1)))return false;
            var from=TraversalGrid.CellCentre(previous);var to=TraversalGrid.CellCentre(cell);
            if(!TraversalSweep.IsWalkable(grid,from.XMillimetres,from.ZMillimetres,to.XMillimetres,to.ZMillimetres))return false;
        }
        return true;
    }
}

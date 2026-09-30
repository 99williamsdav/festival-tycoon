namespace Festival.Simulation;

public sealed record ResponsePostPlacement(GridCell Cell, int QuarterTurns);

public sealed partial class GameSession
{
    public static ResponsePostPlacement ResponsePost(PreparationSnapshot? prep, ResponseRole role) => role == ResponseRole.Medic
        ? prep?.FirstAidPlacement ?? new(MedicalTentCell, 0)
        : prep?.StewardPostPlacement ?? new(DisorderSecurityPostCell, 1);
    public ResponsePostPlacement CaptureResponsePost(ResponseRole role) => ResponsePost(_preparation, role);
    public static GridCell ResponsePostHome(PreparationSnapshot? prep, ResponseRole role, bool hired = false)
    {
        // Absent records retain the exact legacy homes for compatible saved routes.
        if ((role == ResponseRole.Medic ? prep?.FirstAidPlacement : prep?.StewardPostPlacement) is null)
        {
            var old = role == ResponseRole.Medic ? MedicalMedicCell : DisorderSecurityBaseCell;
            return hired ? new(old.X + 2, old.Z) : old;
        }
        var post = ResponsePost(prep, role);
        var offset = RotateWaterOffset(new(hired ? 2 : 0, role == ResponseRole.Medic ? 6 : 5), post.QuarterTurns);
        return new(post.Cell.X + offset.X, post.Cell.Z + offset.Z);
    }
    public static GridCell[] ResponsePostFootprint(ResponsePostPlacement post, ResponseRole role)
    {
        var radius = role == ResponseRole.Medic ? 3 : 2;
        return Enumerable.Range(-radius, radius * 2 + 1).SelectMany(x => Enumerable.Range(-radius, radius * 2 + 1)
            .Select(z => { var d = RotateWaterOffset(new(x, z), post.QuarterTurns); return new GridCell(post.Cell.X + d.X, post.Cell.Z + d.Z); })).ToArray();
    }
    private static IEnumerable<GridCell> ResponsePostReserved(PreparationSnapshot? p) => Enum.GetValues<ResponseRole>()
        .SelectMany(role => ResponsePostFootprint(ResponsePost(p, role), role)
            .Concat(new[] { ResponsePostHome(p, role), ResponsePostHome(p, role, true) }));

    private static bool PlacementAccessClear(PreparationSnapshot p,EquipmentSnapshot? equipment,ImmersionSnapshot? immersion,MedicalSnapshot? medical)
    {
        var blocked=new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain()).Overrides.ToDictionary(c=>c.Key,c=>c.Value);
        void Block(GridCell centre,int rx,int rz){for(var x=centre.X-rx;x<=centre.X+rx;x++)for(var z=centre.Z-rz;z<=centre.Z+rz;z++){var c=new GridCell(x,z);blocked[c]=new(c,GroundSurface.Grass,false);}}
        Block(ResponsePost(p,ResponseRole.Medic).Cell,3,3);
        if(p.StewardPostPlacement is {} steward)Block(steward.Cell,2,2);
        var points=new[]{new WaterPointState("water.main",p.PrimaryWaterCell,[],[],null,0){QuarterTurns=p.PrimaryWaterQuarterTurns,GeometryVersion=p.PrimaryWaterGeometryVersion}}
            .Concat(EffectiveWaterPlacements(p).Select(w=>new WaterPointState(w.Id,w.Cell,[],[],null,0){QuarterTurns=w.QuarterTurns,GeometryVersion=w.GeometryVersion})).ToArray();
        foreach(var water in points)Block(water.Cell,WaterFootprintRadius(water),WaterFootprintRadius(water));
        if(p.WaterTowerOwned)Block(WaterTowerCell,3,3);
        if(equipment is {} unit){var lo=TraversalGrid.WorldToCell(unit.XMillimetres-1500,unit.ZMillimetres-1000);var hi=TraversalGrid.WorldToCell(unit.XMillimetres+1500,unit.ZMillimetres+1000);for(var x=lo.X;x<=hi.X;x++)for(var z=lo.Z;z<=hi.Z;z++){var c=new GridCell(x,z);blocked[c]=new(c,GroundSurface.Grass,false);}}
        for(var x=91;x<=100;x++)for(var z=140;z<=159;z++){var c=new GridCell(x,z);blocked[c]=new(c,GroundSurface.Grass,x is >=92 and <=98 && z is >=143 and <=157 || x is >=98 and <=100 && z is >=156 and <=158);}
        if(immersion is not null)foreach(var c in immersion.Vendors.SelectMany(ImmersionFootprint))blocked[c]=new(c,GroundSurface.Grass,false);
        foreach(var toilet in EffectiveToilets(immersion))
            foreach(var c in ToiletSolidCells(toilet))blocked[c]=new(c,GroundSurface.Grass,false);
        var grid=new TraversalGrid(blocked.Values);
        var access=Enum.GetValues<ResponseRole>().SelectMany(role=>new[]{ResponsePostHome(p,role),ResponsePostHome(p,role,true)})
            .Append(MedicalRestCell).Concat(points.Select(WaterPointServiceCell)).Concat(immersion?.Vendors.Select(ImmersionServiceCell)??[])
            .Concat(EffectiveToilets(immersion).SelectMany(accessToilet => new[] { ToiletInsideCell(accessToilet), ToiletQueueCell(accessToilet,0), ToiletExitCell(accessToilet) }))
            .Concat(medical is null?[]:medical.ExtraWaterPoints.SelectMany(w=>w.QueueCells));
        return access.All(c=>DeterministicPathfinder.FindPath(grid,MedicalExitCell,c).Found);
    }
    private static string? ValidatePersistedResponsePosts(SessionPersistenceSnapshot s)
    {
        if(s.Preparation is not { } p)return null;
        foreach(var role in Enum.GetValues<ResponseRole>())
        {
            var placement=role==ResponseRole.Medic?p.FirstAidPlacement:p.StewardPostPlacement;
            if(placement is null)continue;
            if(s.Medical is null || s.Disorder is null || placement.QuarterTurns is <0 or >3)return "Saved response post identity or orientation invalid.";
            if (!p.BuildPlacements.Any(item => item.Kind == (role == ResponseRole.Medic ? BuildServiceKind.FirstAid : BuildServiceKind.StewardPost) &&
                item.Cell == placement.Cell && item.QuarterTurns == placement.QuarterTurns))
                return "Saved response post differs from the build layout.";
            if(p.Status!=PreparationStatus.Preparing && (s.TraversalGrid is null || ResponsePostFootprint(placement,role).Any(c=>!s.TraversalGrid.Cells.Any(saved=>saved.X==c.X && saved.Z==c.Z && !saved.IsWalkable))))return "Saved response post solid footprint missing.";
        }
        return null;
    }
}

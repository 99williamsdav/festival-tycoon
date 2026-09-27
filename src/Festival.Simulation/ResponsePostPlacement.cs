namespace Festival.Simulation;

public sealed record ResponsePostPlacement(GridCell Cell, int QuarterTurns);
public sealed record MoveResponsePostCommand(ResponseRole Role, GridCell Cell, int QuarterTurns = 0) : SessionCommand;

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

    private CommandResult? ValidateResponsePostPlacement(EntityId? target, MoveResponsePostCommand command)
    {
        if (target is not null || _medical is null || _disorder is null || _preparation is not { Status: PreparationStatus.Preparing })
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "Response posts can be moved only during preparation.");
        if (!Enum.IsDefined(command.Role) || command.QuarterTurns is < 0 or > 3)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Unknown response post or orientation.");
        var issue = ResponsePostPlacementError(_preparation, _equipment, _immersion, _medical, command);
        return issue is null ? null : CommandResult.Rejected(CommandReasonCode.InvalidParameter, issue);
    }
    private static PreparationSnapshot WithResponsePost(PreparationSnapshot p, MoveResponsePostCommand c) => c.Role == ResponseRole.Medic
        ? p with { FirstAidPlacement = new(c.Cell, c.QuarterTurns) } : p with { StewardPostPlacement = new(c.Cell, c.QuarterTurns) };
    private void ApplyResponsePostPlacement(MoveResponsePostCommand command) => _preparation = WithResponsePost(_preparation!, command);

    private static string? ResponsePostPlacementError(PreparationSnapshot p, EquipmentSnapshot? equipment, ImmersionSnapshot? immersion,
        MedicalSnapshot? medical, MoveResponsePostCommand command)
    {
        var candidate = WithResponsePost(p, command);
        var post = ResponsePost(candidate, command.Role);
        var solid = ResponsePostFootprint(post, command.Role);
        var homes = new[] { ResponsePostHome(candidate, command.Role), ResponsePostHome(candidate, command.Role, true) };
        var terrain = new TraversalGrid(Fixtures.NavigationFixture.CreateLowerWitteringTerrain());
        if (solid.Concat(homes).Any(c => c.X is < 68 or > 190 || c.Z is < 108 or > 190 || !terrain.Contains(c) ||
            terrain.Get(c) is not { IsWalkable: true, Surface: GroundSurface.Grass }))
            return "The post footprint and staff fronts need clear grass inside the festival field.";
        var otherRole = command.Role == ResponseRole.Medic ? ResponseRole.Steward : ResponseRole.Medic;
        var reserved = ResponsePostFootprint(ResponsePost(p, otherRole), otherRole)
            .Concat(new[] { ResponsePostHome(p, otherRole), ResponsePostHome(p, otherRole, true), MedicalRestCell }).ToHashSet();
        for(var x=MedicalRestCell.X-1;x<=MedicalRestCell.X+1;x++)for(var z=MedicalRestCell.Z-1;z<=MedicalRestCell.Z+1;z++)reserved.Add(new(x,z));
        foreach (var point in new[] { new WaterPointState("water.main", p.PrimaryWaterCell, [], [], null, 0) { QuarterTurns=p.PrimaryWaterQuarterTurns, GeometryVersion=p.PrimaryWaterGeometryVersion } }
            .Concat(medical?.ExtraWaterPoints ?? []))
        {
            var radius = WaterFootprintRadius(point)+1;
            for (var x=point.Cell.X-radius;x<=point.Cell.X+radius;x++) for(var z=point.Cell.Z-radius;z<=point.Cell.Z+radius;z++) reserved.Add(new(x,z));
            foreach(var cell in point.QueueCells.Length > 0 ? LooseQueueGeometry.Corridor(point.QueueCells) : new[] { WaterPointServiceCell(point) }) reserved.Add(cell);
        }
        for(var x=90;x<=101;x++)for(var z=139;z<=160;z++)reserved.Add(new(x,z));
        if(equipment is { } unit) {var centre=TraversalGrid.WorldToCell(unit.XMillimetres,unit.ZMillimetres);for(var x=centre.X-5;x<=centre.X+5;x++)for(var z=centre.Z-5;z<=centre.Z+5;z++)reserved.Add(new(x,z));}
        if(p.WaterTowerOwned)for(var x=WaterTowerCell.X-4;x<=WaterTowerCell.X+4;x++)for(var z=WaterTowerCell.Z-4;z<=WaterTowerCell.Z+4;z++)reserved.Add(new(x,z));
        if(immersion is not null)foreach(var vendor in immersion.Vendors)foreach(var c in ImmersionFootprint(vendor).Concat(LooseQueueGeometry.Corridor(VendorQueueCells(vendor,immersion))))reserved.Add(c);
        if(solid.Concat(homes).Any(reserved.Contains))return "The post or staff fronts overlap a building, service, physical queue or protected route.";
        // Reuse the tap's exact frontage/padded response reservations as well as
        // the post's solid geometry. An accepted move must survive normal restore.
        foreach(var water in new[]{new WaterPlacement("water.main",p.PrimaryWaterCell){QuarterTurns=p.PrimaryWaterQuarterTurns,GeometryVersion=p.PrimaryWaterGeometryVersion}}.Concat(EffectiveWaterPlacements(p)))
            if(ValidateWaterPlacementCell(water.Cell,candidate,water.Id,equipment,water.QuarterTurns,water.GeometryVersion) is not null)
                return "The post blocks a tap footprint or protected service front.";
        if(medical is not null)
        {
            var points=new[]{new WaterPointState("water.main",p.PrimaryWaterCell,medical.WaterQueue,medical.WaterOverflow,medical.WaterOwnerId,medical.WaterDrinkTicks){QuarterTurns=p.PrimaryWaterQuarterTurns,GeometryVersion=p.PrimaryWaterGeometryVersion,QueueCells=medical.MainWaterQueueCells}}.Concat(medical.ExtraWaterPoints).ToArray();
            if(!ValidSavedWaterGeometry(points,null,candidate))return "The post blocks existing water queue geometry.";
        }
        if(immersion is not null)
        {
            // Apply the existing reciprocal vendor and loose-line rules to the
            // proposed preparation snapshot; this scratch session executes no ticks.
            var geometry=new GameSession(1,new CampaignId(1)){_preparation=candidate,_medical=medical,_immersion=immersion,_equipment=equipment};
            foreach(var vendor in immersion.Vendors)
                if(geometry.ImmersionPlacementError(vendor) is not null || vendor.QueueCells is not null && LooseQueueGeometry.Corridor(vendor.QueueCells).Any(c=>!QueueGroundAllowed(c,candidate)))
                    return "The post blocks a vendor footprint or existing physical queue geometry.";
        }
        if(!PlacementAccessClear(candidate,equipment,immersion,medical))return "This position blocks access to a response post or essential service.";
        return null;
    }
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
        var grid=new TraversalGrid(blocked.Values);
        var access=Enum.GetValues<ResponseRole>().SelectMany(role=>new[]{ResponsePostHome(p,role),ResponsePostHome(p,role,true)})
            .Append(MedicalRestCell).Concat(points.Select(WaterPointServiceCell)).Concat(immersion?.Vendors.Select(ImmersionServiceCell)??[])
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
            var issue=ResponsePostPlacementError(p,s.Equipment,s.Immersion,s.Medical,new(role,placement.Cell,placement.QuarterTurns));
            if(issue is not null)return "Saved response post invalid: "+issue;
            if(p.Status!=PreparationStatus.Preparing && (s.TraversalGrid is null || ResponsePostFootprint(placement,role).Any(c=>!s.TraversalGrid.Cells.Any(saved=>saved.X==c.X && saved.Z==c.Z && !saved.IsWalkable))))return "Saved response post solid footprint missing.";
        }
        return null;
    }
}

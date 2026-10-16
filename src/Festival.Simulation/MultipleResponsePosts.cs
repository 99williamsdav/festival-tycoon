namespace Festival.Simulation;

public sealed record ResponsePostSite(string Id, ResponseRole Role, ResponsePostPlacement Placement, int AssignedWorkers);

public sealed partial class GameSession
{
    private static IEnumerable<BuildPlacement> ExtraResponsePostPlacements(PreparationSnapshot? p) =>
        (p?.BuildPlacements??[]).Where(b=>b.Kind is BuildServiceKind.FirstAid or BuildServiceKind.StewardPost && b.Id.Contains(".extra-",StringComparison.Ordinal));
    private static bool ExtraResponseGroundReserved(PreparationSnapshot? p,GridCell cell) =>
        ExtraResponsePostPlacements(p).Any() && ResponseGroundReserved(p,cell);
    private static IEnumerable<BuildPlacement> PlacedResponsePosts(PreparationSnapshot? p, ResponseRole role) =>
        (p?.BuildPlacements ?? []).Where(b => b.Kind == (role == ResponseRole.Medic ? BuildServiceKind.FirstAid : BuildServiceKind.StewardPost))
            .OrderBy(OrdinalOfKind);
    private static ResponsePostPlacement PostPlacement(BuildPlacement p) => new(p.Cell,p.QuarterTurns);
    private static GridCell[] FirstAidRestCentres(IEnumerable<BuildPlacement> placements) => placements
        .Where(p=>p.Kind==BuildServiceKind.FirstAid).OrderBy(OrdinalOfKind).Select(p=>RestCentreFor(PostPlacement(p))).ToArray();
    private BuildPlacement[]? _restCentreLayout;
    private IReadOnlyList<GridCell>? _restCentreCache;
    public IReadOnlyList<GridCell> CaptureRestCentres()
    {
        var layout=_preparation?.BuildPlacements;
        if(_restCentreCache is null || !ReferenceEquals(layout,_restCentreLayout))
        {
            var centres=FirstAidRestCentres(layout??[]);
            _restCentreLayout=layout;
            _restCentreCache=Array.AsReadOnly(centres.Length==0?[MedicalRestCell]:centres);
        }
        return _restCentreCache;
    }
    private bool ReachableFirstAidRest(ulong id) => CaptureRestCentres().Any(centre => MedicalRouteExists(id,centre));
    public IReadOnlyList<ResponsePostSite> CaptureResponsePosts() => Enum.GetValues<ResponseRole>()
        .SelectMany(role=>PlacedResponsePosts(_preparation,role).Select(p=>new ResponsePostSite(p.Id,role,PostPlacement(p),
            GetResponseStaff().Count(w=>w.Role==role && AssignedResponsePostId(w.AgentId,role)==p.Id)))).ToArray();
    private string? AssignedResponsePostId(ulong id,ResponseRole role)
    {
        var baseline=role==ResponseRole.Medic?_medical?.MedicId:_disorder?.SecurityId;
        var posts=PlacedResponsePosts(_preparation,role).ToArray();
        if(posts.Length==0)return null;
        // The baseline worker keeps the first post; the rest take the next ones in id order, so a third hire
        // would staff a third post rather than doubling up on the second.
        var order=GetResponseStaff().Where(w=>w.Role==role).Select(w=>w.AgentId).OrderBy(w=>w!=baseline).ThenBy(w=>w).ToList();
        var rank=Math.Max(0,order.IndexOf(id));
        return posts[rank<posts.Length?rank:posts.Length==1?0:1+(rank-1)%(posts.Length-1)].Id;
    }
    private static bool ResponseGroundReserved(PreparationSnapshot? p,GridCell cell)
    {
        foreach(var role in Enum.GetValues<ResponseRole>())
        {
            var placed=PlacedResponsePosts(p,role).ToArray();
            var posts=placed.Length==0?[ResponsePost(p,role)]:placed.Select(PostPlacement).ToArray();
            var radius=role==ResponseRole.Medic?3:2;
            foreach(var post in posts)
                if(Math.Abs(cell.X-post.Cell.X)<=radius && Math.Abs(cell.Z-post.Cell.Z)<=radius)return true;
            if(new[]{ResponsePostHome(p,role),ResponsePostHome(p,role,true)}.Contains(cell))return true;
        }
        return FirstAidRestCentres(p?.BuildPlacements??[]).Any(c=>Math.Abs(cell.X-c.X)<=1 && Math.Abs(cell.Z-c.Z)<=1);
    }

    private void BlockAdditionalResponsePosts()
    {
        var extras=ExtraResponsePostPlacements(_preparation).ToArray();
        if(extras.Length==0)return;
        var terrain=_traversalGrid!.Overrides.ToDictionary(c=>c.Key,c=>c.Value);
        foreach(var p in extras)
        foreach(var cell in ResponsePostFootprint(PostPlacement(p),p.Kind==BuildServiceKind.FirstAid?ResponseRole.Medic:ResponseRole.Steward))
            terrain[cell]=new(cell,GroundSurface.Grass,false);
        _traversalGrid=new TraversalGrid(terrain.Values);
    }
}

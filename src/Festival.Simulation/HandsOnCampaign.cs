namespace Festival.Simulation;

/// <summary>
/// How a campaign is played, fixed when it's created and carried through every festival and retry. A classic
/// campaign is the management game. A hands-on campaign is the same campaign with the player also on the ground
/// as one of the stewards, in first person whenever they like.
/// </summary>
public enum CampaignMode
{
    Classic = 0,
    HandsOn = 1,
}

/// <summary>
/// The hands-on campaign: every festival day has the player's own steward, on top of whoever is hired. They are on
/// the roster from the start (a crew member like the sound engineer's slot), cost no wage and take no hire slot, and
/// from the moment the gates open they are under the player's control through the steward shift's machinery: the
/// held move, hold-to-act actions, the radio's calls, thirst and heat. Closing, a death or a retry hands them back.
/// </summary>
public sealed partial class GameSession
{
    /// <summary>The player's own steward's name on the roster.</summary>
    public const string HandsOnStewardName = "You (steward)";

    private CampaignMode _mode;
    public CampaignMode Mode => _mode;
    public bool IsHandsOn => _mode == CampaignMode.HandsOn;

    /// <summary>The player's own steward in a hands-on campaign, on or off duty; null in a classic one.</summary>
    public ulong? HandsOnStewardId => _mode != CampaignMode.HandsOn ? null : _handsOnStewardId ??=
        PeopleIn(PersonView.Roster).FirstOrDefault(person => person.Role == ProtectedPersonRole.Staff && person.Name == HandsOnStewardName)?.Id;
    // The roster's identities never change within a session, retries included.
    private ulong? _handsOnStewardId;

    private static ulong? HandsOnStewardOf(PreparationSnapshot? p) =>
        p?.People.SingleOrDefault(person => person.Role == ProtectedPersonRole.Staff && person.Name == HandsOnStewardName)?.AgentId;

    /// <summary>A fresh Tier 1 hands-on campaign: the ordinary New Game route with the player also on the ground as a steward.</summary>
    public static GameSession CreateHandsOnCampaign(ulong seed) => CreateBuildCampaign(seed, 1, null, null, mode: CampaignMode.HandsOn);

    /// <summary>
    /// The player's steward joins the roster as a crew member, after every other slot, so a classic campaign's
    /// identities are untouched. Like the baseline steward they have no hunger or loo needs while on duty.
    /// </summary>
    private static void SetUpHandsOnSteward(GameSession session)
    {
        var id = session.NextEntityId++;
        session._wallets.Add(new(id), new WalletState { OwnerId = new(id), CashPennies = 500 });
        session.PreparationView = session.PreparationView! with { People = session.PreparationView.People
            .Append(new EditionPerson(id, HandsOnStewardName, ProtectedPersonRole.Staff, 0)).OrderBy(person => person.AgentId).ToArray() };
        session.MedicalView = session.MedicalView! with { Needs = session.MedicalView.Needs.Append(new MedicalNeed(id, 0, 0, MedicalIntent.WatchShow,
            "Steward on duty", -MedicalDecisionCooldownTicks, null, -1, MedicalNeedProfile.Staff)).OrderBy(need => need.AgentId).ToArray() };
    }

    /// <summary>The gates open: the player's own steward is on duty at the second steward's post, theirs to move.</summary>
    private void StartHandsOnShift()
    {
        if (_mode != CampaignMode.HandsOn || HandsOnStewardId is not { } id) return;
        TakeStewardControl(id);
    }

    /// <summary>
    /// The player steward's abilities: a hired steward's own; the player's in a hands-on campaign are the standard
    /// steward slot's (the festival's baseline calming and confrontation), with the same perk training and drink.
    /// </summary>
    private StaffProfile PlayerStewardProfile(ulong me)
    {
        if (GetResponseStaff().SingleOrDefault(item => item.AgentId == me) is { } hired) return hired;
        var d = _disorder!;
        return EffectiveStaffProfile(new StaffProfile(me, _persons[me].Name, ResponseRole.Steward, GetWalkingSpeedPermille(new(me)), 0, d.CalmingSkill, d.ConfrontationSkill));
    }

    /// <summary>
    /// A hands-on festival's steward, checked against the saved snapshot alone: none while preparing, the player's own
    /// steward on duty while the day runs, and handed back (no movement) once it closes or fails.
    /// </summary>
    private static string? ValidatePersistedHandsOnShift(SessionPersistenceSnapshot snapshot)
    {
        if (snapshot.Preparation is not { } p || snapshot.Disorder is not { } d || snapshot.Medical is null || snapshot.CampaignPlanning is null)
            return "A hands-on campaign needs a festival with stewards.";
        if (HandsOnStewardOf(p) is not { } me) return "A hands-on campaign needs the player's steward on its roster.";
        if (d.Stewards.Any(steward => steward.WorkerId == me) || p.StaffProfiles.Any(profile => profile.AgentId == me))
            return "The player's steward is nobody's hire.";
        var shift = snapshot.StewardShift;
        if (p.Status == PreparationStatus.Preparing)
            return shift is null ? null : "Nobody is on shift before the gates open.";
        if (shift is null) return "A hands-on festival day needs the player's steward.";
        if (shift.Version != StewardShiftRules.Version || shift.FacingDegrees is < 0 or > 359 ||
            shift.MoveX is < -1000 or > 1000 || shift.MoveZ is < -1000 or > 1000 || shift.RemainderX is < -999 or > 999 || shift.RemainderZ is < -999 or > 999)
            return "Steward shift input is invalid.";
        if (snapshot.TraversalGrid is null || snapshot.NavigationAgents is null) return "A hands-on festival day needs the field.";
        if (ValidatePersistedStewardWork(shift, snapshot, p) is { } work) return work;
        if (shift.PlayerId is null)
        {
            if (shift.MoveX != 0 || shift.MoveZ != 0 || shift.Run || shift.RemainderX != 0 || shift.RemainderZ != 0) return "A finished steward shift holds no movement.";
            return p.Status is PreparationStatus.Departing or PreparationStatus.Finished or PreparationStatus.Failed ? null : "A running hands-on day needs the player's steward.";
        }
        if (shift.PlayerId != me) return "The player steward must be the player's own.";
        if (p.Status != PreparationStatus.Running) return "Only a running day has the player's steward on shift.";
        if (p.People.Single(item => item.AgentId == me) is not { Admitted: true, Departed: false }) return "The player's steward must be on duty.";
        if (snapshot.NavigationAgents.SingleOrDefault(item => item.Id == me) is not { } nav || nav.Action != (int)AgentNavigationAction.Idle ||
            nav.Route.Length != 0 || nav.DestinationX is not null || nav.IntentId != StewardShiftRules.Intent)
            return "The player steward's navigation must be their own.";
        var cell = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        var grid = new TraversalGrid(snapshot.TraversalGrid.Cells.Select(item => new TerrainCellOverride(new GridCell(item.X, item.Z),
            (GroundSurface)item.Surface, item.IsWalkable, item.CostPermille, item.ElevationMillimetres, item.SlopePermille)));
        if (!StewardShiftRules.InField(cell) || !grid.Get(cell).IsWalkable) return "The player steward must stand on open ground in the field.";
        return null;
    }
}

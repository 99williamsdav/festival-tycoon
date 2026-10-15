namespace Festival.Simulation;

/// <summary>Campaign rules, independently of the player's control role.</summary>
public enum CampaignRuleset { Survival = 0, Classic = 1 }
public enum PlayerControlRole { Management = 0, Steward = 1 }

/// <summary>Rules, player role and management capability are independent of difficulty.</summary>
public sealed record CampaignIdentity(CampaignRuleset Ruleset, PlayerControlRole ControlRole, bool ManagementCapability)
{
    public static CampaignIdentity Management { get; } = new(CampaignRuleset.Survival,PlayerControlRole.Management,true);
    public static CampaignIdentity HandsOn { get; } = new(CampaignRuleset.Survival,PlayerControlRole.Steward,true);
    public static CampaignIdentity StewardOnly { get; } = new(CampaignRuleset.Survival,PlayerControlRole.Steward,false);
    public static CampaignIdentity ClassicManagement { get; } = new(CampaignRuleset.Classic,PlayerControlRole.Management,true);
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsHandsOn => ControlRole==PlayerControlRole.Steward&&ManagementCapability;
    [System.Text.Json.Serialization.JsonIgnore]
    public bool IsStewardOnly => ControlRole==PlayerControlRole.Steward&&!ManagementCapability;
    // Legacy names are a control adapter, never a ruleset selection.
    internal CampaignMode LegacyMode => IsHandsOn?CampaignMode.HandsOn:Enum.IsDefined(ControlRole)?CampaignMode.Classic:(CampaignMode)ControlRole;
    internal static CampaignIdentity FromLegacy(CampaignMode mode)=>mode switch
    {CampaignMode.Classic=>Management,CampaignMode.HandsOn=>HandsOn,_=>new(CampaignRuleset.Survival,(PlayerControlRole)mode,false)};
    internal static string? Issue(CampaignIdentity? identity)=>identity is null?"Campaign identity is required; an explicit save migration is needed.":
        !Enum.IsDefined(identity.Ruleset)||!Enum.IsDefined(identity.ControlRole)?"Unknown campaign ruleset or player control role.":
        identity.Ruleset==CampaignRuleset.Classic&&identity!=ClassicManagement?"Classic currently requires management control and capability.":
        identity.ControlRole==PlayerControlRole.Management&&!identity.ManagementCapability?"Management control requires management capability.":null;
}

public sealed partial class GameSession
{
    private CampaignIdentity _identity=CampaignIdentity.Management;
    public CampaignIdentity Identity=>_identity;
    public bool IsClassicCampaign=>_identity.Ruleset==CampaignRuleset.Classic;
    public int FestivalNumber=>_campaignPlanning?.EditionNumber??1;
    public bool HasManagementCapability=>_identity.ManagementCapability;
    private FestivalDefinition? _festivalDefinition;
    private bool _definitionTrial;
    private CampaignRuleset _definitionRuleset;
    public FestivalDefinition CurrentFestival
    {
        get
        {
            var tier=_preparation?.Tier??1;
            if(_festivalDefinition is null||_festivalDefinition.Tier!=tier||_definitionTrial!=_pondStageTrial||_definitionRuleset!=_identity.Ruleset)
            {_definitionTrial=_pondStageTrial;_definitionRuleset=_identity.Ruleset;_festivalDefinition=CampaignRulesProfile.For(_identity.Ruleset).Festival(tier,_pondStageTrial);}
            if(IsClassicCampaign&&_weather is not null&&_festivalDefinition.Weather!=Weather)
                _festivalDefinition=_festivalDefinition with{Weather=Weather};
            if(IsClassicCampaign && ClassicBookings is { } booking &&
                (_festivalDefinition.Attendance != booking.TicketHolders || _festivalDefinition.TicketPricePennies != booking.TicketPricePennies))
                _festivalDefinition = _festivalDefinition with { Attendance = booking.TicketHolders, TicketPricePennies = booking.TicketPricePennies };
            return _festivalDefinition;
        }
    }
}

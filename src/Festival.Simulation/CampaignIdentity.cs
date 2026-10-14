namespace Festival.Simulation;

/// <summary>Implemented campaign rules. Classic has no profile or selectable identity yet.</summary>
public enum CampaignRuleset { Survival = 0 }
public enum PlayerControlRole { Management = 0, Steward = 1 }

/// <summary>Rules, player role and management capability are independent of difficulty.</summary>
public sealed record CampaignIdentity(CampaignRuleset Ruleset, PlayerControlRole ControlRole, bool ManagementCapability)
{
    public static CampaignIdentity Management { get; } = new(CampaignRuleset.Survival,PlayerControlRole.Management,true);
    public static CampaignIdentity HandsOn { get; } = new(CampaignRuleset.Survival,PlayerControlRole.Steward,true);
    public static CampaignIdentity StewardOnly { get; } = new(CampaignRuleset.Survival,PlayerControlRole.Steward,false);
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
        identity.ControlRole==PlayerControlRole.Management&&!identity.ManagementCapability?"Management control requires management capability.":null;
}

public sealed partial class GameSession
{
    private CampaignIdentity _identity=CampaignIdentity.Management;
    public CampaignIdentity Identity=>_identity;
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
            return _festivalDefinition;
        }
    }
}

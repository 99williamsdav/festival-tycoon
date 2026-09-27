using System.Text.Json;

namespace Festival.Simulation;

public sealed record PerkDefinition(string Id, string Name, string Effect);
public static class PerkCatalogue
{
    public const string TapId = "water.extra-1";
    public static readonly PerkDefinition[] All = [
        new("extra-pair-of-hands", "Extra Pair of Hands", "+1 steward hiring slot. Hire separately for £30 per weekend."),
        new("doctors-orders", "Doctor's Orders", "+1 medic hiring slot. Hire separately for £30 per weekend."),
        new("another-round", "Another Round", "+1 placeable free-water tap above the guaranteed baseline."),
        new("high-pressure", "High Pressure", "Water tower adds +4 flow to each tap."),
        new("smooth-operators", "Smooth Operators", "All stewards walk faster and gain calming and confrontation skill."),
        new("first-responders", "First Responders", "All medics walk faster and treat more quickly."),
        new("something-in-the-water", "Something in the Water", "Drinking free water improves satisfaction."),
        new("thirsty-crowd", "Thirsty Crowd", "Guests grow thirsty faster.")
    ];
}
// This RNG is deliberately separate from the established stream collection: adding an enum
// member there would change the canonical state and checksums of every legacy save.
public sealed record PerkSnapshot(int Version, string[] Equipped, int DraftAttempt, bool Pending,
    bool RerollUsed, string[] Hand, ulong RandomState, ulong RandomIncrement, ulong Cursor,
    bool Ended, string[] FrozenEffects)
{
    public ulong DraftStartCursor { get; init; }
    public string[] StartingEquipped { get; init; } = [];
    public string[] DrawnHand { get; init; } = [];
    public string? ChosenId { get; init; }
    public string? ReplacedId { get; init; }
    public bool Skipped { get; init; }
}
public abstract record PerkCommand(int DraftAttempt, ulong Cursor) : SessionCommand;
public sealed record RerollPerksCommand(int DraftAttempt, ulong Cursor) : PerkCommand(DraftAttempt, Cursor);
public sealed record ChoosePerkCommand(int DraftAttempt, ulong Cursor, string PerkId, string? ReplaceId = null) : PerkCommand(DraftAttempt, Cursor);
public sealed record SkipPerksCommand(int DraftAttempt, ulong Cursor) : PerkCommand(DraftAttempt, Cursor);

public sealed partial class GameSession
{
    private PerkSnapshot? _perks;
    public PerkSnapshot? CapturePerks() => _perks is null ? null : JsonSerializer.Deserialize<PerkSnapshot>(JsonSerializer.Serialize(_perks));
    internal string? PerkCanonicalJson => _perks is null ? null : JsonSerializer.Serialize(_perks);
    public static GameSession CreatePerkCampaign(ulong seed)
    {
        var session = CreateImmersionCampaign(seed);
        var rng = RandomStreamFactory.Create(seed ^ 0x5045524B44524146UL, RandomStreamId.ArtistDecisions);
        session._perks = new(1, [], 0, false, false, [], rng.State, rng.Increment, 0, false, []);
        session.OpenPerkDraft();
        return session;
    }
    private bool HasPerk(string id) => _perks is { Ended: false } p && p.Equipped.Contains(id);
    private void OpenPerkDraft()
    {
        if (_perks is null) return;
        _perks = _perks with { DraftAttempt = _preparation!.Attempt, Pending = true, RerollUsed = false,
            StartingEquipped = _perks.Equipped, DraftStartCursor = _perks.Cursor, ChosenId = null, ReplacedId = null, Skipped = false };
        DrawPerkHand();
    }
    private void DrawPerkHand()
    {
        var p = _perks!;
        var eligible = PerkCatalogue.All.Select(item => item.Id).Where(id => !p.Equipped.Contains(id)).ToArray();
        var random = new Pcg32Random(p.RandomState, p.RandomIncrement);
        var cursor = p.Cursor;
        for (var index = 0; index < 3; index++)
        {
            var bound = (uint)(eligible.Length - index);
            var threshold = unchecked(0u - bound) % bound;
            uint value;
            do { value = random.NextUInt32(); cursor++; } while (value < threshold);
            var selected = index + (int)(value % bound);
            (eligible[index], eligible[selected]) = (eligible[selected], eligible[index]);
        }
        _perks = p with { Hand = eligible.Take(3).ToArray(), DrawnHand = eligible.Take(3).ToArray(), RandomState = random.State, Cursor = cursor };
    }
    private CommandResult? ValidatePerkCommand(EntityId? target, PerkCommand command)
    {
        if (target is not null || _preparation?.Status != PreparationStatus.Preparing || _perks is not { Pending: true, Ended: false } p)
            return CommandResult.Rejected(CommandReasonCode.WrongPhase, "No festival perk choice is pending.");
        if (command.DraftAttempt != p.DraftAttempt || command.Cursor != p.Cursor)
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "This saved hand has changed; review the current cards.");
        if (command is RerollPerksCommand)
            return p.RerollUsed ? CommandResult.Rejected(CommandReasonCode.AlreadyCommitted, "The free whole-hand reroll was used.") : null;
        if (command is SkipPerksCommand)
            return p.Equipped.Length == 5 ? null : CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Choose one perk while a slot is available.");
        var choice = (ChoosePerkCommand)command;
        if (!p.Hand.Contains(choice.PerkId) || p.Equipped.Contains(choice.PerkId) ||
            (p.Equipped.Length == 5 ? choice.ReplaceId is null || !p.Equipped.Contains(choice.ReplaceId) : choice.ReplaceId is not null))
            return CommandResult.Rejected(CommandReasonCode.InvalidParameter, "Choose an eligible card and, when full, an equipped perk to replace.");
        return null;
    }
    private void ApplyPerkCommand(PerkCommand command)
    {
        if (command is RerollPerksCommand) { _perks = _perks! with { RerollUsed = true }; DrawPerkHand(); return; }
        if (command is ChoosePerkCommand choice)
        {
            _perks = _perks! with { Equipped = _perks.Equipped.Where(id => id != choice.ReplaceId).Append(choice.PerkId).Order(StringComparer.Ordinal).ToArray(), ChosenId = choice.PerkId, ReplacedId = choice.ReplaceId };
            SynchronizePerkEffects();
        }
        _perks = _perks! with { Pending = false, Hand = [], Skipped = command is SkipPerksCommand };
    }
    private void SynchronizePerkEffects()
    {
        var p = _preparation!;
        var removeTap = !HasPerk("another-round");
        _preparation = p with { ExtraMedicSlotOwned = HasPerk("doctors-orders"), ExtraStewardSlotOwned = HasPerk("extra-pair-of-hands"),
            WaterTowerOwned = HasPerk("high-pressure"), RespondersUpgraded = false,
            ExtraWaterSiteIds = removeTap ? p.ExtraWaterSiteIds.Where(id => id != PerkCatalogue.TapId).ToArray() : p.ExtraWaterSiteIds,
            WaterPlacements = removeTap ? p.WaterPlacements.Where(site => site.Id != PerkCatalogue.TapId).ToArray() : p.WaterPlacements };
        if (removeTap) _medical = _medical! with { ExtraWaterPoints = _medical.ExtraWaterPoints.Where(site => site.Id != PerkCatalogue.TapId).ToArray() };
    }
    // Loss retires active perks, but keeps the exact failed-world geometry and derived
    // navigation speeds as immutable evidence. FrozenEffects validate that snapshot and
    // show historical responder abilities; frozen commands/ticks cannot apply new effects.
    private void EndPerkCampaign()
    {
        if (_perks is { Ended: false } p) _perks = p with { Equipped = [], FrozenEffects = p.Equipped, Ended = true, Pending = false, Hand = [] };
    }
    private static bool SavedPerkEffect(PerkSnapshot? p, string id) => p is not null && (p.Ended ? p.FrozenEffects : p.Equipped)?.Contains(id) == true;
    private static string? ValidatePersistedPerks(SessionPersistenceSnapshot s)
    {
        if (s.Perks is not { } p) return null;
        if (p.Version != 1 || p.Equipped is null || p.Hand is null || p.FrozenEffects is null || p.StartingEquipped is null || p.DrawnHand is null || p.StartingEquipped.Length > 5 || p.Equipped.Length > 5 || p.FrozenEffects.Length > 5 ||
            s.Preparation is not { } prep || s.Immersion is null || s.Lifecycle is null && (prep.Attempt != 1 || prep.Status != PreparationStatus.Preparing) ||
            p.DraftAttempt != prep.Attempt || p.RandomIncrement % 2 != 1 || p.Cursor < 3 || p.Cursor > (ulong)prep.Attempt * 32 ||
            !p.Equipped.SequenceEqual(p.Equipped.Distinct().Order(StringComparer.Ordinal)) || !p.FrozenEffects.SequenceEqual(p.FrozenEffects.Distinct().Order(StringComparer.Ordinal)) ||
            !p.StartingEquipped.SequenceEqual(p.StartingEquipped.Distinct().Order(StringComparer.Ordinal)) ||
            p.Equipped.Concat(p.Hand).Concat(p.FrozenEffects).Concat(p.StartingEquipped).Concat(p.DrawnHand).Any(id => !PerkCatalogue.All.Any(item => item.Id == id)) ||
            p.Ended != (s.Lifecycle?.Hearings.LastOrDefault()?.Status is (int)HearingStatus.Conceded or (int)HearingStatus.LostNoFavour) ||
            (p.Ended ? p.Equipped.Length != 0 || p.Pending : p.FrozenEffects.Length != 0) ||
            (p.Pending ? prep.Status != PreparationStatus.Preparing || p.Hand.Length != 3 || p.Hand.Distinct().Count() != 3 || p.Hand.Any(p.Equipped.Contains) || prep.AcceptedOffers.Length != 0 : p.Hand.Length != 0))
            return "Perk version, catalogue, capacity, hand or attempt correspondence invalid.";
        // Reconstruct the bounded PCG cursor, without drawing another hand or touching gameplay streams.
        var expected = RandomStreamFactory.Create(s.CampaignSeed ^ 0x5045524B44524146UL, RandomStreamId.ArtistDecisions);
        if(p.DraftStartCursor >= p.Cursor) return "Perk draft start cursor invalid.";
        for (ulong i = 0; i < p.DraftStartCursor; i++) expected.NextUInt32();
        var cursor=p.DraftStartCursor;
        string[] drawn=[];
        for(var roll=0;roll<(p.RerollUsed?2:1);roll++)
        {
            var eligible=PerkCatalogue.All.Select(item=>item.Id).Where(id=>!p.StartingEquipped.Contains(id)).ToArray();
            for(var index=0;index<3;index++)
            {
                var bound=(uint)(eligible.Length-index);var threshold=unchecked(0u-bound)%bound;uint value;
                do{value=expected.NextUInt32();cursor++;if(cursor>p.Cursor)return "Perk roll correspondence invalid.";}while(value<threshold);
                var selected=index+(int)(value%bound);(eligible[index],eligible[selected])=(eligible[selected],eligible[index]);
            }
            drawn=eligible.Take(3).ToArray();
        }
        if (cursor!=p.Cursor || expected.State != p.RandomState || expected.Increment != p.RandomIncrement || !drawn.SequenceEqual(p.DrawnHand)) return "Perk random cursor or saved hand invalid.";
        var result=p.Ended?p.FrozenEffects:p.Equipped;
        if(p.Pending ? p.ChosenId is not null || p.ReplacedId is not null || p.Skipped || !p.Hand.SequenceEqual(drawn) || !result.SequenceEqual(p.StartingEquipped) :
            p.Skipped ? p.StartingEquipped.Length!=5 || p.ChosenId is not null || p.ReplacedId is not null || !result.SequenceEqual(p.StartingEquipped) :
            p.ChosenId is null || !drawn.Contains(p.ChosenId) || (p.StartingEquipped.Length==5 ? p.ReplacedId is null || !p.StartingEquipped.Contains(p.ReplacedId) : p.ReplacedId is not null) ||
                !result.SequenceEqual(p.StartingEquipped.Where(id=>id!=p.ReplacedId).Append(p.ChosenId).Order(StringComparer.Ordinal)))
            return "Perk pending choice, replacement or skip result invalid.";
        if (prep.RespondersUpgraded || prep.WaterTowerOwned != SavedPerkEffect(p,"high-pressure") ||
            prep.ExtraMedicSlotOwned != SavedPerkEffect(p,"doctors-orders") || prep.ExtraStewardSlotOwned != SavedPerkEffect(p,"extra-pair-of-hands") ||
            prep.ExtraWaterSiteIds.Any(id => id != PerkCatalogue.TapId) || prep.ExtraWaterSiteIds.Length > 1 || prep.ExtraWaterSiteIds.Length > 0 && !SavedPerkEffect(p,"another-round") ||
            prep.AcceptedOffers.Contains("staff.extra-medic") && !SavedPerkEffect(p,"doctors-orders") || prep.AcceptedOffers.Contains("staff.extra-steward") && !SavedPerkEffect(p,"extra-pair-of-hands"))
            return "Perk effects or perk-owned tap disagree with the equipped set.";
        return null;
    }
}

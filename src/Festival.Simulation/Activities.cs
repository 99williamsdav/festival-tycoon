namespace Festival.Simulation;

/// <summary>
/// The one activity decision for guests, performers and staff. Each person re-plans once a second
/// (staggered by id). People in a fixed state — collapsed, fighting, escorted, performing their
/// own set, on a staff job, mid-service or resting — are not scored; everyone else compares their
/// default (the music, or a worker's post) with every activity open to them and switches when
/// another plan scores higher. Automatic staff jobs are offered to a worker as one more option
/// (see <see cref="AcceptsAutomaticResponse"/>); a player's direct order is never scored. The
/// facility systems still run the chosen activity: queues, service and relief are unchanged.
/// </summary>
public sealed partial class GameSession
{
    public const int ActivityDecisionTicks = 80;
    public const int OnStageDutyPerSecond = 50_000;     // A performer is needed on stage for their own set.
    public const int PerformerCallLeadTicks = 1_600;    // Stage call before a slot opens.
    public const int PurchaseValueScale = 13;
    // Music's worth per second, as a share of an act's appeal, against need discomfort.
    public const long MusicValuePermille = 800;
    // A worker's worth per second at their post, in place of the music.
    public const long StaffPostPerSecond = 3_000;
    // What an automatic job is worth to the worker offered it, by urgency.
    public const long CriticalResponseValue = 900_000;
    public const long CollapsedResponseValue = 600_000;
    public const long FightResponseValue = 600_000;
    public const long ArgumentResponseValue = 300_000;

    private void AdvanceActivityChoices()
    {
        if (_preparation is not { Status: PreparationStatus.Running } || ImmersionDepartureActive || _immersion is null) return;
        foreach (var person in PeopleIn(PersonView.Medical).ToArray())
            if (CurrentTick % ActivityDecisionTicks == (long)(person.Id % ActivityDecisionTicks) && ActivityChooserOwns(person.Id))
                ChooseActivity(person.Id);
    }

    /// <summary>Scored, not fixed: on site, free to move, and not already committed to a service or response.</summary>
    private bool ActivityChooserOwns(ulong id)
    {
        var person = _persons[id];
        if (!person.Admitted || person.Departed) return false;
        if (HasClaim(id, PersonClaims.StaffJob) || GetStewardResponses().Any(job => job.WorkerId == id && job.Incapacitated)) return false;
        if (person.Intent is MedicalIntent.Rest or MedicalIntent.AwaitMedic or MedicalIntent.Leaving or MedicalIntent.Collapsed or MedicalIntent.Drinking ||
            person.HealthStage is MedicalStage.Collapsed or MedicalStage.Critical || IsCurrentProgrammePerformer(id)) return false;
        if (HasClaim(id, PersonClaim.Fighting | PersonClaim.InterventionTarget | PersonClaim.InterventionWorker | PersonClaim.BeingEscorted |
                PersonClaim.MedicPatient | PersonClaim.StewardTarget | PersonClaim.Performing)) return false;
        if (person.ToiletStage is not (ToiletVisitStage.None or ToiletVisitStage.Approaching or ToiletVisitStage.Queued)) return false;
        if (person.VendorId is { } vendorId && Vendors.Any(vendor => vendor.Id == vendorId && vendor.OwnerId == id)) return false;
        if (person.Intent == MedicalIntent.SeekWater && WaterPoints().Any(point => point.OwnerId == id)) return false;
        return true;
    }

    private ActivityKind CurrentActivity(Person person) =>
        person.Intent == MedicalIntent.SeekWater ? ActivityKind.Water :
        person.ToiletStage != ToiletVisitStage.None ? ActivityKind.Toilet :
        person.VendorId is not null ? person.Order switch
        { ImmersionProduct.Chips => ActivityKind.Food, ImmersionProduct.SoftDrink => ActivityKind.SoftDrink, _ => ActivityKind.Beer } :
        ActivityKind.Watch;

    private bool IsStaffMember(ulong id) => _persons[id].NeedProfile == MedicalNeedProfile.Staff;

    private ActivityScore[] RankActivities(ulong id, ActivityKind current, ActivityOption? extra = null)
    {
        var person = _persons[id];
        var options = ActivityOptions(id, current);
        if (extra is not null) options.Add(extra);
        // On-duty staff heat up an eighth as fast (see AdvanceMedical).
        var growth = new NeedGrowth(20 + (HasPerk("thirsty-crowd") && person.NeedProfile == MedicalNeedProfile.Guest ? 2 : 0),
            IsStaffMember(id) ? 2 : 20, 12, 20);
        var now = new NeedLevels(person.Thirst, person.HeatExposure, person.Hunger, person.ToiletNeed);
        return ActivityChooser.Rank(now, growth, MusicPerSecond(id), options);
    }

    private void ChooseActivity(ulong id)
    {
        var person = _persons[id];
        var current = CurrentActivity(person);
        var ranked = RankActivities(id, current);
        var best = ranked[0];
        var runnerUp = ranked.Length > 1 ? ranked[1] : null;
        var comparison = runnerUp is null ? "" : $" vs {ActivityLabel(runnerUp.Option.Kind, IsStaffMember(id))} {runnerUp.Score / 1_000}";
        if (best.Option.Kind == current)
        {
            var reason = current == ActivityKind.Watch
                ? IsStaffMember(id) ? $"On duty: post {best.Score / 1_000}{comparison}" : $"Watching band: music {best.Score / 1_000}{comparison} incl. travel/wait"
                : $"Staying with {ActivityLabel(current)} {best.Score / 1_000}{comparison}";
            MutatePerson(id, item => { item.Reason = reason; item.NeedDecisionTick = CurrentTick; });
            return;
        }
        var why = $"Chose {ActivityLabel(best.Option.Kind, IsStaffMember(id))} {best.Score / 1_000}{comparison}";
        AbandonActivity(id, current, best.Option.Kind == ActivityKind.Watch, why);
        StartActivity(id, best.Option, why);
    }

    private static string ActivityLabel(ActivityKind kind, bool staff = false) => kind switch
    {
        ActivityKind.Watch => staff ? "post" : "music", ActivityKind.Water => "water", ActivityKind.Rest => "rest", ActivityKind.Food => "chips",
        ActivityKind.SoftDrink => "soft drink", ActivityKind.Beer => "beer", ActivityKind.Respond => "respond", _ => "toilet"
    };

    /// <summary>
    /// Offers an automatic job to one worker as another option in their plan. They take it unless
    /// their own plan scores higher (a steward bursting for the toilet may leave a scuffle to
    /// another worker); a direct player order never comes through here.
    /// </summary>
    private bool AcceptsAutomaticResponse(ulong workerId, ulong targetId, long value, int serviceTicks)
    {
        var person = _persons[workerId];
        var current = CurrentActivity(person);
        var nav = _navigationAgents[new(workerId)];
        var here = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        var target = _navigationAgents[new(targetId)];
        var scene = TraversalGrid.WorldToCell(target.XMillimetres, target.ZMillimetres);
        var complete = EstimateWalkTicks(workerId, here, scene) + serviceTicks;
        var job = new ActivityOption(ActivityKind.Respond, targetId.ToString(), complete,
            complete + EstimateWalkTicks(workerId, scene, StaffAssignedPost(workerId) ?? here), value);
        var ranked = RankActivities(workerId, current, job);
        if (ranked[0].Option.Kind == ActivityKind.Respond) return true;
        var respond = ranked.First(item => item.Option.Kind == ActivityKind.Respond);
        var reason = $"Left {_persons[targetId].Name} to another worker: {ActivityLabel(ranked[0].Option.Kind, true)} {ranked[0].Score / 1_000} vs respond {respond.Score / 1_000}";
        MutatePerson(workerId, item => item.Reason = reason);
        return false;
    }

    /// <summary>A worker can be pulled off a personal errand until its service begins; resting is recovery, not an errand.</summary>
    private bool PersonalActivityInService(ulong id)
    {
        var person = _persons[id];
        return person.Intent is MedicalIntent.Drinking or MedicalIntent.Rest || WaterPoints().Any(point => point.OwnerId == id) ||
            person.ToiletStage is not (ToiletVisitStage.None or ToiletVisitStage.Approaching or ToiletVisitStage.Queued) ||
            person.VendorId is { } vendorId && Vendors.Any(vendor => vendor.Id == vendorId && vendor.OwnerId == id);
    }

    /// <summary>Drops a worker's personal errand (not yet in service) so a job can take their route.</summary>
    private void RecallWorker(ulong id, string reason)
    {
        var person = _persons[id];
        AbandonActivity(id, CurrentActivity(person), false, reason);
    }

    private void AbandonActivity(ulong id, ActivityKind current, bool returnToMusic, string reason)
    {
        switch (current)
        {
            case ActivityKind.Water: LeaveWater(id, reason, reroute: returnToMusic); break;
            case ActivityKind.Toilet: ReleaseToiletPerson(id, returnToMusic); break;
            case ActivityKind.Food or ActivityKind.SoftDrink or ActivityKind.Beer: LeaveImmersionQueue(id, returnToMusic); break;
        }
    }

    private void StartActivity(ulong id, ActivityOption option, string reason)
    {
        switch (option.Kind)
        {
            case ActivityKind.Watch:
                MutatePerson(id, item => { item.Reason = reason; item.NeedDecisionTick = CurrentTick; });
                break;
            case ActivityKind.Water:
                SeekWater(id, reason);
                break;
            case ActivityKind.Rest:
                MedicalRelinquishPerformerStage(id);
                MutatePerson(id, item => { item.Intent = MedicalIntent.Rest; item.Reason = reason; item.NeedDecisionTick = CurrentTick; });
                ApplyAgentDestination(new(id), new(MedicalRestCell, "medical.rest"));
                break;
            case ActivityKind.Toilet:
                var person = _persons[id];
                var kind = ChooseToiletVisit(person);
                if (BestToiletFor(id, kind) is not { } toilet) { ReturnToListening(id); break; }
                SetConsumption(person with { ToiletStage = ToiletVisitStage.Approaching, ToiletChoice = kind, ToiletId = toilet.Id,
                    LastToiletChoiceReviewTick = CurrentTick });
                MutatePerson(id, item => { item.Reason = reason; item.NeedDecisionTick = CurrentTick; });
                ApplyAgentDestination(new(id), new(ToiletQueueCell(toilet, toilet.Queue.Length), "toilet.approach"));
                break;
            default:
                var vendor = Vendors.Single(item => item.Id == option.FacilityId);
                var product = option.Kind switch
                { ActivityKind.Food => ImmersionProduct.Chips, ActivityKind.SoftDrink => ImmersionProduct.SoftDrink, _ => ImmersionProduct.Beer };
                SetConsumption(_persons[id] with { VendorId = vendor.Id, Order = product, ShoppingDecisionTick = CurrentTick });
                MutatePerson(id, item => { item.Reason = reason; item.NeedDecisionTick = CurrentTick; });
                ApplyAgentDestination(new(id), new(ImmersionQueueCell(vendor, vendor.Queue.Length), "immersion.approach"));
                break;
        }
    }

    private List<ActivityOption> ActivityOptions(ulong id, ActivityKind current)
    {
        var person = _persons[id];
        var nav = _navigationAgents[new(id)];
        var here = TraversalGrid.WorldToCell(nav.XMillimetres, nav.ZMillimetres);
        var music = MusicReturnCell(id, here);
        var options = new List<ActivityOption> { new(ActivityKind.Watch, null, 0, current == ActivityKind.Watch ? 0 : EstimateWalkTicks(id, here, music)) };
        ActivityOption? Trip(ActivityKind kind, string facility, GridCell approach, int completeTicks, long enjoyment = 0, long cost = 0) =>
            completeTicks == int.MaxValue ? null
                : new(kind, facility, completeTicks, completeTicks + EstimateWalkTicks(id, approach, music), enjoyment, cost);
        void Add(ActivityOption? option) { if (option is not null) options.Add(option); }

        if (_disorder?.WaterClosed != true)
            Add(WaterPoints().Select(point => Trip(ActivityKind.Water, point.Id, point.Cell, LightWaterTicks(id, point, here)))
                .Where(item => item is not null).OrderBy(item => item!.CompleteTicks).FirstOrDefault());
        if (person.HeatExposure > ActivityChooser.RestHeatTarget)
            Add(Trip(ActivityKind.Rest, "rest", MedicalRestCell,
                EstimateWalkTicks(id, here, MedicalRestCell) + (person.HeatExposure - ActivityChooser.RestHeatTarget) / 8));
        if (current == ActivityKind.Toilet || ImmersionHandsAvailable(id) && person.Intent is MedicalIntent.WatchShow or MedicalIntent.SeekWater)
        {
            var kind = person.ToiletChoice ?? ChooseToiletVisit(person);
            Add(EffectiveToilets(_facilities).Select(toilet => Trip(ActivityKind.Toilet, toilet.Id, toilet.Cell, LightToiletTicks(id, kind, toilet, here)))
                .Where(item => item is not null).OrderBy(item => item!.CompleteTicks).FirstOrDefault());
        }
        foreach (var product in Enum.GetValues<ImmersionProduct>())
        {
            var kind = product switch { ImmersionProduct.Chips => ActivityKind.Food, ImmersionProduct.SoftDrink => ActivityKind.SoftDrink, _ => ActivityKind.Beer };
            if (current != kind && !ActivityPurchaseEligible(person, product)) continue;
            var vendorId = product == ImmersionProduct.Chips ? "food" : "drinks";
            var price = ImmersionPriceFor(id, product) * (2L + person.PriceReluctance / 25) * PurchaseValueScale;
            var enjoyment = product switch
            {
                ImmersionProduct.Beer => (3_000L + person.BeerTaste * 45) * PurchaseValueScale,
                ImmersionProduct.SoftDrink => person.SoftTaste * 35L * PurchaseValueScale,
                _ => 0L
            };
            Add(Vendors.Where(vendor => vendor.Id == vendorId && (current == kind || VendorHasRoom(vendor)))
                .Select(vendor => Trip(kind, vendor.Id, vendor.Cell, LightVendorTicks(id, product, vendor, here), enjoyment, price))
                .Where(item => item is not null).OrderBy(item => item!.CompleteTicks).FirstOrDefault());
        }
        return options;
    }

    private bool ActivityPurchaseEligible(Person person, ImmersionProduct product) =>
        // The vendor's own rule, less what abandoning the current activity would clear.
        person.Held is null && person.VendorId is null && person.Intent is MedicalIntent.WatchShow or MedicalIntent.SeekWater &&
        person.Thirst < MedicalDistressThirst && person.HeatExposure < MedicalDistressHeat && !IsCurrentProgrammePerformer(person.Id) &&
        ImmersionHandsAvailable(person.Id) && ImmersionStock(product) > 0 &&
        _wallets[new(person.Id)].CashPennies >= ImmersionPriceFor(person.Id, product) &&
        (product != ImmersionProduct.Beer || !person.Abstains && person.Intoxication < 7_000 && person.Role != ProtectedPersonRole.Staff);

    private bool VendorHasRoom(ImmersionVendor vendor)
    {
        var waiting = PeopleIn(PersonView.Consumption).Count(item => item.VendorId == vendor.Id);
        return waiting < 10 && (vendor.QueueCells is null || vendor.QueueCells.Length > waiting);
    }

    // Straight-line (octile) walking time: an estimate for comparing plans, not a route.
    private int EstimateWalkTicks(ulong id, GridCell from, GridCell to)
    {
        var dx = Math.Abs(from.X - to.X); var dz = Math.Abs(from.Z - to.Z);
        var thousandthCells = (long)Math.Max(dx, dz) * 1_000 + (long)Math.Min(dx, dz) * 414;
        var perTick = (long)RouteProgressMicrometresPerTick * _navigationAgents[new(id)].WalkingSpeedPermille / 1_000;
        return perTick <= 0 ? int.MaxValue : (int)(thousandthCells * 500 / perTick);
    }

    private GridCell MusicReturnCell(ulong id, GridCell here) =>
        StaffAssignedPost(id) ??
        _livePerformance?.Listeners.FirstOrDefault(item => item.AgentId == id)?.Place ??
        (_persons[id].NeedProfile == MedicalNeedProfile.Performer ? here : PreparedPlace(Array.FindIndex(PeopleIn(PersonView.Roster), item => item.Id == id)));

    private int LightWaterTicks(ulong id, WaterPointState point, GridCell here)
    {
        var members = point.Queue.Concat(point.Overflow).ToArray();
        var position = Array.IndexOf(members, id);
        var joined = position >= 0 || _persons[id].Intent == MedicalIntent.SeekWater && _persons[id].WaterPointId == point.Id;
        var destination = position < 0 ? WaterApproach(point) : position < 10 ? WaterSlot(point, position) : WaterOverflowSlot(point, position - 10);
        var candidate = new QueuedServiceChoice.Candidate(point.Id, EstimateWalkTicks(id, here, destination), WaterDurationTicks(id), true,
            joined || members.Length < 20 && (point.QueueCells.Length == 0 || point.QueueCells.Length > members.Length),
            members.Select(member => new QueuedServiceChoice.Member(member, WaterDurationTicks(member))).ToArray(),
            point.OwnerId, point.OwnerId is { } owner ? WaterDurationTicks(owner) : 0, []);
        return QueuedServiceChoice.EstimateTicks(id, candidate);
    }

    private int LightToiletTicks(ulong id, ToiletVisitKind kind, ToiletFacility toilet, GridCell here)
    {
        var people = PeopleIn(PersonView.Consumption);
        var position = Array.IndexOf(toilet.Queue, id);
        var destination = ToiletQueueCell(toilet, position >= 0 ? position : Math.Min(toilet.Queue.Length, ToiletRules.MaximumQueue - 1));
        var active = toilet.OwnerId is { } owner ? _persons[owner] : null;
        var ownerRemaining = active?.ToiletStage switch
        { ToiletVisitStage.Using => toilet.ServiceTicks, ToiletVisitStage.Entering => ToiletServiceDuration(active.ToiletChoice), _ => 0 };
        var candidate = new QueuedServiceChoice.Candidate(toilet.Id, EstimateWalkTicks(id, here, destination), ToiletServiceDuration(kind),
            !toilet.IsFull && toilet.InterruptedOccupantId is null && toilet.CanAccept(kind),
            position >= 0 || _persons[id].ToiletId == toilet.Id || toilet.Queue.Length < ToiletRules.MaximumQueue,
            toilet.Queue.Select(member => new QueuedServiceChoice.Member(member, ToiletServiceDuration(_persons[member].ToiletChoice))).ToArray(),
            toilet.OwnerId, ownerRemaining, []);
        return QueuedServiceChoice.EstimateTicks(id, candidate);
    }

    private int LightVendorTicks(ulong id, ImmersionProduct product, ImmersionVendor vendor, GridCell here)
    {
        var position = Array.IndexOf(vendor.Queue, id);
        var destination = ImmersionQueueCell(vendor, position >= 0 ? position : vendor.Queue.Length);
        var candidate = new QueuedServiceChoice.Candidate(vendor.Id, EstimateWalkTicks(id, here, destination), ImmersionServiceDuration(product), true, true,
            vendor.Queue.Select(member => new QueuedServiceChoice.Member(member, ImmersionServiceDuration(_persons[member].Order ?? ImmersionProduct.SoftDrink))).ToArray(),
            vendor.OwnerId, vendor.ServiceTicks, []);
        return QueuedServiceChoice.EstimateTicks(id, candidate);
    }

    /// <summary>
    /// Music on offer each second of the horizon, from the published slot times. The act playing
    /// now follows the live stage, so a finished set is silence even inside its slot.
    /// </summary>
    private long[] MusicPerSecond(ulong id)
    {
        var values = new long[ActivityChooser.Samples];
        if (IsStaffMember(id))
        {
            Array.Fill(values, StaffPostPerSecond);
            return values;
        }
        var started = _preparation!.StartedTick;
        var q = _programme;
        var ownSlot = q?.Performers.FirstOrDefault(item => item.AgentId == id)?.SlotIndex ?? -1;
        var slotAppeal = new int[FestivalSlotStarts.Length];
        for (var slot = 0; slot < slotAppeal.Length; slot++)
            slotAppeal[slot] = q is { ActIds.Length: 3 }
                ? 2_500 + FestivalAffinity(id, FestivalActs.Single(act => act.Id == q.ActIds[slot])) * 50 + FestivalActs.Single(act => act.Id == q.ActIds[slot]).Popularity * 10
                : 2_500;
        for (var sample = 0; sample < values.Length; sample++)
        {
            var tick = CurrentTick + (sample + 1L) * ActivityChooser.SampleTicks;
            var appeal = 2_500;
            for (var slot = 0; slot < FestivalSlotStarts.Length; slot++)
            {
                var start = started + FestivalSlotStarts[slot];
                var end = q?.CurrentSlot == slot && _livePerformance?.Stage is LiveSetStage.Live or LiveSetStage.Interrupted ? Math.Max(q.SlotEndTick, started + FestivalSlotEnds[slot]) : started + FestivalSlotEnds[slot];
                var finished = q is { } programme && (slot < programme.CurrentSlot || slot == programme.CurrentSlot && _livePerformance?.Stage == LiveSetStage.Finished);
                if (!finished && tick >= start && tick < end) appeal = slotAppeal[slot];
                if (slot == ownSlot && !finished && tick >= start - PerformerCallLeadTicks && tick < end) appeal = (int)(OnStageDutyPerSecond * 1_000L / MusicValuePermille);
            }
            values[sample] = appeal * MusicValuePermille / 1_000;
        }
        return values;
    }
}

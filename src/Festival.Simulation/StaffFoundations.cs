namespace Festival.Simulation;

public enum ResponseRole { Medic, Steward }
public sealed record StaffProfile(ulong AgentId, string Name, ResponseRole Role, int WalkingSpeedPermille,
    int TreatmentTicks, int CalmingSkill, int ConfrontationSkill);

public sealed partial class GameSession
{
    private bool StaffMedicalBoundaryOnNextTick => !IsPaused && MedicalOperationsActive &&
        GetMedicResponses().Any(job => job.Stage == MedicalResponseStage.Travelling && _navigationAgents[new(job.WorkerId)].Action == AgentNavigationAction.Arrived ||
            job.Stage == MedicalResponseStage.Treating && (!IntoxicationCareOwns(job) && CurrentTick + 1 >= job.StartedTick + GetResponseStaff().Single(item => item.AgentId == job.WorkerId).TreatmentTicks || IntoxicationCareBoundary(job)));
    public IReadOnlyList<MedicResponse> GetMedicResponses() => _medical is not { } m ? [] :
        new[] { new MedicResponse(m.MedicId, m.ResponseStage, m.ResponsePatientId, m.ResponseStartedTick, m.Response, m.ResponseDispatchedTick) }.Concat(m.ExtraResponses).ToArray();
    public IReadOnlyList<StewardResponse> GetStewardResponses() => _disorder is not { } d ? [] :
        new[] { new StewardResponse(d.SecurityId, d.ResponseStage, d.ResponseTargetId, d.ResponseStartedTick, d.SecurityIncapacitated, d.Response, d.ResponseDispatchedTick) }.Concat(d.ExtraResponses).ToArray();
    private void SetMedicResponse(MedicResponse response)
    {
        var m = _medical!;
        _medical = response.WorkerId == m.MedicId ? m with { ResponseStage = response.Stage, ResponsePatientId = response.PatientId,
            ResponseStartedTick = response.StartedTick, Response = response.Description, ResponseDispatchedTick = response.DispatchedTick } :
            m with { ExtraResponses = m.ExtraResponses.Select(item => item.WorkerId == response.WorkerId ? response : item).ToArray() };
    }
    private void SetStewardResponse(StewardResponse response)
    {
        var d = _disorder!;
        _disorder = response.WorkerId == d.SecurityId ? d with { ResponseStage = response.Stage, ResponseTargetId = response.TargetId,
            ResponseStartedTick = response.StartedTick, SecurityIncapacitated = response.Incapacitated, Response = response.Description, ResponseDispatchedTick = response.DispatchedTick } :
            d with { ExtraResponses = d.ExtraResponses.Select(item => item.WorkerId == response.WorkerId ? response : item).ToArray() };
    }
    private bool IsSteward(ulong id) => GetStewardResponses().Any(item => item.WorkerId == id);
    private string StaffResponseCausalSummary()
    {
        string Name(ulong? id) => id is { } value ? _persons[value].Name : "none";
        return "medics: " + string.Join("; ", GetMedicResponses().Select(job => $"{Name(job.WorkerId)}→{Name(job.PatientId)} {job.Stage}, dispatch tick {job.DispatchedTick}, treatment tick {job.StartedTick}")) +
            ". Stewards: " + string.Join("; ", GetStewardResponses().Select(job => $"{Name(job.WorkerId)}→{Name(job.TargetId)} {job.Stage}, dispatch tick {job.DispatchedTick}, response tick {job.StartedTick}")) +
            ". Physical interventions: " + string.Join("; ", CaptureStaffInterventions().Select(job => $"{Name(job.WorkerId)}→{Name(job.GuestId)} {job.Action}/{job.Stage}, dispatch tick {job.DispatchedTick}, arrival tick {job.StartedTick}, end tick {job.EndedTick}"));
    }
    private static bool MedicBusy(MedicResponse item) => item.Stage is MedicalResponseStage.Travelling or MedicalResponseStage.Treating or MedicalResponseStage.Removing;
    private static bool StewardBusy(StewardResponse item) => item.Stage is SecurityResponseStage.Travelling or SecurityResponseStage.Calming or SecurityResponseStage.Confronting;
    private GridCell StaffDutyCell(ulong id, ResponseRole role)
    {
        var baseline = role == ResponseRole.Medic ? _medical!.MedicId : _disorder!.SecurityId;
        return ResponsePostHome(_preparation,role,id!=baseline);
    }
    /// <summary>Current assigned work location, shared by arrivals, returns and consumption.</summary>
    public GridCell? StaffAssignedPost(ulong id)
    {
        if (PersonIn(PersonView.Roster, id)?.Role != ProtectedPersonRole.Staff) return null;
        if (GetResponseStaff().SingleOrDefault(person => person.AgentId == id) is { } response)
            return StaffDutyCell(id, response.Role);
        if (_equipment?.WorkerId == id) return EquipmentWorkCell;
        // The sound engineer's established listening/mixing location in the audience apron.
        return PreparedPlace(Array.FindIndex(PeopleIn(PersonView.Roster), person => person.Id == id));
    }
    /// <summary>Derived presentation hint only: no job, route, hash or save state is changed.</summary>
    public ResponseRole? IdleResponseStaffRole(EntityId id, double renderedXMillimetres, double renderedZMillimetres)
    {
        var profile = GetResponseStaff().SingleOrDefault(item => item.AgentId == id.Value);
        if (profile is null || _preparation is not { Status: PreparationStatus.Running } ||
            !_navigationAgents.TryGetValue(id, out var nav) || nav.Action is not (AgentNavigationAction.Idle or AgentNavigationAction.Arrived) ||
            PersonCollapsed(id.Value) || MedicalOwnsNavigation(id.Value) || ImmersionOwnsNavigation(id.Value) ||
            InterventionOwnsWorker(id.Value) || InterventionOwnsTarget(id.Value) ||
            GetMedicResponses().Any(job => job.WorkerId == id.Value && MedicBusy(job)) ||
            GetStewardResponses().Any(job => job.WorkerId == id.Value && (StewardBusy(job) || job.Incapacitated))) return null;
        var duty = StaffDutyCell(id.Value, profile.Role);
        var centre = TraversalGrid.CellCentre(duty);
        return nav.Destination == duty && nav.XMillimetres == centre.XMillimetres && nav.ZMillimetres == centre.ZMillimetres &&
            Math.Abs(renderedXMillimetres - centre.XMillimetres) < .01 && Math.Abs(renderedZMillimetres - centre.ZMillimetres) < .01
            ? profile.Role : null;
    }
    public IReadOnlyList<StaffProfile> GetResponseStaff()
    {
        var profiles = new List<StaffProfile>();
        if (_medical is { } m) profiles.Add(new(m.MedicId, "Riley Hart", ResponseRole.Medic,
            GetWalkingSpeedPermille(new(m.MedicId)), MedicalTreatmentTicks, 0, 0));
        if (_disorder is { } d) profiles.Add(new(d.SecurityId, "Jordan Hale", ResponseRole.Steward,
            GetWalkingSpeedPermille(new(d.SecurityId)), 0, d.CalmingSkill, d.ConfrontationSkill));
        if (_preparation is { } p) profiles.AddRange(p.StaffProfiles.Where(profile => InView(PersonView.Roster, profile.AgentId)));
        return profiles.Select(EffectiveStaffProfile).OrderBy(profile => profile.AgentId).ToArray();
    }
    private static bool RoleTrained(PreparationSnapshot? prep, PerkSnapshot? perks, ResponseRole role) =>
        prep?.RespondersUpgraded == true || SavedPerkEffect(perks, role == ResponseRole.Medic ? "first-responders" : "smooth-operators");
    private StaffProfile EffectiveStaffProfile(StaffProfile profile) => RoleTrained(_preparation, _perks, profile.Role) ? profile with
        {
            WalkingSpeedPermille = Math.Min(1150, profile.WalkingSpeedPermille + 100),
            TreatmentTicks = profile.Role == ResponseRole.Medic ? Math.Max(360, profile.TreatmentTicks - 120) : 0,
            CalmingSkill = profile.Role == ResponseRole.Steward ? Math.Min(8000, profile.CalmingSkill + 500) : 0,
            ConfrontationSkill = profile.Role == ResponseRole.Steward ? Math.Min(8000, profile.ConfrontationSkill + 500) : 0
        } : profile;

    public StaffProfile? GetOptionalStaffOfferProfile(ResponseRole role) => _preparation is not { } p || _disorder is null ? null :
        EffectiveStaffProfile(p.StaffProfiles.SingleOrDefault(item => item.Role == role) ?? CreateOptionalStaff(CampaignSeed, NextEntityId, role));

    // Route-only estimate: occupancy delays are deliberately not promised as a fixed arrival time.
    public int? EstimateStaffTravelTicks(ulong id)
    {
        if (!_navigationAgents.TryGetValue(new(id), out var nav)) return null;
        if (nav.Action == AgentNavigationAction.Arrived) return 0;
        if (nav.Action != AgentNavigationAction.Travelling || _traversalGrid is null) return null;
        var x = nav.XMillimetres; var z = nav.ZMillimetres; long distance = 0;
        foreach (var cell in nav.Route.Skip(nav.RouteIndex))
        {
            var centre = TraversalGrid.CellCentre(cell);
            var dx = centre.XMillimetres - x; var dz = centre.ZMillimetres - z;
            distance += IntegerSquareRoot((long)dx * dx * 1_000_000L + (long)dz * dz * 1_000_000L) * _traversalGrid.Get(cell).CostPermille / 1000;
            x = centre.XMillimetres; z = centre.ZMillimetres;
        }
        var perTick = (long)RouteProgressMicrometresPerTick * nav.WalkingSpeedPermille / 1000;
        return checked((int)((distance + perTick - 1) / perTick));
    }

    private static StaffProfile CreateOptionalStaff(ulong seed, ulong id, ResponseRole role)
    {
        var random = RandomStreamFactory.Create(seed ^ (role == ResponseRole.Medic ? 0x52494C4559UL : 0x4A4F5244414EUL), RandomStreamId.IndividualBehaviour);
        return new(id, role == ResponseRole.Medic ? "Avery Brooks" : "Sam Ellis", role,
            GetWalkingSpeedPermille(new(id)), role == ResponseRole.Medic ? 360 + 120 * (int)(random.NextUInt32() % 3) : 0,
            role == ResponseRole.Steward ? 3500 + (int)(random.NextUInt32() % 4501) : 0,
            role == ResponseRole.Steward ? 3500 + (int)(random.NextUInt32() % 4501) : 0);
    }

    private void HireOptionalStaff(ResponseRole role)
    {
        var p = _preparation!;
        var profile = p.StaffProfiles.SingleOrDefault(item => item.Role == role);
        if (profile is null)
        {
            profile = CreateOptionalStaff(CampaignSeed, NextEntityId++, role);
            _wallets.Add(new(profile.AgentId), new WalletState { OwnerId = new(profile.AgentId), CashPennies = 500 });
        }
        PreparationView = p with { StaffProfiles = p.StaffProfiles.Append(profile).Distinct().OrderBy(item => item.AgentId).ToArray(),
            People = PreparationView!.People.Append(new EditionPerson(profile.AgentId, profile.Name, ProtectedPersonRole.Staff, 0)).OrderBy(item => item.AgentId).ToArray() };
        if (role == ResponseRole.Medic)
            _medical = _medical! with { ExtraResponses = [new(profile.AgentId, MedicalResponseStage.None, null, -1, "Available")] };
        else
        {
            _disorder = _disorder! with { ExtraResponses = [new(profile.AgentId, SecurityResponseStage.None, null, -1, false, "Available")] };
            MedicalView = MedicalView! with { Needs = MedicalView.Needs.Append(new MedicalNeed(profile.AgentId, 0, 0,
                MedicalIntent.WatchShow, "Steward on duty", -MedicalDecisionCooldownTicks, null, -1, MedicalNeedProfile.Staff)).OrderBy(item => item.AgentId).ToArray() };
        }
    }

    private void AdvanceMedicResponses()
    {
        foreach (var original in GetMedicResponses())
        {
            var job = original;
            if (MedicBusy(job) && PersonCollapsed(job.WorkerId))
            {
                SetMedicResponse(job with { Stage = MedicalResponseStage.None, PatientId = null, StartedTick = -1, Description = "Medic incapacitated; response released for another physical medic" });
                continue;
            }
            if (MedicBusy(job) && _navigationAgents[new(job.WorkerId)].Action == AgentNavigationAction.NoRoute)
            {
                SetMedicResponse(job with { Stage = MedicalResponseStage.None, PatientId = null, StartedTick = -1, Description = "Physical route failed; response released without remote treatment" });
                continue;
            }
            if (!ImmersionDepartureActive && !ImmersionOwnsNavigation(job.WorkerId) && !MedicalOwnsNavigation(job.WorkerId) && !InterventionOwnsWorker(job.WorkerId) && job.Stage == MedicalResponseStage.Completed &&
                _navigationAgents[new(job.WorkerId)].Destination != StaffDutyCell(job.WorkerId, ResponseRole.Medic))
                ApplyAgentDestination(new(job.WorkerId), new(StaffDutyCell(job.WorkerId, ResponseRole.Medic), "medical.return-to-tent"));
            if (job.Stage is not (MedicalResponseStage.Travelling or MedicalResponseStage.Treating)) continue;
            var m = _medical!;
            var positioned = MedicalTreatmentPositionValid(m with { MedicId = job.WorkerId, ResponsePatientId = job.PatientId });
            if (!positioned && job.PatientId is { } bedsidePatient && PersonCollapsed(bedsidePatient) && !MedicHasValidBedsideDestination(job.WorkerId,bedsidePatient) && MedicalResponseCell(job.WorkerId,bedsidePatient) is { } bedsideCell &&
                (_navigationAgents[new(job.WorkerId)].Destination!=bedsideCell || job.Stage==MedicalResponseStage.Treating))
            {
                ApplyAgentDestination(new(job.WorkerId),new(bedsideCell,"medical.dispatch"));
                job=job with { Stage=MedicalResponseStage.Travelling,StartedTick=-1,Description="Physically approaching collapsed patient bedside" };SetMedicResponse(job);
            }
            if (job.Stage == MedicalResponseStage.Travelling && positioned)
            {
                job = job with { Stage = MedicalResponseStage.Treating, StartedTick = CurrentTick, Description = "Physical arrival; treatment underway" };
                SetMedicResponse(job);
                MedicalEvent("medical:treatment-start", $"Worker {job.WorkerId} reached patient {job.PatientId}; treatment {GetResponseStaff().Single(item => item.AgentId == job.WorkerId).TreatmentTicks} ticks.");
            }
            if (job.Stage == MedicalResponseStage.Treating && !positioned)
            {
                SetMedicResponse(job with { Stage = MedicalResponseStage.None, PatientId = null, StartedTick = -1, Description = "Treatment interrupted: moved out of reach; redispatch before the deadline" });
                MedicalEvent("medical:treatment-interrupted", $"Worker {job.WorkerId}, patient {job.PatientId}: moved out of reach.");
                continue;
            }
            if (job.Stage == MedicalResponseStage.Treating && AdvanceIntoxicationCare(job)) continue;
            if (job.Stage != MedicalResponseStage.Treating || CurrentTick < job.StartedTick + GetResponseStaff().Single(item => item.AgentId == job.WorkerId).TreatmentTicks) continue;
            var patientId = job.PatientId!.Value;
            // Never rescue beyond a real causal deadline. Boundary-tick completion retains the existing ordering.
            var need = _persons[patientId];
            var deadline = _disorder?.Incidents.LastOrDefault(item => item.VictimId == patientId && item.InjuryTick >= 0) is { } injury
                ? injury.InjuryTick + DisorderInjuryDeathTicks : patientId == m.AtRiskGuestId && m.CollapseTick >= 0
                ? m.CollapseTick + MedicalDeathDelayTicks : need.HealthCollapseTick >= 0 ? need.HealthCollapseTick + MedicalDeathDelayTicks : long.MaxValue;
            if (CurrentTick > deadline) continue;
            UpdatePerson(patientId, item => item with { Thirst = 2000, HeatExposure = 3000, Intent = MedicalIntent.WatchShow,
                HealthStage = MedicalStage.Treated, Reason = "Basic first aid completed after physical medic arrival" });
            ReturnToListening(patientId);
            if (patientId == m.AtRiskGuestId) _medical = _medical! with { Stage = MedicalStage.Treated };
            SetMedicResponse(job with { Stage = MedicalResponseStage.Completed, Description = "Basic first aid completed" });
            MedicalEvent("medical:treatment-complete", $"Worker {job.WorkerId} completed first aid for patient {patientId}.");
        }
    }

    private void FinishStaffResponsesForDeparture()
    {
        ReleaseInterventionsForBoundary("Weekend ended; intervention released for physical departure");
        foreach (var job in GetMedicResponses().Where(MedicBusy))
            SetMedicResponse(job with { Stage = MedicalResponseStage.Completed, PatientId = null, Description = "Weekend ended; response released for physical departure" });
        foreach (var job in GetStewardResponses().Where(StewardBusy))
            SetStewardResponse(job with { Stage = SecurityResponseStage.Completed, TargetId = null, Description = "Weekend ended; response released for physical departure" });
    }

    private static string? ValidatePersistedStaffResponses(SessionPersistenceSnapshot s)
    {
        if (s.Preparation is not { } p) return null;
        if (s.Medical is { Needs: null }) return "Medical needs required for staff jobs.";
        var medics = s.Medical is not { } m ? [] : new[] { new MedicResponse(m.MedicId, m.ResponseStage, m.ResponsePatientId, m.ResponseStartedTick, m.Response, m.ResponseDispatchedTick) }.Concat(m.ExtraResponses ?? []).ToArray();
        var stewards = s.Disorder is not { } d ? [] : new[] { new StewardResponse(d.SecurityId, d.ResponseStage, d.ResponseTargetId, d.ResponseStartedTick, d.SecurityIncapacitated, d.Response, d.ResponseDispatchedTick) }.Concat(d.ExtraResponses ?? []).ToArray();
        bool ActiveProfile(StaffProfile profile) => p.AcceptedOffers.Contains(profile.Role == ResponseRole.Medic ? "staff.extra-medic" : "staff.extra-steward");
        if (s.Medical is { ExtraResponses: null } || s.Disorder is { ExtraResponses: null } ||
            (s.Medical?.ExtraResponses ?? []).Any(item => item is null) || (s.Disorder?.ExtraResponses ?? []).Any(item => item is null) ||
            !(s.Medical?.ExtraResponses ?? []).Select(item => item.WorkerId).SequenceEqual(p.StaffProfiles.Where(item => item.Role == ResponseRole.Medic && ActiveProfile(item)).Select(item => item.AgentId)) ||
            !(s.Disorder?.ExtraResponses ?? []).Select(item => item.WorkerId).SequenceEqual(p.StaffProfiles.Where(item => item.Role == ResponseRole.Steward && ActiveProfile(item)).Select(item => item.AgentId)) ||
            medics.Any(item => !Enum.IsDefined(item.Stage) || string.IsNullOrWhiteSpace(item.Description) || item.StartedTick < -1 || item.StartedTick > s.CurrentTick ||
                item.DispatchedTick < -1 || item.DispatchedTick > s.CurrentTick || item.Stage == MedicalResponseStage.Removing && item.WorkerId != s.Medical!.MedicId ||
                item.PatientId is { } patient && s.Medical?.Needs.Any(need => need.AgentId == patient) != true ||
                MedicBusy(item) && (item.PatientId is null || item.WorkerId == item.PatientId) ||
                item.Stage == MedicalResponseStage.Treating && item.StartedTick < 0 ||
                p.Status == PreparationStatus.Preparing && item.Stage != MedicalResponseStage.None) ||
            stewards.Any(item => !Enum.IsDefined(item.Stage) || string.IsNullOrWhiteSpace(item.Description) || item.StartedTick < -1 || item.StartedTick > s.CurrentTick ||
                item.DispatchedTick < -1 || item.DispatchedTick > s.CurrentTick ||
                item.TargetId is { } target && s.Disorder?.People.Any(person => person.AgentId == target) != true ||
                StewardBusy(item) && (item.TargetId is null || item.StartedTick < 0 || item.Incapacitated) ||
                item.Incapacitated != (s.Medical?.Needs.SingleOrDefault(need => need.AgentId == item.WorkerId)?.Stage == MedicalStage.Collapsed) ||
                p.Status == PreparationStatus.Preparing && item.Stage != SecurityResponseStage.None) ||
            medics.Where(MedicBusy).Select(item => item.PatientId).Distinct().Count() != medics.Count(MedicBusy) ||
            stewards.Where(StewardBusy).Select(item => item.TargetId).Distinct().Count() != stewards.Count(StewardBusy) ||
            stewards.Where(StewardBusy).Any(job => medics.Any(other => MedicBusy(other) && other.PatientId == job.TargetId)))
            return "Staff roles, paid response roster, ownership or response clocks invalid.";
        if (p.StaffProfiles.Length > 0 || p.RespondersUpgraded || s.Perks is not null)
        {
            if (p.Status is PreparationStatus.Running or PreparationStatus.Departing &&
                (medics.Where(MedicBusy).Any(job => !p.People.Any(person => person.AgentId == job.WorkerId && person.Admitted && !person.Departed) ||
                    !p.People.Any(person => person.AgentId == job.PatientId && person.Admitted && !person.Departed) ||
                    s.Medical!.Needs.Single(need => need.AgentId == job.PatientId).Intent is not (MedicalIntent.AwaitMedic or MedicalIntent.Collapsed or MedicalIntent.Leaving)) ||
                 stewards.Where(StewardBusy).Any(job => !p.People.Any(person => person.AgentId == job.WorkerId && person.Admitted && !person.Departed) ||
                    !p.People.Any(person => person.AgentId == job.TargetId && person.Admitted && !person.Departed))))
                return "Active staff jobs require physically admitted workers and owned target intentions.";
            foreach (var id in medics.Select(item => item.WorkerId).Concat(stewards.Select(item => item.WorkerId)))
            {
                var nav = s.NavigationAgents?.SingleOrDefault(item => item.Id == id);
                var role = medics.Any(item => item.WorkerId == id) ? ResponseRole.Medic : ResponseRole.Steward;
                var speed = Math.Min(1150, GetWalkingSpeedPermille(new(id)) + (RoleTrained(p, s.Perks, role) ? 100 : 0));
                if (nav is not null && nav.WalkingSpeedPermille != speed) return "Staff movement disagrees with saved role training.";
            }
        }
        if (stewards.Where(StewardBusy).Any(job =>
            s.Disorder!.People.Single(person => person.AgentId == job.TargetId) is { Stage: DisorderStage.Fight, OpponentId: { } opponent } &&
            stewards.Any(other => other.WorkerId != job.WorkerId && StewardBusy(other) && other.TargetId == opponent)))
            return "A reciprocal fight pair may only have one active steward response.";
        return null;
    }
}

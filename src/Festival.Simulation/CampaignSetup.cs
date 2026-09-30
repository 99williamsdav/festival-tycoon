namespace Festival.Simulation;

public sealed partial class GameSession
{
    /// <summary>
    /// A fresh Tier 1 campaign on the ordinary New Game route. Each stage adds one
    /// system in a fixed order; the order also fixes every entity identity.
    /// </summary>
    public static GameSession CreateBuildCampaign(ulong seed) => CreateBuildCampaign(seed, TierOneGuests);

    /// <summary>A Build campaign with a non-standard guest count, for scale diagnostics only.</summary>
    internal static GameSession CreateBuildCampaign(ulong seed, int guests)
    {
        var session = CreateFoodAndDrinkBaseline(seed, guests);
        SetUpPerks(session, seed);
        SetUpEditablePlan(session);
        SetUpResults(session);
        SetUpBuild(session);
        return session;
    }

    /// <summary>The campaign through its food-and-drink stage: the baseline a retried edition resets to.</summary>
    private static GameSession CreateFoodAndDrinkBaseline(ulong seed, int guests = TierOneGuests)
    {
        var session = CreateProgrammeBaseline(seed, guests);
        SetUpFoodAndDrink(session, seed);
        return session;
    }

    /// <summary>The campaign through its programme stage: the saved roster's reference identities.</summary>
    private const int TierOneGuests = 20;

    private static GameSession CreateProgrammeBaseline(ulong seed, int guests = TierOneGuests)
    {
        const int tier = 1;
        var session = CreateCampaign(seed);
        SetUpEdition(session, seed, tier, guests);
        SetUpStagePower(session);
        SetUpHotWeather(session);
        SetUpSecurity(session, seed);
        SetUpProgramme(session, seed);
        return session;
    }

    /// <summary>The edition roster, stock and preparation plan on the inherited farm.</summary>
    private static void SetUpEdition(GameSession session, ulong seed, int tier, int guests)
    {
        session.Phase = SessionPhase.OpeningCheck;
        // Retain campaign identity, inherited farm and opening loan. The old planning-week
        // shell is dormant in this route; it remains available for legacy saves/fixtures.
        var owner = session._festivalFinances.Keys.Single();
        var stock = new EntityId(session.NextEntityId++);
        session._ownedStocks.Add(stock, new OwnedStockState
        { ServiceId = stock, OwnerId = owner, Quantity = 40, UnitCostBasisPennies = 60 });
        var people = Enumerable.Range(0, guests + 4).Select(index => new EditionPerson(
            session.NextEntityId++, index < guests ? $"Guest {index + 1:00}" : index == guests ? "Casey Vale" : new[] { "Alex Reed", "Blair Moss", "Kit Rowan" }[index - guests - 1],
            index < guests ? ProtectedPersonRole.Guest : index == guests ? ProtectedPersonRole.Staff : ProtectedPersonRole.Performer,
            index % 4 == 0 ? 1 - (int)(seed % 2) : (int)(seed % 2))).ToArray();
        foreach (var person in people)
            session._wallets.Add(new(person.AgentId), new WalletState { OwnerId = new(person.AgentId), CashPennies = 500 });
        session.PreparationView = new(1, tier, seed ^ ((ulong)tier * 0x9E3779B97F4A7C15UL), 1,
            PreparationStatus.Preparing, owner.Value, stock.Value, 0,
            [], [], [], [], [], people, [], 0, CampaignDefaults.OpeningCashPennies);
    }

    /// <summary>The trailer stage generator and its overload chain.</summary>
    private static void SetUpStagePower(GameSession session)
    {
        session._equipment = new(2, EquipmentXMillimetres, EquipmentZMillimetres, 120, 8_000, EquipmentStage.Normal, -1, false,
            "No response", null, MaintenanceStage.None, -1, -1,
            [new("equipment:load", 0, "Stage sound and lights demand 120% of safe capacity; condition 80%. Free load shedding and emergency cutoff available; maintenance contract offered before opening.")]);
    }

    /// <summary>Hot conditions: free water, the baseline medic and every person's thirst and heat.</summary>
    private static void SetUpHotWeather(GameSession session)
    {
        session._equipment = session._equipment! with { Stage = EquipmentStage.Resolved, LoadPercent = 80,
            Response = "Hot scenario baseline: generator load balanced before opening" };
        var medicId = session.NextEntityId++;
        session._wallets.Add(new(medicId), new WalletState { OwnerId = new(medicId), CashPennies = 500 });
        session.PreparationView = session.PreparationView! with { People = session.PreparationView.People.Append(
            new EditionPerson(medicId, "Riley Hart", ProtectedPersonRole.Staff, 0)).ToArray() };
        var guests = session.PreparationView!.People.Where(item => item.Role == ProtectedPersonRole.Guest).ToArray();
        var atRisk = guests[19].AgentId;
        var needs = guests.Select((guest, index) => new MedicalNeed(guest.AgentId,
            index == 19 ? 8_500 : index < 8 ? 7_600 : 2_000 + (index * 43) % 500,
            index == 19 ? 7_500 : 2_500, MedicalIntent.WatchShow,
            index == 19 ? "Strong act interest outweighs early water trip" : "Water need below show preference",
            -MedicalDecisionCooldownTicks, null, -1))
            .Concat(session.PreparationView.People.Where(item => item.Role == ProtectedPersonRole.Performer)
                .Select((person, index) => new MedicalNeed(person.AgentId, 6_900 + index * 100, 6_000 + index * 100,
                    MedicalIntent.WatchShow, "Performing; free water and first aid remain available",
                    -MedicalDecisionCooldownTicks, null, -1, MedicalNeedProfile.Performer))).ToArray();
        session.MedicalView = new(5, true, medicId, atRisk, needs,
            MedicalStage.Clear, MedicalResponseStage.None, null, -1, -1, -1, -1, "No response",
            [new("medical:hot", 0, "Fixed Hot scenario; free water and a baseline medic are available before opening.")]);
        session._facilities = new FacilitiesSnapshot(1, [OpeningMainTap()], [], []);
        session._preparation = session._preparation! with { PrimaryWaterGeometryVersion = 1 };
    }

    /// <summary>The baseline steward and each guest's temperament.</summary>
    private static void SetUpSecurity(GameSession session, ulong seed)
    {
        var securityId = session.NextEntityId++;
        session._wallets.Add(new(securityId), new WalletState { OwnerId = new(securityId), CashPennies = 500 });
        session.PreparationView = session.PreparationView! with { People = session.PreparationView.People.Append(
            new EditionPerson(securityId, "Jordan Hale", ProtectedPersonRole.Staff, 0)).ToArray() };
        session.MedicalView = session.MedicalView! with { Version = 6, Needs = session.MedicalView.Needs.Append(
            new MedicalNeed(securityId, 0, 0, MedicalIntent.WatchShow, "Security on duty", -MedicalDecisionCooldownTicks,
                null, -1, MedicalNeedProfile.Staff)).ToArray() };
        var skills = RandomStreamFactory.Create(seed ^ securityId, RandomStreamId.IndividualBehaviour);
        var people = session.PreparationView!.People.Where(item => item.Role == ProtectedPersonRole.Guest).Select(person =>
        {
            var random = RandomStreamFactory.Create(seed ^ person.AgentId, RandomStreamId.IndividualBehaviour);
            return new DisorderPerson(person.AgentId, 2_000 + (int)(random.NextUInt32() % 6_001),
                320 + (int)(random.NextUInt32() % 801), 0, DisorderGrievance.None, DisorderStage.Calm,
                -1, -1, -1, -1, -1, null);
        }).ToArray();
        session.DisorderView = new(1, securityId, 3_500 + (int)(skills.NextUInt32() % 4_501),
            3_500 + (int)(skills.NextUInt32() % 4_501), false, false, people,
            SecurityResponseStage.None, null, -1, "Security available", []);
    }

    /// <summary>Six performers and the three-slot festival programme.</summary>
    private static void SetUpProgramme(GameSession session, ulong seed)
    {
        var p = session.PreparationView!;
        var people = p.People.Select(person => person.Role == ProtectedPersonRole.Guest ? person with { ExpectedGenre = (int)((person.AgentId * 17 + seed) % 4) } : person).ToList();
        string[] names = ["Robin Shaw", "Ellis Brook", "Taylor Finch", "Ash Dale", "Rowan Lake", "Sky Morgan"];
        foreach (var name in names)
        {
            var id = session.NextEntityId++;
            session._wallets.Add(new(id), new WalletState { OwnerId = new(id), CashPennies = 500 });
            people.Add(new(id, name, ProtectedPersonRole.Performer, 0));
            session.MedicalView = session.MedicalView! with { Needs = session.MedicalView.Needs.Append(new MedicalNeed(id, 2500, 2500, MedicalIntent.WatchShow, "Awaiting set; water and rest available", -MedicalDecisionCooldownTicks, null, -1, MedicalNeedProfile.Performer)).ToArray() };
        }
        session.PreparationView = p with { People = people.OrderBy(person => person.AgentId).ToArray() };
        session.MedicalView = session.MedicalView! with { Needs = session.MedicalView.Needs.Select(need => need.Profile == MedicalNeedProfile.Performer ? need with { Thirst = 2500, HeatExposure = 2500 } : need).OrderBy(need => need.AgentId).ToArray() };
        session._programme = new(4, [], people.Where(person => person.Role == ProtectedPersonRole.Performer).Select((person, i) => new ProgrammePerformer(person.AgentId, i / 3, i % 3)).ToArray(), -1, -1, "Choose three acts");
    }

    /// <summary>Vendors, the toilet, stock and each person's tastes and budget.</summary>
    private static void SetUpFoodAndDrink(GameSession session, ulong seed)
    {
        session.ImmersionView = NewImmersion(seed, session.PreparationView!.People);
        session.SetVendors(OpeningVendors().Select(NewLooseVendor).ToArray());
        session.SetToilets([OpeningMainToilet()]);
        session.SynchronizeImmersionPeople();
        foreach (var person in session.PeopleIn(PersonView.Consumption)) session._wallets[new(person.Id)].CashPennies = person.OpeningBudgetPennies;
    }

    /// <summary>The perk deck and opening draft.</summary>
    private static void SetUpPerks(GameSession session, ulong seed)
    {
        var rng = RandomStreamFactory.Create(seed ^ 0x5045524B44524146UL, RandomStreamId.ArtistDecisions);
        session._perks = new(1, [], 0, false, false, [], rng.State, rng.Increment, 0, false, []);
        session.OpenPerkDraft();
    }

    /// <summary>The unpaid, editable preparation plan.</summary>
    private static void SetUpEditablePlan(GameSession session)
    {
        session._preparation = session._preparation! with { Version = 2, Plan = EmptyPreparationPlan(), SetupPayments = [] };
    }

    /// <summary>Newspaper and Accounts metrics.</summary>
    private static void SetUpResults(GameSession session)
    {
        session._preparation = session._preparation! with { FinishedBeerIds = [], GuestMedicalCollapses = 0 };
    }

    /// <summary>The editable Build layout and staggered guest arrivals.</summary>
    private static void SetUpBuild(GameSession session)
    {
        session._preparation = session._preparation! with { BuildPlacements = [] };
        session.ApplyBuildGuestOpeningNeeds();
        session.SyncBuildPhysicalLayout();
    }
}

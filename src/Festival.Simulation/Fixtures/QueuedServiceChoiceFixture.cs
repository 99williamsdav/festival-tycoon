namespace Festival.Simulation;

/// <summary>Labelled deterministic diagnostic only; it is not an ordinary festival start.</summary>
public sealed record QueuedServiceChoiceFixtureResult(GameSession Session, string ToiletFrom, string ToiletTo,
    string WaterFrom, string WaterTo, bool ToiletOwnerRetained, bool OldToiletPlaceForfeited,
    bool WaterOwnerRetained, bool OldWaterPlaceForfeited, string AuthoritativeHash);

public sealed partial class GameSession
{
    public static QueuedServiceChoiceFixtureResult CreateQueuedServiceChoiceFixture()
    {
        var session = CreateBuildCampaign(20260929);
        static void Accept(GameSession s, SessionCommand command)
        {
            var result = s.Execute(new(new(s.NextSubmissionSequence + 1), s.CampaignId, s.Phase,
                s.CurrentTick, s.NextSubmissionSequence, null, command));
            if (!result.IsAccepted) throw new InvalidOperationException($"Queue-choice fixture command rejected: {result.Message}");
        }
        var perk = session.CapturePerks()!;
        Accept(session, new ChoosePerkCommand(perk.DraftAttempt, perk.Cursor, perk.Hand[0]));
        Accept(session, new UseDefaultBuildLayoutCommand());
        Accept(session, new PlaceBuildServiceCommand(BuildServiceKind.Toilet, new(140, 160), 2));
        GridCell? extraTap = null;
        for (var x = 70; x <= 125 && extraTap is null; x += 5)
        for (var z = 115; z <= 170 && extraTap is null; z += 5)
        {
            var cell = new GridCell(x, z);
            var command = new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, cell);
            if (session.ValidateCommand(new(new(session.NextSubmissionSequence + 1), session.CampaignId,
                session.Phase, session.CurrentTick, session.NextSubmissionSequence, null, command)) is null) extraTap = cell;
        }
        if (extraTap is null) throw new InvalidOperationException("Queue-choice fixture has no second tap site.");
        Accept(session, new PlaceBuildServiceCommand(BuildServiceKind.WaterTap, extraTap.Value));
        Accept(session, new SetProgrammeCommand(["act.meadow-lanterns", "act.barnstorm-circuit", "act.neon-postcards"]));
        Accept(session, new AcceptPreparationOfferCommand("staff.steward"));
        Accept(session, new AcceptPreparationOfferCommand("equipment.rent"));
        Accept(session, new StartPreparedEditionCommand());

        var guests = session.PeopleIn(PersonView.Roster).Where(person => person.Role == ProtectedPersonRole.Guest)
            .Select(person => person.Id).ToArray();
        var reviewIds = guests.Where(id => id % QueuedServiceChoice.ReviewStagger ==
            (ulong)(session.CurrentTick % QueuedServiceChoice.ReviewStagger)).ToArray();
        if (reviewIds.Length < 2) throw new InvalidOperationException("Queue-choice fixture needs two due reviewers.");
        var toiletSeeker = reviewIds[0]; var waterSeeker = reviewIds[1];
        var remaining = guests.Where(id => id != toiletSeeker && id != waterSeeker &&
            id % QueuedServiceChoice.ReviewStagger != (ulong)(session.CurrentTick % QueuedServiceChoice.ReviewStagger)).ToArray();
        var toiletAhead = remaining.Take(5).ToArray();
        var waterAhead = remaining.Skip(5).Take(5).ToArray();
        var toiletMembers = toiletAhead.Append(toiletSeeker).ToArray();
        var waterMembers = waterAhead.Append(waterSeeker).ToArray();
        foreach (var id in toiletMembers.Concat(waterMembers))
            session.UpdatePerson(id, person => person with { Admitted = true });

        var mainToilet = session.GetToilet("toilet.main");
        session.SetToilet(mainToilet with { Queue = toiletMembers, OwnerId = toiletAhead[0],
            DoorOpen = false, ServiceTicks = ToiletRules.PooServiceTicks });
        foreach (var person in session.PeopleIn(PersonView.Consumption).Where(person => toiletMembers.Contains(person.Id)))
            session._persons.Set(person with { ToiletNeed = 9_000,
                ToiletId = mainToilet.Id, ToiletStage = person.Id == toiletAhead[0]
                    ? ToiletVisitStage.Using : ToiletVisitStage.Queued,
                ToiletChoice = person.Id == toiletSeeker ? ToiletVisitKind.Wee : ToiletVisitKind.Poo });
        PlaceFixtureAgent(session, toiletAhead[0], ToiletInsideCell(mainToilet), "toilet.enter");
        for (var index = 1; index < toiletMembers.Length; index++)
            PlaceFixtureAgent(session, toiletMembers[index], ToiletQueueCell(mainToilet, index), "toilet.queue");

        var mainWater = session.WaterPoints().Single(point => point.Id == "water.main");
        session.SetWaterPoint(mainWater with { Queue = waterMembers, Overflow = [], OwnerId = waterAhead[0], DrinkTicks = 0 });
        if (!session.GrowWaterQueue(mainWater.Id)) throw new InvalidOperationException("Queue-choice fixture could not grow physical water line.");
        mainWater = session.WaterPoints().Single(point => point.Id == "water.main");
        foreach (var need in session.PeopleIn(PersonView.Medical).Where(need => waterMembers.Contains(need.Id)))
            session._persons.Set(need with { Thirst = 9_000, WaterPointId = mainWater.Id,
                Intent = need.Id == waterAhead[0] ? MedicalIntent.Drinking : MedicalIntent.SeekWater,
                WaterQueueSlot = Array.IndexOf(waterMembers, need.Id), LastWaterChoiceReviewTick = -160 });
        for (var index = 0; index < waterMembers.Length; index++)
            PlaceFixtureAgent(session, waterMembers[index], WaterSlot(mainWater, index), "medical.free-water-queue");

        // The activity chooser decides whether a clearly shorter line is worth forfeiting a place for.
        session.ChooseActivity(waterSeeker);
        session.ChooseActivity(toiletSeeker);
        var toiletTo = session._persons[toiletSeeker].ToiletId!;
        var waterTo = session._persons[waterSeeker].WaterPointId;
        Accept(session, new SetPausedCommand(true)); // freeze this labelled visual diagnostic
        return new(session, mainToilet.Id, toiletTo, mainWater.Id, waterTo,
            session.GetToilet(mainToilet.Id).OwnerId == toiletAhead[0],
            !session.GetToilet(mainToilet.Id).Queue.Contains(toiletSeeker),
            session.WaterPoints().Single(point => point.Id == mainWater.Id).OwnerId == waterAhead[0],
            !session.WaterPoints().Single(point => point.Id == mainWater.Id).Queue.Contains(waterSeeker),
            session.CaptureSnapshot().AuthoritativeHash);
    }

    private static void PlaceFixtureAgent(GameSession session, ulong id, GridCell cell, string intent)
    {
        var nav = session._navigationAgents[new(id)];
        var centre = TraversalGrid.CellCentre(cell);
        nav.XMillimetres = nav.SegmentOriginXMillimetres = centre.XMillimetres;
        nav.ZMillimetres = nav.SegmentOriginZMillimetres = centre.ZMillimetres;
        nav.Route = []; nav.RouteIndex = 0; nav.SegmentProgressMicrometres = 0;
        nav.Action = AgentNavigationAction.Arrived; nav.Destination = cell; nav.IntentId = intent;
    }
}

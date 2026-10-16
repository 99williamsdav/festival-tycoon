using Festival.Simulation;

namespace Festival.Tests;

[TestClass]
public sealed class ClassicDevelopmentTests
{
    internal static void StaffAndSite(GameSession s)
    {
        BuildSession.Accept(s,new UseDefaultBuildLayoutCommand());
        var sounds=s.GetStaffCandidates().Where(c=>c.Role==StaffRole.Sound).OrderBy(c=>c.Traits.Length).ThenBy(c=>c.WagePennies).Take(s.Stages.Count).ToArray();
        for(var stage=0;stage<s.Stages.Count;stage++)BuildSession.Accept(s,new AcceptPreparationOfferCommand(GameSession.StageSoundOfferId(sounds[stage],stage)));
        foreach(var c in s.GetStaffCandidates().Where(c=>c.Role!=StaffRole.Sound).GroupBy(c=>c.Role).Select(g=>g.OrderBy(c=>c.Traits.Length).ThenBy(c=>c.WagePennies).First()))
            BuildSession.Accept(s,new AcceptPreparationOfferCommand(c.Id));
        BuildSession.Accept(s,new AcceptPreparationOfferCommand("maintenance.worker"));
        BuildSession.Accept(s,new SetPreparationStockCommand(160,80));
    }
    private static GameSession RoundTrip(GameSession s)
    {
        var restored=GameSession.Restore(s.CapturePersistenceSnapshot());
        Assert.IsTrue(restored.IsSuccess,restored.Error);
        Assert.AreEqual(s.CaptureSnapshot().AuthoritativeHash,restored.Session!.CaptureSnapshot().AuthoritativeHash);
        return restored.Session;
    }
    internal static void BookAll(GameSession s)
    {
        var perk=s.CapturePerks()!;if(perk.Pending)BuildSession.Accept(s,new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
        var acts=s.GetFestivalActs().Where(a=>s.ActStandingOf(a)!=ActStanding.Locked).OrderBy(s.ActFee).ThenBy(a=>a.Id,StringComparer.Ordinal)
            .Take(s.Stages.Count*3).Select(a=>a.Id).ToArray();
        Assert.AreEqual(s.Stages.Count*3,acts.Length);
        for(var stage=0;stage<s.Stages.Count;stage++)BuildSession.Accept(s,new SetProgrammeCommand(acts.Skip(stage*3).Take(3).ToArray()){StageId=s.Stages[stage].Id});
        BuildSession.Accept(s,new ConfirmClassicBookingsCommand());
    }
    [TestMethod]
    [DataRow(1)][DataRow(2)][DataRow(3)][DataRow(4)][DataRow(5)][DataRow(6)][DataRow(7)]
    public void EveryNonemptyStageSubsetHasOnlyItsRealBandsAndGeneratorsAndRestores(int mask)
    {
        var s=GameSession.CreateClassicDevelopmentStart(mask);var p=s.CapturePreparation()!;
        Assert.IsTrue(s.IsClassicDevelopment);Assert.AreEqual(2,s.FestivalNumber);Assert.AreEqual(40,p.Reputation);
        Assert.AreEqual(26190L,p.OpeningCashPennies);Assert.AreEqual(26190L,p.CarriedIn!.CashPennies);
        Assert.AreEqual(14,s.CaptureClassicHistory()!.CompletedFestivals.Single().ClosingStanding.Reputation);
        Assert.AreEqual(3,p.SeenActs.Length);Assert.AreEqual(3,p.RelationshipHistory!.Length);
        var minimum=s.GetStaffCandidates().GroupBy(c=>c.Role).Sum(g=>g.OrderBy(c=>c.WagePennies).Take(g.Key==StaffRole.Sound?s.Stages.Count:1).Sum(c=>(long)c.WagePennies))+
            new[]{BuildServiceKind.WaterTap,BuildServiceKind.Toilet,BuildServiceKind.FirstAid,BuildServiceKind.StewardPost}.Sum(GameSession.BuildServiceFeePennies);
        Assert.AreEqual(minimum,s.ClassicBookingEstimate!.MinimumRequiredSitePennies);
        Assert.AreEqual(100,p.People.Count(p=>p.Role==ProtectedPersonRole.Guest));
        Assert.AreEqual(9*s.Stages.Count,p.People.Count(p=>p.Role==ProtectedPersonRole.Performer));
        Assert.AreEqual(s.Stages.Count+2,p.People.Count(p=>p.Role==ProtectedPersonRole.Staff));
        CollectionAssert.AreEqual(s.Stages.Where(stage=>stage.Id!=FestivalStages.MainId).Select(stage=>stage.Id).ToArray(),s.CaptureStageGenerators().Select(g=>g.StageId).ToArray());
        s=RoundTrip(s);BookAll(s);s=RoundTrip(s);
        Assert.AreEqual(s.Stages.Count*3,s.CaptureClassicBookingDetails()!.Contracts.Length);
        BuildSession.Accept(s,new UseDefaultBuildLayoutCommand());
        var candidates=s.GetStaffCandidates().Where(c=>c.Role==StaffRole.Sound).OrderBy(c=>c.WagePennies).Take(s.Stages.Count).ToArray();
        for(var stage=0;stage<s.Stages.Count;stage++)BuildSession.Accept(s,new AcceptPreparationOfferCommand(GameSession.StageSoundOfferId(candidates[stage],stage)));
        foreach(var c in s.GetStaffCandidates().Where(c=>c.Role!=StaffRole.Sound).GroupBy(c=>c.Role).Select(g=>g.OrderBy(c=>c.WagePennies).First()))
            BuildSession.Accept(s,new AcceptPreparationOfferCommand(c.Id));
        Assert.AreEqual(0,s.GetPreparationStartBlockers().Count,string.Join("; ",s.GetPreparationStartBlockers().Select(b=>b.Message)));
        s=RoundTrip(s);BuildSession.Accept(s,new StartPreparedEditionCommand());RoundTrip(s);
        for(var stage=0;stage<s.Stages.Count;stage++)
            if(s.Stages[stage].MixingPlace is {} mixing)Assert.AreEqual(mixing,s.StaffAssignedPost(s.StageEngineerId(s.Stages[stage].Id)!.Value));
    }
    [TestMethod]
    [DataRow(false,false)][DataRow(true,false)][DataRow(false,true)][DataRow(true,true)]
    public void ExplicitDefaultsOpenAllThreeStagesAndKeepOrdinaryCosts(bool guarantee,bool cash)
    {
        var s=GameSession.CreateClassicDevelopmentTestDefaults(guarantee,cash);
        Assert.AreEqual(3,s.Stages.Count);Assert.AreEqual(9,s.CaptureClassicBookingDetails()!.Contracts.Length);
        Assert.AreEqual(3,Enumerable.Range(0,3).Count(stage=>s.ChosenStageEngineer(stage) is not null));
        Assert.IsTrue(s.PreparationPlanCost>0);Assert.IsTrue(s.BuildDraftCost>0);
        Assert.AreEqual(0,s.GetPreparationStartBlockers().Count,string.Join("; ",s.GetPreparationStartBlockers().Select(b=>b.Message)));
        RoundTrip(s);BuildSession.Accept(s,new StartPreparedEditionCommand());RoundTrip(s);
    }
    [TestMethod]
    [DataRow(false,false)][DataRow(true,false)][DataRow(false,true)][DataRow(true,true)]
    public void DiagnosticGuaranteeAndOneTimeCashGrantReplayIndependently(bool guarantee,bool cash)
    {
        var s=GameSession.CreateClassicDevelopmentStart(7,guarantee,cash);
        Assert.AreEqual(cash?150000L:26190L,s.CapturePersistentCampaignState()!.CashPennies);
        var perk=s.CapturePerks()!;BuildSession.Accept(s,new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
        BuildSession.Accept(s,new SetClassicTicketPriceCommand(5000));BookAll(s);
        Assert.AreEqual(guarantee?100:0,s.CaptureClassicBookingDetails()!.TicketHolders);
        var confirmed=RoundTrip(s);var finance=confirmed.CapturePersistentCampaignState()!.CashPennies;
        var b=confirmed.CaptureClassicBookingDetails()!;
        Assert.AreEqual((cash?150000L:26190L)+b.CashChangePennies,finance);
        BuildSession.Accept(confirmed,new CancelClassicFestivalCommand());
        Assert.AreEqual(cash?150000L:26190L,confirmed.CapturePersistentCampaignState()!.CashPennies);
        Assert.AreEqual(35,confirmed.CapturePreparation()!.Reputation);RoundTrip(confirmed);
    }
    [TestMethod]
    public void DiagnosticProvenanceCannotBeRemovedOrAttachedToOrdinaryCampaigns()
    {
        var s=GameSession.CreateClassicDevelopmentStart(7);var snapshot=s.CapturePersistenceSnapshot();
        foreach(var bad in new[]{snapshot with{ClassicDevelopment=null},snapshot with{Identity=CampaignIdentity.Management},
            snapshot with{ClassicDevelopment=snapshot.ClassicDevelopment! with{StageMask=0}},
            snapshot with{ClassicDevelopment=snapshot.ClassicDevelopment! with{TopUpCash=true}}})
            Assert.IsFalse(GameSession.Restore(bad).IsSuccess);
        var ordinary=GameSession.CreateClassicCampaign(20260922).CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(ordinary with{ClassicDevelopment=snapshot.ClassicDevelopment}).IsSuccess);
    }
    [TestMethod]
    public void NineSlotRebookingRefundsOnlyTheHighestImpactWithoutRepeatingGrants()
    {
        var s=GameSession.CreateClassicDevelopmentTestDefaults(true,true);var booking=s.CaptureClassicBookingDetails()!;
        var opening=s.CapturePreparation()!.OpeningCashPennies;
        var ids=booking.Contracts.Select(a=>a.ActId).ToArray();ids[8]="";
        BuildSession.Accept(s,new RebookClassicProgrammeCommand(ids));
        Assert.AreEqual(40,s.CaptureClassicBookingDetails()!.RefundedTickets);
        Assert.AreEqual(60,s.CapturePreparation()!.People.Count(p=>p.Role==ProtectedPersonRole.Guest));s=RoundTrip(s);
        ids[8]=booking.Contracts[8].ActId;BuildSession.Accept(s,new RebookClassicProgrammeCommand(ids));s=RoundTrip(s);
        Assert.AreEqual(40,s.CaptureClassicBookingDetails()!.RefundedTickets);
        BuildSession.Accept(s,new CancelClassicFestivalCommand());RoundTrip(s);
        Assert.AreEqual(opening,s.CapturePersistentCampaignState()!.CashPennies);
    }
    [TestMethod]
    public void PerformanceObservationsDoNotChangeGameplayOrSavedHashes()
    {
        var s=GameSession.CreateClassicDevelopmentTestDefaults(true,true);BuildSession.Accept(s,new StartPreparedEditionCommand());
        var other=RoundTrip(s);s.PerformanceTimingsEnabled=true;s.AdvanceWithoutSnapshot(800);other.AdvanceWithoutSnapshot(800);
        Assert.IsTrue(s.CapturePerformanceTimings().Any(t=>t.Calls>0));Assert.AreEqual(0,other.CapturePerformanceTimings().Count);
        Assert.AreEqual(other.CaptureSnapshot().AuthoritativeHash,s.CaptureSnapshot().AuthoritativeHash);RoundTrip(s);
    }
    [TestMethod]
    public void EveryHundredAttendeeOriginHasAWalkableRouteToTheGate()
    {
        var s=GameSession.CreateClassicDevelopmentStart(7,true,true);BookAll(s);StaffAndSite(s);
        BuildSession.Accept(s,new StartPreparedEditionCommand());
        var people=s.CapturePreparation()!.People.ToDictionary(p=>p.AgentId);
        var blocked=s.CaptureSnapshot().NavigationAgents.Select(a=>(Agent:a,Cell:TraversalGrid.WorldToCell(a.XMillimetres,a.ZMillimetres)))
            .Where(a=>!s.TraversalGrid!.Get(a.Cell).IsWalkable||!DeterministicPathfinder.FindPath(s.TraversalGrid!,a.Cell,GameSession.MedicalExitCell).Found)
            .Select(a=>$"{a.Agent.Id.Value} {people[a.Agent.Id.Value].Role} {a.Cell}").ToArray();
        Assert.AreEqual(0,blocked.Length,string.Join("; ",blocked));
        for(var index=0;index<s.CapturePreparation()!.People.Length;index++)
        {
            var destination=(GridCell)typeof(GameSession).GetMethod("PreparedPlace",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic)!.Invoke(s,[index])!;
            Assert.IsTrue(DeterministicPathfinder.FindPath(s.TraversalGrid!,GameSession.MedicalExitCell,destination).Found,$"Arrival destination {index}: {destination}");
        }
    }
    [TestCategory("Slow")]
    [TestMethod]
    public void HundredAttendeesAndThreeStagesCompleteAndCarryThroughASecondPaidFestival()
    {
        var s=GameSession.CreateClassicDevelopmentStart(7,true,true);var evidence=new List<object>();
        for(var festival=2;festival<=3;festival++)
        {
            BookAll(s);StaffAndSite(s);s=RoundTrip(s);
            BuildSession.Accept(s,new StartPreparedEditionCommand());
            Assert.AreEqual(100,s.CapturePreparation()!.People.Count(p=>p.Role==ProtectedPersonRole.Guest));
            Assert.AreEqual(133,s.CapturePreparation()!.People.Length);
            s.AdvanceWithoutSnapshot(11000);s=RoundTrip(s);
            var overlapping=s.CaptureLivePerformances().Select(l=>new {l.StageId,l.Stage,ready=l.Performers.Count(p=>p.OnStage)}).ToArray();
            for(var elapsed=11000;elapsed<65000&&s.PreparedStatus!=PreparationStatus.Finished;elapsed+=800)s.AdvanceWithoutSnapshot(800);
            var p=s.CapturePreparation()!;
            evidence.Add(new {festival,status=p.Status,tick=s.CurrentTick,guests=p.People.Count(p=>p.Role==ProtectedPersonRole.Guest),
                total=p.People.Length,admitted=p.People.Count(p=>p.Admitted),departed=p.People.Count(p=>p.Departed),
                overlapping,result=p.Result,records=s.PerformanceRecords,accounts=s.CompletedFestivalAccounts,
                noRoute=s.CaptureSnapshot().NavigationAgents.Where(a=>a.Action==AgentNavigationAction.NoRoute).Select(a=>new {id=a.Id.Value,a.XMillimetres,a.ZMillimetres,a.Destination,a.IntentId,
                    person=p.People.Single(p=>p.AgentId==a.Id.Value)}).ToArray()});
            if(Environment.GetEnvironmentVariable("FESTIVAL_CLASSIC_STRESS_EVIDENCE_ROOT") is {Length:>0} output)
            {Directory.CreateDirectory(output);File.WriteAllText(Path.Combine(output,"two-festivals.json"),System.Text.Json.JsonSerializer.Serialize(evidence,new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));}
            Assert.AreEqual(PreparationStatus.Finished,p.Status);Assert.IsTrue(p.People.All(p=>p.Admitted&&p.Departed));
            Assert.AreEqual(100,p.Result!.AdmittedGuests);Assert.AreEqual(9,s.PerformanceRecords.Count);
            s=RoundTrip(s);var cash=s.CapturePersistentCampaignState()!.CashPennies;
            s=s.CreateNextFestival();Assert.AreEqual(cash,s.CapturePreparation()!.OpeningCashPennies,"No second cash grant.");
            RoundTrip(s);
        }
    }
}

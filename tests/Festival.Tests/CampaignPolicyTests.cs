using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class CampaignPolicyTests
{
    [TestMethod]
    public void PersistentCampaignViewReusesHoldingsWithoutOwningFestivalSetupOrMutatingAuthority()
    {
        var session=GameSession.CreateRecordedTierTwoStart();var before=session.CaptureSnapshot().AuthoritativeHash;
        var state=session.CapturePersistentCampaignState()!;
        Assert.AreEqual(112260L,state.CashPennies);Assert.AreEqual(20000L,state.Loan.OutstandingPrincipalPennies);Assert.AreEqual(14,state.Standing.Reputation);
        Assert.AreEqual(3,state.SeenActs.Length);Assert.AreEqual(3,state.EarlierGigs.Length);Assert.AreEqual(1,state.FavourBalance);Assert.IsFalse(state.CommunityWaterShareUsed);
        Assert.AreEqual(0,session.CapturePreparation()!.AcceptedOffers.Length);
        state.Standing.SceneCredibility[0]=99;state.SeenActs[0]="changed-view";
        foreach(var gig in state.EarlierGigs.Where(g=>g.Hiccups.Length>0))gig.Hiccups[0]="changed-view";
        Assert.AreEqual(before,session.CaptureSnapshot().AuthoritativeHash,"The derived view must not become another authority.");
        Assert.AreEqual(10,session.CapturePersistentCampaignState()!.Standing.SceneCredibility[0]);
        Assert.AreEqual(new IncidentTimingParameters(1600,800,2400,2400,2400,3600,4800),session.IncidentTiming,"Existing danger timings are unchanged.");
    }
    private static void KillAtNextMedicalTick(GameSession session)
    {
        var patient=session.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Performer&&p.Admitted);var tick=session.CurrentTick;
        typeof(GameSession).GetMethod("UpdatePerson",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(session,[patient.AgentId,(Func<Person,Person>)(p=>p with{HealthStage=MedicalStage.Critical,Thirst=10000,HeatExposure=10000,Intent=MedicalIntent.Collapsed,HealthWarningTick=tick-4000,HealthCollapseTick=tick-GameSession.MedicalDeathDelayTicks,HealthCriticalTick=tick-1})]);
        session.AdvanceWithoutSnapshot(1);Assert.AreEqual(PreparationStatus.Failed,session.PreparedStatus);
    }
    private static GameSession RainFatal()
    {
        var session=NextFestivalTests.Ready(GameSession.CreateDevelopmentFestival(20260922,2));BuildSession.Accept(session,new StartPreparedEditionCommand());
        session.AdvanceWithoutSnapshot(8000);KillAtNextMedicalTick(session);return session;
    }
    private static string? ShelterIssue(SessionPersistenceSnapshot snapshot)=>
        (string?)typeof(GameSession).GetMethod("ValidatePersistedShelter",BindingFlags.Static|BindingFlags.NonPublic)!.Invoke(null,[snapshot,snapshot.Preparation!]);
    [TestMethod]
    public void FrozenRainShelterRestoresWithoutChangingRoutesAndResolvedHearingsAlsoRestore()
    {
        var session=RainFatal();var saved=session.CapturePersistenceSnapshot();Assert.IsTrue(saved.NavigationAgents!.Any(n=>n.IntentId=="weather.shelter"),"Actual shelter navigation must be retained by the death.");
        Assert.IsNull(ShelterIssue(saved));var restored=GameSession.Restore(saved);Assert.IsTrue(restored.IsSuccess,restored.Error);
        Assert.AreEqual(saved.AuthoritativeHash,restored.Session!.CapturePersistenceSnapshot().AuthoritativeHash);
        session.AdvanceWithoutSnapshot(800);Assert.AreEqual(saved.AuthoritativeHash,session.CaptureSnapshot().AuthoritativeHash,"A fatal Survival day remains frozen.");
        var concede=restored.Session;BuildSession.Accept(concede,new ConcedeCouncilHearingCommand());BuildSession.Restored(concede);
        BuildSession.Accept(session,new SpendCouncilFavourCommand());var perk=session.CapturePerks()!;BuildSession.Accept(session,new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
        BuildSession.Accept(session,new StartPreparedEditionCommand());session.AdvanceWithoutSnapshot(8000);KillAtNextMedicalTick(session);
        Assert.AreEqual(2,session.CaptureLifecycleSnapshot()!.Casualties.Count);Assert.AreEqual(HearingStatus.LostNoFavour,session.CaptureLifecycleSnapshot()!.Hearings.Last().Status);
        BuildSession.Restored(session);Assert.IsFalse(session.CanStartNextFestival);
    }
    [TestMethod]
    public void FrozenShelterExceptionRequiresTheActualFatalAttemptAndValidUniqueShelterPlaces()
    {
        var saved=RainFatal().CapturePersistenceSnapshot();var life=saved.Lifecycle!;
        foreach(var invalid in new[]{saved with{CurrentTick=saved.CurrentTick+1},saved with{Lifecycle=life with{CurrentAttemptId=life.CurrentAttemptId+1}},saved with{Lifecycle=life with{Casualties=[]}},saved with{Preparation=saved.Preparation! with{Status=PreparationStatus.Preparing}},saved with{Preparation=saved.Preparation! with{Status=PreparationStatus.Finished}}})
            Assert.IsNotNull(ShelterIssue(invalid),"Closed/preparation or fabricated fatal state cannot claim the exception.");
        var agents=saved.NavigationAgents!.ToArray();var sheltered=agents.Select((agent,index)=>(agent,index)).Where(x=>x.agent.IntentId=="weather.shelter").ToArray();Assert.IsTrue(sheltered.Length>=2);
        var bad=agents.ToArray();bad[sheltered[0].index]=sheltered[0].agent with{DestinationX=0,DestinationZ=0};StringAssert.Contains(ShelterIssue(saved with{NavigationAgents=bad})!,"no shelter");
        bad=agents.ToArray();bad[sheltered[1].index]=sheltered[1].agent with{DestinationX=sheltered[0].agent.DestinationX,DestinationZ=sheltered[0].agent.DestinationZ};StringAssert.Contains(ShelterIssue(saved with{NavigationAgents=bad})!,"one place");
    }
}

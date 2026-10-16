using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class CampaignIdentityTests
{
    [TestMethod]
    public void AllCurrentControlsUseSurvivalWithExplicitRolesAndCapabilities()
    {
        var management=GameSession.CreateBuildCampaign(20260922);var handsOn=GameSession.CreateHandsOnCampaign(20260922);var shift=GameSession.CreateStewardShift(20260922);
        Assert.AreEqual(CampaignIdentity.Management,management.Identity);Assert.AreEqual(CampaignIdentity.HandsOn,handsOn.Identity);Assert.AreEqual(CampaignIdentity.StewardOnly,shift.Identity);
        Assert.IsTrue(management.HasManagementCapability&&handsOn.HasManagementCapability);Assert.IsFalse(shift.HasManagementCapability);
        Assert.IsFalse(management.IsHandsOn||management.IsStewardShift);Assert.IsTrue(handsOn.IsHandsOn);Assert.IsTrue(shift.IsStewardShift);
        CollectionAssert.AreEqual(new[]{CampaignRuleset.Survival,CampaignRuleset.Classic},Enum.GetValues<CampaignRuleset>());
        foreach(var session in new[]{management,handsOn,shift})Assert.AreEqual(session.Identity,GameSession.Restore(session.CapturePersistenceSnapshot()).Session!.Identity);
    }
    [TestMethod]
    public void FestivalDefinitionsResolveTheExistingTierParameters()
    {
        foreach(var tier in new[]{1,2})
        {
            var session=GameSession.CreateDevelopmentFestival(20260922,tier);var definition=session.CurrentFestival;
            Assert.AreEqual(tier*25,definition.Attendance);Assert.AreEqual(tier==1?1000:1500,definition.TicketPricePennies);
            Assert.AreEqual(FestivalTickets.RevenuePennies(tier),definition.TicketReceiptsPennies);Assert.AreEqual(session.PreparedEditionDurationTicks,definition.DurationTicks);
            Assert.AreEqual(GameSession.BuildLimits(tier),definition.BuildLimits);Assert.AreEqual(tier==2,definition.ExtraMedicSlot);Assert.AreEqual(tier==2,definition.SecondSoundEngineerRequired);
            Assert.AreEqual(tier==1?9:18,definition.PerformerCount);Assert.AreEqual(tier==1?WeatherKind.Heatwave:WeatherKind.Rain,definition.Weather);
            CollectionAssert.AreEqual(tier==1?new[]{FestivalStages.MainId}:new[]{FestivalStages.MainId,FestivalStages.BarnId},definition.Stages.Select(s=>s.Id).ToArray());
        }
    }
    [TestMethod]
    public void UnsupportedOrMissingIdentityAndImpossibleCapabilityFailRestore()
    {
        var snapshot=GameSession.CreateBuildCampaign(20260922).CapturePersistenceSnapshot();
        foreach(var identity in new CampaignIdentity?[]{null,new((CampaignRuleset)7,PlayerControlRole.Management,true),new(CampaignRuleset.Survival,(PlayerControlRole)7,true),new(CampaignRuleset.Survival,PlayerControlRole.Management,false)})
            Assert.IsFalse(GameSession.Restore(snapshot with{Identity=identity}).IsSuccess);
        Assert.IsFalse(GameSession.Restore(snapshot with{Identity=CampaignIdentity.HandsOn}).IsSuccess,"Identity cannot invent a player steward or bypass roster/hash validation.");
        var shift=GameSession.CreateStewardShift(20260922).CapturePersistenceSnapshot();
        Assert.IsFalse(GameSession.Restore(shift with{Identity=CampaignIdentity.Management}).IsSuccess,"A live shift cannot masquerade as management.");
    }
    private static GameSession Retried(int tier)
    {
        var session=GameSession.CreateStewardShift(20260922,tier);session.AdvanceWithoutSnapshot(6500); // late enough that the radio has taken a call
        var victim=session.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Performer&&session.CapturePerson(p.AgentId)!.Admitted);var tick=session.CurrentTick;
        typeof(GameSession).GetMethod("UpdatePerson",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(session,[victim.AgentId,(Func<Person,Person>)(p=>p with{HealthStage=MedicalStage.Critical,Thirst=10000,HeatExposure=10000,Intent=MedicalIntent.Collapsed,HealthWarningTick=tick-4000,HealthCollapseTick=tick-GameSession.MedicalDeathDelayTicks,HealthCriticalTick=tick-1})]);
        for(var i=0;i<6000&&session.PreparedStatus==PreparationStatus.Running;i++)session.AdvanceWithoutSnapshot(1);
        Assert.AreEqual(PreparationStatus.Failed,session.PreparedStatus);BuildSession.Accept(session,new SpendCouncilFavourCommand());return session;
    }
    [TestMethod]
    [DataRow(1)]
    [DataRow(2)]
    public void ReleasedStewardRetryRestoresAndReopensWithTheSameBehavior(int tier)
    {
        var session=Retried(tier);var saved=session.CapturePersistenceSnapshot();var restored=GameSession.Restore(saved);
        Assert.IsTrue(restored.IsSuccess,restored.Error);Assert.AreEqual(saved.AuthoritativeHash,restored.Session!.CapturePersistenceSnapshot().AuthoritativeHash);
        Assert.AreEqual(CampaignIdentity.StewardOnly,restored.Session.Identity);Assert.IsNull(restored.Session.PlayerStewardId);
        foreach(var current in new[]{session,restored.Session})
        {var perk=current.CapturePerks()!;BuildSession.Accept(current,new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));Assert.AreEqual(0,current.GetPreparationStartBlockers().Count);BuildSession.Accept(current,new StartPreparedEditionCommand());current.AdvanceWithoutSnapshot(800);BuildSession.Restored(current);}
        Assert.AreEqual(session.CaptureSnapshot().AuthoritativeHash,restored.Session.CaptureSnapshot().AuthoritativeHash);
    }
    [TestMethod]
    public void ReleasedRetryValidationStillRejectsActiveOrMalformedShiftState()
    {
        static void Invalid(SessionPersistenceSnapshot snapshot)
        {var result=GameSession.Restore(snapshot);Assert.IsFalse(result.IsSuccess);Assert.IsFalse(result.Error!.StartsWith("Authoritative state hash mismatch",StringComparison.Ordinal),"Semantic validation must reject this before its changed hash.");}
        var saved=Retried(1).CapturePersistenceSnapshot();var shift=saved.StewardShift!;
        foreach(var malformed in new[]{shift with{MoveX=1},shift with{Run=true},shift with{EndedTick=shift.EndedTick+1},shift with{PlayerId=saved.Preparation!.People.First().AgentId},shift with{Autopilot=new()},shift with{Action=new(StewardActionKind.Drink,"water.main",saved.CurrentTick,999999)}})
            Invalid(saved with{StewardShift=malformed});
        var later=saved with{CurrentTick=saved.CurrentTick+800};
        Assert.IsTrue(shift.Calls!.Length>0,"The retained first-attempt radio log must actually be exercised.");
        var calls=shift.Calls.ToArray();calls[0]=calls[0] with{OpenedTick=shift.EndedTick!.Value+1,ClosedTick=shift.EndedTick.Value+2};Invalid(later with{StewardShift=shift with{Calls=calls}});
        Invalid(later with{StewardShift=shift with{LastOutcome=new(StewardActionKind.Drink,"water.main",true,true,shift.EndedTick!.Value+1,"fabricated after release")}});
        var active=GameSession.CreateStewardShift(20260922).CapturePersistenceSnapshot();
        Invalid(active with{StewardShift=null});Invalid(active with{TraversalGrid=null,NavigationAgents=null});
    }
}

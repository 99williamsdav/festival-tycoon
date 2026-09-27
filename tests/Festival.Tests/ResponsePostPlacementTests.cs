using Festival.Persistence;
using Festival.Simulation;
using System.Reflection;

namespace Festival.Tests;

[TestClass]
public sealed class ResponsePostPlacementTests
{
    private static CommandEnvelope Envelope(GameSession s,SessionCommand c)=>new(new CommandId(s.NextSubmissionSequence+1),s.CampaignId,s.Phase,s.CurrentTick,s.NextSubmissionSequence,null,c);
    private static void Accept(GameSession s,SessionCommand c){var result=s.Execute(Envelope(s,c));Assert.IsTrue(result.IsAccepted,result.Message);}
    private static string Hash(GameSession s)=>s.CaptureSnapshot().AuthoritativeHash;
    private static void Ready(GameSession s)
    {
        Accept(s,new AcceptPreparationOfferCommand("staff.steward"));
        Accept(s,new SetProgrammeCommand(["act.meadow-lanterns","act.barnstorm-circuit","act.field-frequency"]));
    }
    [TestMethod]
    public void MoveRotationValidationIsPureAndFieldsHashAndRestore()
    {
        var s=GameSession.CreateImmersionCampaign(20260922);var original=Hash(s);
        foreach(var role in Enum.GetValues<ResponseRole>())foreach(var turns in Enumerable.Range(0,4))
        {
            var cell=role==ResponseRole.Medic?new GridCell(80,120):new GridCell(145,170);
            var command=new MoveResponsePostCommand(role,cell,turns);
            Assert.IsNull(s.ValidateCommand(Envelope(s,command)));
            var before=Hash(s);s.ValidateCommand(Envelope(s,command));Assert.AreEqual(before,Hash(s));
            Accept(s,command);Assert.AreNotEqual(before,Hash(s));
            Assert.AreEqual(new ResponsePostPlacement(cell,turns),s.CaptureResponsePost(role));
            var restored=GameSession.Restore(s.CapturePersistenceSnapshot());Assert.IsTrue(restored.IsSuccess,restored.Error);
            Assert.AreEqual(Hash(s),Hash(restored.Session!));
        }
        Assert.AreNotEqual(original,Hash(s));
    }
    [TestMethod]
    public void InvalidSitesPhaseAndReciprocalPlacementRejectWithoutMutation()
    {
        var s=GameSession.CreateImmersionCampaign(20260922);
        foreach(var cell in new[]{new GridCell(0,0),new GridCell(95,150),s.CaptureImmersion()!.Vendors[0].Cell,GameSession.MedicalWaterCell,GameSession.MedicalRestCell,GameSession.DisorderSecurityPostCell})
        {
            var before=Hash(s);Assert.IsFalse(s.Execute(Envelope(s,new MoveResponsePostCommand(ResponseRole.Medic,cell,0))).IsAccepted);Assert.AreEqual(before,Hash(s));
        }
        Accept(s,new MoveResponsePostCommand(ResponseRole.Medic,new(80,120),1));
        var blockedFront=s.Execute(Envelope(s,new MoveResponsePostCommand(ResponseRole.Steward,new(121,175),1)));
        Assert.IsFalse(blockedFront.IsAccepted,"The solid post fits grass but its rotated front lands on the protected track.");
        var home=GameSession.ResponsePostHome(s.CapturePreparation(),ResponseRole.Medic);
        Assert.IsFalse(s.Execute(Envelope(s,new MoveWaterPointCommand("water.main",home))).IsAccepted);
        Assert.IsFalse(s.Execute(Envelope(s,new PlaceImmersionVendorCommand("food",new(80,120)))).IsAccepted);
        var snapshot=s.CapturePersistenceSnapshot();
        var corrupt=snapshot with {Preparation=snapshot.Preparation! with{FirstAidPlacement=new(new(0,0),0)}};
        Assert.IsFalse(GameSession.Restore(corrupt).IsSuccess);
        corrupt=snapshot with {Preparation=snapshot.Preparation! with {FirstAidPlacement=new(new(80,120),4),PrimaryWaterCell=new(70,120),PrimaryWaterGeometryVersion=1},Medical=snapshot.Medical! with {MainWaterCell=new(70,120),MainWaterGeometryVersion=1}};
        Assert.IsFalse(GameSession.Restore(corrupt).IsSuccess,"Malformed orientation must return a controlled rejection.");
        Ready(s);Accept(s,new StartPreparedEditionCommand());var hash=Hash(s);
        Assert.IsFalse(s.Execute(Envelope(s,new MoveResponsePostCommand(ResponseRole.Medic,new(80,125),2))).IsAccepted);Assert.AreEqual(hash,Hash(s));
    }
    [TestMethod]
    public void RotatedPaidAndBaselineStaffArriveInFrontAndPhysicalDispatchContinuesAcrossSave()
    {
        var s=GameSession.CreateImmersionCampaign(20260922);
        foreach(var role in Enum.GetValues<ResponseRole>())
        {
            Accept(s,new ApplyStaffFoundationEffectCommand(role==ResponseRole.Medic?"staff.medic-slot":"staff.steward-slot"));
            Accept(s,new AcceptPreparationOfferCommand(role==ResponseRole.Medic?"staff.extra-medic":"staff.extra-steward"));
            Accept(s,new MoveResponsePostCommand(role,role==ResponseRole.Medic?new(80,120):new(145,170),1));
        }
        Ready(s);Accept(s,new StartPreparedEditionCommand());s.AdvanceWithoutSnapshot(1800);
        foreach(var worker in s.GetResponseStaff())
        {
            var nav=s.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==worker.AgentId);
            var home=GameSession.ResponsePostHome(s.CapturePreparation(),worker.Role,worker.Name is "Avery Brooks" or "Sam Ellis");
            var destination=s.CaptureSnapshot().NavigationAgents.Single(n=>n.Id.Value==worker.AgentId).Destination;
            Assert.AreEqual(home,destination,worker.Name);Assert.AreEqual(worker.Role,s.IdleResponseStaffRole(nav.Id,nav.XMillimetres,nav.ZMillimetres),worker.Name);
            var front=GameSession.RotateWaterOffset(new(0,1),1);var post=s.CaptureResponsePost(worker.Role);
            Assert.IsTrue((home.X-post.Cell.X)*front.X+(home.Z-post.Cell.Z)*front.Z>0);
        }
        var patient=s.CapturePreparation()!.People.First(p=>p.Role==ProtectedPersonRole.Guest).AgentId;
        var medical=s.CaptureMedical()!;
        typeof(GameSession).GetField("_medical",BindingFlags.NonPublic|BindingFlags.Instance)!.SetValue(s,medical with {Needs=medical.Needs.Select(n=>n.AgentId==patient?n with {Stage=MedicalStage.Distress,WarningTick=s.CurrentTick}:n).ToArray()});
        var medic=s.GetResponseStaff().Single(p=>p.Name=="Avery Brooks");Accept(s,new MedicalCommand(patient,MedicalAction.DispatchMedic,medic.AgentId));
        var guided=s.CapturePreparation()!.People.Where(p=>p.Role==ProtectedPersonRole.Guest && p.AgentId!=patient).First().AgentId;
        var steward=s.GetResponseStaff().Single(p=>p.Name=="Sam Ellis");Accept(s,new StaffInterventionCommand(guided,steward.AgentId,StaffInterventionAction.GuideToWater));
        Assert.AreEqual(MedicalResponseStage.Travelling,s.GetMedicResponses().Single(j=>j.WorkerId==medic.AgentId).Stage);
        var restored=GameSession.Restore(s.CapturePersistenceSnapshot());Assert.IsTrue(restored.IsSuccess,restored.Error);
        s.AdvanceWithoutSnapshot(100);restored.Session!.AdvanceWithoutSnapshot(100);Assert.AreEqual(Hash(s),Hash(restored.Session));
        for(var elapsed=0;elapsed<2500 && s.GetMedicResponses().Single(j=>j.WorkerId==medic.AgentId).Stage!=MedicalResponseStage.Completed;elapsed+=20)
        {s.AdvanceWithoutSnapshot(20);restored.Session.AdvanceWithoutSnapshot(20);}
        Assert.AreEqual(MedicalResponseStage.Completed,s.GetMedicResponses().Single(j=>j.WorkerId==medic.AgentId).Stage);
        s.AdvanceWithoutSnapshot(1200);restored.Session.AdvanceWithoutSnapshot(1200);Assert.AreEqual(Hash(s),Hash(restored.Session));
        Assert.AreEqual(GameSession.ResponsePostHome(s.CapturePreparation(),ResponseRole.Medic,true),s.CaptureSnapshot().NavigationAgents.Single(n=>n.Id.Value==medic.AgentId).Destination);
        var medicNav=s.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==medic.AgentId);
        Assert.AreEqual(ResponseRole.Medic,s.IdleResponseStaffRole(medicNav.Id,medicNav.XMillimetres,medicNav.ZMillimetres));
        for(var elapsed=0;elapsed<2500 && s.CaptureStaffInterventions().Single(j=>j.WorkerId==steward.AgentId).Stage!=StaffInterventionStage.Completed;elapsed+=20)
        {s.AdvanceWithoutSnapshot(20);restored.Session.AdvanceWithoutSnapshot(20);}
        Assert.AreEqual(StaffInterventionStage.Completed,s.CaptureStaffInterventions().Single(j=>j.WorkerId==steward.AgentId).Stage);
        s.AdvanceWithoutSnapshot(1200);restored.Session.AdvanceWithoutSnapshot(1200);Assert.AreEqual(Hash(s),Hash(restored.Session));
        var stewardNav=s.CaptureObservation().NavigationAgents.Single(n=>n.Id.Value==steward.AgentId);
        Assert.AreEqual(ResponseRole.Steward,s.IdleResponseStaffRole(stewardNav.Id,stewardNav.XMillimetres,stewardNav.ZMillimetres));
    }
    [TestMethod]
    public void ActualPriorCompatibleSaveDefaultsPreserveHashBytesAndExactContinuation()
    {
        var directory=new DirectoryInfo(AppContext.BaseDirectory);while(directory is not null && !File.Exists(Path.Combine(directory.FullName,"ROGUELIKE_DESIGN.md")))directory=directory.Parent;
        var path=Path.Combine(directory!.FullName,"reports/evidence/R0.05h/final-1280x720/saves/manual-preparation.ftsave");
        var bytes=File.ReadAllBytes(path);var compatibility=new SaveCompatibility("0.0.1-r0.05-hearing-v1",LowerWitteringFarmScenario.ContentCompatibilityHash,"r0-disorder-layout-v13");
        var loaded=SaveFileAdapter.LoadFile(path,compatibility);Assert.IsTrue(loaded.IsSuccess,loaded.Error);
        Assert.IsNull(loaded.Session!.CapturePreparation()!.FirstAidPlacement);Assert.IsNull(loaded.Session.CapturePreparation()!.StewardPostPlacement);
        Assert.AreEqual(new ResponsePostPlacement(GameSession.MedicalTentCell,0),loaded.Session.CaptureResponsePost(ResponseRole.Medic));
        CollectionAssert.AreEqual(bytes,File.ReadAllBytes(path));
        var restored=GameSession.Restore(loaded.Session.CapturePersistenceSnapshot());Assert.IsTrue(restored.IsSuccess,restored.Error);Assert.AreEqual(Hash(loaded.Session),Hash(restored.Session!));
    }
    [TestMethod]
    public void TapThenPostReciprocalClearanceAcceptsOnlyRestorableLayouts()
    {
        var s=GameSession.CreateImmersionCampaign(20260922);Accept(s,new MoveWaterPointCommand("water.main",new(70,120)));
        foreach(var turns in Enumerable.Range(0,4))
        {
            var copy=GameSession.Restore(s.CapturePersistenceSnapshot()).Session!;
            var command=new MoveResponsePostCommand(ResponseRole.Medic,new(74,126),turns);
            var accepted=copy.Execute(Envelope(copy,command)).IsAccepted;
            Assert.IsFalse(accepted,"Diagonal tap service edge must preserve the tap validator's protected access.");
        }
        foreach(var x in Enumerable.Range(74,6))
        {
            var copy=GameSession.Restore(s.CapturePersistenceSnapshot()).Session!;
            var accepted=copy.Execute(Envelope(copy,new MoveResponsePostCommand(ResponseRole.Medic,new(x,120),0))).IsAccepted;
            if(accepted){var restored=GameSession.Restore(copy.CapturePersistenceSnapshot());Assert.IsTrue(restored.IsSuccess,restored.Error);}
        }
    }
    [TestMethod]
    public void PostRejectsAdjacentLooseVendorQueueHomeWithoutMutation()
    {
        var s=GameSession.CreateImmersionCampaign(20260922);
        var food=s.CaptureImmersion()!.Vendors.Single(v=>v.Id=="food");
        Accept(s,new PlaceImmersionVendorCommand("food",food.Cell,food.QuarterTurns));
        Assert.IsNotNull(s.CaptureImmersion()!.Vendors.Single(v=>v.Id=="food").QueueCells);
        var hash=Hash(s);var rejected=s.Execute(Envelope(s,new MoveResponsePostCommand(ResponseRole.Medic,new(144,130),2)));
        Assert.IsFalse(rejected.IsAccepted,rejected.Message);Assert.AreEqual(hash,Hash(s));
        Assert.IsTrue(GameSession.Restore(s.CapturePersistenceSnapshot()).IsSuccess);
    }
}

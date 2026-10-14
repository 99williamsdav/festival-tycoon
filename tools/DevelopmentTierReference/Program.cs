using Festival.Simulation;
using System.Text.Json;

// Run from the repository root. This recorder writes evidence only; replacing the embedded reference is deliberate.
var output=args.Length>0?args[0]:"artifacts/development-tier-reference";
Directory.CreateDirectory(output);
var json=new JsonSerializerOptions{WriteIndented=true};
var seed=20260922UL;
var session=GameSession.CreateBuildCampaign(seed);
var commands=new List<object>();
void Send(SessionCommand command)
{
    var result=session.Execute(new(new(session.NextSubmissionSequence+1),session.CampaignId,session.Phase,session.CurrentTick,session.NextSubmissionSequence,null,command));
    if(!result.IsAccepted)throw new InvalidOperationException($"{command}: {result.Message}");
    commands.Add(new{type=command.GetType().Name,data=JsonSerializer.SerializeToElement(command,command.GetType(),json)});
}
var initial=session.CaptureSnapshot().FestivalFinances.Single().CashPennies;
Send(new AcknowledgeFestivalChallengeCommand(1,1));
var perk=session.CapturePerks()!;Send(new ChoosePerkCommand(perk.DraftAttempt,perk.Cursor,perk.Hand[0]));
Send(new UseDefaultBuildLayoutCommand());
var acts=session.GetFestivalActs().Where(a=>session.ActStandingOf(a)==ActStanding.Available).OrderBy(session.ActFee).ThenBy(a=>a.Id,StringComparer.Ordinal).Take(3).Select(a=>a.Id).ToArray();
Send(new SetProgrammeCommand(acts));Send(new SetPreparationStockCommand(40,32));
foreach(var group in session.GetStaffCandidates().GroupBy(c=>c.Role))
{
    var person=group.OrderBy(c=>c.Traits.Length>0).ThenBy(c=>c.Grade>0).ThenBy(c=>Math.Abs(c.Grade)).ThenBy(c=>c.Id,StringComparer.Ordinal).First();
    Send(new AcceptPreparationOfferCommand(person.Id));
}
Send(new StartPreparedEditionCommand());
for(var i=0;i<80 && session.PreparedStatus is PreparationStatus.Running or PreparationStatus.Departing;i++)session.AdvanceWithoutSnapshot(1000);
var finished=session.CapturePreparation()!;
Console.WriteLine(JsonSerializer.Serialize(new{seed,initial,session.PreparedStatus,session.CurrentTick,session.CanStartNextFestival,result=session.CompletedFestivalResult,accounts=session.CompletedFestivalAccounts,finished.Reputation,finished.SceneCredibility,finished.SeenActs},json));
if(!session.CanStartNextFestival)throw new InvalidOperationException("This run did not earn the normal next-festival transition.");
File.WriteAllText(Path.Combine(output,"recorded-tier-one-clear.json"),JsonSerializer.Serialize(session.CapturePersistenceSnapshot(),json));
File.WriteAllText(Path.Combine(output,"commands.json"),JsonSerializer.Serialize(commands,json));
var next=session.CreateNextFestival();var p=next.CapturePreparation()!;
File.WriteAllText(Path.Combine(output,"tier-two-summary.json"),JsonSerializer.Serialize(new{seed,p.Tier,p.OpeningCashPennies,p.CarriedIn,p.Reputation,p.SceneCredibility,p.SeenActs,p.ActRelationships,p.RelationshipHistory,p.ActPopularity,stages=next.Stages.Select(s=>s.Id)},json));

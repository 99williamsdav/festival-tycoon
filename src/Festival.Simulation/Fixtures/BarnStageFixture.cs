namespace Festival.Simulation.Fixtures;

/// <summary>Opt-in unsaveable two-slot inspection session. Normal campaign allocation is unchanged.</summary>
public static class BarnStageFixture
{
    public static GameSession Create(ulong seed) => GameSession.CreateBarnDiagnostic(seed);
    public static GameSession Started(ulong seed)
    {
        var session=Create(seed);
        void Send(SessionCommand command)
        {
            var result=session.Execute(new(new(session.NextSubmissionSequence+1),session.CampaignId,session.Phase,session.CurrentTick,session.NextSubmissionSequence,null,command));
            if(!result.IsAccepted) throw new InvalidOperationException($"Barn fixture {command.GetType().Name}: {result.Message}");
        }
        var draft=session.CapturePerks()!;
        Send(new ChoosePerkCommand(draft.DraftAttempt,draft.Cursor,draft.Hand[0]));
        Send(new UseDefaultBuildLayoutCommand());
        Send(new SetProgrammeCommand(["act.meadow-lanterns","act.overdue-library-books","act.glitter-rota"]));
        Send(new SetProgrammeCommand(["act.low-battery","act.unlicensed-bouncy-castle","act.septic-tank"]){StageId=BarnLayout.Stage.Id});
        Send(new SetPreparationStockCommand(40,32));
        var crew=session.GetStaffCandidates().GroupBy(c=>c.Role).Select(group=>group.OrderBy(c=>c.Traits.Length>0).ThenBy(c=>c.Grade>0).ThenBy(c=>Math.Abs(c.Grade)).ThenBy(c=>c.Id,StringComparer.Ordinal).First()).ToArray();
        foreach(var person in crew)Send(new AcceptPreparationOfferCommand(person.Id));
        var extra=session.GetStaffCandidates().Where(c=>c.Role==StaffRole.Sound && !crew.Any(p=>p.Id==c.Id)).OrderBy(c=>c.Traits.Length>0).ThenBy(c=>c.Grade>0).ThenBy(c=>Math.Abs(c.Grade)).ThenBy(c=>c.Id,StringComparer.Ordinal).First();
        Send(new AcceptPreparationOfferCommand(extra.ExtraOfferId));
        Send(new StartPreparedEditionCommand()); return session;
    }
}

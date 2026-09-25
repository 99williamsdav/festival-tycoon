using Festival.Simulation;

namespace Festival.ContentAdapter;

/// <summary>Player-facing account of a saved casualty; technical cause stays in the authoritative record.</summary>
public sealed record CouncilHearingPresentation(string Person, string Cause, string Sequence);

public static class CouncilHearingPresenter
{
    public static CouncilHearingPresentation From(CasualtySnapshot casualty)
    {
        var role = casualty.Role switch
        {
            ProtectedPersonRole.Guest => "attendee",
            ProtectedPersonRole.Staff => "staff member",
            ProtectedPersonRole.Performer => "performer",
            _ => "person"
        };
        var (cause, sequence) = casualty.TransactionId switch
        {
            var id when id.StartsWith("medical-death:", StringComparison.Ordinal) =>
                ("Heat and thirst led to a collapse. Care did not prevent the fatal outcome.",
                    "Distress → collapse → fatal heat illness"),
            var id when id.StartsWith("disorder-death:", StringComparison.Ordinal) =>
                ("A confrontation escalated into a fight. The resulting injury was fatal.",
                    "Grievance → argument → fight → fatal injury"),
            var id when id.StartsWith("equipment-death:", StringComparison.Ordinal) =>
                ("A generator overload became a dangerous fault and caused a fatal accident near the stage.",
                    "Overload → fault warning → fatal generator incident"),
            _ => ("A fatal incident ended the weekend. The full evidence remains in the campaign record.",
                "Incident → emergency → loss")
        };
        return new($"{casualty.PersonId} · {role}", cause, sequence);
    }
}

using System.Reflection;
using Festival.Simulation;

namespace Festival.Tests;

// Historical queue/hazard regressions isolate their old instantaneous setup controls.
// These explicitly enabled fixtures do not certify the production intervention path;
// StaffInterventionTests always retain the default-disabled gate.
internal static class LegacyInterventionFixture
{
    public static SessionCommand For(GameSession session, SessionCommand command)
    {
        if (session.CaptureMedical() is { DevelopmentInterventionFixturesEnabled: false } medical)
            typeof(GameSession).GetField("_medical", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(session,
                medical with { DevelopmentInterventionFixturesEnabled = true });
        return command switch
        {
            MedicalCommand { Action: not MedicalAction.DispatchMedic } action => new DevelopmentMedicalFixtureCommand(action.GuestId, action.Action),
            DisorderCommand { Action: DisorderAction.SafeEgress, PersonId: { } id } => new DevelopmentDisorderEgressFixtureCommand(id),
            _ => command
        };
    }
}

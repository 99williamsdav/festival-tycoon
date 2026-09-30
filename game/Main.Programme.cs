using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

public partial class Main
{
    private BookingPanel? _bookingView;
    private BookingPanel Booking => _bookingView ??= new(this, LayoutOwnedPerkWorkspace, text => { if (_hudStatus is not null) _hudStatus.Text = text; });
    private string? _presentedActId;
    private const bool FestivalRockAudioApproved = true;

    private string PersonPresentationName(EditionPerson person) => _session.FestivalPerformerTitle(person.AgentId) is { } title
        ? $"{title} • {person.Name}" : person.Name;
    private string PersonPresentationRole(EditionPerson person) => _session.FestivalPerformerTitle(person.AgentId) ??
        (person.Name == "Jordan Hale" ? "Steward" : person.Role.ToString());





    private int PerformerPresentationRole(ulong id, string name)
    {
        var performer = _session.CaptureProgramme()?.Performers.FirstOrDefault(item => item.AgentId == id);
        return performer?.RoleIndex ?? (name == "Alex Reed" ? 0 : name == "Blair Moss" ? 1 : 2);
    }

    private string FestivalCopy(string text) => FestivalWording(_session, text);
}

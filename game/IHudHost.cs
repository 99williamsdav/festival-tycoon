using Festival.Persistence;
using Festival.Simulation;
using Godot;

namespace Festival.Game;

/// <summary>What a HUD panel may use from the game: the session host, the viewport and the shared status line.</summary>
internal interface IHudHost
{
    SessionHost Host { get; }
    GameSession Session => Host.Session;
    Viewport Viewport { get; }
    /// <summary>The status line shown in the HUD; panels report outcomes here.</summary>
    string Message { get; set; }
    /// <summary>Re-renders every HUD panel from the current session.</summary>
    void RefreshHud();
    /// <summary>Applies a player action through the host and reports the outcome.</summary>
    void Commit(SessionCommand command);
}

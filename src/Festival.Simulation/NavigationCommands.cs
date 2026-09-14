namespace Festival.Simulation;

/// <summary>Development fixture: creates the M0.07 static grid and one autonomous attendee.</summary>
public sealed record InitializeNavigationFixtureCommand(
    GridCell Start, IReadOnlyList<TerrainCellOverride> Terrain) : SessionCommand;

/// <summary>Internal attendee-AI command. This is not a player-facing destination control.</summary>
public sealed record SetAgentDestinationCommand(GridCell Destination, string IntentId) : SessionCommand;

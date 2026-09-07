using Festival.Simulation;
using Godot;

namespace Festival.Game;

public partial class Main : Control
{
    public override void _Ready()
    {
        var version = Engine.GetVersionInfo()["string"].AsString();
        GetNode<Label>("Margin/Panel/Content/BuildInformation").Text =
            $"Build {ToolchainSmoke.BuildVersion}\nGodot {version}\nSimulation shell ready";

        GD.Print($"FESTIVAL_TYCOON_LAUNCHED build={ToolchainSmoke.BuildVersion} godot={version}");
    }
}

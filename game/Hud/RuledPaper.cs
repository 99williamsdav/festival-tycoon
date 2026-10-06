using Godot;

namespace Festival.Game;

/// <summary>Notebook paper: faint rules every few pixels and, optionally, a pink margin line down the left.</summary>
public partial class RuledPaper : Control
{
    public float Pitch { get; set; } = 23;
    public float FirstRule { get; set; } = 61;
    public float MarginX { get; set; } = -1;
    public Color Rule { get; set; } = Ui.PaperRule;

    public override void _Ready() => MouseFilter = MouseFilterEnum.Ignore;

    public override void _Draw()
    {
        for (var y = Ui.S(FirstRule); y < Size.Y; y += Ui.S(Pitch))
            DrawLine(new Vector2(0, y), new Vector2(Size.X, y), Rule, 1);
        if (MarginX >= 0) DrawLine(new Vector2(Ui.S(MarginX), Ui.S(8)), new Vector2(Ui.S(MarginX), Size.Y), new Color("e7b7a0"), 1);
    }
}

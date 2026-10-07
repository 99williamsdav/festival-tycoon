using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

/// <summary>What the live bottom bar's buttons ask the HUD shell to do.</summary>
internal interface ILiveBarActions
{
    void ToggleRoster();
    void TogglePerks();
    void RotateView();
    void Zoom(float amount);
}

/// <summary>
/// The bottom bar on festival day: People and Perks, the latest message with the festival time it
/// arrived, and rotate and zoom.
/// </summary>
internal sealed class LiveBar(IHudHost _hud, ILiveBarActions _actions)
{
    private PanelContainer? _bar;
    private Button? _people;
    private Button? _perks;
    private Label? _message;
    private Label? _stamp;
    private string _lastMessage = "";
    private long _messageTick;

    public Control? Panel => _bar;
    /// <summary>The message line; the shell writes status and outcome messages here.</summary>
    public Label? Message => _message;

    public void Build(CanvasLayer layer, Vector2 size)
    {
        _bar = new PanelContainer { Position = new Vector2(0, size.Y - Ui.S(64)), Size = new Vector2(size.X, Ui.S(64)), Theme = HudTheme(), Visible = false };
        var style = Ui.Box(Ui.Bar, 0, padX: 16, padY: 10);
        style.BorderColor = Ui.Gold; style.BorderWidthTop = Ui.Px(2);
        _bar.AddThemeStyleboxOverride("panel", style); layer.AddChild(_bar);
        var row = new HBoxContainer(); row.AddThemeConstantOverride("separation", Ui.Px(10)); _bar.AddChild(row);
        _people = Ui.IconButton("People", "users", Ui.ButtonKind.Bar, _actions.ToggleRoster, 14.5f);
        _people.CustomMinimumSize = new Vector2(0, Ui.S(42)); _people.TooltipText = "Everyone on the farm"; row.AddChild(_people);
        _perks = Ui.IconButton("Perks", "star", Ui.ButtonKind.Bar, _actions.TogglePerks, 14.5f);
        _perks.CustomMinimumSize = new Vector2(0, Ui.S(42)); _perks.TooltipText = "Your equipped perks"; row.AddChild(_perks);
        foreach (var state in new[] { "icon_normal_color", "icon_hover_color", "icon_pressed_color", "icon_focus_color" }) _perks.AddThemeColorOverride(state, Ui.Gold);
        var line = new PanelContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        line.AddThemeStyleboxOverride("panel", Ui.Box(Ui.BarDeep, 8, padX: 14));
        row.AddChild(line);
        var words = new HBoxContainer(); words.AddThemeConstantOverride("separation", Ui.Px(10)); line.AddChild(words);
        var tick = Ui.IconRect("check", 18, Ui.Good); tick.SizeFlagsVertical = Control.SizeFlags.ShrinkCenter; words.AddChild(tick);
        _message = Ui.Text("", 14, new Color("d9e1d6")); _message.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _message.VerticalAlignment = VerticalAlignment.Center; _message.ClipText = true; _message.TextOverrunBehavior = TextServer.OverrunBehavior.TrimEllipsis;
        _message.CustomMinimumSize = new Vector2(1, 0); _message.MouseFilter = Control.MouseFilterEnum.Pass;
        words.AddChild(_message);
        _stamp = Ui.Text("", 12.5f, new Color("8e9a91")); _stamp.VerticalAlignment = VerticalAlignment.Center; words.AddChild(_stamp);
        foreach (var (icon, tip, action) in new (string, string, Action)[]
                 { ("rotate-cw", "Rotate view (Q / E)", _actions.RotateView), ("plus", "Zoom in (wheel)", () => _actions.Zoom(-4)), ("minus", "Zoom out (wheel)", () => _actions.Zoom(4)) })
        {
            var button = Ui.Style(new Button { Icon = Ui.Icon(icon), ExpandIcon = true, IconAlignment = HorizontalAlignment.Center, TooltipText = tip,
                CustomMinimumSize = Ui.S(42, 42), MouseDefaultCursorShape = Control.CursorShape.PointingHand }, Ui.ButtonKind.Bar, radius: 8);
            button.Pressed += action; row.AddChild(button);
        }
    }

    public void Refresh(bool shown)
    {
        if (_bar is null) return;
        _bar.Visible = shown;
        if (!shown) return;
        var session = _hud.Session;
        var p = session.CapturePreparation()!;
        _people!.Text = $"People · {p.People.Length}";
        var perks = session.CapturePerks();
        _perks!.Visible = perks is { Ended: false };
        if (perks is not null) _perks.Text = $"Perks {perks.Equipped.Length}/5";
        if (_message!.Text != _lastMessage)
        {
            _lastMessage = _message.Text; _messageTick = session.CurrentTick;
            _stamp!.Text = FestivalClockText(session.CurrentTick - p.StartedTick);
        }
        // Twenty festival minutes on, the message is old news: fade it rather than leave it reading as current.
        var faded = session.CurrentTick - _messageTick > 20 * 80;
        _message.Modulate = _stamp!.Modulate = new Color(1, 1, 1, faded ? 0.45f : 1f);
        _message.TooltipText = _message.Text;
    }
}

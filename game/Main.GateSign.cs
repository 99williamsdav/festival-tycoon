using Godot;
using System;

namespace Festival.Game;

/// <summary>
/// The festival's sign at the farm gate, standing on a little apron of verge and lane outside it. The sign grows
/// with the festival: a painted old door at tier 1, a proper board at tier 2, an arch over the lane from tier 3.
/// Its lettering is the festival's own name, set at runtime so any name the player chooses fits.
/// </summary>
public partial class Main
{
    private static readonly Vector3 GateSignOrigin = new(0, 0, 32);
    // Each tier's clear lettering rectangle on its board, in metres.
    private static readonly Vector2[] GateLetteringSizes = [new(2.2f, 0.78f), new(2.48f, 0.78f), new(4.0f, 1.15f)];
    private static readonly Color[] GateLetteringInk = [new("3b4a3a"), new("8e3b2a"), new("f3e7c4")];
    private Node3D? _gateSign;
    private (int Tier, string Name) _gateSignShown = (0, "");
    private double _gateSignSync;

    private void BuildGateApron() => AddAsset("res://assets/environment/lwf_gate_apron_v1.glb", GateSignOrigin);

    private void ProcessGateSign(double delta)
    {
        _gateSignSync -= delta;
        if (_gateSignSync > 0 || _session is null) return;
        _gateSignSync = .5;
        var tier = Math.Clamp(_session.CaptureLifecycleSnapshot()?.TierOrdinal ?? 1, 1, 3);
        var name = _session.CaptureCampaignPlanningSnapshot()?.FestivalName ?? "Lower Wittering Festival";
        if (_gateSignShown == (tier, name)) return;
        _gateSignShown = (tier, name);
        _gateSign?.QueueFree();
        _gateSign = AddAsset($"res://assets/environment/lwf_gate_sign_tier{tier}_v1.glb", GateSignOrigin);
        if (_gateSign.FindChild("LetteringArea", true, false) is Node3D area) Letter(area, name, tier);
    }

    /// <summary>The name, on one line if it fits or two if not, sized to the board's clear rectangle.</summary>
    private static void Letter(Node3D area, string name, int tier)
    {
        var size = GateLetteringSizes[tier - 1];
        var font = GD.Load<FontFile>("res://assets/ui/fonts/ZillaSlab-Bold.ttf");
        const int fontSize = 96;
        var lineWidth = font.GetStringSize(name, HorizontalAlignment.Left, -1, fontSize).X;
        var lines = lineWidth > size.X / size.Y * font.GetHeight(fontSize) * 1.6f && name.Contains(' ') ? 2 : 1;
        var widest = lines == 1 ? lineWidth : HalfWidth(font, name, fontSize);
        var pixel = Math.Min(size.X * .92f / widest, size.Y * .88f / (lines * font.GetHeight(fontSize)));
        area.AddChild(new Label3D
        {
            Text = lines == 1 ? name : Split(name), Font = font, FontSize = fontSize, PixelSize = pixel,
            Modulate = GateLetteringInk[tier - 1], OutlineSize = 0, Shaded = true, DoubleSided = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center,
            Position = new Vector3(0, 0, .004f),
        });
    }

    /// <summary>Breaks a name at the space nearest its middle.</summary>
    private static string Split(string name)
    {
        var best = -1;
        for (var i = 0; i < name.Length; i++)
            if (name[i] == ' ' && (best < 0 || Math.Abs(i - name.Length / 2) < Math.Abs(best - name.Length / 2))) best = i;
        return best < 0 ? name : name[..best] + "\n" + name[(best + 1)..];
    }

    private static float HalfWidth(FontFile font, string name, int fontSize)
    {
        var width = 0f;
        foreach (var line in Split(name).Split('\n'))
            width = Math.Max(width, font.GetStringSize(line, HorizontalAlignment.Left, -1, fontSize).X);
        return width;
    }
}

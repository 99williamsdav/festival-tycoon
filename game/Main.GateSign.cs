using Godot;
using System;

namespace Festival.Game;

/// <summary>
/// The festival's sign at the farm gate, standing on a little apron of verge and lane outside it. The sign grows
/// with the festival: a painted old door at tier 1, a proper board at tier 2, an arch over the lane from tier 3.
/// Its lettering is the festival's own name, set at runtime so any name the player chooses fits. The sign fonts
/// are SIL Open Font License; their licences sit beside them in assets/fonts/sign.
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
        // Preparation knows the tier before the gates first open; the lifecycle only exists from then.
        var tier = Math.Clamp(_session.CapturePreparation()?.Tier ?? _session.CaptureLifecycleSnapshot()?.TierOrdinal ?? 1, 1, 3);
        // One name everywhere: the gate, the top bar, the box office and the band.
        const string name = "Lower Wittering Festival";
        if (_gateSignShown == (tier, name)) return;
        _gateSignShown = (tier, name);
        _gateSign?.QueueFree();
        _gateSign = AddAsset($"res://assets/environment/lwf_gate_sign_tier{tier}_v1.glb", GateSignOrigin);
        if (_gateSign.FindChild("LetteringArea", true, false) is Node3D area) Letter(area, name, tier);
    }

    // Each tier's sign-writing: a rough painted hand, a casual brush script, then a confident serif.
    private static Font GateLetteringFont(int tier) => tier switch
    {
        1 => GD.Load<FontFile>("res://assets/fonts/sign/CaveatBrush-Regular.ttf"),
        2 => GD.Load<FontFile>("res://assets/fonts/sign/Kalam-Bold.ttf"),
        _ => new FontVariation
        {
            BaseFont = GD.Load<FontFile>("res://assets/fonts/sign/PlayfairDisplay-Variable.ttf"),
            VariationOpentype = new Godot.Collections.Dictionary { { "wght", 700 } },
        },
    };

    /// <summary>
    /// The name, on one line if it fits or two if not, sized to the board's clear rectangle. A name too long to
    /// stay legible is cut short with an ellipsis rather than shrunk to nothing.
    /// </summary>
    private static void Letter(Node3D area, string name, int tier)
    {
        var size = GateLetteringSizes[tier - 1];
        var font = GateLetteringFont(tier);
        const int fontSize = 96;
        var minimumPixel = size.Y * .3f / font.GetHeight(fontSize);
        var (text, pixel) = Fit(font, name, size, fontSize);
        while (pixel < minimumPixel && name.Length > 4)
        {
            // Cut whole characters, never half of a surrogate pair.
            var keep = name.Length - 2;
            if (keep > 0 && char.IsLowSurrogate(name[keep])) keep--;
            name = name[..keep].TrimEnd() + "…";
            (text, pixel) = Fit(font, name, size, fontSize);
        }
        Label3D Label(Color ink, Vector3 at) => new()
        {
            Text = text, Font = font, FontSize = fontSize, PixelSize = pixel,
            Modulate = ink, OutlineSize = 0, Shaded = true, DoubleSided = false,
            HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, Position = at,
        };
        // The arch is sign-written with a dark drop shade; the homemade signs are painted flat, the door a touch askew.
        if (tier == 3) area.AddChild(Label(new Color("1d2a22"), new Vector3(.035f, -.035f, .006f)));
        var top = Label(GateLetteringInk[tier - 1], new Vector3(0, 0, .01f));
        top.RenderPriority = 1; // Always over the shade, whatever the sort order.
        if (tier == 1) top.RotationDegrees = new Vector3(0, 0, -2.5f);
        area.AddChild(top);
    }

    /// <summary>One line or two, whichever lets the lettering be larger; one line wins unless two are clearly bigger.</summary>
    private static (string Text, float Pixel) Fit(Font font, string name, Vector2 size, int fontSize)
    {
        float Pixel(int lines, float widest) => Math.Min(size.X * .92f / widest, size.Y * .88f / (lines * font.GetHeight(fontSize)));
        var one = Pixel(1, font.GetStringSize(name, HorizontalAlignment.Left, -1, fontSize).X);
        if (!name.Contains(' ')) return (name, one);
        var two = Pixel(2, HalfWidth(font, name, fontSize));
        return two > one / .85f ? (Split(name), two) : (name, one);
    }

    /// <summary>Breaks a name at the space nearest its middle.</summary>
    private static string Split(string name)
    {
        var best = -1;
        for (var i = 0; i < name.Length; i++)
            if (name[i] == ' ' && (best < 0 || Math.Abs(i - name.Length / 2) < Math.Abs(best - name.Length / 2))) best = i;
        return best < 0 ? name : name[..best] + "\n" + name[(best + 1)..];
    }

    private static float HalfWidth(Font font, string name, int fontSize)
    {
        var width = 0f;
        foreach (var line in Split(name).Split('\n'))
            width = Math.Max(width, font.GetStringSize(line, HorizontalAlignment.Left, -1, fontSize).X);
        return width;
    }
}

using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// The site map in first person (the designer's B1 and A1, assets/source/ui/site-map-v1/INTEGRATION.md). M raises the
/// laminated plan in the right hand on its clipboard: the plan is drawn into a SubViewport and shown on the GLB's sheet,
/// with the stands from the live layout, Control's anchored marker notes, a sticker for each open call (peeling off as it
/// closes), the crew (All-Rounder only) and you. While it's up the sim walks you at 60% with no running
/// (<see cref="SetSitePlanCommand"/>), and the prompt card, the hands and the Q menu wait (E or Q puts it away first).
/// The corner minimap is always on otherwise: it turns so ahead is up, its icons upright, pins clamped to the rim with
/// their distance, and cross-fades out as the plan comes up. Neither ever shows guests or an incident Control hasn't
/// called in. Measurements are mockup units; the plan's are its texture's pixels.
/// </summary>
public partial class Main
{
    private sealed record SiteStand(string Kind, string Label, float X, float Z);
    private sealed record SiteSticker(string Id, bool Urgent, string Label, float X, float Z)
    {
        public float Peel { get; set; } = -1;
    }

    private SubViewport? _planViewport, _miniViewport;
    private Control? _planCanvas;
    private Node3D? _planArm;
    private Control? _miniMask, _miniMap, _miniOverlay, _planHint;
    private SiteStand[] _siteStands = [];
    private (string Text, float X, float Z, bool Arrow)[] _siteNotes = [];
    private readonly Dictionary<string, SiteSticker> _siteStickers = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Texture2D> _siteArt = new(StringComparer.Ordinal);
    private int _siteTier = 1;
    private bool _sitePond, _siteBarn;
    private float _planT, _planRefresh, _miniAlpha = 1;
    private Vector3 _armBob;

    private static readonly Vector3 PlanRest = new(0.10f, -0.62f, 0.12f);
    private static readonly Color MarkerBlue = new("2b4a8c");
    private const float PlanPxPerMetre = 20.625f, MiniUnitsPerMetre = 3.143f;

    /// <summary>The sim's word on it: the plan is up in the player's hand.</summary>
    private bool SitePlanUp => _session.CaptureStewardShift()?.PlanUp == true;
    /// <summary>
    /// The plan's eased raise, 0 down to 1 up: the minimap fades by the same amount. Raising is an ease-out cubic over
    /// the time; lowering an ease-in cubic, which run backwards is the same curve, so a turn mid-way never jumps.
    /// </summary>
    private float PlanEase => EaseOut(_planT);

    private Texture2D SiteArt(string name)
    {
        if (!_siteArt.TryGetValue(name, out var texture)) _siteArt[name] = texture = GD.Load<Texture2D>($"res://assets/ui/sitemap/{name}.png");
        return texture;
    }

    private static Vector2 PlanPoint(float x, float z) => new(40 + (x + 32) * PlanPxPerMetre, 150 + (z + 13) * PlanPxPerMetre);

    /// <summary>The map's static layer from the live layout, once a day: each stand's icon kind and label, and Control's notes.</summary>
    private void CollectSiteStands()
    {
        var stands = new List<SiteStand>();
        var placements = _session.CaptureBuildPlacements();
        var taps = placements.Where(p => p.Kind == BuildServiceKind.WaterTap).OrderBy(p => p.Id == "water.main" ? 0 : 1).ThenBy(p => p.Id, StringComparer.Ordinal).Select(p => p.Id).ToList();
        foreach (var place in placements.OrderBy(p => p.Id, StringComparer.Ordinal))
        {
            var (x, z) = TraversalGrid.CellCentre(place.Cell);
            var (kind, label) = place.Kind switch
            {
                BuildServiceKind.WaterTap => ("tap", taps.IndexOf(place.Id) is var n && n > 0 ? $"Tap {n + 1}" : "Tap"),
                BuildServiceKind.Toilet => ("loo", place.Id.EndsWith(".main", StringComparison.Ordinal) ? "Loos" : ""),
                BuildServiceKind.Bar => ("bar", _session.StallDisplayName(place.Id)),
                BuildServiceKind.FoodVan => ("van", _session.TraderAt(place.Id).Menu),
                BuildServiceKind.FirstAid => ("aid", "First aid"),
                BuildServiceKind.StewardPost => ("post", "Steward post"),
                BuildServiceKind.Bin => ("bin", ""),
                _ => ("marquee", "Marquee"),
            };
            stands.Add(new(kind, label, x / 1000f, z / 1000f));
        }
        _siteStands = stands.ToArray();
        _sitePond = _session.Stages.Any(stage => stage.Id == FestivalStages.PondId);
        _siteBarn = _session.BarnStageOpen;
        _siteTier = _session.CapturePreparation()?.Tier ?? 1;
        var notes = new List<(string, float, float, bool)> { ("IN / OUT", 0 - 6.5f, 32 - 2f, false), ("keep the lane\nclear for Henry", -2 - 7.5f, -9, true) };
        if (_sitePond) notes.Add(("generator -\nno guests!", 14.25f + 12.75f, 18 - 7f, false));
        else if (_siteStands.FirstOrDefault(s => s.Kind == "van") is { } van) notes.Add(("chips queue\ngets long here", van.X + 6.75f, van.Z + 6.75f, false));
        _siteNotes = notes.ToArray();
    }

    private void BuildSiteMap()
    {
        ClearSiteMap();
        CollectSiteStands();
        _siteStickers.Clear();
        _planT = 0; _miniAlpha = 1;

        // B1: the plan, drawn into its own texture.
        _planViewport = new SubViewport { Name = "SitePlanTexture", Size = new Vector2I(1400, 1276), TransparentBg = false, Disable3D = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Once };
        _planCanvas = new Control { Size = new Vector2(1400, 1276), MouseFilter = Control.MouseFilterEnum.Ignore };
        _planCanvas.Draw += () => DrawSitePlan(_planCanvas);
        _planViewport.AddChild(_planCanvas);
        AddChild(_planViewport);

        // A1: the minimap, drawn into its own texture and shown through its mask (a shader alpha mask), with the frame
        // and north tick over it unclipped.
        _miniViewport = new SubViewport { Name = "MinimapTexture", TransparentBg = true, Disable3D = true, RenderTargetUpdateMode = SubViewport.UpdateMode.Always };
        _miniMap = new Control { Name = "MinimapLayer", MouseFilter = Control.MouseFilterEnum.Ignore };
        _miniMap.Draw += () => DrawMinimap(_miniMap);
        _miniViewport.AddChild(_miniMap);
        AddChild(_miniViewport);
        var mask = new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item;\nuniform sampler2D mask : filter_linear;\nvoid fragment() { COLOR.a *= texture(mask, UV).r; }" } };
        mask.SetShaderParameter("mask", SiteArt("mini_mask_v1"));
        _miniMask = new TextureRect { Name = "Minimap", Texture = _miniViewport.GetTexture(), ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Ignore, Material = mask };
        _shiftHud!.AddChild(_miniMask);
        _miniOverlay = Drawn("MinimapFrame", DrawMinimapFrame);
        _planHint = Drawn("SitePlanHint", DrawSitePlanHint);
        PlaceMinimap();
    }

    private void ClearSiteMap()
    {
        _planViewport?.QueueFree(); _miniViewport?.QueueFree();
        _planViewport = null; _miniViewport = null; _planCanvas = null; _planArm = null;
        _miniMask = _miniMap = _miniOverlay = _planHint = null;
    }

    /// <summary>All-Rounder sits it under the V chip; Steward Only in the corner itself.</summary>
    private void PlaceMinimap()
    {
        if (_miniMask is null) return;
        var size = Ui.S(176);
        _miniMask.Position = new Vector2(ShiftScreen.X - Ui.S(1280 - 1088), _session.IsHandsOn ? Ui.S(64) : Ui.S(16));
        _miniMask.Size = new Vector2(size, size);
        var pixels = new Vector2I((int)size, (int)size);
        if (_miniViewport!.Size != pixels) _miniViewport.Size = pixels;
        _miniMap!.Position = Vector2.Zero; _miniMap.Size = _miniMask.Size;
    }

    /// <summary>The plan arm in the viewmodel pass, with the plan's texture on its sheet; hidden at rest.</summary>
    private void BuildSitePlanArm()
    {
        _planArm = LoadArm("lwf_fp_arm_right_siteplan_v1.glb");
        _planArm.Name = "StewardSitePlanArm";
        _planArm.Position = PlanRest;
        _planArm.Visible = false;
        if (_planArm.FindChild("LWF_FPPlan_Sheet", true, false) is MeshInstance3D sheet && _planViewport is not null)
            sheet.MaterialOverride = new StandardMaterial3D { AlbedoTexture = _planViewport.GetTexture(), Roughness = .55f,
                TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic };
    }

    // ---- Input -----------------------------------------------------------------------------------------------

    private void ToggleSitePlan()
    {
        if (_session.PlayerStewardId is null || _session.IsPaused || RadialMenuOpen) return;
        if (_shiftKeyDown) { _shiftKeyDown = false; ReleaseShiftAction(); }
        _host.Submit(new SetSitePlanCommand(!SitePlanUp));
    }

    /// <summary>E or Q with the plan up: down it goes first.</summary>
    private void LowerSitePlan()
    {
        if (SitePlanUp) _host.Submit(new SetSitePlanCommand(false));
    }

    // ---- Per frame -------------------------------------------------------------------------------------------

    private void UpdateSiteMap(float delta, StewardCall[] heard)
    {
        if (_miniMask is null) return;
        PlaceMinimap();
        // Raise over 0.25 s (ease-out), lower over 0.18 s (ease-in), following the sim.
        var up = SitePlanUp && _session.PlayerStewardId is not null;
        _planT = Mathf.MoveToward(_planT, up ? 1 : 0, delta / (up ? .25f : .18f));
        var e = PlanEase;
        if (_planArm is not null)
        {
            _planArm.Visible = e > .001f;
            _planArm.Position = PlanRest.Lerp(Vector3.Zero, e) + (up && _planT >= 1 ? _armBob * .5f : Vector3.Zero);
        }

        // The live layer: a sticker for each of the player's open calls, peeling off once it's over.
        var open = heard.Where(call => call.CoveredBy is null).ToArray();
        foreach (var call in open)
            if (_session.StewardCallPoint(call) is { } at)
            {
                if (_siteStickers.TryGetValue(call.Id, out var known) && known.Peel < 0) _siteStickers[call.Id] = known with { X = at.X / 1000f, Z = at.Z / 1000f };
                else if (!_siteStickers.ContainsKey(call.Id)) _siteStickers[call.Id] = new(call.Id, call.Urgent, StickerText(call), at.X / 1000f, at.Z / 1000f);
            }
        var peeling = false;
        foreach (var sticker in _siteStickers.Values.ToArray())
        {
            if (sticker.Peel < 0 && !open.Any(call => call.Id == sticker.Id)) sticker.Peel = 0;
            if (sticker.Peel >= 0) { sticker.Peel += delta; peeling = true; if (sticker.Peel >= .25f) _siteStickers.Remove(sticker.Id); }
        }

        // The plan's texture: at 4 Hz while it's in sight, every frame while a sticker peels.
        _planRefresh -= delta;
        if (_planViewport is not null && e > .001f && (_planRefresh <= 0 || peeling))
        {
            _planRefresh = .25f;
            _planCanvas!.QueueRedraw();
            _planViewport.RenderTargetUpdateMode = SubViewport.UpdateMode.Once;
        }
        // The minimap gives way as the plan comes up (the same eased value), and comes back over 0.25 s once it's down.
        _miniAlpha = up ? Mathf.Min(_miniAlpha, 1 - e) : _planT > 0 ? Mathf.Min(_miniAlpha, 1 - e) : Mathf.MoveToward(_miniAlpha, 1, delta / .25f);
        _miniMask.Modulate = new Color(1, 1, 1, _miniAlpha);
        _miniMask.Visible = _miniAlpha > .001f && !StewardShiftReportShown;
        _miniViewport!.RenderTargetUpdateMode = _miniMask.Visible ? SubViewport.UpdateMode.Always : SubViewport.UpdateMode.Disabled;
        _miniOverlay!.Modulate = _miniMask.Modulate;
        _miniOverlay.Visible = _miniMask.Visible;
        _miniMap!.QueueRedraw(); _miniOverlay.QueueRedraw(); _planHint!.QueueRedraw();
    }

    private string StickerText(StewardCall call) => call.Kind switch
    {
        StewardCallKind.Collapse => "collapse!",
        StewardCallKind.Fight => "fight!",
        StewardCallKind.Argument => "argument",
        StewardCallKind.StuckInLoo => _siteStands.Count(s => s.Kind == "loo") > 1 && LooNumber(call.Subject) is { } n ? $"loo {n} stuck?" : "loo stuck?",
        StewardCallKind.BrokenTap => "tap burst",
        StewardCallKind.FullBin => "bin full",
        _ => "cow loose!",
    };

    private int? LooNumber(string toiletId)
    {
        var loos = _session.CaptureToilets().Select(t => t.Id).OrderBy(id => id.EndsWith(".main", StringComparison.Ordinal) ? 0 : 1).ThenBy(id => id, StringComparer.Ordinal).ToList();
        var index = loos.IndexOf(toiletId);
        return index < 0 ? null : index + 1;
    }

    /// <summary>The player's heading, clockwise from north (0 = up the map), from the camera's yaw.</summary>
    private float ShiftHeading => Mathf.PosMod(-Mathf.RadToDeg(_shiftYaw), 360f);

    private (float X, float Z) ShiftWhere => _eyeCamera is { } eye ? (eye.GlobalPosition.X, eye.GlobalPosition.Z) : (0, 0);

    // ---- B1: the plan's texture ------------------------------------------------------------------------------

    private static void PlanText(Control c, Font font, Vector2 at, string text, int px, Color colour, string anchor = "lt")
    {
        var size = font.GetStringSize(text, fontSize: px);
        var ascent = font.GetAscent(px);
        var x = anchor[0] switch { 'r' => at.X - size.X, 'm' => at.X - size.X / 2, _ => at.X };
        var y = anchor[1] switch { 'm' => at.Y - (ascent + font.GetDescent(px)) / 2 + ascent, 'b' => at.Y - font.GetDescent(px), _ => at.Y + ascent };
        c.DrawString(font, new Vector2(x, y), text, fontSize: px, modulate: colour);
    }

    /// <summary>Control's biro: Zilla Slab SemiBold, slanted (an x-shear of −0.22, the top leaning right), centred on its anchor.</summary>
    private static void MarkerText(Control c, Vector2 at, string text, int px, Color colour, bool bottomLeft = false)
    {
        var lines = text.Split('\n');
        var font = Ui.Slab;
        var height = font.GetHeight(px);
        c.DrawSetTransformMatrix(new Transform2D(new Vector2(1, 0), new Vector2(-.22f, 1), at));
        for (var i = 0; i < lines.Length; i++)
        {
            var w = font.GetStringSize(lines[i], fontSize: px).X;
            var top = bottomLeft ? -height * (lines.Length - i) : -height * lines.Length / 2 + height * i;
            c.DrawString(font, new Vector2(bottomLeft ? 0 : -w / 2, top + font.GetAscent(px)), lines[i], fontSize: px, modulate: colour);
        }
        c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
    }

    private void DrawSitePlan(Control c)
    {
        c.DrawTextureRect(SiteArt(_sitePond ? "plan_base_t2_v1" : "plan_base_t1_v1"), new Rect2(0, 0, 1400, 1276), false);
        PlanText(c, Ui.BodyBold, new Vector2(40, 34), "LOWER WITTERING FESTIVAL", 26, Ui.InkMuted);
        PlanText(c, Ui.SlabBold, new Vector2(40, 64), "Site plan", 54, Ui.Ink);
        PlanText(c, Ui.BodyBold, new Vector2(1360, 82), $"TIER {_siteTier} · STEWARDS' COPY", 22, Ui.Alert, "rt");
        if (_siteBarn)
        {
            // The barn is north of the existing printed field crop: mark its edge rather than invent a new base map.
            var at = PlanPoint(6.2f,-24.5f); at.Y = 150;
            c.DrawLine(at,new Vector2(at.X,112),MarkerBlue,3);
            PlanText(c,Ui.BodyBold,at+new Vector2(14,0),"BARN STAGE · under cover",21,MarkerBlue);
            var generator = PlanPoint(BarnLayout.GeneratorX/1000f,BarnLayout.GeneratorZ/1000f);
            c.DrawArc(generator,9,0,Mathf.Tau,24,MarkerBlue,2);
            PlanText(c,Ui.BodyBold,generator+new Vector2(-32,-8),"generator outside",18,MarkerBlue,"rm");
        }

        // Icons first, so the labels can keep clear of every one.
        var taken = new List<Rect2>();
        foreach (var stand in _siteStands)
        {
            var icon = SiteArt($"plan_icon_{stand.Kind}_v1");
            var p = PlanPoint(stand.X, stand.Z);
            var size = icon.GetSize();
            c.DrawTextureRect(icon, new Rect2(p - size / 2, size), false);
            taken.Add(new Rect2(p - size / 2, size));
        }
        var labelFont = Ui.BodyBold;
        foreach (var stand in _siteStands.Where(s => s.Label != ""))
        {
            var p = PlanPoint(stand.X, stand.Z);
            var r = SiteArt($"plan_icon_{stand.Kind}_v1").GetSize().X / 2;
            var size = labelFont.GetStringSize(stand.Label, fontSize: 32); size.Y = 32;
            (Vector2 At, string Anchor, Rect2 Box)[] spots =
            [
                (new(p.X, p.Y + r + 2), "mt", new Rect2(p.X - size.X / 2, p.Y + r + 2, size)),
                (new(p.X - r - 4, p.Y), "rm", new Rect2(p.X - r - 4 - size.X, p.Y - size.Y / 2, size)),
                (new(p.X + r + 4, p.Y), "lm", new Rect2(p.X + r + 4, p.Y - size.Y / 2, size)),
                (new(p.X, p.Y - r - 2), "mb", new Rect2(p.X - size.X / 2, p.Y - r - 2 - size.Y, size)),
            ];
            var pick = spots.FirstOrDefault(spot => !taken.Any(box => box.Intersects(spot.Box)));
            if (pick.Anchor is null) pick = spots[0];
            taken.Add(pick.Box);
            PlanText(c, labelFont, pick.At, stand.Label, 32, Ui.Ink, pick.Anchor);
        }

        // Control's notes, each anchored to something on the ground.
        foreach (var (text, x, z, arrow) in _siteNotes)
        {
            var p = PlanPoint(x, z);
            MarkerText(c, p, text, 31, MarkerBlue);
            if (arrow)
            {
                // A short biro arrow from the note to the lane.
                var lane = PlanPoint(-2, -9);
                var from = new Vector2(p.X + 118, p.Y + 6); var to = new Vector2(lane.X - 8, lane.Y + 4);
                c.DrawLine(from, to, MarkerBlue, 3, true);
                var dir = (to - from).Normalized();
                c.DrawLine(to, to - dir.Rotated(.5f) * 14, MarkerBlue, 3, true);
                c.DrawLine(to, to - dir.Rotated(-.5f) * 14, MarkerBlue, 3, true);
            }
        }

        // The live layer: stickers, the crew, you.
        foreach (var sticker in _siteStickers.Values)
        {
            var p = PlanPoint(sticker.X, sticker.Z);
            var t = sticker.Peel < 0 ? 0 : Mathf.Clamp(sticker.Peel / .25f, 0, 1);
            var scale = 1 - .4f * t; var alpha = 1 - t;
            var art = SiteArt(sticker.Urgent ? "plan_sticker_urgent_v1" : "plan_sticker_call_v1");
            c.DrawTextureRect(art, new Rect2(p - new Vector2(44, 44) * scale, new Vector2(88, 88) * scale), false, new Color(1, 1, 1, alpha));
            MarkerText(c, p + new Vector2(33, -33), sticker.Label, 31, new Color(sticker.Urgent ? Ui.Alert : Ui.Gold, alpha), bottomLeft: true);
        }
        if (_session.IsHandsOn)
            foreach (var member in _session.StewardRadioCrew())
            {
                var p = PlanPoint(member.XMillimetres / 1000f, member.ZMillimetres / 1000f);
                c.DrawTextureRect(SiteArt(member.Busy is null ? "plan_crew_free_v1" : "plan_crew_busy_v1"), new Rect2(p - new Vector2(20, 20), new Vector2(40, 40)), false);
                PlanText(c, Ui.BodyBold, p + new Vector2(24, 0), member.Name.Split(' ')[0], 27, Ui.Teal, "lm");
            }
        var (yx, yz) = ShiftWhere;
        c.DrawSetTransform(PlanPoint(yx, yz), Mathf.DegToRad(ShiftHeading), Vector2.One);
        c.DrawTextureRect(SiteArt("plan_you_v1"), new Rect2(-56, -56, 112, 112), false);
        c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);

        // The legend, then the laminate's sheen over everything.
        var x0 = 40f;
        foreach (var (kind, label) in new[] { ("tap", "Water"), ("loo", "Loos"), ("bar", "Bar"), ("van", "Food"), ("aid", "First aid"), ("bin", "Bin"), ("post", "Post") })
        {
            c.DrawTextureRect(SiteArt($"plan_icon_{kind}_v1"), new Rect2(x0, 1174, 32, 32), false);
            PlanText(c, Ui.BodyBold, new Vector2(x0 + 38, 1190), label, 22, Ui.Ink, "lm");
            x0 += 38 + Ui.BodyBold.GetStringSize(label, fontSize: 22).X + 22;
        }
        x0 = 40;
        var row = new List<(string, string)> { ("plan_sticker_call_v1", "radio call"), ("plan_sticker_urgent_v1", "urgent"), ("plan_you_v1", "you") };
        if (_session.IsHandsOn) row.Add(("plan_crew_free_v1", "crew"));
        foreach (var (art, label) in row)
        {
            c.DrawTextureRect(SiteArt(art), new Rect2(x0, 1222, 34, 34), false);
            PlanText(c, Ui.BodyBold, new Vector2(x0 + 40, 1239), label, 22, Ui.Ink, "lm");
            x0 += 40 + Ui.BodyBold.GetStringSize(label, fontSize: 22).X + 26;
        }
        PlanText(c, Ui.BodyBold, new Vector2(1360, 1239), "M  put away", 22, Ui.InkMuted, "rm");
        c.DrawTextureRect(SiteArt("plan_laminate_v1"), new Rect2(0, 0, 1400, 1276), false);
    }

    // ---- A1: the minimap -------------------------------------------------------------------------------------

    /// <summary>A point in the world on the minimap, relative to its centre in screen pixels: turned so ahead is up.</summary>
    private Vector2 MiniLocal(float x, float z)
    {
        var (mx, mz) = ShiftWhere;
        return new Vector2((x - mx) * Ui.S(MiniUnitsPerMetre), (z - mz) * Ui.S(MiniUnitsPerMetre)).Rotated(-Mathf.DegToRad(ShiftHeading));
    }

    private void DrawMinimap(Control c)
    {
        var size = c.Size.X;
        var centre = new Vector2(size / 2, size / 2);
        var k = Ui.S(MiniUnitsPerMetre);
        c.DrawCircle(centre, size / 2, new Color(Ui.BarDeep, .78f));
        // The ground: the whole field, (−36, −36) m at its top-left, turned about you.
        var (mx, mz) = ShiftWhere;
        var ground = SiteArt(_sitePond ? "mini_base_t2_v1" : "mini_base_t1_v1");
        c.DrawSetTransform(centre, -Mathf.DegToRad(ShiftHeading), Vector2.One);
        c.DrawTextureRect(ground, new Rect2(new Vector2(-(mx + 36) * k, -(mz + 36) * k), new Vector2(72 * k, 72 * k)), false);
        c.DrawSetTransform(Vector2.Zero, 0, Vector2.One);
        void Upright(Texture2D art, Vector2 local, float units, bool bottom = false, float scale = 1)
        {
            var s = Ui.S(units) * scale;
            c.DrawTextureRect(art, new Rect2(centre + local - new Vector2(s / 2, bottom ? s : s / 2), new Vector2(s, s)), false);
        }
        if (_siteBarn)
        {
            var local = MiniLocal(6.2f,-24.5f); if(local.Length()>Ui.S(75)) local=local.Normalized()*Ui.S(75);
            c.DrawCircle(centre+local,Ui.S(8),Ui.Gold);
            CentredText(c,Ui.BodyBold,centre+local,"B",10,Ui.Bar);
        }
        foreach (var stand in _siteStands)
        {
            var local = MiniLocal(stand.X, stand.Z);
            if (local.Length() <= Ui.S(84)) Upright(SiteArt($"mini_icon_{stand.Kind}_v1"), local, stand.Kind == "bin" ? 11 : 16);
        }
        if (_session.IsHandsOn)
            foreach (var member in _session.StewardRadioCrew())
            {
                var local = MiniLocal(member.XMillimetres / 1000f, member.ZMillimetres / 1000f);
                if (local.Length() <= Ui.S(84)) Upright(SiteArt(member.Busy is null ? "mini_crew_free_v1" : "mini_crew_busy_v1"), local, 9);
            }
        foreach (var sticker in _siteStickers.Values.Where(s => s.Peel < 0))
        {
            var local = MiniLocal(sticker.X, sticker.Z);
            var pulse = sticker.Urgent ? 1 + .075f * (1 - Mathf.Cos(Time.GetTicksMsec() / 1000f * Mathf.Tau * 2)) : 1;
            var colour = sticker.Urgent ? Ui.Alert : Ui.Gold;
            if (local.Length() > Ui.S(75))
            {
                var metres = Mathf.Round(local.Length() / k);
                var u = local.Normalized();
                local = u * Ui.S(75);
                var chip = centre + local - u * Ui.S(24) - new Vector2(0, Ui.S(6));
                var text = $"{metres:0} m";
                var w = TextWidth(Ui.BodyBold, text, 10.5f) + Ui.S(12);
                c.DrawStyleBox(Ui.Box(new Color(Ui.BarDeep, .82f), Ui.S(8)), new Rect2(chip - new Vector2(w / 2, Ui.S(8)), new Vector2(w, Ui.S(16))));
                CentredText(c, Ui.BodyBold, chip, text, 10.5f, colour);
            }
            Upright(SiteArt(sticker.Urgent ? "mini_pin_urgent_v1" : "mini_pin_call_v1"), local, 22, bottom: true, scale: pulse);
        }
        Upright(SiteArt("mini_you_v1"), Vector2.Zero, 22);
    }

    private void DrawMinimapFrame(Control c)
    {
        if (_miniMask is null) return;
        var rect = new Rect2(_miniMask.Position, _miniMask.Size);
        c.DrawTextureRect(SiteArt("mini_frame_v1"), rect, false);
        var centre = rect.GetCenter();
        var north = -Mathf.DegToRad(ShiftHeading);
        var at = centre + new Vector2(Mathf.Sin(north), -Mathf.Cos(north)) * Ui.S(78);
        var s = Ui.S(18);
        c.DrawTextureRect(SiteArt("mini_north_v1"), new Rect2(at - new Vector2(s / 2, s / 2), new Vector2(s, s)), false);
    }

    /// <summary>"WALKING SLOWLY · M to put away" under the compass while the plan is up.</summary>
    private void DrawSitePlanHint(Control c)
    {
        var a = PlanEase;
        if (a <= .01f || StewardShiftReportShown) return;
        const string text = "WALKING SLOWLY · M TO PUT AWAY";
        var w = TextWidth(Ui.CapsFont, text, 11) + Ui.S(24);
        var rect = new Rect2((ShiftScreen.X - w) / 2, Ui.S(70), w, Ui.S(22));
        c.DrawStyleBox(Ui.Box(new Color(Ui.BarDeep, .75f * a), Ui.S(11)), rect);
        CentredText(c, Ui.CapsFont, rect.GetCenter(), text, 11, new Color(Ui.BarText, a));
    }
}

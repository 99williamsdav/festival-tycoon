using Festival.Simulation;
using Godot;
using System;
using System.Collections.Generic;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Light and weather over the festival day: the sun sinks from a hot early afternoon to dusk as the festival clock
/// runs, clouds pass mid-afternoon, a breeze stirs the hedges, and lights come on for the last set. Presentation only:
/// it reads the clock and never feeds the simulation.
/// </summary>
public partial class Main
{
    /// <param name="At">Share of the festival day, 0 at the gates and 1 at the close.</param>
    /// <param name="Elevation">Sun height in degrees.</param>
    private sealed record DayKey(float At, float Elevation, float Yaw, Color Sun, float SunEnergy, Color Ambient, float AmbientEnergy, Color Sky);

    // Dusk and golden hour are kept lighter than a real evening so guests, rings and labels stay easy to read.
    private static readonly DayKey[] DayKeys =
    [
        new(0.00f, 62, -32, new("fff1c5"), 1.25f, new("d9e7c2"), 0.72f, new("8fc4dc")),
        new(0.25f, 54, -30, new("fff1cf"), 1.22f, new("d6e4cb"), 0.72f, new("9ccbe0")),
        new(0.56f, 36, -28, new("ffe6b4"), 1.18f, new("d3dbd6"), 0.76f, new("bcd3dd")),
        new(0.66f, 22, -26, new("ffbe82"), 1.15f, new("cdd3e2"), 0.86f, new("f0d9b4")),
        new(0.76f, 12, -24, new("ffa684"), 0.80f, new("b8c6e0"), 0.98f, new("b3a8c0")),
        new(1.00f, 6, -22, new("ff9a80"), 0.50f, new("a4b6dc"), 1.08f, new("6a7098")),
    ];
    // A heatwave bleaches the early-afternoon sky and hardens the sun until the clouds come.
    private static readonly Color HeatSky = new("d3e6ec"), HeatSun = new("fff7de");
    private const float HeatSunEnergy = 1.32f;
    private const float CloudsFrom = 0.22f, CloudsFull = 0.30f, CloudsFading = 0.50f, CloudsGone = 0.60f, CloudCoverage = 0.5f;
    private const float LightsFrom = 0.70f, LightsFull = 0.78f;
    private static readonly Vector3 Wind = new Vector3(1, 0, 0.55f).Normalized();
    private const float WindMetresPerSecond = 1.8f;

    private DirectionalLight3D _sun = null!;
    private Godot.Environment _environment = null!;
    private ShaderMaterial _cloudMaterial = null!;
    private Vector2 _cloudOffset;
    private double _breezeSeconds, _dayFittingsSync;
    private readonly List<(Node3D Node, Transform3D Home, float Phase, float Strength)> _breezeHedges = [];
    private readonly List<(Node3D Pole, Node3D[] Strings)> _festoonPoles = [];
    private readonly List<Node3D> _festoonStrings = [];
    private StandardMaterial3D _bulbMaterial = null!;
    private readonly List<Light3D> _dayPracticals = [];
    private readonly Dictionary<string, OmniLight3D> _vendorGlows = new(StringComparer.Ordinal);
    /// <summary>For verification captures: pins the day to a share of the festival instead of the clock.</summary>
    private float? _dayFractionOverride;
    private bool _dayIsHot;
    private MeshInstance3D _cloudShadows = null!;

    private void BuildDayCycle(Godot.Environment environment, DirectionalLight3D sun)
    {
        _environment = environment; _sun = sun;
        foreach (var arg in OS.GetCmdlineUserArgs())
            if (arg.StartsWith("--day-fraction=", StringComparison.Ordinal) &&
                float.TryParse(arg["--day-fraction=".Length..], System.Globalization.CultureInfo.InvariantCulture, out var pinned))
                _dayFractionOverride = Math.Clamp(pinned, 0, 1);
        // The default 100 m leaves the far corner of the field unshadowed from the rig's camera distance.
        _sun.DirectionalShadowMaxDistance = 160;
        BuildCloudShadows();
        BuildDuskPracticals();
        ApplyDayCycle(0);
    }

    /// <summary>
    /// Cloud shadows are a soft, partial darkening drifting over the field's ground. Real shadows would be either
    /// fully dark or absent; these only take the edge off the light so nothing on the field is ever hidden.
    /// </summary>
    private void BuildCloudShadows()
    {
        var shader = new Shader
        {
            Code = """
                shader_type spatial;
                render_mode unshaded, blend_mul, depth_draw_never, cull_disabled, shadows_disabled, fog_disabled;
                uniform sampler2D noise : repeat_enable, filter_linear;
                uniform vec2 offset;
                uniform float coverage;
                uniform float darkness = 0.3;
                uniform float scale = 0.006;
                void fragment() {
                    vec2 world = (INV_VIEW_MATRIX * vec4(VERTEX, 1.0)).xz;
                    float n = texture(noise, world * scale + offset).r;
                    float shade = smoothstep(1.0 - coverage - 0.06, 1.0 - coverage + 0.06, n) * step(0.001, coverage);
                    ALBEDO = vec3(1.0 - darkness * shade);
                }
                """,
        };
        _cloudMaterial = new ShaderMaterial { Shader = shader };
        _cloudMaterial.SetShaderParameter("noise", new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true,
            Noise = new FastNoiseLite { Frequency = 0.012f, FractalOctaves = 3, Seed = 7 },
        });
        _cloudMaterial.SetShaderParameter("coverage", 0f);
        // Just above the track; the field only, so the sky behind it never darkens.
        AddChild(_cloudShadows = new MeshInstance3D
        {
            Name = "CloudShadows", Position = new Vector3(0, 0.09f, 0),
            Mesh = new PlaneMesh { Size = new Vector2(64, 64) }, MaterialOverride = _cloudMaterial,
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
    }

    /// <summary>
    /// The last set's lights: festoons along the track through the middle of the field, an amber and rose wash on
    /// the stage, the farmhouse windows, and glow at each stall.
    /// </summary>
    private void BuildDuskPracticals()
    {
        _bulbMaterial = new StandardMaterial3D
        {
            AlbedoColor = new Color("f2d9a6"), EmissionEnabled = true, Emission = new Color("ffbf5e"), EmissionEnergyMultiplier = 0,
        };
        var wood = new StandardMaterial3D { AlbedoColor = new Color("6e4c31"), Roughness = 1 };
        var cord = new StandardMaterial3D { AlbedoColor = new Color("2d2a26"), Roughness = 1 };
        // Festoons along the south-east side of the track, pole to pole down the middle of the field: light and
        // a bit of atmosphere where people cross between the stage and the stalls, without fencing anyone in.
        const float poleHeight = 3.6f, side = 2.9f;
        // The first pole stands well back from the gate lane, where everyone arrives and leaves.
        float[] poleZ = [22, 14, 6, -2, -10];
        Node3D Pole(Vector3 at)
        {
            var pole = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = .06f, BottomRadius = .08f, Height = poleHeight, RadialSegments = 6 },
                MaterialOverride = wood, Position = at + new Vector3(0, poleHeight / 2, 0),
            };
            AddChild(pole); return pole;
        }
        var poles = poleZ.Select(z => Pole(new Vector3(side, 0, z))).ToArray();
        var runs = new Node3D[poleZ.Length - 1];
        for (var i = 0; i < runs.Length; i++)
        {
            Vector3 from = new(side, poleHeight - .1f, poleZ[i]), to = new(side, poleHeight - .1f, poleZ[i + 1]);
            runs[i] = Festoon(from, to, .6f, cord);
            // A little warm light pooled under each run; it goes dark with its run if a pole has to give way.
            var glow = new OmniLight3D { Position = (to - from) / 2 + new Vector3(0, -1, 0), LightColor = new Color("ffc477"),
                OmniRange = 6.5f, LightEnergy = 0, ShadowEnabled = false };
            glow.SetMeta("full", 1.4f);
            runs[i].AddChild(glow); _dayPracticals.Add(glow);
        }
        for (var i = 0; i < poles.Length; i++)
            _festoonPoles.Add((poles[i], runs.Where((_, run) => run == i - 1 || run == i).ToArray()));
        _festoonStrings.AddRange(runs);

        OmniLight3D Glow(Vector3 at, Color colour, float range)
        {
            var light = new OmniLight3D { Position = at, LightColor = colour, OmniRange = range, LightEnergy = 0, ShadowEnabled = false };
            AddChild(light); _dayPracticals.Add(light); light.SetMeta("full", 1f); return light;
        }
        Glow(new(-22, 2.4f, -8.4f), new("ffbd6a"), 6).SetMeta("full", 2f); // Farmhouse windows.
        SpotLight3D Wash(Vector3 at, Color colour)
        {
            var spot = new SpotLight3D { Position = at, LightColor = colour, SpotRange = 16, SpotAngle = 28, LightEnergy = 0 };
            AddChild(spot); spot.LookAt(new Vector3(-15.5f, 1.4f, 11), Vector3.Up);
            _dayPracticals.Add(spot); spot.SetMeta("full", 14f); return spot;
        }
        Wash(new(-6.5f, 5, 8.5f), new("ffb24f"));
        Wash(new(-6.5f, 5, 13.5f), new("ff6f8e"));
    }

    /// <summary>A sagging cord between two points, hung with bulbs about every 0.9 m.</summary>
    private Node3D Festoon(Vector3 from, Vector3 to, float sag, Material cord)
    {
        var root = new Node3D { Position = from };
        AddChild(root);
        var span = to - from;
        var bulbs = Math.Max(2, Mathf.RoundToInt(span.Length() / 1.3f));
        Vector3 At(float t) => span * t + new Vector3(0, -4 * sag * t * (1 - t), 0);
        const int segments = 16;
        for (var i = 0; i < segments; i++)
        {
            Vector3 a = At(i / (float)segments), b = At((i + 1) / (float)segments);
            var piece = new MeshInstance3D
            {
                Mesh = new CylinderMesh { TopRadius = .015f, BottomRadius = .015f, Height = (b - a).Length(), RadialSegments = 4 },
                MaterialOverride = cord, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
            };
            root.AddChild(piece);
            piece.Position = (a + b) / 2;
            piece.Basis = new Basis(new Quaternion(Vector3.Up, (b - a).Normalized()));
        }
        var multimesh = new MultiMesh
        {
            TransformFormat = MultiMesh.TransformFormatEnum.Transform3D, InstanceCount = bulbs,
            Mesh = new SphereMesh { Radius = .12f, Height = .24f, RadialSegments = 6, Rings = 3 },
        };
        for (var i = 0; i < bulbs; i++)
            multimesh.SetInstanceTransform(i, new Transform3D(Basis.Identity, At((i + .5f) / bulbs) + new Vector3(0, -.09f, 0)));
        root.AddChild(new MultiMeshInstance3D
        {
            Multimesh = multimesh, MaterialOverride = _bulbMaterial, CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        });
        return root;
    }

    /// <param name="strength">How far it leans relative to a hedge; a tall crown needs much less to look right.</param>
    private void RegisterBreezeHedge(Node3D hedge, float strength = 1) =>
        _breezeHedges.Add((hedge, hedge.Transform, _breezeHedges.Count * 0.9f, strength));

    /// <summary>
    /// The share of the festival day the light shows: the clock while running (held where it stopped if the day
    /// failed), dusk once guests leave, and early afternoon while preparing.
    /// </summary>
    private float DayFraction()
    {
        if (_dayFractionOverride is { } pinned) return pinned;
        if (_session?.CapturePreparation() is not { } p) return 0;
        return p.Status switch
        {
            PreparationStatus.Running or PreparationStatus.Failed =>
                Math.Clamp((_session.CurrentTick - p.StartedTick) / (float)GameSession.PreparedDayTicks, 0, 1),
            PreparationStatus.Departing or PreparationStatus.Finished => 1,
            _ => 0,
        };
    }

    private static float Smooth(float from, float to, float x) => Mathf.SmoothStep(from, to, x);

    private void ProcessDayCycle(double delta)
    {
        if (_session is null) return;
        var running = !_session.IsPaused || _dayFractionOverride is not null;
        if (running) _breezeSeconds += delta;
        var day = DayFraction();
        ApplyDayCycle(day);
        // Clouds drift with the wind and the breeze gusts about every 20 seconds.
        if (running) _cloudOffset += new Vector2(Wind.X, Wind.Z) * (float)(WindMetresPerSecond * delta * 0.006);
        _cloudMaterial.SetShaderParameter("offset", -_cloudOffset);
        var t = (float)_breezeSeconds;
        var gust = Mathf.Pow(Mathf.Max(0, Mathf.Sin(t * Mathf.Tau / 20f)), 4);
        foreach (var (node, home, phase, strength) in _breezeHedges)
        {
            var lean = strength * (.012f + .035f * gust) * Mathf.Sin(t * 1.7f + phase);
            // Lean the hedge top downwind while its base stays planted: a shear, not a tilt.
            var shear = new Basis(Vector3.Right, new Vector3(Wind.X * lean, 1, Wind.Z * lean), Vector3.Back);
            node.Transform = new Transform3D(shear * home.Basis, home.Origin);
        }
        _dayFittingsSync -= delta;
        if (_dayFittingsSync <= 0) { _dayFittingsSync = .5; SyncDayFittings(); }
    }

    private void ApplyDayCycle(float day)
    {
        var next = Array.FindIndex(DayKeys, key => key.At >= day);
        if (next <= 0) next = 1;
        DayKey a = DayKeys[next - 1], b = DayKeys[next];
        var w = Smooth(0, 1, Math.Clamp((day - a.At) / (b.At - a.At), 0, 1));
        var sun = a.Sun.Lerp(b.Sun, w); var sky = a.Sky.Lerp(b.Sky, w);
        var sunEnergy = Mathf.Lerp(a.SunEnergy, b.SunEnergy, w);
        // The heatwave look holds until the clouds arrive.
        if (_dayIsHot)
        {
            var heat = 1 - Smooth(CloudsFrom, CloudsFull + .05f, day);
            sky = sky.Lerp(HeatSky, heat); sun = sun.Lerp(HeatSun, heat); sunEnergy = Mathf.Lerp(sunEnergy, HeatSunEnergy, heat);
        }
        var clouds = CloudCoverage * Smooth(CloudsFrom, CloudsFull, day) * (1 - Smooth(CloudsFading, CloudsGone, day));
        _cloudMaterial.SetShaderParameter("coverage", clouds);
        _cloudShadows.Visible = clouds > 0;
        _sun.RotationDegrees = new Vector3(-Mathf.Lerp(a.Elevation, b.Elevation, w), Mathf.Lerp(a.Yaw, b.Yaw, w), 0);
        _sun.LightColor = sun; _sun.LightEnergy = sunEnergy;
        // A sun on the horizon would throw shadows across the whole field; they fade as it sets.
        _sun.ShadowOpacity = 1 - .45f * Smooth(.7f, 1, day);
        _environment.BackgroundColor = sky;
        _environment.AmbientLightColor = a.Ambient.Lerp(b.Ambient, w);
        _environment.AmbientLightEnergy = Mathf.Lerp(a.AmbientEnergy, b.AmbientEnergy, w);
        var lights = Smooth(LightsFrom, LightsFull, day);
        // A dark light still costs a lighting pass for everything it reaches, so it is hidden until it comes on.
        _bulbMaterial.EmissionEnergyMultiplier = 1.5f * lights;
        foreach (var light in _dayPracticals) { light.LightEnergy = lights * (float)light.GetMeta("full"); light.Visible = lights > 0; }
        foreach (var glow in _vendorGlows.Values) { glow.LightEnergy = 2.4f * lights; glow.Visible = lights > 0; }
    }

    /// <summary>Stall glows follow wherever the stalls are built; a pole gives way to anything built on its spot.</summary>
    private void SyncDayFittings()
    {
        _dayIsHot = _session.CaptureMedical() is { IsHot: true };
        foreach (var id in _vendorGlows.Keys.Where(id => !_immersionVendors.ContainsKey(id)).ToArray())
        { _vendorGlows[id].QueueFree(); _vendorGlows.Remove(id); }
        foreach (var (id, body) in _immersionVendors)
        {
            if (!_vendorGlows.TryGetValue(id, out var glow))
            {
                glow = new OmniLight3D { LightColor = new Color("ffd59a"), OmniRange = 6, LightEnergy = 0, ShadowEnabled = false, Visible = false };
                AddChild(glow); _vendorGlows.Add(id, glow);
            }
            glow.Position = body.Position + new Vector3(0, 2.6f, 0);
        }
        var built = _session.CaptureBuildPlacements().Select(item => ImmersionPosition(item.Cell)).ToArray();
        var hidden = new HashSet<Node3D>();
        foreach (var (pole, strings) in _festoonPoles)
        {
            var at = new Vector2(pole.Position.X, pole.Position.Z);
            var blocked = built.Any(item => new Vector2(item.X, item.Z).DistanceTo(at) < 3f);
            pole.Visible = !blocked;
            if (blocked) foreach (var festoon in strings) hidden.Add(festoon);
        }
        foreach (var festoon in _festoonStrings) festoon.Visible = !hidden.Contains(festoon);
    }
}

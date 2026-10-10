using Festival.Simulation;
using Godot;
using System;
using System.Linq;

namespace Festival.Game;

/// <summary>
/// Rain over the field: streaks falling round the camera's view, splashes on the ground, rings on the puddles, the
/// ground going glossy and dark as it soaks, a grey veil, and the sun dimming under each shower. Everything is driven
/// from two values: <c>rain</c> (0 dry, 0.6 heavy showers, 1 a downpour), eased in and out of each of the simulation's
/// showers, and <c>wetness</c>, which follows it up quickly and down slowly. Presentation only.
/// </summary>
public partial class Main
{
    private const float HeavyRain = .6f;
    // A shower takes about 15 s of festival time to build and to die away.
    private const int RainRampTicks = 1_200;
    // Wetness follows the rain up in about 30 s and down over about four minutes.
    private const float WetUpPerTick = 1f / (30 * 80), WetDownPerTick = 1f / (240 * 80);
    private float _rain, _wetness;
    private long _wetnessTick = -1;
    private Node3D? _rainRig;
    private GpuParticles3D? _rainStreaks, _rainSplashes;
    private QuadMesh? _streakMesh;
    private StandardMaterial3D? _streakMaterial;
    private ColorRect? _rainVeil;
    private ShaderMaterial? _veilMaterial;
    private const int StreaksAtDownpour = 5_800, SplashesAtDownpour = 200;

    /// <summary>The rain as drawn: each shower eased in after it starts and out before it ends.</summary>
    private float RainLevel()
    {
        if (_session?.CaptureWeather() is not { Kind: WeatherKind.Rain } weather || _session.CapturePreparation() is not { } p ||
            p.Status is PreparationStatus.Preparing) return 0;
        var day = _session.CurrentTick - p.StartedTick;
        foreach (var spell in weather.Spells)
        {
            if (day < spell.StartTick || day >= spell.EndTick) continue;
            var target = spell.Intensity == RainIntensity.Downpour ? 1f : HeavyRain;
            var ramp = Math.Min(1f, Math.Min(day - spell.StartTick, spell.EndTick - day) / (float)RainRampTicks);
            return target * Mathf.SmoothStep(0, 1, ramp);
        }
        return 0;
    }

    private void BuildRain()
    {
        _rainRig = new Node3D { Name = "Rain" };
        AddChild(_rainRig);
        _streakMesh = new QuadMesh { Size = new Vector2(.02f, .9f) };
        // Unshaded and tinted by the light of the day, so they read in the dim of a shower and fade at dusk.
        _streakMaterial = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>("res://assets/environment/lwf_rain_streak_v1.png"), AlbedoColor = new Color("e2eaf0"),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            VertexColorUseAsAlbedo = true, CullMode = BaseMaterial3D.CullModeEnum.Disabled, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _streakMesh.Material = _streakMaterial;
        var fall = new ParticleProcessMaterial
        {
            EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(30, .5f, 30),
            // About 8 degrees off vertical, towards screen right whichever way the camera faces.
            Direction = new Vector3(Mathf.Sin(Mathf.DegToRad(8)), -1, 0).Normalized(), Spread = 0, Gravity = Vector3.Zero,
            InitialVelocityMin = 9, InitialVelocityMax = 10, ParticleFlagAlignY = true,
            ScaleMin = .8f, ScaleMax = 1.25f,
            ColorInitialRamp = new GradientTexture1D
            {
                Gradient = new Gradient { Offsets = [0, 1], Colors = [new Color(1, 1, 1, .5f), new Color(1, 1, 1, .85f)] },
            },
        };
        _rainStreaks = new GpuParticles3D
        {
            Name = "Streaks", Amount = StreaksAtDownpour, Lifetime = 1.35, Emitting = false, ProcessMaterial = fall, DrawPass1 = _streakMesh,
            Position = new Vector3(0, 12, 0), VisibilityAabb = new Aabb(new Vector3(-60, -14, -60), new Vector3(120, 16, 120)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off, FixedFps = 0, Preprocess = 1.4,
        };
        _rainRig.AddChild(_rainStreaks);
        var splashMesh = new QuadMesh { Size = new Vector2(.14f, .14f), CenterOffset = new Vector3(0, .07f, 0) };
        splashMesh.Material = new StandardMaterial3D
        {
            AlbedoTexture = GD.Load<Texture2D>("res://assets/environment/lwf_rain_splash_v1.png"), AlbedoColor = new Color(.9f, .94f, .97f, .8f),
            Transparency = BaseMaterial3D.TransparencyEnum.Alpha, BillboardMode = BaseMaterial3D.BillboardModeEnum.Particles,
            ParticlesAnimHFrames = 4, ParticlesAnimVFrames = 1, ParticlesAnimLoop = false, ShadingMode = BaseMaterial3D.ShadingModeEnum.Unshaded,
        };
        _rainSplashes = new GpuParticles3D
        {
            Name = "Splashes", Amount = SplashesAtDownpour, Lifetime = .2, Emitting = false, DrawPass1 = splashMesh,
            ProcessMaterial = new ParticleProcessMaterial
            {
                EmissionShape = ParticleProcessMaterial.EmissionShapeEnum.Box, EmissionBoxExtents = new Vector3(30, 0, 30),
                Gravity = Vector3.Zero, AnimSpeedMin = 1, AnimSpeedMax = 1,
            },
            Position = new Vector3(0, .03f, 0), VisibilityAabb = new Aabb(new Vector3(-60, -1, -60), new Vector3(120, 2, 120)),
            CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
        };
        _rainRig.AddChild(_rainSplashes);
        // The grey veil over the view: lighter in heavy showers, thicker with drifting bands in a downpour.
        _veilMaterial = new ShaderMaterial
        {
            Shader = new Shader
            {
                Code = """
                    shader_type canvas_item;
                    uniform vec4 veil : source_color = vec4(0.604, 0.627, 0.667, 0.0);
                    uniform float bands = 0.0;
                    uniform sampler2D noise : repeat_enable, filter_linear;
                    uniform vec2 drift;
                    void fragment() {
                        float n = texture(noise, SCREEN_UV * vec2(0.8, 0.5) + drift).r;
                        COLOR = vec4(veil.rgb, veil.a + bands * smoothstep(0.45, 0.75, n));
                    }
                    """,
            },
        };
        _veilMaterial.SetShaderParameter("noise", new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true, Noise = new FastNoiseLite { Frequency = 0.008f, FractalOctaves = 2, Seed = 23 },
        });
        var layer = new CanvasLayer { Name = "RainVeil", Layer = 0 };
        AddChild(layer);
        _rainVeil = new ColorRect { Material = _veilMaterial, MouseFilter = Control.MouseFilterEnum.Ignore, Visible = false };
        _rainVeil.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(_rainVeil);
    }

    /// <summary>Works out this frame's rain and wetness before the day's light is set from them.</summary>
    private void UpdateRainLevel()
    {
        _rain = RainLevel();
        var tick = _session?.CurrentTick ?? 0;
        if (_session?.CaptureWeather() is null || _session.PreparedStatus is PreparationStatus.Preparing) { _wetness = 0; _wetnessTick = tick; return; }
        // Wetness eases by festival time, so it's the same at any speed; a jump (a load) settles at once.
        var elapsed = _wetnessTick < 0 ? 0 : tick - _wetnessTick;
        _wetnessTick = tick;
        var target = Math.Min(1f, _rain / HeavyRain);
        if (elapsed > 2_400 || elapsed < 0) _wetness = Math.Max(_wetness, target);
        else if (target > _wetness) _wetness = Math.Min(target, _wetness + elapsed * WetUpPerTick);
        else _wetness = Math.Max(target, _wetness - elapsed * WetDownPerTick);
    }

    private void ProcessRain()
    {
        if (_rainRig is null || _rainStreaks is null || _rainSplashes is null || _streakMesh is null || _rainVeil is null || _veilMaterial is null) return;
        var raining = _rain > .01f;
        _rainStreaks.Emitting = raining; _rainSplashes.Emitting = raining;
        _rainStreaks.SpeedScale = _rainSplashes.SpeedScale = _session.IsPaused ? 0 : 1;
        // A constant count on screen at any zoom: the emitter covers the camera's ground footprint, the amount stays put.
        var camera = _rig.Camera;
        var size = camera.Size;
        var aspect = GetViewport().GetVisibleRect().Size.X / Math.Max(1f, GetViewport().GetVisibleRect().Size.Y);
        var across = size * aspect / 2 + 4; var along = size / Mathf.Sin(Mathf.DegToRad(30)) / 2 + 4;
        _rainRig.Position = _rig.Focus;
        _rainRig.Rotation = new Vector3(0, camera.GlobalRotation.Y, 0);
        ((ParticleProcessMaterial)_rainStreaks.ProcessMaterial).EmissionBoxExtents = new Vector3(across, .5f, along);
        ((ParticleProcessMaterial)_rainSplashes.ProcessMaterial).EmissionBoxExtents = new Vector3(across, 0, along);
        var downpour = Math.Clamp((_rain - HeavyRain) / (1 - HeavyRain), 0, 1);
        var heavyShare = 3_100f / StreaksAtDownpour;
        var ratio = _rain <= HeavyRain ? _rain / HeavyRain * heavyShare : Mathf.Lerp(heavyShare, 1, downpour);
        _rainStreaks.AmountRatio = ratio; _rainSplashes.AmountRatio = ratio;
        // A pixel floor on the streak width, so they never vanish when zoomed out.
        var pixelsPerMetre = GetViewport().GetVisibleRect().Size.Y / Math.Max(1f, size);
        _streakMesh.Size = new Vector2(Math.Max(Mathf.Lerp(.02f, .025f, downpour), Mathf.Lerp(1.3f, 1.6f, downpour) / pixelsPerMetre), Mathf.Lerp(.9f, 1.15f, downpour));
        var dusk = Smooth(.7f, .95f, DayFraction());
        if (_streakMaterial is not null) _streakMaterial.AlbedoColor = new Color("e2eaf0").Lerp(new Color("5d6474"), dusk);
        var veilColour = new Color("9aa0aa").Lerp(new Color("282c3c"), dusk);
        var veil = _rain <= HeavyRain ? .07f * _rain / HeavyRain : Mathf.Lerp(.07f, .15f, downpour);
        _veilMaterial.SetShaderParameter("veil", new Color(veilColour, veil));
        _veilMaterial.SetShaderParameter("bands", .10f * downpour);
        _veilMaterial.SetShaderParameter("drift", new Vector2((float)_breezeSeconds * .01f, (float)_breezeSeconds * .004f));
        _rainVeil.Visible = veil > .002f;
        if (_groundMaterial is not null)
        {
            _groundMaterial.SetShaderParameter("rain", _rain);
            _groundMaterial.SetShaderParameter("wetness", _wetness);
        }
        ApplyPropSheen();
    }

    /// <summary>
    /// The shower light: multipliers on whatever the day is doing, by how hard it's raining (the board's table).
    /// When the rain clears with the ground still wet, the sun comes back a touch warmer: the glistening frame.
    /// </summary>
    private void ApplyShowerLight()
    {
        if (_rain <= 0 && _wetness <= 0) return;
        float At(float dry, float arriving, float heavy, float downpour) => _rain <= .3f ? Mathf.Lerp(dry, arriving, _rain / .3f) :
            _rain <= HeavyRain ? Mathf.Lerp(arriving, heavy, (_rain - .3f) / .3f) : Mathf.Lerp(heavy, downpour, (_rain - HeavyRain) / (1 - HeavyRain));
        Color Tint(Color now, Color heavy, Color downpour, float arrivingShare) => _rain <= .3f ? now.Lerp(heavy, arrivingShare * _rain / .3f) :
            _rain <= HeavyRain ? now.Lerp(heavy, Mathf.Lerp(arrivingShare, 1, (_rain - .3f) / .3f)) : heavy.Lerp(downpour, (_rain - HeavyRain) / (1 - HeavyRain));
        // Gentler than the board's table: this field's palette is darker, and the crowd must stay readable.
        _sun.LightEnergy *= At(1, .62f, .45f, .36f);
        _sun.LightAngularDistance = At(0, 10, 28, 32);
        _sun.LightColor = Tint(_sun.LightColor, new Color("ebf2ff"), new Color("e6eefc"), .5f);
        _environment.AmbientLightColor = Tint(_environment.AmbientLightColor, new Color("9ba8b8"), new Color("909dad"), .4f);
        _environment.AmbientLightEnergy *= At(1, 1, 1, .95f);
        _environment.BackgroundColor = Tint(_environment.BackgroundColor, new Color("6b7580"), new Color("636c77"), .5f);
        _environment.TonemapExposure = 1 + At(0, .05f, .08f, .06f);
        // Clearing: the sun returns, a little warmer while the ground still shines.
        if (_rain <= .01f && _wetness > .05f) _sun.LightColor = _sun.LightColor.Lerp(new Color("ffe2b0"), .25f * _wetness);
        // No drifting cloud shadows under a shower: the sun is too soft for them.
        if (_rain > .3f) _cloudShadows.Visible = false;
    }

    // The props' own materials, darkened and glossed as everything gets wet (heavy-rain INTEGRATION §4).
    private readonly System.Collections.Generic.Dictionary<StandardMaterial3D, (Color Albedo, float Roughness)> _sheenMaterials = [];
    private float _sheenShown;
    private double _sheenScan;

    private void ApplyPropSheen()
    {
        _sheenScan -= GetProcessDeltaTime();
        if (_sheenScan <= 0 && _wetness > 0)
        {
            _sheenScan = 3;
            // Bars, vans, first aid, the steward post and the matting: their shared palette materials.
            var meshes = _immersionVendors.Values.Cast<Node>().Concat(_responsePostVisuals.Values)
                .SelectMany(node => node.FindChildren("*", "MeshInstance3D", true, false).OfType<MeshInstance3D>())
                .Select(mesh => (Mesh: (Mesh?)mesh.Mesh, Instance: (MeshInstance3D?)mesh)).Append((Mesh: _matMesh, Instance: null));
            foreach (var (mesh, instance) in meshes)
                for (var surface = 0; surface < (mesh?.GetSurfaceCount() ?? 0); surface++)
                        if ((instance?.GetSurfaceOverrideMaterial(surface) ?? mesh!.SurfaceGetMaterial(surface)) is StandardMaterial3D material &&
                            !material.EmissionEnabled && material.Transparency == BaseMaterial3D.TransparencyEnum.Disabled && !_sheenMaterials.ContainsKey(material))
                            _sheenMaterials[material] = (material.AlbedoColor, material.Roughness);
        }
        if (Math.Abs(_wetness - _sheenShown) < .02f) return;
        _sheenShown = _wetness;
        var darker = new Color(1, 1, 1).Lerp(new Color(.70f, .73f, .76f), _wetness);
        foreach (var (material, dry) in _sheenMaterials)
        {
            material.AlbedoColor = new Color(dry.Albedo.R * darker.R, dry.Albedo.G * darker.G, dry.Albedo.B * darker.B, dry.Albedo.A);
            material.Roughness = Mathf.Lerp(dry.Roughness, .22f, _wetness);
        }
    }

    /// <summary>
    /// A guest's rain look: soaked colours once wet through, a poncho once bought, and the clip for where they are: out
    /// in it, hurrying to cover, or huddled under it.
    /// </summary>
    private void SetRainLook(NavigationObservation agent, Node3D visual, Vector3 at, RainIntensity rain)
    {
        var id = agent.Id.Value;
        var cell = TraversalGrid.WorldToCell((int)(at.X * 1000), (int)(at.Z * 1000));
        var mode = rain == RainIntensity.Dry ? CrowdBodies.RainMode.None : _session.CoveredCell(cell) ? CrowdBodies.RainMode.Cover :
            agent.IntentId == ShelterRules.Intent ? CrowdBodies.RainMode.Hurry : CrowdBodies.RainMode.Out;
        Bodies.SetRain(visual, _session.Soaked(id), mode, _session.OwnsPoncho(id));
    }
}

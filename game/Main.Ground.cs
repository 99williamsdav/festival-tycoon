using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

/// <summary>
/// The field's grass: mown stripes, clover drifts, a wildflower margin by the hedges and the odd buttercup drift,
/// worn by the day's footfall from flattened grass through browned to bare earth. The wear comes from the
/// simulation's per-cell ground state; this only draws it.
/// </summary>
public partial class Main
{
    private const int GroundMapCells = GroundRules.FieldLastCell - GroundRules.FieldFirstCell + 1;
    private ShaderMaterial _groundMaterial = null!;
    private Image _groundImage = null!;
    private ImageTexture _groundTexture = null!;
    private (GameSession? Session, long Version) _groundShown;
    private double _groundSync;

    private ShaderMaterial GroundMaterial()
    {
        if (_groundMaterial is not null) return _groundMaterial;
        _groundImage = Image.CreateEmpty(GroundMapCells, GroundMapCells, false, Image.Format.L8);
        _groundTexture = ImageTexture.CreateFromImage(_groundImage);
        _groundMaterial = new ShaderMaterial { Shader = new Shader { Code = GroundShader } };
        _groundMaterial.SetShaderParameter("palette", GD.Load<Texture2D>("res://assets/environment/lwf_field_grass_tile_8m_v1_field_track_palette.png"));
        _groundMaterial.SetShaderParameter("wear_map", _groundTexture);
        _groundMaterial.SetShaderParameter("noise", new NoiseTexture2D
        {
            Width = 256, Height = 256, Seamless = true,
            Noise = new FastNoiseLite { Frequency = 0.02f, FractalOctaves = 3, Seed = 11 },
        });
        return _groundMaterial;
    }

    private void ApplyGroundMaterial(Node3D tile)
    {
        foreach (var node in tile.FindChildren("*", "MeshInstance3D", true, false))
            ((MeshInstance3D)node).MaterialOverride = GroundMaterial();
    }

    private void ProcessGround(double delta)
    {
        _groundSync -= delta;
        if (_groundSync > 0 || _session is null) return;
        _groundSync = .5;
        if (_groundShown.Session == _session && _groundShown.Version == _session.GroundVersion) return;
        _groundShown = (_session, _session.GroundVersion);
        RedrawGroundWear();
    }

    /// <summary>
    /// Feet don't land on exact cells, so the wear is spread about two metres either way before it is drawn, then graded so
    /// a well-trodden route shows as flattened grass and a standing crowd's patch goes to bare earth.
    /// </summary>
    private void RedrawGroundWear()
    {
        const int n = GroundMapCells, first = GroundRules.FieldFirstCell;
        float[] kernel = [1, 2, 3, 4, 3, 2, 1];
        const float total = 16;
        var raw = new float[n * n];
        for (var z = 0; z < n; z++)
        for (var x = 0; x < n; x++)
            raw[z * n + x] = _session.GroundWearAt(new GridCell(first + x, first + z));
        var across = new float[n * n];
        var pixels = new byte[n * n];
        for (var z = 0; z < n; z++)
        for (var x = 0; x < n; x++)
        {
            float sum = 0;
            for (var k = -3; k <= 3; k++) sum += kernel[k + 3] * raw[z * n + Math.Clamp(x + k, 0, n - 1)];
            across[z * n + x] = sum / total;
        }
        for (var z = 0; z < n; z++)
        for (var x = 0; x < n; x++)
        {
            float sum = 0;
            for (var k = -3; k <= 3; k++) sum += kernel[k + 3] * across[Math.Clamp(z + k, 0, n - 1) * n + x];
            pixels[z * n + x] = (byte)Mathf.RoundToInt(255 * Mathf.Sqrt(Math.Min(1f, sum / total / GroundRules.BareEarthWear)));
        }
        _groundImage.SetData(n, n, false, Image.Format.L8, pixels);
        _groundTexture.Update(_groundImage);
    }

    private const string GroundShader = """
        shader_type spatial;
        uniform sampler2D palette : source_color, filter_nearest;
        uniform sampler2D wear_map : filter_linear;
        uniform sampler2D noise : filter_linear, repeat_enable;
        varying vec2 field;

        vec3 lin(vec3 srgb) { return pow(srgb, vec3(2.2)); }
        float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

        void vertex() { field = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz; }

        void fragment() {
            vec3 c = texture(palette, UV).rgb;
            vec2 p = field;
            float n1 = texture(noise, p * 0.035).r;
            // Mown stripes, 4 m wide, running with the track.
            c *= mod(floor((p.x + 32.0) / 4.0), 2.0) > 0.5 ? 1.04 : 0.965;
            // Darker clover drifts.
            float clover = smoothstep(0.6, 0.68, n1);
            c = mix(c, c * vec3(0.84, 0.92, 0.9), clover * 0.75);
            // The uncut margin along the hedges, longer and yellower, broken at the gate.
            float edge = 32.0 - max(abs(p.x), abs(p.y));
            float margin = (1.0 - smoothstep(2.6, 3.6, edge + (n1 - 0.5) * 1.5)) * (1.0 - step(abs(p.x), 4.0) * step(27.0, p.y));
            c = mix(c, c * vec3(1.06, 1.04, 0.86), margin * 0.7);
            // Flowers: buttercups and daisies in the margin, a few in drifts out in the field.
            vec2 g = floor(p / 0.45);
            float h = hash(g);
            float drift = smoothstep(0.68, 0.74, texture(noise, p * 0.05 + vec2(0.37, 0.11)).r);
            float flower = step(length(fract(p / 0.45) - 0.5), 0.17) * step(h, margin * 0.24 + drift * 0.12 + clover * 0.04);
            vec3 bloom = fract(h * 13.0) < 0.6 ? lin(vec3(1.0, 0.85, 0.22)) : lin(vec3(0.96, 0.95, 0.88));

            // Wear: flattened, then dry and browned, then dry earth, with ragged edges.
            float w = texture(wear_map, (p + 32.0) / 64.0).r;
            w = clamp(w + (texture(noise, p * 0.25).r - 0.5) * 0.2 * step(0.04, w), 0.0, 1.0);
            float flattened = smoothstep(0.1, 0.16, w), browned = smoothstep(0.42, 0.48, w), earth = smoothstep(0.7, 0.75, w);
            float grey = dot(c, vec3(0.3, 0.59, 0.11));
            // Pressed grass is duller and a touch darker, never paler: a pale patch would read as a highlight.
            c = mix(c, mix(c, vec3(grey) * vec3(1.02, 1.0, 0.86), 0.4) * 0.94, flattened * 0.85);
            c = mix(c, lin(vec3(0.66, 0.62, 0.47)) * (0.92 + 0.16 * n1), browned * 0.75);
            float scuff = texture(noise, p * 0.6).r;
            float tuft = step(0.78, texture(noise, p * 0.9 + vec2(0.5)).r);
            // Pale, dusty and matte: never the dark of mud or the orange of the track.
            c = mix(c, mix(lin(vec3(0.69, 0.66, 0.6)) * (0.9 + 0.2 * scuff), c, tuft * 0.6), earth);
            c = mix(c, bloom, flower * (1.0 - flattened));
            ALBEDO = c;
            ROUGHNESS = 0.97;
        }
        """;
}

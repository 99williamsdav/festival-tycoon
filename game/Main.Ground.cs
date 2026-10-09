using Festival.Simulation;
using Godot;
using System;

namespace Festival.Game;

/// <summary>
/// The field's grass: soft pasture, clover drifts, a wildflower margin by the hedges and the odd buttercup drift,
/// worn by the day's footfall from flattened grass through browned to bare earth, and where water lies, soaked
/// grass, puddles, churned mud and sludge. It all comes from the simulation's per-cell ground state; this only
/// draws it.
/// </summary>
public partial class Main
{
    private const int GroundMapCells = GroundRules.FieldLastCell - GroundRules.FieldFirstCell + 1;
    private ShaderMaterial _groundMaterial = null!;
    private Image _groundImage = null!;
    private ImageTexture _groundTexture = null!;
    private Image _wetImage = null!;
    private ImageTexture _wetTexture = null!;
    private (GameSession? Session, long Version) _groundShown;
    private double _groundSync;
    private MeshInstance3D? _pastureGrassMesh;
    private Material? _pastureGrassOriginal;
    private ShaderMaterial? _pastureGrassMaterial;

    private void ApplyPastureGrass(Node3D pasture)
    {
        _pastureGrassMesh = (MeshInstance3D)pasture.FindChildren("*", "MeshInstance3D", true, false)[0];
        _pastureGrassOriginal = _pastureGrassMesh.GetActiveMaterial(0);
        _pastureGrassMaterial = (ShaderMaterial)GroundMaterial().Duplicate();
        _pastureGrassMaterial.SetShaderParameter("pasture", true);
        _pastureGrassMaterial.SetShaderParameter("palette", ((StandardMaterial3D)_pastureGrassOriginal).AlbedoTexture);
        _pastureGrassMesh.MaterialOverride = _pastureGrassMaterial;
    }

    private ShaderMaterial GroundMaterial()
    {
        if (_groundMaterial is not null) return _groundMaterial;
        _groundImage = Image.CreateEmpty(GroundMapCells, GroundMapCells, false, Image.Format.L8);
        _groundTexture = ImageTexture.CreateFromImage(_groundImage);
        _wetImage = Image.CreateEmpty(GroundMapCells, GroundMapCells, false, Image.Format.Rg8);
        _wetTexture = ImageTexture.CreateFromImage(_wetImage);
        _groundMaterial = new ShaderMaterial { Shader = new Shader { Code = GroundShader } };
        _groundMaterial.SetShaderParameter("palette", GD.Load<Texture2D>("res://assets/environment/lwf_field_grass_tile_8m_v1_field_track_palette.png"));
        _groundMaterial.SetShaderParameter("wear_map", _groundTexture);
        _groundMaterial.SetShaderParameter("wet_map", _wetTexture);
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
        RedrawGroundWater();
    }

    /// <summary>
    /// Water in red and mud in green, softened only a little so a puddle keeps its shape: a quarter of the way to
    /// red is a puddle, half is brimming; a third of the way to green is mud, two thirds is a swamp's sludge.
    /// </summary>
    private void RedrawGroundWater()
    {
        const int n = GroundMapCells, first = GroundRules.FieldFirstCell;
        const float wetScale = GroundRules.WetSpill * 2, mudScale = GroundRules.SwampMud * 1.5f;
        var wet = new float[n * n]; var mud = new float[n * n];
        for (var z = 0; z < n; z++)
        for (var x = 0; x < n; x++)
        {
            var cell = new GridCell(first + x, first + z);
            wet[z * n + x] = Math.Min(1f, _session.GroundWetAt(cell) / wetScale);
            mud[z * n + x] = Math.Min(1f, _session.GroundMudAt(cell) / mudScale);
        }
        var pixels = new byte[n * n * 2];
        for (var z = 0; z < n; z++)
        for (var x = 0; x < n; x++)
        {
            float Soft(float[] layer)
            {
                float sum = 4 * layer[z * n + x];
                sum += layer[z * n + Math.Max(0, x - 1)] + layer[z * n + Math.Min(n - 1, x + 1)];
                sum += layer[Math.Max(0, z - 1) * n + x] + layer[Math.Min(n - 1, z + 1) * n + x];
                return sum / 8;
            }
            pixels[(z * n + x) * 2] = (byte)Mathf.RoundToInt(255 * Soft(wet));
            pixels[(z * n + x) * 2 + 1] = (byte)Mathf.RoundToInt(255 * Soft(mud));
        }
        _wetImage.SetData(n, n, false, Image.Format.Rg8, pixels);
        _wetTexture.Update(_wetImage);
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
        render_mode cull_disabled;
        uniform sampler2D palette : source_color, filter_nearest;
        uniform sampler2D wear_map : filter_linear;
        uniform sampler2D wet_map : filter_linear;
        uniform sampler2D noise : filter_linear, repeat_enable;
        uniform bool pasture = false;
        varying vec2 field;

        vec3 lin(vec3 srgb) { return pow(srgb, vec3(2.2)); }
        float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

        void vertex() { field = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz; }

        void fragment() {
            vec2 p = field;
            float n1 = texture(noise, p * 0.035).r;
            float tint = texture(noise, p * 0.011 + vec2(0.61, 0.29)).r;
            // Approved Soft Pasture: world-space colour replaces the repeating per-face lime palette.
            vec3 c = mix(lin(vec3(0.51, 0.57, 0.34)), lin(vec3(0.53, 0.61, 0.39)), smoothstep(0.25, 0.75, tint));
            c *= 0.95 + 0.10 * n1;
            // Short irregular strokes fade with projected pixel size rather than sparkling at farm zoom.
            float pixel = max(length(dFdx(p)), length(dFdy(p)));
            // Half-metre tufts carry texture at the normal farm view; tiny blades remain close-up detail.
            float tuft_detail = 1.0 - smoothstep(0.10, 0.28, pixel);
            float grain = texture(noise, p * 0.65 + vec2(0.13, 0.47)).r;
            c *= 1.0 + (grain - 0.5) * 0.14 * tuft_detail;
            vec2 tuft_cell = floor(p / 0.55);
            vec2 tuft_jitter = vec2(hash(tuft_cell + 3.1), hash(tuft_cell + 27.4));
            vec2 tuft_pos = (fract(p / 0.55) - (0.25 + 0.5 * tuft_jitter)) * 0.55;
            tuft_pos.x += tuft_pos.y * (tuft_jitter.x - 0.5);
            float tuft_aa = max(0.012, pixel * 0.65);
            float grass_tuft = (1.0 - smoothstep(0.025, 0.025 + tuft_aa, abs(tuft_pos.x)))
                * (1.0 - smoothstep(0.09, 0.17 + tuft_aa, abs(tuft_pos.y)));
            grass_tuft *= smoothstep(0.30, 0.55, grain);
            c *= 1.0 + grass_tuft * (tuft_jitter.y < 0.55 ? -0.16 : 0.11) * tuft_detail;
            float detail = 1.0 - smoothstep(0.018, 0.08, pixel);
            vec2 blade_cell = floor(p / 0.12);
            vec2 blade_jitter = vec2(hash(blade_cell), hash(blade_cell + vec2(17.3, 6.7)));
            vec2 blade = fract(p / 0.12) - (0.25 + 0.5 * blade_jitter);
            blade.x += blade.y * (blade_jitter.x - 0.5);
            float stroke = (1.0 - smoothstep(0.06, 0.16, abs(blade.x))) * (1.0 - smoothstep(0.18, 0.4, abs(blade.y)));
            stroke *= step(0.6, blade_jitter.y);
            c *= 1.0 + (stroke - 0.12) * 0.12 * detail;
            // Darker clover drifts.
            float clover = smoothstep(0.6, 0.68, n1);
            c = mix(c, c * vec3(0.88, 0.94, 0.9), clover * 0.55);
            // The uncut margin along the hedges, longer and yellower, broken at the gate.
            float edge = 32.0 - max(abs(p.x), abs(p.y));
            float margin = (1.0 - smoothstep(2.6, 3.6, edge + (n1 - 0.5) * 1.5)) * (1.0 - step(abs(p.x), 4.0) * step(27.0, p.y));
            c = mix(c, c * vec3(1.015, 1.015, 0.92), margin * 0.5);
            // Sparse, offset little blossoms, mostly at the hedge; no regularly centred confetti dots.
            vec2 g = floor(p / 0.75);
            float h = hash(g);
            float drift = smoothstep(0.68, 0.74, texture(noise, p * 0.05 + vec2(0.37, 0.11)).r);
            vec2 blossom = (fract(p / 0.75) - (0.2 + 0.6 * vec2(hash(g + 4.7), hash(g + 19.2)))) * 0.75;
            float flower = (1.0 - smoothstep(0.022, 0.045 + pixel, length(blossom))) * step(h, margin * 0.09 + drift * 0.004 + clover * 0.006);
            flower *= 1.0 - smoothstep(0.045, 0.12, pixel);
            vec3 bloom = fract(h * 13.0) < 0.5 ? lin(vec3(0.75, 0.67, 0.32)) : lin(vec3(0.78, 0.79, 0.65));
            if (pasture) {
                // Delivered pasture palette slots 7/8/9: grass, grazed grass and worn grass.
                int slot = int(floor(UV.x * 24.0));
                if (slot == 8) c *= vec3(0.90, 0.94, 0.88);
                else if (slot == 9) c = mix(c, lin(vec3(0.55, 0.53, 0.36)), 0.55);
                else if (slot != 7) c = texture(palette, UV).rgb;
                flower = 0.0;
            }

            // Wear: flattened, then dry and browned, then dry earth, with ragged edges.
            float w = pasture ? 0.0 : texture(wear_map, (p + 32.0) / 64.0).r;
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

            // Water and mud win over dry wear: soaked grass, then puddles, churned mud and a swamp's sludge.
            vec2 wm = pasture ? vec2(0.0) : texture(wet_map, (p + 32.0) / 64.0).rg;
            // Broad lobes plus fine fray, so a pool spreads unevenly instead of in a ring.
            float ragged = (texture(noise, p * 0.12 + vec2(0.21, 0.73)).r - 0.5) * 0.5 + (texture(noise, p * 0.5).r - 0.5) * 0.12;
            float water = clamp(wm.r + ragged * step(0.02, wm.r), 0.0, 1.0);
            float mud = clamp(wm.g + ragged * step(0.02, wm.g), 0.0, 1.0);
            float soaked = smoothstep(0.08, 0.14, water);
            c = mix(c, c * vec3(0.62, 0.7, 0.66), soaked * (1.0 - flower));
            float churned = smoothstep(0.28, 0.36, mud);
            c = mix(c, lin(vec3(0.36, 0.28, 0.2)) * (0.8 + 0.35 * texture(noise, p * 0.7).r), churned);
            float pooled = smoothstep(0.42, 0.5, water);
            float sludge = smoothstep(0.6, 0.7, mud);
            vec3 pool = mix(lin(vec3(0.42, 0.5, 0.54)), lin(vec3(0.5, 0.41, 0.29)) * (0.85 + 0.3 * texture(noise, p * 0.3).r), sludge);
            c = mix(c, pool, pooled);
            ALBEDO = c;
            ROUGHNESS = mix(0.97, mix(0.55, 0.12, pooled), max(pooled, churned * 0.6));
            SPECULAR = mix(0.5, 0.7, pooled);
        }
        """;
}

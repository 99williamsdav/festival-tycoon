# Heavy rain v1: integration

No code has been changed. These are suggested set-ups and values, tuned against the board renders and
`verification/`. "B" is heavy showers (the norm) and "C" is the downpour (rare).

## 1. Showers over the afternoon

- **Shape:** showers come and go across the afternoon and into dusk. A shower ramps in over about 20 s of real time,
  holds for a few minutes, then ramps out, with dry spells between.
- **Intensity:** drive everything below from one value, `rain` (0 to 1), which eases between targets. 0 is dry, 0.6 is
  B and 1.0 is C.
- **Downpour:** suggest C at most once per Tier 2 day, about a 1 in 4 chance per afternoon, lasting 6 to 10 game
  minutes. Its forecast says "Downpour" (see `../../ui/rain-forecast-icons-v1/`).
- **Wetness:** a second value, `wetness` (0 to 1), follows `rain` up quickly (about 30 s) and down slowly (several
  minutes). It drives the sheen and the soaked guests, so the ground stays glossy into a dry spell. This is the board's
  "clearing" frame.

## 2. Falling rain: GPUParticles3D following the camera

The key rule is **a constant streak count per screen at any zoom**, which keeps zoom 62 readable.

- **Emitter:** one `GPUParticles3D`, a child of the camera rig's focus point, so it moves and turns with the view.
- **Emission box:** sized to the ground footprint of the camera frustum. The footprint grows with the square of the
  zoom while `amount` stays fixed, so the number on screen stays the same.
- **Billboard:** use `Billboard = Particles` with `Particle Billboard Y To Velocity` (velocity-aligned quads) and
  `lwf_rain_streak_v1.png`. The material is lit (not unshaded), so at dusk only the streaks near festoons and stage
  lights show, as in `rain-B-dusk-zoom24.png`. The alternative is unshaded with the colour tinted by the light probe.

| | B: heavy showers | C: downpour |
|---|---|---|
| `amount` (1920 × 1080 window) | 3,100 | 5,800 |
| Streaks per megapixel of screen | 1,500 | 2,800 |
| Streak length (world, the quad's height) | 0.7–1.1 m | 0.9–1.4 m |
| Streak width | max(0.02 m, 1.3 px) | max(0.025 m, 1.6 px) |
| Colour | `e2eaf0` | `e2eaf0` |
| Peak alpha at the head (random per particle) | 0.45–0.75 | 0.50–0.85 |
| Fall speed | 9 m/s | 10 m/s |
| Wind | 8° slant, camera-relative (screen right), so it looks the same in all four rotations | 10° |
| Emitter height above ground | 12 m | 12 m |
| Lifetime | 1.4 s | 1.3 s |
| Veil: full-screen `ColorRect` (or fog), `9aa0aa` | 7% | 15% |
| Drifting bands: a large scrolling noise mask over the veil | none | +10% in the bands |
| Splashes (`lwf_rain_splash_v1`, 4-frame flipbook, 0.2 s, 0.12 m billboard on the ground) | 1 per 6 streaks | 1 per 5 |

**Notes:**
- The streak width needs a pixel floor: at zoom 62, 0.02 m is under 1 px at 1080p. Do it in a small particle shader
  (`VERTEX.x *= max(1.0, px_floor / world_width_in_px)`) or by scaling the quad by zoom.
- **Fade with `rain`:** scale `amount_ratio` (Godot 4.2+) and the veil alpha by `rain / 0.6` for B, and lerp the table
  values up to C as `rain` goes from 0.6 to 1.0.
- **Dusk:** the veil colour drops to `282c3c`, and the streaks are lit.
- **Performance:** about 3–6k quads, trivially cheap on GPU particles. The CPU fallback (`CPUParticles3D`) should halve
  `amount`.

## 3. Puddle ripples (in the ground shader)

On puddle cells (`wet_map.r` at or above the puddle threshold), and also swamp, only while `rain > 0`:

```glsl
uniform sampler2D ripple_atlas : source_color, filter_linear_mipmap;   // lwf_rain_ripple_v1.png, 4 x 4 frames
uniform float rain;                                                    // 0..1
// p = world xz in metres; puddle = smoothstep(0.24, 0.30, wet_map.r) (the existing puddle threshold)
vec2 cellp = p / 0.5;  vec2 cid = floor(cellp);  vec2 uvc = fract(cellp);              // one ripple slot per 0.5 m cell
float h = fract(sin(dot(cid, vec2(12.9898, 78.233))) * 43758.5453);                   // per-cell phase
float rate = mix(1.2, 2.4, rain);                                                      // ripples per second per cell
float t = fract(TIME * rate + h);  float frame = floor(t * 16.0);
vec2 auv = (vec2(mod(frame, 4.0), floor(frame / 4.0)) + uvc) / 4.0;
float ring = texture(ripple_atlas, auv).a * step(h, rain * 0.9);                       // fewer active cells in lighter rain
c = mix(c, vec3(0.86, 0.90, 0.93), ring * puddle * 0.6);
```

This is the "rings on the puddles" in `rain-B-puddles-zoom7.png`. The board also notes that the current puddle band
looks frozen in the rain without them.

## 4. Wet sheen (`wetness`)

**Ground shader:** add `uniform float wetness;`, then darken and gloss:
```glsl
c *= mix(vec3(1.0), vec3(0.70, 0.73, 0.76), wetness);       // about 30% darker, very slightly cooler
ROUGHNESS = mix(ROUGHNESS, 0.22, wetness);
SPECULAR  = mix(SPECULAR, 0.5, wetness);
```
Soaked grass then reads as wet, which was the weakest band in the board's in-game check.

**Props** (vans, stalls, roofs, the tent sail, track): these are `StandardMaterial3D` on a handful of shared palette
materials. When `wetness` changes by more than 0.02, walk the cached materials and set
`AlbedoColor = dry * mix(1, (0.70, 0.73, 0.76), wetness)` and `Roughness = mix(dry, 0.22, wetness)`. Skip
guests: they use the soaked palette instead (see `rain-guests-v1`). Skip emissive and UI materials.

## 5. Shower light (DayCycle)

These are multipliers on whatever the day cycle is doing at that moment, applied by `rain`:

| | Dry | A shower arriving (rain 0.3) | Heavy (B, 0.6) | Downpour (C, 1.0) |
|---|---|---|---|---|
| Sun (`DirectionalLight3D.light_energy`) | × 1.0 | × 0.5 | × 0.28 | × 0.22 |
| Sun softness (`light_angular_distance`) | as now | 10° | 28° | 32° |
| Sun colour | as now | lerp 50% to `ebf2ff` | `ebf2ff` | `e6eefc` |
| Ambient colour | as now | lerp 40% to `9ba8b8` | `9ba8b8` | `909dad` |
| Ambient energy | as now | × 1.0 | × 1.0 | × 0.95 |
| Sky / background | as now | lerp 50% to `6b7580` | `6b7580` | `636c77` |
| Exposure | as now | +0.05 | +0.08 | +0.06 |

The cloud-shadow drift used for "cloud" days can stay on in the dry spells between showers, as on the board's first
frame. During a shower the sun is so soft it's moot.

**Clearing:** when `rain` falls back to 0, the sun returns to normal, and a little warmer for a minute, while
`wetness` is still high. That's the glistening frame.

## 6. Order of work, if it helps

1. `rain` and `wetness` values with the light multipliers: the look changes at once.
2. Particles B, then C.
3. Ground sheen and ripples.
4. Prop sheen.
5. Soaked guests and the rain clips (`rain-guests-v1`).
6. Forecast chip.

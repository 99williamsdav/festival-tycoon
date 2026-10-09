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

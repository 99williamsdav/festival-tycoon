shader_type spatial;
        uniform sampler2D palette : source_color, filter_nearest;
        uniform sampler2D wear_map : filter_linear;
        uniform sampler2D wet_map : filter_linear;
        uniform sampler2D noise : filter_linear, repeat_enable;
        varying vec2 field;

        vec3 lin(vec3 srgb) { return pow(srgb, vec3(2.2)); }
        float hash(vec2 p) { return fract(sin(dot(p, vec2(12.9898, 78.233))) * 43758.5453); }

        void vertex() { field = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xz; }

        void fragment() {
            vec3 c = texture(palette, UV).rgb;
            vec2 p = field;
            float n1 = texture(noise, p * 0.035).r;
            // A slow tint drift over about 30 m, lusher one way and drier the other, so the 8 m tile never repeats.
            float tint = texture(noise, p * 0.011 + vec2(0.61, 0.29)).r;
            c *= mix(vec3(0.95, 0.96, 0.97), vec3(1.05, 1.04, 0.97), smoothstep(0.3, 0.7, tint));
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

            // Water and mud win over dry wear: soaked grass, then puddles, churned mud and a swamp's sludge.
            vec2 wm = texture(wet_map, (p + 32.0) / 64.0).rg;
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

// Mana bursts (ManaBurst) of Arcane Boots and Enchanted Mango: additive, drawn in four shapes by _Shape. 0 — a wave on the
// ground (a flat quad, uv -1..1 from its centre): a faint ring runs out to the edge with fine rays behind it.
// 1 — a helix round a hero who got the mana (an open cylinder that narrows as it plays, uv.x around, uv.y up): two
// bright strands wind up it with sparks running along them, and a light flows down into the hero. 2 — a rune circle
// under that hero (a flat quad): two rings with turning dashes and marks between them and a glow in the middle.
// 3 — mango petals under a hero who ate one (a flat quad): juicy petals open and turn, glowing at their middles, with
// drops of light flying out. _Color and _Core tint every shape. _Progress runs 0 → 1 over the effect's life;
// colours go over 1 so a bloom (if the camera has one) makes them shine.
Shader "PudgeWars/ManaBurst"
{
    Properties
    {
        _Color ("Colour", Color) = (0.25, 0.65, 1, 1)
        _Core ("Core colour", Color) = (0.85, 0.97, 1, 1)
        _Intensity ("Intensity", Float) = 2.5
        _Progress ("Progress", Range(0, 1)) = 0
        _Shape ("Shape (0 wave, 1 helix, 2 rune circle, 3 mango petals)", Float) = 0
        _Seed ("Seed", Float) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Core;
                half _Intensity;
                half _Progress;
                half _Shape;
                half _Seed;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionCS = TransformObjectToHClip(v.positionOS.xyz);
                o.uv = v.uv;
                return o;
            }

            float Hash(float2 p)
            {
                p = frac(p * float2(123.34, 456.21) + _Seed);
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            half3 Wave(float2 uv)
            {
                float d = length(uv);
                if (d > 1) return 0;
                float t = 1 - pow(1 - _Progress, 3);           // fast out, soft landing
                float fade = pow(1 - _Progress, 1.4);
                float angle = atan2(uv.y, uv.x);
                // the main ring and a thinner one behind it: thin and faint, the heroes' effects are the bright part
                float ring = exp(-pow((d - t) / 0.035, 2)) * 0.6;
                float echo = exp(-pow((d - t * 0.72) / 0.02, 2)) * 0.25;
                // rays out of the centre, flickering, only inside the ring
                float rays = pow(saturate(0.5 + 0.5 * sin(angle * 26 + _Seed * 7 + _Time.y * 2)), 8) * step(d, t) * (1 - d) * 0.25;
                // a soft glow left behind, strongest at the start
                float glow = saturate(1 - d / max(t, 0.001)) * (1 - _Progress) * 0.12;
                float edge = saturate((1 - d) * 8);           // no hard rim at the quad's circle
                float shape = (ring + echo + rays + glow) * fade * edge;
                return lerp(_Color.rgb, _Core.rgb, saturate(ring * 1.2)) * shape;
            }

            half3 Helix(float2 uv)
            {
                float t = _Progress;
                float fade = smoothstep(0, 0.12, t) * pow(1 - t, 1.3);
                float turn = uv.y * 1.6 - _Time.y * 1.4 - t * 2;    // the strands wind up and turn
                float strands = 0;
                for (int k = 0; k < 2; k++)
                {
                    float phase = frac(uv.x - turn - k * 0.5);
                    float across = min(phase, 1 - phase);              // 0 on a strand
                    strands += exp(-pow(across / 0.035, 2));
                }
                // sparks running up the strands
                float run = frac(uv.y * 3 - _Time.y * 1.8 + _Seed);
                float sparks = strands * smoothstep(0.08, 0, abs(run - 0.5) - 0.02) * 1.5;
                // light flowing down into the hero, brightest at its feet as the effect ends
                float flow = pow(saturate(1 - uv.y), 2) * smoothstep(0.3, 1, t) * 0.5;
                float height = smoothstep(0, 0.08, uv.y) * smoothstep(1, 0.75, uv.y);
                // a faint veil of light all round between the strands: the hero is seen wrapped in it
                float veil = 0.14 * (0.6 + 0.4 * sin(uv.y * 9 - _Time.y * 3 + uv.x * 6.2832));
                float shape = (strands * 0.9 + sparks + flow + veil) * height * fade;
                return lerp(_Color.rgb, _Core.rgb, saturate(sparks + strands * 0.3)) * shape;
            }

            // Enchanted Mango eaten: juicy petals opening round the hero's feet and turning, glowing at their middles,
            // with drops of light flying out among them
            half3 Petals(float2 uv)
            {
                float d = length(uv);
                if (d > 1) return 0;
                float t = _Progress;
                float open = 1 - pow(1 - saturate(t * 1.6), 3);           // they open fast and settle
                float fade = smoothstep(0, 0.08, t) * pow(1 - t, 1.4);
                float angle = atan2(uv.y, uv.x) / 6.2832 + 0.5;
                float reach = 0.25 + 0.75 * open;
                float across = abs(frac(angle * 6 + t * 0.25 + _Seed) - 0.5); // 0 along a petal's middle
                float width = 0.22 * saturate(1 - d / reach) * saturate(d * 6);
                float petal = smoothstep(width, width - 0.04, across) * step(d, reach);
                float vein = smoothstep(0.03, 0, across) * step(d, reach) * saturate(1 - d / reach);
                float edge = exp(-pow((d - reach) / 0.04, 2)) * 0.6;
                // drops flying out between the petals
                float2 cell = float2(angle * 40, d * 10 - t * 9);
                float drop = step(0.9, Hash(floor(cell))) * smoothstep(0.45, 0, length(frac(cell) - 0.5)) * step(d, reach + 0.1);
                float core = exp(-pow(d / 0.22, 2)) * 0.8;
                float shape = (petal * 0.55 + vein * 0.9 + edge + drop * 1.4 + core) * fade;
                return lerp(_Color.rgb, _Core.rgb, saturate(vein + drop + core)) * shape;
            }

            half3 Runes(float2 uv)
            {
                float d = length(uv);
                if (d > 1) return 0;
                float t = _Progress;
                float fade = smoothstep(0, 0.1, t) * pow(1 - t, 1.5);
                float angle = atan2(uv.y, uv.x) / 6.2832 + 0.5;       // 0..1 round
                float outer = exp(-pow((d - 0.86) / 0.025, 2));
                float inner = exp(-pow((d - 0.62) / 0.018, 2));
                // dashes between the rings turning one way, marks turning the other
                float dashes = step(0.5, frac(angle * 18 + _Time.y * 0.6)) * exp(-pow((d - 0.74) / 0.03, 2));
                float marks = smoothstep(0.03, 0, abs(frac(angle * 6 - _Time.y * 0.35) - 0.5) - 0.01)
                              * smoothstep(0.56, 0.5, d) * smoothstep(0.3, 0.36, d);
                float core = exp(-pow(d / 0.32, 2)) * 0.6;
                float shape = (outer + inner * 0.8 + dashes * 0.8 + marks * 0.9 + core) * fade;
                return lerp(_Color.rgb, _Core.rgb, saturate(outer + core)) * shape;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 c = _Shape < 0.5 ? Wave(i.uv) : _Shape < 1.5 ? Helix(i.uv) : _Shape < 2.5 ? Runes(i.uv) : Petals(i.uv);
                return half4(c * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}

// Mana burst of Arcane Boots (ManaBurst): bright blue, additive, drawn in two shapes by _Shape. 0 — a wave on the
// ground (a flat quad, uv -1..1 from its centre): a glowing ring runs out to the edge with fine rays and a soft
// glow behind it. 1 — a column over a hero who got the mana (an open cylinder, uv.x around, uv.y up): a bright band
// rises along it through spiralling streaks and sparks. _Progress runs 0 → 1 over the effect's life; colours go
// over 1 so a bloom (if the camera has one) makes them shine.
Shader "PudgeWars/ManaBurst"
{
    Properties
    {
        _Color ("Colour", Color) = (0.25, 0.65, 1, 1)
        _Core ("Core colour", Color) = (0.85, 0.97, 1, 1)
        _Intensity ("Intensity", Float) = 2.5
        _Progress ("Progress", Range(0, 1)) = 0
        _Shape ("Shape (0 wave, 1 column)", Float) = 0
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
                // the main ring and a thinner one behind it
                float ring = exp(-pow((d - t) / 0.055, 2));
                float echo = exp(-pow((d - t * 0.72) / 0.03, 2)) * 0.55;
                // rays out of the centre, flickering, only inside the ring
                float rays = pow(saturate(0.5 + 0.5 * sin(angle * 26 + _Seed * 7 + _Time.y * 2)), 6) * step(d, t) * (1 - d) * 0.6;
                // a soft glow left behind, strongest at the start
                float glow = saturate(1 - d / max(t, 0.001)) * (1 - _Progress) * 0.45;
                float edge = saturate((1 - d) * 8);           // no hard rim at the quad's circle
                float shape = (ring + echo + rays + glow) * fade * edge;
                return lerp(_Color.rgb, _Core.rgb, saturate(ring * 1.2)) * shape;
            }

            half3 Column(float2 uv)
            {
                float t = _Progress;
                float fade = pow(1 - t, 1.2);
                // a band rising from the feet, with a tail below it
                float rise = t * 1.25;
                float band = exp(-pow((uv.y - rise) / 0.14, 2));
                float tail = saturate(1 - (rise - uv.y) / 0.6) * step(uv.y, rise) * 0.5;
                // spiralling streaks round the column
                float streaks = pow(saturate(0.5 + 0.5 * sin((uv.x * 5 + uv.y * 2.5 - _Time.y * 2.2) * 6.2832)), 4) * 0.7;
                // sparks flying up
                float2 cell = float2(uv.x * 28, uv.y * 14 - _Time.y * 6);
                float spark = step(0.93, Hash(floor(cell))) * smoothstep(0.5, 0, length(frac(cell) - 0.5));
                float height = saturate(1 - uv.y) * saturate(uv.y * 10); // soft at the feet and the top
                float shape = (band * 1.4 + tail + streaks * (band + tail) + spark * 1.6) * height * fade;
                return lerp(_Color.rgb, _Core.rgb, saturate(band + spark)) * shape;
            }

            half4 frag(Varyings i) : SV_Target
            {
                half3 c = _Shape < 0.5 ? Wave(i.uv) : Column(i.uv);
                return half4(c * _Intensity, 1);
            }
            ENDHLSL
        }
    }
}

// The reach of an aura on the ground (AuraRange), shown while its effect is hovered: a flat quad, uv -1..1 from the
// giver. A bright rim at the edge with a soft glow inside it, a ring of dashes turning along the rim, a thin inner ring
// turning the other way, waves running from the rim in toward the giver, and a faint fill that brightens near the
// edge. Additive; _Fade brings it in and out, _Color tints it.
Shader "PudgeWars/AuraRange"
{
    Properties
    {
        _Color ("Colour", Color) = (0.3, 0.62, 1, 1)
        _Core ("Rim colour", Color) = (0.8, 0.94, 1, 1)
        _Intensity ("Intensity", Float) = 1.4
        _Fade ("Fade", Range(0, 1)) = 1
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
                half _Fade;
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

            half4 frag(Varyings i) : SV_Target
            {
                float d = length(i.uv);
                if (d > 1.02) return 0;
                float t = _Time.y + _Seed;
                float angle = atan2(i.uv.y, i.uv.x) / 6.2832 + 0.5; // 0..1 round

                // the rim, breathing a little, and its glow inward
                float rim = exp(-pow((d - 0.985) / 0.012, 2)) * (0.85 + 0.15 * sin(t * 3));
                float glow = exp(-(1 - d) * 9) * 0.35 * step(d, 1);
                // dashes turning along the rim, a thinner ring of marks turning the other way
                float dashes = step(0.45, frac(angle * 64 + t * 0.06)) * exp(-pow((d - 0.94) / 0.012, 2)) * 0.7;
                float marks = smoothstep(0.02, 0, abs(frac(angle * 12 - t * 0.04) - 0.5) - 0.012)
                              * exp(-pow((d - 0.88) / 0.02, 2)) * 0.6;
                float inner = exp(-pow((d - 0.88) / 0.006, 2)) * 0.35;
                // waves running in from the rim toward the giver, fading as they go
                float wave = pow(saturate(1 - abs(frac(d * 1.6 + t * 0.35) - 0.5) * 2), 12) * d * 0.35;
                float fill = 0.05 + 0.08 * d * d;

                float shape = (rim + glow + dashes + marks + inner + wave + fill) * step(d, 1) + rim * step(1, d);
                half3 c = lerp(_Color.rgb, _Core.rgb, saturate(rim + dashes * 0.5));
                return half4(c * shape * _Intensity * _Fade, 1);
            }
            ENDHLSL
        }
    }
}

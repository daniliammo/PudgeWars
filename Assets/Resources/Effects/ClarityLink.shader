// Camera-facing ribbon: translucent blue veil, winding filaments and a pulse flowing towards the recipient.
Shader "PudgeWars/ClarityLink"
{
    Properties
    {
        _Color ("Veil colour", Color) = (0.12, 0.44, 1, 0.48)
        _Core ("Filament colour", Color) = (0.55, 0.85, 1, 0.8)
        _Progress ("Lifetime", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _Color;
                half4 _Core;
                float _Progress;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float x = input.uv.x;
                float y = input.uv.y * 2 - 1;
                float fade = smoothstep(0, 0.09, _Progress) * (1 - smoothstep(0.55, 1, _Progress));
                float edge = pow(saturate(1 - y * y), 2);
                float ends = smoothstep(0, 0.05, x) * (1 - smoothstep(0.94, 1, x));
                float wave = sin(x * 27 - _Progress * 18) * 0.38;
                float strands = exp(-pow((y - wave) * 12, 2)) + exp(-pow((y + wave) * 15, 2)) * 0.6;
                float pulse = exp(-pow((x - _Progress * 1.35) * 12, 2));
                float motes = pow(saturate(sin(x * 125 - _Progress * 65)), 18) * strands;
                half3 colour = lerp(_Color.rgb, _Core.rgb, saturate(strands + pulse));
                float opacity = saturate(0.18 + strands * 0.48 + pulse * 0.42 + motes * 0.3);
                return half4(colour, opacity * edge * ends * fade * _Color.a);
            }
            ENDHLSL
        }
    }
}

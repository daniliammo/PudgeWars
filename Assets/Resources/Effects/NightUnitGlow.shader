Shader "PudgeWars/NightUnitGlow"
{
    Properties { [HDR] _GlowColor ("Glow colour", Color) = (0, 0, 0, 0) }
    SubShader
    {
        Tags { "Queue"="Transparent-10" "RenderType"="Transparent" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="UniversalForward" }
            Blend One One
            ZWrite Off
            ZTest LEqual
            Cull Off
            Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float2 uv : TEXCOORD0; };
            struct Varyings { float4 positionCS : SV_POSITION; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                float radius = length(input.uv);
                float edge = 1.0 - smoothstep(0.82, 1.0, radius);
                float ringDistance = (radius - 0.65) / 0.055;
                float haloDistance = (radius - 0.65) / 0.19;
                float ring = exp(-ringDistance * ringDistance) * 0.65;
                float halo = exp(-haloDistance * haloDistance) * 0.22;
                half3 glow = _GlowColor.rgb * (ring + halo) * edge;
                return half4(MixFogColor(glow, half3(0, 0, 0), input.fog), 1);
            }
            ENDHLSL
        }
    }
}

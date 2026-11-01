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
            Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _GlowColor;
            CBUFFER_END
            struct Attributes { float4 positionOS : POSITION; float3 normalOS : NORMAL; };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                half3 normalWS : TEXCOORD1;
                half fog : TEXCOORD2;
            };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fog = ComputeFogFactor(output.positionCS.z);
                return output;
            }
            half4 frag(Varyings input) : SV_Target
            {
                half facing = saturate(dot(normalize(input.normalWS), GetWorldSpaceNormalizeViewDir(input.positionWS)));
                half rim = (1.0h - facing) * (1.0h - facing);
                half3 glow = _GlowColor.rgb * (0.12h + rim * 0.88h);
                return half4(MixFogColor(glow, half3(0, 0, 0), input.fog), 1);
            }
            ENDHLSL
        }
    }
}

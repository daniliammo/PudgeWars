// Blood: splats on the ground, trees and bodies, and the flying drops (BloodSplatter). Transparent, tinted by the
// vertex colour (its alpha fades a splat out), lit by the main light with its shadows and the ambient light, with a
// wet highlight. Splats are drawn a little toward the camera, so they lie on a surface without flickering into it.
Shader "PudgeWars/Blood"
{
    Properties
    {
        [MainTexture] _BaseMap ("Texture", 2D) = "white" {}
        [MainColor] _BaseColor ("Colour", Color) = (0.4, 0.01, 0.02, 1)
        _Gloss ("Wet highlight", Range(0, 1)) = 0.35
        _NormalUp ("Light as if facing up (drops)", Range(0, 1)) = 0
        _DepthOffset ("Depth offset", Float) = -2
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            Offset [_DepthOffset], [_DepthOffset]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _Gloss;
                half _NormalUp;
                float _DepthOffset;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS : TEXCOORD2;
                half4 color : COLOR;
                half fog : TEXCOORD3;
            };

            Varyings vert(Attributes v)
            {
                Varyings o;
                o.positionWS = TransformObjectToWorld(v.positionOS.xyz);
                o.positionCS = TransformWorldToHClip(o.positionWS);
                o.normalWS = TransformObjectToWorldNormal(v.normalOS);
                o.uv = TRANSFORM_TEX(v.uv, _BaseMap);
                o.color = v.color;
                o.fog = ComputeFogFactor(o.positionCS.z);
                return o;
            }

            half4 frag(Varyings i, bool front : SV_IsFrontFace) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv);
                half4 albedo = tex * _BaseColor * i.color;
                float3 n = normalize(i.normalWS) * (front ? 1 : -1);
                n = normalize(lerp(n, float3(0, 1, 0), _NormalUp));
                Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS));
                half lit = saturate(dot(n, light.direction)) * light.shadowAttenuation;
                half3 colour = albedo.rgb * (SampleSH(n) + light.color * lit);
                float3 h = normalize(light.direction + GetWorldSpaceNormalizeViewDir(i.positionWS));
                colour += light.color * lit * pow(saturate(dot(n, h)), 48) * _Gloss * tex.a;
                colour = MixFog(colour, i.fog);
                return half4(colour, albedo.a);
            }
            ENDHLSL
        }
    }
}

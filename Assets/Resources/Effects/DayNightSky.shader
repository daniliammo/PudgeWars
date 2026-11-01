Shader "PudgeWars/DayNightSky"
{
    Properties
    {
        _Day ("Daylight", Range(0, 1)) = 1
        _Twilight ("Twilight", Range(0, 1)) = 0
        _SunDirection ("Sun direction", Vector) = (1, 0, 0, 0)
    }
    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" "RenderPipeline"="UniversalPipeline" }
        Cull Off ZWrite Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            float _Day, _Twilight;
            float4 _SunDirection;
            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; float3 direction : TEXCOORD0; };
            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.direction = input.positionOS.xyz;
                return output;
            }
            float hash(float3 p) { return frac(sin(dot(p, float3(127.1, 311.7, 74.7))) * 43758.5453); }
            half4 frag(Varyings input) : SV_Target
            {
                float3 ray = normalize(input.direction);
                float elevation = saturate(ray.y);
                float3 zenith = lerp(float3(0.009, 0.018, 0.055), float3(0.12, 0.36, 0.68), _Day);
                float3 horizon = lerp(float3(0.045, 0.07, 0.14), float3(0.64, 0.78, 0.89), _Day);
                horizon = lerp(horizon, float3(0.85, 0.31, 0.16), _Twilight * 0.65);
                float3 color = lerp(horizon, zenith, pow(elevation, 0.45));
                // A three-dimensional grid avoids a seam and stretched stars at the poles.
                float3 cell = floor(ray * 260.0);
                float3 cellOffset = frac(ray * 260.0) - 0.5;
                float star = (1.0 - smoothstep(0.0, 0.13, length(cellOffset)));
                star *= step(0.975, hash(cell)) * smoothstep(0.0, 0.18, ray.y);
                color += star * (1.0 - _Day) * float3(0.72, 0.83, 1.0) * 2.0;
                float sunDot = saturate(dot(ray, normalize(_SunDirection.xyz)));
                float moonDot = saturate(dot(ray, -normalize(_SunDirection.xyz)));
                float sunDisc = smoothstep(0.9996, 0.99985, sunDot);
                float moonDisc = smoothstep(0.9993, 0.99965, moonDot);
                color += (pow(sunDot, 48.0) * 0.3 + sunDisc * 3.0) * float3(1.0, 0.70, 0.36) * _Day;
                color += (pow(moonDot, 90.0) * 0.07 + moonDisc * 0.85) * float3(0.65, 0.78, 1.0) * (1.0 - _Day);
                color = lerp(float3(0.025, 0.035, 0.05), color, smoothstep(-0.2, 0.0, ray.y));
                return half4(color, 1);
            }
            ENDHLSL
        }
    }
}

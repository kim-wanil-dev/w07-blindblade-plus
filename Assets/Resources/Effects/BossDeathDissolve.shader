Shader "BlindBlade/BossDeathDissolve"
{
    Properties
    {
        _BaseColor ("Body Color", Color) = (0.5, 0.5, 0.5, 1)
        _EdgeColor ("Ash Edge", Color) = (0.72, 0.69, 0.65, 1)
        _Dissolve ("Dissolve", Range(0, 1)) = 0
        _MinY ("Bottom", Float) = 0
        _Height ("Height", Float) = 2
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry+10" }
        Pass
        {
            Name "BossAsh"
            Tags { "LightMode"="UniversalForward" }
            Cull Back
            ZWrite On
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                half4 _EdgeColor;
                float _Dissolve;
                float _MinY;
                float _Height;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                return output;
            }

            float Hash(float3 value)
            {
                return frac(sin(dot(value, float3(12.9898, 78.233, 37.719))) * 43758.5453);
            }

            float SmoothNoise(float3 position)
            {
                float3 cell = floor(position);
                float3 blend = frac(position);
                blend = blend * blend * (3.0 - 2.0 * blend);
                float low = lerp(lerp(Hash(cell), Hash(cell + float3(1, 0, 0)), blend.x),
                    lerp(Hash(cell + float3(0, 1, 0)), Hash(cell + float3(1, 1, 0)), blend.x), blend.y);
                float high = lerp(lerp(Hash(cell + float3(0, 0, 1)), Hash(cell + float3(1, 0, 1)), blend.x),
                    lerp(Hash(cell + float3(0, 1, 1)), Hash(cell + float3(1, 1, 1)), blend.x), blend.y);
                return lerp(low, high, blend.z);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float noise = SmoothNoise(input.positionWS * 10.0);
                float height = saturate((_MinY + _Height - input.positionWS.y) / max(_Height, 0.01));
                float threshold = _Dissolve * 1.4 - height * 0.4;
                clip(noise - threshold);
                float edge = 1.0 - smoothstep(threshold, threshold + 0.16, noise);
                return half4(lerp(_BaseColor.rgb, _EdgeColor.rgb, edge * 0.65), 1);
            }
            ENDHLSL
        }
    }
}

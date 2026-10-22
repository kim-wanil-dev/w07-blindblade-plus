Shader "BlindBlade/DefenseMeleeSwordSmoke"
{
    Properties
    {
        _BaseMap ("Smoke Pattern", 2D) = "white" {}
        _BaseColor ("Smoke Color", Color) = (0.8588, 0.7554, 0.7490, 0.72)
        _PatternScale ("Pattern Scale", Range(0.5, 4)) = 1.6
        _FlowSpeed ("Smoke Flow Speed", Range(0, 3)) = 0.45
        _WobbleStrength ("Edge Wobble", Range(0, 0.03)) = 0.006
        _WobbleFrequency ("Wobble Frequency", Range(1, 24)) = 10
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "SmokeSword"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                float _PatternScale;
                float _FlowSpeed;
                float _WobbleStrength;
                float _WobbleFrequency;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                float phase = input.positionOS.y * _WobbleFrequency + _Time.y * _FlowSpeed * 6.28318;
                float crossPhase = input.positionOS.x * _WobbleFrequency * 0.7 - _Time.y * _FlowSpeed * 4.1;
                float wobble = sin(phase) * sin(crossPhase) * _WobbleStrength;
                float3 positionOS = input.positionOS.xyz + normalize(input.normalOS) * wobble;
                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float timeOffset = _Time.y * _FlowSpeed;
                float2 uv = input.uv * _PatternScale;
                half first = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                    uv + float2(0.13, -0.07) * timeOffset).a;
                half second = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap,
                    uv * 0.73 + float2(-0.08, 0.11) * timeOffset).a;
                half density = saturate(first * 0.6 + second * 0.4);
                half3 color = _BaseColor.rgb * lerp(0.62h, 1.0h, density);
                half opacity = _BaseColor.a * lerp(0.4h, 1.0h, density);
                return half4(color, opacity);
            }
            ENDHLSL
        }
    }
}

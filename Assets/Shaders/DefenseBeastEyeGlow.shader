Shader "BlindBlade/DefenseBeastEyeGlow"
{
    Properties
    {
        _CoreColor ("Eye Core", Color) = (0.72, 0.96, 1.2, 1)
        _GlowColor ("Blue Halo", Color) = (0.08, 0.38, 1, 1)
        _CoreRadius ("Core Radius", Range(0.1, 0.8)) = 0.38
        _GlowIntensity ("Halo Intensity", Range(0, 2)) = 0.55
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "EyeGlow"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _GlowColor;
                float _CoreRadius;
                float _GlowIntensity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);
                Varyings output;
                output.positionHCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 centeredUv = (input.uv - 0.5) * 2.0;
                float radius = length(centeredUv);
                float core = 1.0 - smoothstep(_CoreRadius * 0.72, _CoreRadius, radius);
                float halo = exp(-radius * radius * 7.0) * _GlowIntensity;
                half3 color = _CoreColor.rgb * (half)core + _GlowColor.rgb * (half)halo;
                return half4(color, 1.0);
            }
            ENDHLSL
        }
    }
}

Shader "Hidden/BlindBlade/OutlineMeleeWarningMask"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _MeleeWarningIntensity ("Melee Warning Intensity", Range(0, 1)) = 0
        _MeleeWarningAlwaysVisible ("Melee Warning Always Visible", Range(0, 1)) = 0
        _TransparentThreshold ("Transparent Threshold", Float) = 0.015

    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "Queue" = "Transparent" }

        Pass
        {
            Name "MeleeWarningMask"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One One
            BlendOp Max
            ColorMask RG

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _MeleeWarningIntensity;
                float _MeleeWarningAlwaysVisible;
                float _TransparentThreshold;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings Vert(Attributes input)
            {
                UNITY_SETUP_INSTANCE_ID(input);

                Varyings output;
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half coverage = texel.a * input.color.a * _BaseColor.a;
                half3 visibleRgb = texel.rgb * input.color.rgb * _BaseColor.rgb * coverage;
                half brightness = max(visibleRgb.r, max(visibleRgb.g, visibleRgb.b));
                half shape = smoothstep(0.0, max(_TransparentThreshold, 0.0001), brightness);

                // R은 파동으로 드러난 테두리를 붉게 물들일 세기, G는 파동 없이도 붉은 테두리를 그릴 세기다.
                half warning = shape * saturate(_MeleeWarningIntensity);
                return half4(warning, warning * saturate(_MeleeWarningAlwaysVisible), 0, 1);
            }
            ENDHLSL
        }
    }
}   
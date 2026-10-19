Shader "Hidden/BlindBlade/OutlineMeleeWarningMask"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _MeleeWarningIntensity ("Melee Warning Intensity", Range(0, 1)) = 0
        _MeleeWarningAlwaysVisible ("Melee Warning Always Visible", Range(0, 1)) = 0
        _MeleeWarningSeparateGlint ("Melee Warning Separate Glint", Range(0, 1)) = 0
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
            ColorMask RGB

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
                float _MeleeWarningSeparateGlint;
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
                half separateGlint = saturate(_MeleeWarningSeparateGlint);
                half4 maskColor = lerp(_BaseColor, half4(1, 1, 1, 1), separateGlint);
                half coverage = texel.a * input.color.a * maskColor.a;
                half3 visibleRgb = texel.rgb * input.color.rgb * maskColor.rgb * coverage;
                half brightness = max(visibleRgb.r, max(visibleRgb.g, visibleRgb.b));
                half shape = smoothstep(0.0, max(_TransparentThreshold, 0.0001), brightness);

                // R은 붉은 전조, G는 항상 보이는 붉은 테두리, B는 몸통과 분리해 그릴 눈 섬광이다.
                half warning = shape * saturate(_MeleeWarningIntensity);
                return half4(warning * (1 - separateGlint),
                    warning * saturate(_MeleeWarningAlwaysVisible) * (1 - separateGlint),
                    warning * separateGlint, 1);
            }
            ENDHLSL
        }
    }
}   

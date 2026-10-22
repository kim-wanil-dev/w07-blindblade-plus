Shader "Hidden/BlindBlade/OutlineMeleeWarningMask"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _MeleeWarningIntensity ("Melee Warning Intensity", Range(0, 1)) = 0
        _MeleeWarningAlwaysVisible ("Melee Warning Always Visible", Range(0, 1)) = 0
        _MeleeWarningSeparateGlint ("Melee Warning Separate Glint", Range(0, 1)) = 0
        _MeleeWarningThickGlint ("Melee Warning Thick Glint", Range(0, 1)) = 0
        _MeleeWarningSolidShape ("Melee Warning Solid Shape", Range(0, 1)) = 0
        _SwordOutlineMask ("Sword Outline Mask", Range(0, 1)) = 0
        _FlowSpeed ("Sword Smoke Flow Speed", Float) = 0
        _WobbleFrequency ("Sword Wobble Frequency", Float) = 0
        _WobbleStrength ("Sword Wobble Strength", Float) = 0
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
            ColorMask RGBA

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
                float _MeleeWarningThickGlint;
                float _MeleeWarningSolidShape;
                float _SwordOutlineMask;
                float _FlowSpeed;
                float _WobbleFrequency;
                float _WobbleStrength;
                float _TransparentThreshold;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
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
                float3 positionOS = input.positionOS.xyz;
                if (_WobbleStrength > 0.0)
                {
                    float phase = input.positionOS.y * _WobbleFrequency + _Time.y * _FlowSpeed * 6.28318;
                    float crossPhase = input.positionOS.x * _WobbleFrequency * 0.7 - _Time.y * _FlowSpeed * 4.1;
                    float wobble = sin(phase) * sin(crossPhase) * _WobbleStrength;
                    positionOS += normalize(input.normalOS) * wobble;
                }
                output.positionCS = TransformObjectToHClip(positionOS);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half solidShape = saturate(_MeleeWarningSolidShape);
                half4 vertexColor = lerp(input.color, half4(1, 1, 1, 1), solidShape);
                texel = lerp(texel, half4(1, 1, 1, 1), solidShape);
                half separateGlint = saturate(_MeleeWarningSeparateGlint);
                half4 maskColor = lerp(_BaseColor, half4(1, 1, 1, 1), separateGlint);
                half coverage = texel.a * vertexColor.a * maskColor.a;
                half3 visibleRgb = texel.rgb * vertexColor.rgb * maskColor.rgb * coverage;
                half brightness = max(visibleRgb.r, max(visibleRgb.g, visibleRgb.b));
                half shape = smoothstep(0.0, max(_TransparentThreshold, 0.0001), brightness);

                if (_SwordOutlineMask > 0.5)
                    return half4(shape, 0, 0, 0);

                // R은 붉은 전조, G는 항상 보이는 붉은 테두리, B는 몸통과 분리해 그릴 눈 섬광, A는 두껍게 그릴 눈 섬광이다.
                half warning = shape * saturate(_MeleeWarningIntensity);
                half thickGlint = saturate(_MeleeWarningThickGlint);
                return half4(warning * (1 - separateGlint),
                    warning * saturate(_MeleeWarningAlwaysVisible) * (1 - separateGlint),
                    warning * separateGlint * (1 - thickGlint),
                    warning * separateGlint * thickGlint);
            }
            ENDHLSL
        }
    }
}   

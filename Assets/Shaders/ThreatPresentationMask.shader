Shader "Hidden/BlindBlade/ThreatPresentationMask"
{
    Properties
    {
        _PresentationColor ("Presentation Color", Color) = (1, 1, 1, 1)
        _PresentationFill ("Presentation Fill", Range(0, 1)) = 0.6
        _PresentationObjectId ("Presentation Object Id", Float) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" "RenderType" = "Opaque" }
        HLSLINCLUDE
        #pragma target 3.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
        CBUFFER_START(UnityPerMaterial)
            float4 _PresentationColor;
            float _PresentationFill;
            float _PresentationObjectId;
        CBUFFER_END
        struct Attributes { float4 positionOS : POSITION; UNITY_VERTEX_INPUT_INSTANCE_ID };
        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float eyeDepth : TEXCOORD0;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        // 입력 정점을 변환하고 위협 전용 깊이와 경계 색상 선택에 쓸 눈 공간 거리를 전달한다.
        Varyings Vert(Attributes input)
        {
            UNITY_SETUP_INSTANCE_ID(input);
            Varyings output;
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
            float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
            output.positionCS = TransformWorldToHClip(positionWS);
            output.eyeDepth = -TransformWorldToView(positionWS).z;
            return output;
        }
        ENDHLSL

        Pass
        {
            Name "ThreatCapture"
            Tags { "LightMode" = "ThreatPresentation" }
            ZWrite On
            ZTest LEqual
            Cull Off
            Blend Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragCapture
            #pragma multi_compile_instancing
            struct CaptureOutput { float4 color : SV_Target0; float4 data : SV_Target1; };

            // 개별 색상과 가시성, 외형 점유·채움·대상 번호·깊이를 별도 타깃에 기록한다.
            CaptureOutput FragCapture(Varyings input)
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                CaptureOutput output;
                output.color = _PresentationColor;
                output.data = float4(1, _PresentationFill, _PresentationObjectId, input.eyeDepth);
                return output;
            }
            ENDHLSL
        }
        Pass
        {
            Name "HiddenBaseSurface"
            Tags { "LightMode" = "UniversalForwardOnly" }
            ZWrite Off
            ZTest Always
            Cull Off
            ColorMask 0
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragHidden
            #pragma multi_compile_instancing

            // 기본 URP 경로에서는 색상·깊이·노멀을 기록하지 않아 거리 밖 외형 누출을 막는다.
            float4 FragHidden(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }
    }
    FallBack Off
}

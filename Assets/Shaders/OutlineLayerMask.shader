Shader "Hidden/BlindBlade/OutlineLayerMask"
{
    Properties
    {
        _MaskZTest ("Mask ZTest", Float) = 4
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _MaskUseAlpha ("Mask Use Alpha", Float) = 0
        _TransparentThreshold ("Transparent Threshold", Float) = 0.015
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off
            ZTest [_MaskZTest]
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                half4 _BaseColor;
                float _MaskUseAlpha;
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

            // 표시 대상의 정점을 화면 좌표로 변환하고 알파 모양 계산용 UV와 정점 색을 넘긴다.
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

            // 표시 대상이 덮는 화면 영역을 흰색으로 기록한다.
            // 알파 모양을 넘긴 파티클은 선택 VFX 윤곽선과 같은 밝기 기준 밖을 버려 연기 모양만 남긴다.
            half4 Frag(Varyings input) : SV_Target
            {
                if (_MaskUseAlpha > 0.5)
                {
                    half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                    half coverage = texel.a * input.color.a * _BaseColor.a;
                    half3 visibleRgb = texel.rgb * input.color.rgb * _BaseColor.rgb * coverage;
                    clip(max(visibleRgb.r, max(visibleRgb.g, visibleRgb.b)) - _TransparentThreshold);
                }
                return half4(1, 1, 1, 1);
            }
            ENDHLSL
        }
    }
}

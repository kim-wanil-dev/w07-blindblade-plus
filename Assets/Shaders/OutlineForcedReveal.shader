Shader "Hidden/BlindBlade/OutlineForcedReveal"
{
    Properties
    {
        _ForcedReveal ("Forced Reveal", Vector) = (1, 0, 0, 0)
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 렌더러별 MaterialPropertyBlock으로 받는다. x: 파동 가시성에 곱할 세기.
            CBUFFER_START(UnityPerMaterial)
                float4 _ForcedReveal;
            CBUFFER_END

            struct Attributes { float4 positionOS : POSITION; };
            struct Varyings { float4 positionCS : SV_POSITION; };

            // 세기 조절 대상의 정점을 화면 좌표로 변환한다.
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            // r에 세기, b에 덮임 표시를 기록한다.
            half4 Frag(Varyings input) : SV_Target
            {
                return half4(saturate(_ForcedReveal.x), 0, 1, 1);
            }
            ENDHLSL
        }
    }
}

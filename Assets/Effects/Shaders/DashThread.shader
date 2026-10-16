Shader "BlindBlade/DashThread"
{
    Properties
    {
        [HDR] _Color ("Core Color", Color) = (1.6, 1.6, 1.6, 1)
        _EdgeColor ("Edge Color", Color) = (0.85, 0.87, 0.9, 1)
        _Opacity ("Opacity", Range(0, 1)) = 1
        _EdgeSoftness ("Edge Softness", Range(0.05, 1)) = 0.25
        _BranchOpacity ("Branch Opacity", Range(0, 1)) = 1
        _BackOpacity ("Back Opacity", Range(0, 1)) = 0.2
        _OccludedOpacity ("Occluded Opacity", Range(0, 1)) = 0
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        CBUFFER_START(UnityPerMaterial)
            half4 _Color;
            half4 _EdgeColor;
            float _Opacity;
            float _EdgeSoftness;
            float _BranchOpacity;
            float _BackOpacity;
            float _OccludedOpacity;
        CBUFFER_END

        struct Attributes
        {
            float4 positionOS : POSITION;
            half4 color : COLOR;
            float4 uv0 : TEXCOORD0;
            float4 uv1 : TEXCOORD1;
        };

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            half4 color : COLOR;
            float4 uv0 : TEXCOORD0;
            float4 uv1 : TEXCOORD1;
        };

        // 실 띠의 위치와 가닥 정보를 그대로 전달한다. 모양과 두께는 스크립트가 매 프레임 계산한다.
        Varyings Vert(Attributes input)
        {
            Varyings output;
            output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
            output.color = input.color;
            output.uv0 = input.uv0;
            output.uv1 = input.uv1;
            return output;
        }

        // 가운데가 밝고 가장자리로 부드럽게 사라지는 매끈한 획에 visibility를 곱해 반환한다. 뒤쪽과 잔가지는 조금 옅게 한다.
        half4 Shade(Varyings input, float visibility)
        {
            float across = abs(input.uv0.y * 2.0 - 1.0);
            float glow = input.uv0.z;
            float front = input.uv0.w;
            float branch = input.uv1.x;

            float alpha = 1.0 - smoothstep(1.0 - _EdgeSoftness, 1.0, across);
            alpha *= lerp(_BackOpacity, 1.0, saturate(front * 0.5 + 0.5));
            alpha *= lerp(1.0, _BranchOpacity, branch);
            half3 color = lerp(_EdgeColor.rgb, _Color.rgb, 1.0 - smoothstep(0.0, 0.85, across)) * (1.0 + glow);

            alpha = saturate(alpha * input.color.a * _Opacity * visibility);
            return half4(color * input.color.rgb * alpha, alpha);
        }
        ENDHLSL

        // 칼집·손잡이 등에 가려진 부분은 옅게 비쳐 보여 실이 뒤로 돌아도 사라지지 않는다.
        Pass
        {
            Name "Occluded"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest Greater
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragOccluded

            // 가려진 부분을 _OccludedOpacity만큼 옅게 그린다.
            half4 FragOccluded(Varyings input) : SV_Target
            {
                return Shade(input, _OccludedOpacity);
            }
            ENDHLSL
        }

        // 보이는 부분은 온전한 밝기로 그린다.
        Pass
        {
            Name "Visible"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment FragVisible

            // 보이는 부분을 그대로 그린다.
            half4 FragVisible(Varyings input) : SV_Target
            {
                return Shade(input, 1.0);
            }
            ENDHLSL
        }
    }
}

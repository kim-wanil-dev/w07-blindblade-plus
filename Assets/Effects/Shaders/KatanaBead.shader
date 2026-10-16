Shader "BlindBlade/KatanaBead"
{
    Properties
    {
        _ShellColor ("Shell Color", Color) = (0.5, 0.5, 0.53, 0.1)
        _RimColor ("Shell Rim Color", Color) = (0.82, 0.82, 0.86, 0.65)
        _RimPower ("Shell Rim Power", Range(0.5, 8)) = 2.2
        _Opacity ("Opacity", Range(0, 1)) = 1
    }
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent+10" "RenderType"="Transparent" "IgnoreProjector"="True" }

        Pass
        {
            Name "Bead"
            Tags { "LightMode"="UniversalForward" }
            Blend One OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _ShellColor;
                half4 _RimColor;
                float _RimPower;
                float _Opacity;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                half4 color : COLOR;
                float2 uv0 : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float shell : TEXCOORD2;
            };

            // 구슬 껍질·실·끈·금 정점을 그대로 전달한다. 모양과 색은 스크립트가 매 프레임 계산한다.
            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.color = input.color;
                output.shell = input.uv0.x;
                return output;
            }

            // 껍질(uv0.x = 1)은 가장자리로 갈수록 밝고 진해지는 텅 빈 유리처럼, 나머지는 정점 색으로 그린다.
            half4 Frag(Varyings input) : SV_Target
            {
                float3 viewDir = normalize(GetWorldSpaceViewDir(input.positionWS));
                float facing = abs(dot(normalize(input.normalWS), viewDir));
                half4 shell = lerp(_ShellColor, _RimColor, pow(1.0 - saturate(facing), _RimPower));
                half4 color = lerp(input.color, shell * input.color, input.shell);
                color.a = saturate(color.a * _Opacity);
                return half4(color.rgb * color.a, color.a);
            }
            ENDHLSL
        }
    }
}

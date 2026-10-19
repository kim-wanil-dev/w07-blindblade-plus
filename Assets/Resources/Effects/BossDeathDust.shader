Shader "BlindBlade/BossDeathDust"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Transparent" "Queue"="Transparent" }
        Pass
        {
            Name "AshParticles"
            Tags { "LightMode"="UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            Cull Off
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 point = input.uv * 2.0 - 1.0;
                float angle = atan2(point.y, point.x);
                float radius = length(point);
                float seed = frac(dot(input.color.rgb, float3(17.13, 31.71, 47.33)));
                float edge = 0.58 + 0.11 * sin(angle * 3.0 + seed * 6.283)
                    + 0.07 * sin(angle * 7.0 - seed * 11.4)
                    + 0.035 * sin(angle * 11.0 + seed * 17.2);
                float alpha = 1.0 - smoothstep(edge - 0.11, edge, radius);
                clip(alpha - 0.02);
                return half4(input.color.rgb, input.color.a * alpha);
            }
            ENDHLSL
        }
    }
}

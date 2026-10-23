Shader "BlindBlade/ThreadLiquidDensity"
{
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "IgnoreProjector" = "True" }

        Cull Off
        ZWrite Off
        ZTest Always
        Blend One One

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "UnityCG.cginc"

            struct AppData
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
            };

            struct VertexData
            {
                float4 vertex : SV_POSITION;
                float density : TEXCOORD0;
            };

            // 액체 반지름 단위 좌표를 그대로 클립 좌표로 쓴다. 텍스처 원점이 위인 플랫폼에서는 위아래를 뒤집어 다른 플랫폼과 같은 방향으로 저장한다.
            VertexData Vert(AppData input)
            {
                VertexData output;
                output.vertex = float4(input.vertex.xy, 0.5, 1.0);
                #if UNITY_UV_STARTS_AT_TOP
                output.vertex.y = -output.vertex.y;
                #endif
                output.density = input.color.a;
                return output;
            }

            // 실 한 겹의 무게를 겹침 수 1/8 단위로 더해 여덟 겹까지 담는다.
            float4 Frag(VertexData input) : SV_Target
            {
                return float4(input.density * 0.125, 0.0, 0.0, 0.0);
            }
            ENDCG
        }
    }
}

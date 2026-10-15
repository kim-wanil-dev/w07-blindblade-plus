Shader "Hidden/BlindBlade/PerceptionRevealUpdate"
{
    Properties
    {
        _MainTex ("Previous Reveal", 2D) = "black" {}
    }

    SubShader
    {
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment Frag
            #pragma target 3.5

            #include "UnityCG.cginc"

            #define MAX_RIPPLES 128
            #define MAX_STAMPS 32

            sampler2D _MainTex;
            float4 _RevealBounds;
            float _RevealTime;
            float _RevealEdgeFade;
            float _RevealClear;
            int _RevealRippleCount;
            float4 _RevealRipples[MAX_RIPPLES];
            float4 _RevealRippleParams[MAX_RIPPLES];
            int _RevealStampCount;
            float4 _RevealStamps[MAX_STAMPS];

            // 반경 바깥 경계로 갈수록 잔상 길이를 줄여 공개 영역의 가장자리를 부드럽게 만든다.
            float EdgeFactor(float distanceFromCenter, float radius)
            {
                float innerRadius = max(0.0, radius - max(_RevealEdgeFade, 0.0001));
                return 1.0 - smoothstep(innerRadius, radius, distanceFromCenter);
            }

            // 만료 시각이 더 늦은 후보를 남기며 해당 후보의 잔상 길이를 함께 기록한다.
            void Keep(inout float2 reveal, float expireTime, float afterimage)
            {
                if (expireTime > reveal.x)
                    reveal = float2(expireTime, afterimage);
            }

            // 이전 만료 시각에 이번 프레임의 3겹 파동 전선과 이동 시야원을 합성한다.
            float4 Frag(v2f_img input) : SV_Target
            {
                float2 reveal = _RevealClear > 0.5
                    ? float2(-1.0, 1.0)
                    : tex2D(_MainTex, input.uv).rg;
                float2 position = _RevealBounds.xy + input.uv * _RevealBounds.zw;

                [loop]
                for (int i = 0; i < _RevealRippleCount; i++)
                {
                    float4 ripple = _RevealRipples[i];
                    float4 rippleParams = _RevealRippleParams[i];
                    float distanceFromCenter = distance(position, ripple.xy);
                    if (distanceFromCenter > ripple.w)
                        continue;
                    // 전선이 이 지점을 지난 시각은 감속 확산식의 역함수로 구한다.
                    float hitTime = rippleParams.x * (1.0 - sqrt(saturate(1.0 - distanceFromCenter / ripple.w)));
                    float afterimage = rippleParams.z * EdgeFactor(distanceFromCenter, ripple.w);
                    for (int ring = 0; ring < (int)rippleParams.w; ring++)
                    {
                        float ringStart = ripple.z + ring * rippleParams.y;
                        if (_RevealTime >= ringStart + hitTime)
                            Keep(reveal, ringStart + hitTime + afterimage, rippleParams.z);
                    }
                }

                [loop]
                for (int j = 0; j < _RevealStampCount; j++)
                {
                    float4 stamp = _RevealStamps[j];
                    float distanceFromCenter = distance(position, stamp.xy);
                    if (distanceFromCenter <= stamp.z)
                        Keep(reveal, _RevealTime + stamp.w * EdgeFactor(distanceFromCenter, stamp.z), stamp.w);
                }
                return float4(reveal, 0.0, 1.0);
            }
            ENDCG
        }
    }
}

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

            sampler2D _MainTex;
            float4 _RevealBounds;
            float _RevealTime;
            float _RevealEdgeFade;
            float _RevealClear;
            int _RevealRippleCount;
            float4 _RevealRipples[MAX_RIPPLES];
            float4 _RevealRippleParams[MAX_RIPPLES];

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

            // 이전 만료 시각에 이번 프레임의 3겹 파동 전선을 합성한다.
            // RG는 일반 파동, BA는 발생자 몸체를 밝히지 않는 파동의 만료 시각과 잔상 길이다.
            float4 Frag(v2f_img input) : SV_Target
            {
                float4 reveal = _RevealClear > 0.5
                    ? float4(-1.0, 1.0, -1.0, 1.0)
                    : tex2D(_MainTex, input.uv);
                float2 general = reveal.rg;
                float2 ownerHidden = reveal.ba;
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
                    // 겹 수가 음수이면 발생자 몸체를 밝히지 않는 파동이다.
                    bool hideOwner = rippleParams.w < 0.0;
                    int ringCount = (int)abs(rippleParams.w);
                    for (int ring = 0; ring < ringCount; ring++)
                    {
                        float ringStart = ripple.z + ring * rippleParams.y;
                        if (_RevealTime < ringStart + hitTime)
                            continue;
                        if (hideOwner)
                            Keep(ownerHidden, ringStart + hitTime + afterimage, rippleParams.z);
                        else
                            Keep(general, ringStart + hitTime + afterimage, rippleParams.z);
                    }
                }
                return float4(general, ownerHidden);
            }
            ENDCG
        }
    }
}

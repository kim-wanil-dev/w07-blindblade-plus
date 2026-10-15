Shader "BlindBlade/RainStage"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }
        Pass
        {
            Name "RainStage"
            Tags { "LightMode"="SRPDefaultUnlit" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct RainDrop
            {
                float4 PositionState;
                float4 Data;
            };

            StructuredBuffer<RainDrop> _RainDrops;
            float3 _CameraRight;
            float3 _CameraUp;
            float _Roundness;
            float _DropSize;
            float _BulletDropletScale;
            float _WiggleAmplitude;
            float _WiggleTime;
            float _StreakLength;
            float _SplashSize;
            float _SplashDuration;
            float _OutlineWidth;
            float _OutlineOpacity;
            float4 _OutlineColor;
            float4 _SplashColor;

            struct Attributes
            {
                float3 positionOS : POSITION;
                uint instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float state : TEXCOORD1;
                float splashAge : TEXCOORD2;
                float seed : TEXCOORD3;
            };

            Varyings Vert(Attributes input)
            {
                RainDrop drop = _RainDrops[input.instanceID];
                Varyings output;
                float isSplash = step(0.5, drop.PositionState.w);
                float splashProgress = saturate(drop.Data.y / max(_SplashDuration, 0.0001));
                float halfWidth = lerp(_DropSize * 0.17, _DropSize, _Roundness);
                float halfHeight = lerp(_StreakLength * 0.5, _DropSize, _Roundness);
                float dropletScale = lerp(1.0, _BulletDropletScale, _Roundness);
                float splashHalfSize = _SplashSize * (0.15 + 0.35 * splashProgress);
                halfWidth = lerp(halfWidth * dropletScale, splashHalfSize, isSplash);
                halfHeight = lerp(halfHeight * dropletScale, splashHalfSize, isSplash);
                float3 world = drop.PositionState.xyz
                    + _CameraRight * input.positionOS.x * halfWidth
                    + _CameraUp * input.positionOS.y * halfHeight;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.positionOS.xy;
                output.state = drop.PositionState.w;
                output.splashAge = splashProgress;
                output.seed = drop.Data.x;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                if (input.state > 1.5) discard;
                float2 uv = input.uv;
                float radius = length(uv);
                if (input.state > 0.5)
                {
                    float progress = input.splashAge;
                    float ringRadius = lerp(0.18, 0.82, progress);
                    float ring = 1.0 - smoothstep(0.04, 0.12, abs(radius - ringRadius));
                    float dots = 0.0;
                    [unroll]
                    for (int i = 0; i < 6; i++)
                    {
                        float angle = (i + input.seed) * 1.04719755;
                        float2 direction = float2(cos(angle), sin(angle));
                        float2 splashPoint = direction * ringRadius;
                        dots += 1.0 - smoothstep(0.04, 0.12, length(uv - splashPoint));
                    }
                    float alpha = saturate(ring * 0.55 + dots) * (1.0 - progress);
                    return half4(_SplashColor.rgb, saturate(alpha * _SplashColor.a * 1.8));
                }

                float angle = atan2(uv.y, uv.x);
                float frozenAmount = saturate(_Roundness);
                float seedPhase = input.seed * 6.2831853;
                float directionUp = max(sin(angle), 0.0);
                float topTaper = 0.14 * directionUp * directionUp * directionUp * directionUp;
                float irregularity = 0.07 * sin(angle * 2.0 + seedPhase)
                    + 0.04 * sin(angle * 3.0 - seedPhase * 1.7);
                float oscillation = 0.65 * sin(angle * 2.7 + _WiggleTime * 3.5 + seedPhase)
                    + 0.35 * sin(angle * 4.1 - _WiggleTime * 2.6 + seedPhase * 1.7);
                float boundary = 1.0 + frozenAmount
                    * (-topTaper + irregularity + _WiggleAmplitude * oscillation);
                float edgeDistance = abs(radius - boundary);
                float edgeAntialias = max(fwidth(edgeDistance), 0.002);
                float outline = 1.0 - smoothstep(_OutlineWidth,
                    _OutlineWidth + edgeAntialias, edgeDistance);
                return half4(_OutlineColor.rgb,
                    outline * _OutlineColor.a * _OutlineOpacity);
            }
            ENDHLSL
        }
    }
}

Shader "BlindBlade/RainStage"
{
    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "Queue"="Transparent" "RenderType"="Transparent" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        struct RainDrop
        {
            float4 PositionState;
            float4 Data;
        };

        struct GroundRipple
        {
            float4 PositionStart;
            float4 Data;
        };

        struct Attributes
        {
            float3 positionOS : POSITION;
            uint instanceID : SV_InstanceID;
        };

        float _RainClock;
        ENDHLSL

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

            StructuredBuffer<RainDrop> _RainDrops;
            StructuredBuffer<GroundRipple> _GroundRipples;
            float _RippleMode;
            float _GroundRippleDuration;
            float _GroundRippleRingInterval;
            float4 _GroundRippleColor;

            #define GROUND_RIPPLE_RING_COUNT 3
            #define GROUND_RIPPLE_RING_RADIUS 0.88
            #define GROUND_RIPPLE_RING_HALF_WIDTH 0.025
            #define GROUND_RIPPLE_LATER_RING_ALPHA 0.7
            // 이보다 덜 밝혀진 빗방울은 면적 없는 삼각형으로 접어 그리지 않는다.
            #define MINIMUM_DROP_VISIBILITY 0.003
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
            float _IdleDropVisibility;
            float4 _OutlineColor;
            float4 _SplashColor;

            // 아래 전역 인지 값은 ScreenSpaceOutline 셰이더와 같은 이름·크기이며, 빗방울 위치의 공개 정도를 같은 규칙으로 구한다.
            #define MAX_SOUND_REGIONS 16
            #define MAX_REVEAL_SPHERES 32
            #define MAX_HIDDEN_OWNERS 32
            #define MAX_IMPACT_RIPPLES 32
            #define OWNER_HIDE_EDGE 0.2
            float4 _OutlineObserverRegion;
            float4 _OutlineSoundRegions[MAX_SOUND_REGIONS];
            float4 _OutlineSoundRegionRadii[MAX_SOUND_REGIONS];
            int _OutlineSoundCount;
            float _OutlineSoundStartRadius;
            float _OutlineSoundEndRadius;
            float _OutlineSoundLifetime;
            float _OutlineSoundFadeDuration;
            float _OutlineEdgeFade;
            float _OutlinePerceptionTime;
            float4 _OutlinePulseRegion;
            float _OutlinePulseVisibility;
            TEXTURE2D(_OutlineRevealMap);
            SAMPLER(sampler_OutlineRevealMap);
            float4 _OutlineRevealBounds;
            float _OutlineRevealEnabled;
            int _OutlineRevealSphereCount;
            float4 _OutlineRevealSpheres[MAX_REVEAL_SPHERES];
            float4 _OutlineRevealSphereStrengths[MAX_REVEAL_SPHERES];
            int _OutlineHiddenOwnerCount;
            float4 _OutlineHiddenOwners[MAX_HIDDEN_OWNERS];
            int _OutlineImpactRippleCount;
            float4 _OutlineImpactRipples[MAX_IMPACT_RIPPLES];
            float4 _OutlineImpactRippleParams[MAX_IMPACT_RIPPLES];

            #include "Assets/Shaders/PerceptionSphereRipples.hlsl"

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float state : TEXCOORD1;
                float splashAge : TEXCOORD2;
                float seed : TEXCOORD3;
                float visibility : TEXCOORD4;
            };

            // 영역 반경의 안쪽 경계부터 가장자리까지 공개 정도를 부드럽게 줄인다.
            float RegionVisibility(float3 position, float3 center, float radius)
            {
                if (radius <= 0.0)
                    return 0.0;
                float innerRadius = max(0.0, radius - max(_OutlineEdgeFade, 0.0001));
                return 1.0 - smoothstep(innerRadius, radius, distance(position, center));
            }

            // 잔상 만료 시각과 잔상 길이로 현재 공개 정도를 반환한다.
            float ExpireVisibility(float2 reveal)
            {
                return smoothstep(0.0, 1.0,
                    saturate((reveal.x - _OutlinePerceptionTime) / max(reveal.y, 0.0001)));
            }

            // 몸체를 밝히지 않는 파동의 발생자 주변일수록 1에 가까운 제외 배율을 반환한다.
            float OwnerExclusion(float3 position)
            {
                float exclusion = 0.0;
                [loop]
                for (int i = 0; i < _OutlineHiddenOwnerCount; i++)
                {
                    float4 owner = _OutlineHiddenOwners[i];
                    float innerRadius = max(0.0, owner.w - OWNER_HIDE_EDGE);
                    exclusion = max(exclusion,
                        1.0 - smoothstep(innerRadius, owner.w, distance(position.xz, owner.xz)));
                }
                return exclusion;
            }

            // 걸음 파동 수평 잔상과 구형 강조로 현재 공개 정도를 반환한다.
            float RevealMapVisibility(float3 position)
            {
                if (_OutlineRevealEnabled < 0.5)
                    return 0.0;
                float visibility = 0.0;
                float2 revealUv = (position.xz - _OutlineRevealBounds.xy) / _OutlineRevealBounds.zw;
                if (all(revealUv >= 0.0) && all(revealUv <= 1.0))
                {
                    float4 reveal = SAMPLE_TEXTURE2D_LOD(_OutlineRevealMap, sampler_OutlineRevealMap, revealUv, 0);
                    visibility = ExpireVisibility(reveal.rg);
                    float hiddenVisibility = ExpireVisibility(reveal.ba);
                    if (hiddenVisibility > 0.0)
                        visibility = max(visibility, hiddenVisibility * (1.0 - OwnerExclusion(position)));
                }
                [loop]
                for (int i = 0; i < _OutlineRevealSphereCount; i++)
                {
                    float4 sphere = _OutlineRevealSpheres[i];
                    float progress = saturate(distance(position, sphere.xyz) / max(sphere.w, 0.0001));
                    visibility = max(visibility,
                        (1.0 - smoothstep(0.0, 1.0, progress)) * _OutlineRevealSphereStrengths[i].x);
                }
                return visibility;
            }

            // 약한 충격 파동이 구 모양으로 퍼진 전선이 지나간 뒤 잔상 동안 흐려지는 세기를 모두 더해 반환한다.
            float ImpactRippleVisibility(float3 position)
            {
                float visibility = 0.0;
                [loop]
                for (int i = 0; i < _OutlineImpactRippleCount; i++)
                {
                    float4 ripple = _OutlineImpactRipples[i];
                    float4 rippleParams = _OutlineImpactRippleParams[i];
                    float distanceFromCenter = distance(position, ripple.xyz);
                    if (distanceFromCenter > ripple.w)
                        continue;
                    float hitTime = rippleParams.x * (1.0 - sqrt(saturate(1.0 - distanceFromCenter / ripple.w)));
                    float age = _OutlinePerceptionTime - (rippleParams.w + hitTime);
                    if (age < 0.0)
                        continue;
                    float fade = smoothstep(0.0, 1.0, saturate(1.0 - age / max(rippleParams.y, 0.0001)));
                    float innerRadius = max(0.0, ripple.w - max(_OutlineEdgeFade, 0.0001));
                    visibility += fade * (1.0 - smoothstep(innerRadius, ripple.w, distanceFromCenter)) * rippleParams.z;
                }
                return saturate(visibility);
            }

            // 빗방울 position의 선명함을 0~1로 반환한다. 평상시는 _IdleDropVisibility이고 퍼펙트 패링·극한 회피 파동 안에서 1까지 밝아진다.
            // 다른 인지 파동은 떨어지는 빗방울을 밝히지 않으며, 파동은 높이를 무시하고 수평 거리로 판정한다.
            float DropVisibility(float3 position)
            {
                float pulseVisibility = RegionVisibility(
                    float3(position.x, 0.0, position.z),
                    float3(_OutlinePulseRegion.x, 0.0, _OutlinePulseRegion.z),
                    _OutlinePulseRegion.w) * _OutlinePulseVisibility;
                return saturate(max(_IdleDropVisibility, pulseVisibility));
            }

            Varyings RippleVert(Attributes input)
            {
                GroundRipple ripple = _GroundRipples[input.instanceID];
                Varyings output = (Varyings)0;
                float age = _RainClock - ripple.PositionStart.w;
                float lifetime = _GroundRippleDuration + (GROUND_RIPPLE_RING_COUNT - 1) * _GroundRippleRingInterval;
                // 아직 시작 전이거나 마지막 링까지 끝난 파동은 면적 없는 삼각형으로 접어 그리지 않는다.
                if (age < 0.0 || age >= lifetime) return output;
                float3 world = ripple.PositionStart.xyz + float3(0, 0.02, 0)
                    + float3(input.positionOS.x, 0, input.positionOS.y) * ripple.Data.x;
                output.positionCS = TransformWorldToHClip(world);
                output.uv = input.positionOS.xy;
                output.state = 3;
                output.splashAge = age;
                output.seed = ripple.Data.y;
                output.visibility = 1;
                return output;
            }

            Varyings Vert(Attributes input)
            {
                if (_RippleMode > 0.5) return RippleVert(input);
                RainDrop drop = _RainDrops[input.instanceID];
                Varyings output = (Varyings)0;
                // 떨어지는 빗방울은 평상시 흐리게 보이다 퍼펙트 패링·극한 회피 파동 안에서 선명해지고, 검격 파열은 그대로 보인다.
                float visibility = drop.PositionState.w < 0.5 ? DropVisibility(drop.PositionState.xyz) : 1.0;
                if (visibility < MINIMUM_DROP_VISIBILITY) return output;
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
                output.visibility = visibility;
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;
                float radius = length(uv);
                float radiusAntialias = max(fwidth(radius), 0.002);
                if (input.state > 2.5)
                {
                    // 발걸음 바닥 링처럼 세 겹이 시간차로 퍼지고, 뒤 링일수록 옅으며 각 링은 후반부에 흐려진다.
                    float alpha = 0.0;
                    [unroll]
                    for (int ringIndex = 0; ringIndex < GROUND_RIPPLE_RING_COUNT; ringIndex++)
                    {
                        float ringProgress = (input.splashAge - ringIndex * _GroundRippleRingInterval)
                            / max(_GroundRippleDuration, 0.0001);
                        if (ringProgress < 0.0 || ringProgress >= 1.0) continue;
                        float growth = 1.0 - (1.0 - ringProgress) * (1.0 - ringProgress);
                        float halfWidth = GROUND_RIPPLE_RING_HALF_WIDTH * growth;
                        float ring = 1.0 - smoothstep(halfWidth, halfWidth + radiusAntialias,
                            abs(radius - GROUND_RIPPLE_RING_RADIUS * growth));
                        float fade = 1.0 - smoothstep(0.5, 1.0, ringProgress);
                        alpha = max(alpha, ring * fade * pow(GROUND_RIPPLE_LATER_RING_ALPHA, ringIndex));
                    }
                    return half4(_GroundRippleColor.rgb, alpha * _GroundRippleColor.a);
                }
                if (input.state > 1.5) discard;
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
                    outline * _OutlineColor.a * _OutlineOpacity * input.visibility);
            }
            ENDHLSL
        }

        Pass
        {
            // RainStageWeather의 화면 공개 패스에서만 그린다. 빗방울 파동 구가 덮는 화면 픽셀의 표면 공개 정도를 더한다.
            Name "RainReveal"
            Tags { "LightMode"="RainReveal" }
            Blend One One
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma target 4.5
            #pragma vertex RevealVert
            #pragma fragment RevealFrag

            // 구가 원근으로 조금 더 크게 보이는 만큼 사각형을 넓힌다.
            #define REVEAL_QUAD_SLACK 1.25
            StructuredBuffer<GroundRipple> _RevealRipples;
            TEXTURE2D_X_FLOAT(_RainSceneDepth);
            float _RevealRadiusScale;
            float _RevealExpandDuration;
            float _RevealAfterimage;
            float _RevealStrength;

            struct RevealVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 worldPosition : TEXCOORD0;
                nointerpolation float4 sphere : TEXCOORD1;
                nointerpolation float startedAt : TEXCOORD2;
            };

            RevealVaryings RevealVert(Attributes input)
            {
                GroundRipple ripple = _RevealRipples[input.instanceID];
                RevealVaryings output = (RevealVaryings)0;
                float radius = ripple.Data.x * _RevealRadiusScale;
                float age = _RainClock - ripple.PositionStart.w;
                // 시작 전이거나 잔상까지 끝난 파동은 면적 없는 삼각형으로 접어 그리지 않는다.
                if (radius <= 0.0 || age < 0.0 || age >= _RevealExpandDuration + _RevealAfterimage)
                    return output;
                float3 right = UNITY_MATRIX_V[0].xyz;
                float3 up = UNITY_MATRIX_V[1].xyz;
                float3 world = ripple.PositionStart.xyz
                    + (right * input.positionOS.x + up * input.positionOS.y) * radius * REVEAL_QUAD_SLACK;
                output.positionCS = TransformWorldToHClip(world);
                output.worldPosition = world;
                output.sphere = float4(ripple.PositionStart.xyz, radius);
                output.startedAt = ripple.PositionStart.w;
                return output;
            }

            half4 RevealFrag(RevealVaryings input) : SV_Target
            {
                // 픽셀의 장면 깊이로 카메라 광선 위 실제 표면 위치를 구해 구 안에 있는지 본다.
                float rawDepth = LOAD_TEXTURE2D_X(_RainSceneDepth, uint2(input.positionCS.xy)).r;
                float eyeDepth = LinearEyeDepth(rawDepth, _ZBufferParams);
                float3 ray = input.worldPosition - _WorldSpaceCameraPos;
                float3 forward = -UNITY_MATRIX_V[2].xyz;
                float3 surface = _WorldSpaceCameraPos + ray * (eyeDepth / max(dot(ray, forward), 0.0001));
                float radius = input.sphere.w;
                float distanceFromCenter = distance(surface, input.sphere.xyz);
                if (distanceFromCenter > radius)
                    return 0;
                // 충격 파동과 같은 감속 확산식으로 전선이 지난 뒤 잔상 동안 흐려진다.
                float hitTime = _RevealExpandDuration * (1.0 - sqrt(saturate(1.0 - distanceFromCenter / radius)));
                float age = _RainClock - (input.startedAt + hitTime);
                if (age < 0.0)
                    return 0;
                float fade = smoothstep(0.0, 1.0, saturate(1.0 - age / max(_RevealAfterimage, 0.0001)));
                float edge = 1.0 - smoothstep(radius * 0.6, radius, distanceFromCenter);
                return half4(fade * edge * _RevealStrength, 0, 0, 0);
            }
            ENDHLSL
        }
    }
}

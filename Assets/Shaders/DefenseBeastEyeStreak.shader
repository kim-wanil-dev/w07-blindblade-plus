Shader "BlindBlade/DefenseBeastEyeStreak"
{
    Properties
    {
        _CoreColor ("Hot Core", Color) = (1, 0.8, 0.65, 1)
        _GlowColor ("Red Glow", Color) = (1, 0.06, 0.02, 1)
        _CoreWidth ("Core Width", Range(0.05, 1)) = 0.3
        _GlowFalloff ("Glow Falloff", Range(0.5, 8)) = 2
        _TipBoost ("Tip Boost", Range(0, 4)) = 1.5
        _TipLength ("Tip Length", Range(0.01, 1)) = 0.15
        _Intensity ("Intensity", Float) = 1
        _DotMode ("Dot Mode", Float) = 0
        _UsePerception ("Use Perception", Float) = 1
        _OutlineOnly ("Outline Only", Float) = 1
        _LineColor ("Outline Color", Color) = (1, 0.1, 0.06, 1)
        _LineWidth ("Outline Width (px)", Range(0.5, 6)) = 1
        _DotRing ("Dot Ring Radius", Range(0.1, 1)) = 0.85
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "Queue" = "Transparent+10"
            "RenderType" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "EyeStreak"
            Blend One One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/GlobalSamplers.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _CoreColor;
                half4 _GlowColor;
                float _CoreWidth;
                float _GlowFalloff;
                float _TipBoost;
                float _TipLength;
                float _Intensity;
                float _DotMode;
                float _UsePerception;
                float _OutlineOnly;
                half4 _LineColor;
                float _LineWidth;
                float _DotRing;
            CBUFFER_END

            // 아래 전역 인지 값은 ScreenSpaceOutline 셰이더와 같은 이름·크기이며, 눈 줄기 위치의 공개 정도를 같은 규칙으로 구한다.
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
            float _OutlinePulseSpherical;
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
            TEXTURE2D(_OutlineRainReveal);
            float _OutlineRainRevealEnabled;

            #include "Assets/Shaders/PerceptionSphereRipples.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                half4 color : COLOR;
                float2 uv : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
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

            // 걸음 파동이 남긴 XZ 잔상과 구형 강조로 현재 공개 정도를 반환한다.
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

            // 윤곽선의 적 몸체 공개 규칙(시야원·소리·보상 파동·걸음 잔상·충격·구형 파동·빗방울)으로 position의 공개 정도를 반환한다.
            float PerceptionVisibility(float3 position, float2 screenUv)
            {
                float visibility = RegionVisibility(position, _OutlineObserverRegion.xyz, _OutlineObserverRegion.w);
                [loop]
                for (int i = 0; i < _OutlineSoundCount; i++)
                {
                    float age = max(0.0, _OutlinePerceptionTime - _OutlineSoundRegions[i].w);
                    float progress = saturate(age / max(_OutlineSoundLifetime, 0.0001));
                    float expansion = smoothstep(0.0, 1.0, progress);
                    float4 regionRadii = _OutlineSoundRegionRadii[i];
                    float revealRadius = regionRadii.z > 0.0
                        ? lerp(regionRadii.x, regionRadii.y, expansion)
                        : lerp(_OutlineSoundStartRadius, _OutlineSoundEndRadius, expansion);
                    float timeVisibility = 1.0 - smoothstep(_OutlineSoundLifetime,
                        _OutlineSoundLifetime + max(_OutlineSoundFadeDuration, 0.0001), age);
                    visibility = max(visibility,
                        RegionVisibility(position, _OutlineSoundRegions[i].xyz, revealRadius) * timeVisibility);
                }
                float pulseHeight = _OutlinePulseSpherical > 0.5 ? 1.0 : 0.0;
                visibility = max(visibility, RegionVisibility(
                    float3(position.x, position.y * pulseHeight, position.z),
                    float3(_OutlinePulseRegion.x, _OutlinePulseRegion.y * pulseHeight, _OutlinePulseRegion.z),
                    _OutlinePulseRegion.w) * _OutlinePulseVisibility);
                visibility = max(visibility, RevealMapVisibility(position));
                visibility = max(visibility, ImpactRippleVisibility(position));
                visibility = max(visibility, SphereRippleVisibility(position, _OutlinePerceptionTime, _OutlineEdgeFade));
                if (_OutlineRainRevealEnabled > 0.5)
                    visibility = max(visibility,
                        saturate(SAMPLE_TEXTURE2D_LOD(_OutlineRainReveal, sampler_PointClamp, screenUv, 0).r));
                return saturate(visibility);
            }

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.color = input.color;
                output.uv = input.uv;
                return output;
            }

            // 화면 픽셀 단위 거리 pixels가 _LineWidth 안이면 1에 가까운 선 세기를 반환한다. 경계는 한 픽셀만 부드럽게 한다.
            float LineStrength(float pixels)
            {
                return 1.0 - smoothstep(_LineWidth - 0.5, _LineWidth + 0.5, pixels);
            }

            // 다른 윤곽선처럼 안쪽은 비우고 가장자리만 _LineWidth 픽셀 굵기 선으로 그린다.
            // 줄기는 띠의 양옆(uv.y 0·1)을, 눈 점은 _DotRing 반지름의 고리를 선으로 삼는다.
            float OutlineStrength(float2 uv)
            {
                if (_DotMode > 0.5)
                {
                    float radius = length(uv * 2.0 - 1.0);
                    return LineStrength(abs(radius - _DotRing) / max(fwidth(radius), 0.00001));
                }
                // 끝을 막는 선 없이 양옆 선만 그려 꼬리에서 옅어지며 자연스럽게 사라지게 한다.
                return LineStrength(min(uv.y, 1.0 - uv.y) / max(fwidth(uv.y), 0.00001));
            }

            // 윤곽선 모드(_OutlineOnly 1)는 가장자리 선만 _LineColor로 그린다. 발광 모드는 줄기 가로(uv.y) 가운데의 뜨거운 심과
            // 붉은 번짐을, 길이(uv.x) 앞쪽 눈 가까이를 더 밝게 그리고, 눈 점(_DotMode 1)은 사각형 가운데에서 바깥으로 같은 심·번짐을 그린다.
            // 두 모드 모두 정점 알파로 꼬리를 흐리게 하고 인지 공개 정도를 곱한다.
            half4 Frag(Varyings input) : SV_Target
            {
                float visibility = 1.0;
                if (_UsePerception > 0.5)
                    visibility = PerceptionVisibility(input.positionWS, GetNormalizedScreenSpaceUV(input.positionCS));
                if (_OutlineOnly > 0.5)
                {
                    float strength = OutlineStrength(input.uv);
                    return half4(_LineColor.rgb * _LineColor.a * strength * input.color.a * _Intensity * visibility, 1.0);
                }

                float across = abs(input.uv.y * 2.0 - 1.0);
                float along = input.uv.x;
                if (_DotMode > 0.5)
                {
                    across = length(input.uv * 2.0 - 1.0);
                    along = 0.0;
                }
                float core = 1.0 - smoothstep(_CoreWidth * 0.6, _CoreWidth, across);
                float glow = pow(saturate(1.0 - across), _GlowFalloff);
                float tip = 1.0 + exp(-along / max(_TipLength, 0.0001)) * _TipBoost;
                half3 color = (_GlowColor.rgb * glow + _CoreColor.rgb * core) * tip;
                return half4(color * input.color.rgb * input.color.a * _Intensity * visibility, 1.0);
            }
            ENDHLSL
        }
    }
}

Shader "Hidden/BlindBlade/ScreenSpaceOutline"
{
    Properties
    {
        _LineColor ("Line Color", Color) = (1, 1, 1, 1)
        _LineWidth ("Line Width", Float) = 1
        _DepthSensitivity ("Depth Sensitivity", Float) = 50
        _NormalSensitivity ("Normal Sensitivity", Float) = 4
        _TransparentThreshold ("Transparent Coverage Threshold", Float) = 0.015
        _SceneryColor ("Scenery Color", Color) = (0.3, 0.3, 0.3, 1)
        _ThreatColor ("Threat Color", Color) = (1, 0.1, 0.06, 1)
        _ThreatFillOpacity ("Threat Fill Opacity", Float) = 0.6
        _ThreatHighlightFillOpacity ("Threat Highlight Fill Opacity", Float) = 0.6
        _MeleeWarningColor ("Melee Warning Color", Color) = (1, 0.1, 0.06, 1)
        _BeastGlintColor ("Beast Glint Color", Color) = (0.5691823, 0.8328799, 1, 1)
        _BeastThickGlintWidthScale ("Beast Thick Glint Width Scale", Float) = 2.5
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        Pass
        {
            Name "ScreenSpaceOutline"
            ZWrite Off
            ZTest Always
            Cull Off
            Blend Off

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareNormalsTexture.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _LineColor;
                float _LineWidth;
                float _DepthSensitivity;
                float _NormalSensitivity;
                float _UsePerceptionMask;
                float _TransparentThreshold;
                half4 _SceneryColor;
                half4 _ThreatColor;
                float _ThreatFillOpacity;
                float _ThreatHighlightFillOpacity;
                half4 _MeleeWarningColor;
                half4 _BeastGlintColor;
                float _BeastThickGlintWidthScale;
            CBUFFER_END

            // 불릿 타임·극한 회피 파동 동안 플레이어가 전역으로 넘기는 위협 채움 강조 세기(0~1).
            float _ThreatSilhouetteWeight;

            TEXTURE2D_X(_OutlineOpaqueColor);
            TEXTURE2D_X(_OutlineAlwaysVisibleVfxColor);
            TEXTURE2D_X(_OutlineTransparentDepth);
            TEXTURE2D_X(_OutlineSceneryMask);
            TEXTURE2D_X(_OutlineThreatMask);
            TEXTURE2D_X(_OutlineMeleeWarningMask);
            TEXTURE2D_X(_OutlineBeastOutlineExclusionMask);
            TEXTURE2D_X(_OutlineForcedReveal);
            TEXTURE2D_X(_ThreatPresentationColor);
            TEXTURE2D_X(_ThreatPresentationData);

            #define MAX_SOUND_REGIONS 16
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
            // rgb: 파동이 밝힌 윤곽선 색, a: 물들이는 정도. 보스 반격 파동만 붉게 쓴다.
            float4 _OutlinePulseTint;
            // 1이면 보상 파동 영역을 3D 거리 구로 공개한다(쌍둥이 거합 파동). 0이면 기존처럼 수평거리다.
            float _OutlinePulseSpherical;
            TEXTURE2D(_OutlineTerrainMemory);
            SAMPLER(sampler_OutlineTerrainMemory);
            float4 _OutlineMemoryBounds;
            float4 _OutlineMemoryColor;
            float _OutlineMemoryEnabled;
            TEXTURE2D(_OutlineRevealMap);
            SAMPLER(sampler_OutlineRevealMap);
            float4 _OutlineRevealBounds;
            float _OutlineRevealEnabled;
            #define MAX_REVEAL_SPHERES 32
            #define MAX_HIDDEN_OWNERS 32
            #define OWNER_HIDE_EDGE 0.2
            int _OutlineRevealSphereCount;
            float4 _OutlineRevealSpheres[MAX_REVEAL_SPHERES];
            float4 _OutlineRevealSphereStrengths[MAX_REVEAL_SPHERES];
            int _OutlineHiddenOwnerCount;
            float4 _OutlineHiddenOwners[MAX_HIDDEN_OWNERS];
            // x: 적 파동 바닥 높이, y: 배경을 온전히 밝히는 높이, z: 그 위에서 흐려지는 높이
            float4 _OutlineHiddenSceneryHeight;
            #define MAX_IMPACT_RIPPLES 32
            int _OutlineImpactRippleCount;
            // xyz: 중심, w: 최대 반경
            float4 _OutlineImpactRipples[MAX_IMPACT_RIPPLES];
            // x: 확산 시간, y: 잔상 시간, z: 세기, w: 시작 시각
            float4 _OutlineImpactRippleParams[MAX_IMPACT_RIPPLES];
            // RainyStage 빗방울 파동이 화면 픽셀마다 남긴 공개 정도다. Enabled가 0이면 읽지 않는다.
            TEXTURE2D(_OutlineRainReveal);
            float _OutlineRainRevealEnabled;

            #include "Assets/Shaders/PerceptionSphereRipples.hlsl"

            // 약한 충격 파동이 구 모양으로 퍼진 전선이 지나간 뒤 잔상 동안 흐려지는 세기를 모두 더해 반환한다. 겹치면 더 밝아진다.
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
                    // 걸음 파동과 같은 감속 확산식으로 전선이 이 지점을 지난 시각을 구한다.
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

            // 적 파동이 배경을 밝히는 정도를 파동 바닥에서의 높이로 줄인다. 대나무가 기둥째 밝혀지지 않게 한다.
            float HiddenSceneryHeightFade(float3 position)
            {
                float height = position.y - _OutlineHiddenSceneryHeight.x;
                return 1.0 - smoothstep(_OutlineHiddenSceneryHeight.y,
                    _OutlineHiddenSceneryHeight.y + max(_OutlineHiddenSceneryHeight.z, 0.0001), height);
            }

            // 걸음 파동이 남긴 XZ 잔상과 이동 시야원의 구형 강조로 현재 공개 정도를 반환한다.
            // isScenery가 1이면 적 파동 잔상만 높이에 따라 흐리게 한다.
            float RevealMapVisibility(float3 position, float isScenery)
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
                    if (isScenery > 0.5)
                        hiddenVisibility *= HiddenSceneryHeightFade(position);
                    if (hiddenVisibility > 0.0)
                        visibility = max(visibility, hiddenVisibility * (1.0 - OwnerExclusion(position)));
                }
                // 구형 강조는 중심에서 가장자리로 갈수록 흐려진다.
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

            // 영역 반경의 안쪽 경계부터 가장자리까지 공간 가시성을 부드럽게 줄인다.
            float RegionVisibility(float3 position, float3 center, float radius)
            {
                if (radius <= 0.0)
                    return 0.0;
                float innerRadius = max(0.0, radius - max(_OutlineEdgeFade, 0.0001));
                return 1.0 - smoothstep(innerRadius, radius, distance(position, center));
            }

            // 현재 인지를 선 색상으로 표시하고 일반 지형에만 누적 기억 색상을 합성한다.
            // isScenery가 1이면 배경 표면으로 보고 적 파동 공개에 높이 제한을 건다.
            // forced.b가 1이면 세기 조절 표면이다. forced.g가 0이면 파동으로 밝혀진 정도에 forced.r을 곱하고, 1이면 파동을 무시하고 forced.r만 쓴다.
            float3 PerceptionColor(float2 uv, float rawDepth, float hasSurface, float includeMemory, float isScenery,
                float4 forced)
            {
                if (hasSurface == 0.0)
                    return 0.0;
                if (_UsePerceptionMask < 0.5)
                    return _LineColor.rgb * _LineColor.a;

                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                float3 position = ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
                float visibility = RegionVisibility(
                    position, _OutlineObserverRegion.xyz, _OutlineObserverRegion.w);
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
                    float soundVisibility = RegionVisibility(
                        position, _OutlineSoundRegions[i].xyz, revealRadius) * timeVisibility;
                    visibility = max(visibility, soundVisibility);
                }
                // 보상 파동은 높이를 무시하고 C의 대상 판정과 같은 수평거리로 공개한다. 구형 보스 파동만 3D 거리를 쓴다.
                float pulseHeight = _OutlinePulseSpherical > 0.5 ? 1.0 : 0.0;
                float pulseVisibility = RegionVisibility(
                    float3(position.x, position.y * pulseHeight, position.z),
                    float3(_OutlinePulseRegion.x, _OutlinePulseRegion.y * pulseHeight, _OutlinePulseRegion.z),
                    _OutlinePulseRegion.w) * _OutlinePulseVisibility;
                visibility = max(visibility, pulseVisibility);
                visibility = max(visibility, RevealMapVisibility(position, isScenery));
                visibility = max(visibility, ImpactRippleVisibility(position));
                visibility = max(visibility, SphereRippleVisibility(position, _OutlinePerceptionTime, _OutlineEdgeFade));
                if (_OutlineRainRevealEnabled > 0.5)
                    visibility = max(visibility,
                        saturate(SAMPLE_TEXTURE2D_LOD(_OutlineRainReveal, sampler_PointClamp, uv, 0).r));
                if (forced.b > 0.5)
                    visibility = lerp(visibility * forced.r, forced.r, forced.g);
                float memoryVisibility = 0.0;
                if (includeMemory > 0.5 && _OutlineMemoryEnabled > 0.5)
                {
                    float2 memoryUv = (position.xz - _OutlineMemoryBounds.xy) / _OutlineMemoryBounds.zw;
                    if (all(memoryUv >= 0.0) && all(memoryUv <= 1.0))
                        memoryVisibility = SAMPLE_TEXTURE2D_LOD(_OutlineTerrainMemory,
                            sampler_OutlineTerrainMemory, memoryUv, 0).r;
                }
                float3 memoryColor = _OutlineMemoryColor.rgb * _OutlineMemoryColor.a * memoryVisibility;
                // 파동이 밝힌 만큼만 선 색을 파동 색조로 바꿔 보스 반격 파동 안의 윤곽을 붉게 보인다.
                float3 lineColor = lerp(_LineColor.rgb * _LineColor.a, _OutlinePulseTint.rgb,
                    saturate(pulseVisibility) * _OutlinePulseTint.a);
                return memoryColor * (1.0 - visibility) + lineColor * visibility;
            }

            // 배경 표면은 상시 표시하지 않고 현재 인지로만 드러내며 지형 기억에는 남기지 않는다.
            // 세기 조절 표면도 배경처럼 지형 기억에 남기지 않는다.
            float3 SurfaceColor(float2 uv, float rawDepth, float hasSurface)
            {
                if (hasSurface == 0.0)
                    return PerceptionColor(uv, rawDepth, hasSurface, 1.0, 0.0, 0.0);
                float4 forced = SAMPLE_TEXTURE2D_X(_OutlineForcedReveal, sampler_PointClamp, uv);
                if (forced.b > 0.5
                    || SAMPLE_TEXTURE2D_X(_OutlineSceneryMask, sampler_PointClamp, uv).r > 0.5)
                    return PerceptionColor(uv, rawDepth, hasSurface, 0.0, 1.0, forced);
                return PerceptionColor(uv, rawDepth, hasSurface, 1.0, 0.0, forced);
            }

            // 위협 대상이 덮는 화면 비율을 반환한다.
            float ThreatCoverage(float2 uv)
            {
                return SAMPLE_TEXTURE2D_X(_OutlineThreatMask, sampler_LinearClamp, uv).r;
            }

            // 깊이의 배경값을 구분하여 하늘의 노멀로 생기는 선을 제외한다.
            float HasSurface(float depth)
            {
                #if UNITY_REVERSED_Z
                    return depth > 0.000001;
                #else
                    return depth < 0.999999;
                #endif
            }

            // 원근 및 직교 카메라의 장치 깊이를 같은 눈 공간 거리로 변환한다.
            float EyeDepth(float rawDepth)
            {
                if (unity_OrthoParams.w > 0.5)
                {
                    #if UNITY_REVERSED_Z
                        return lerp(_ProjectionParams.y, _ProjectionParams.z, 1.0 - rawDepth);
                    #else
                        return lerp(_ProjectionParams.y, _ProjectionParams.z, rawDepth);
                    #endif
                }
                return LinearEyeDepth(rawDepth, _ZBufferParams);
            }

            // 화면 좌표와 장치 깊이로 표면의 월드 위치를 복원한다.
            float3 SurfacePosition(float2 uv, float rawDepth)
            {
                #if !UNITY_REVERSED_Z
                    rawDepth = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, rawDepth);
                #endif
                return ComputeWorldSpacePosition(uv, rawDepth, UNITY_MATRIX_I_VP);
            }

            // 표면 평면에서 벗어난 거리와 노멀 차이로 실제 경계 강도를 반환한다.
            float3 EdgeBetween(float2 firstUv, float2 secondUv)
            {
                float firstRawDepth = SampleSceneDepth(firstUv);
                float secondRawDepth = SampleSceneDepth(secondUv);
                float firstSurface = HasSurface(firstRawDepth);
                float secondSurface = HasSurface(secondRawDepth);
                if (firstSurface != secondSurface)
                {
                    return max(
                        SurfaceColor(firstUv, firstRawDepth, firstSurface),
                        SurfaceColor(secondUv, secondRawDepth, secondSurface));
                }
                if (firstSurface == 0.0)
                    return 0.0;

                float firstDepth = EyeDepth(firstRawDepth);
                float secondDepth = EyeDepth(secondRawDepth);
                float3 firstNormal = SampleSceneNormals(firstUv);
                float3 secondNormal = SampleSceneNormals(secondUv);
                float3 surfaceDelta = SurfacePosition(secondUv, secondRawDepth)
                    - SurfacePosition(firstUv, firstRawDepth);
                // 같은 평면의 원근 깊이 변화는 제외하고 표면을 벗어난 간격만 검출한다.
                float planeSeparation = max(abs(dot(surfaceDelta, firstNormal)),
                    abs(dot(surfaceDelta, secondNormal)));
                float depthDifference = planeSeparation
                    / max(min(firstDepth, secondDepth), 0.0001);
                float normalDifference = length(firstNormal - secondNormal);

                float edgeStrength = max(
                    depthDifference * _DepthSensitivity,
                    normalDifference * _NormalSensitivity);
                float edge = smoothstep(1.0, 1.25, edgeStrength);
                if (edge <= 0.0)
                    return 0.0;
                return edge * max(
                    SurfaceColor(firstUv, firstRawDepth, firstSurface),
                    SurfaceColor(secondUv, secondRawDepth, secondSurface));
            }

            // 선택 VFX의 실제 RGB 기여에서 모양을 검출하고 보이지 않는 알파 면은 제외한다.
            float TransparentCoverage(float2 uv)
            {
                float4 source = SAMPLE_TEXTURE2D_X(_OutlineOpaqueColor, sampler_LinearClamp, uv);
                float difference = max(abs(source.r), max(abs(source.g), abs(source.b)));
                float depth = SAMPLE_TEXTURE2D_X(_OutlineTransparentDepth, sampler_PointClamp, uv).r;
                return smoothstep(_TransparentThreshold, _TransparentThreshold * 2.0, difference) * HasSurface(depth);
            }

            // 보이는 RGB 모양의 경계만 표시하며 기하 깊이는 인지 위치 계산에만 사용한다.
            float3 TransparentEdgeBetween(float2 firstUv, float2 secondUv)
            {
                float first = TransparentCoverage(firstUv);
                float second = TransparentCoverage(secondUv);
                float firstDepth = SAMPLE_TEXTURE2D_X(_OutlineTransparentDepth, sampler_PointClamp, firstUv).r;
                float secondDepth = SAMPLE_TEXTURE2D_X(_OutlineTransparentDepth, sampler_PointClamp, secondUv).r;
                float edge = saturate(abs(first - second) * 1.5);
                return edge * max(PerceptionColor(firstUv, firstDepth, first, 0.0, 0.0, 0.0),
                    PerceptionColor(secondUv, secondDepth, second, 0.0, 0.0, 0.0));
            }

            // 인지 예외 VFX만 별도로 검출하여 일반 VFX의 색상과 깊이가 섞이지 않게 한다.
            float AlwaysVisibleCoverage(float2 uv)
            {
                float3 source = abs(SAMPLE_TEXTURE2D_X(
                    _OutlineAlwaysVisibleVfxColor, sampler_LinearClamp, uv).rgb);
                return smoothstep(_TransparentThreshold, _TransparentThreshold * 2.0,
                    max(source.r, max(source.g, source.b)));
            }

            // 외형 점유와 대상 번호가 바뀌는 위치에서 개별 위협의 경계를 검출한다.
            float PresentationBoundary(float4 first, float4 second)
            {
                float coverage = abs(first.r - second.r);
                float identity = step(0.5, abs(first.b - second.b)) * first.r * second.r;
                return max(coverage, identity);
            }

            // 경계 주변에서 가장 가까운 위협의 색상을 골라 먼 대상 색이 앞쪽 선에 섞이지 않게 한다.
            void SelectPresentation(float2 uv, inout float4 selectedColor, inout float4 selectedData)
            {
                float4 candidate = SAMPLE_TEXTURE2D_X(_ThreatPresentationData, sampler_PointClamp, uv);
                if (candidate.r > 0.5 && (selectedData.r < 0.5 || candidate.a < selectedData.a))
                {
                    selectedData = candidate;
                    selectedColor = SAMPLE_TEXTURE2D_X(_ThreatPresentationColor, sampler_PointClamp, uv);
                }
            }

            // 대상별 색상·거리 가시성과 내부 채움을 기존 월드 결과에 한 번 합성한다.
            float3 ApplyPresentation(float3 scene, float2 uv, float2 firstUv, float2 secondUv,
                float2 thirdUv, float2 fourthUv)
            {
                float4 centerData = SAMPLE_TEXTURE2D_X(_ThreatPresentationData, sampler_PointClamp, uv);
                float4 firstData = SAMPLE_TEXTURE2D_X(_ThreatPresentationData, sampler_PointClamp, firstUv);
                float4 secondData = SAMPLE_TEXTURE2D_X(_ThreatPresentationData, sampler_PointClamp, secondUv);
                float4 thirdData = SAMPLE_TEXTURE2D_X(_ThreatPresentationData, sampler_PointClamp, thirdUv);
                float4 fourthData = SAMPLE_TEXTURE2D_X(_ThreatPresentationData, sampler_PointClamp, fourthUv);
                float outline = saturate(max(PresentationBoundary(firstData, secondData),
                    PresentationBoundary(thirdData, fourthData)) * 1.5);
                float4 selectedData = centerData;
                float4 selectedColor = SAMPLE_TEXTURE2D_X(_ThreatPresentationColor, sampler_PointClamp, uv);
                SelectPresentation(firstUv, selectedColor, selectedData);
                SelectPresentation(secondUv, selectedColor, selectedData);
                SelectPresentation(thirdUv, selectedColor, selectedData);
                SelectPresentation(fourthUv, selectedColor, selectedData);
                float opacity = saturate(selectedColor.a) * saturate(max(outline, centerData.r * selectedData.g));
                return lerp(scene, selectedColor.rgb, opacity);
            }

            #define MAX_GROUND_MARKS 8
            #define GROUND_MARK_HEIGHT_TOLERANCE 0.3
            int _OutlineGroundMarkCount;
            // xy: 시작 XZ, zw: 끝 XZ
            float4 _OutlineGroundMarkSegments[MAX_GROUND_MARKS];
            // x: 땅 높이, y: 반폭, z: 세기, w: 양 끝 흐림 비율
            float4 _OutlineGroundMarkParams[MAX_GROUND_MARKS];
            half4 _OutlineGroundMarkColor;

            float _OutlineGroundGuideEnabled;
            float _OutlineGroundGuideHeight;
            float _OutlineGroundGuideHeightTolerance;
            float _OutlineGroundGuideNearDistance;
            float _OutlineGroundGuideFarDistance;
            half4 _OutlineGroundGuideFillColor;
            half4 _OutlineGroundGuideEdgeColor;

            // 화면 픽셀의 표면이 땅 흔적 선분 위에 있으면 흔적 색을 반환한다. 파동과 상관없이 그리며 장면 깊이로 가려진다.
            float3 GroundMarkColor(float2 uv)
            {
                if (_OutlineGroundMarkCount <= 0)
                    return 0.0;
                float rawDepth = SampleSceneDepth(uv);
                if (HasSurface(rawDepth) == 0.0)
                    return 0.0;
                float3 position = SurfacePosition(uv, rawDepth);
                float coverage = 0.0;
                [loop]
                for (int i = 0; i < _OutlineGroundMarkCount; i++)
                {
                    float4 segment = _OutlineGroundMarkSegments[i];
                    float4 markParams = _OutlineGroundMarkParams[i];
                    if (abs(position.y - markParams.x) > GROUND_MARK_HEIGHT_TOLERANCE)
                        continue;
                    float2 along = segment.zw - segment.xy;
                    float progress = saturate(dot(position.xz - segment.xy, along) / max(dot(along, along), 0.0001));
                    float distanceFromLine = distance(position.xz, segment.xy + along * progress);
                    // 가장자리와 양 끝을 부드럽게 흐려 계단이 지지 않게 한다.
                    float side = 1.0 - smoothstep(markParams.y * 0.5, markParams.y, distanceFromLine);
                    float ends = smoothstep(0.0, max(markParams.w, 0.0001), progress)
                        * smoothstep(0.0, max(markParams.w, 0.0001), 1.0 - progress);
                    coverage = max(coverage, side * ends * markParams.z);
                }
                return _OutlineGroundMarkColor.rgb * _OutlineGroundMarkColor.a * coverage;
            }

            // 실제 깊이와 위쪽 노멀을 가진 지면만 안내 대상으로 삼는다.
            float GroundGuideCoverage(float2 uv)
            {
                float rawDepth = SampleSceneDepth(uv);
                if (HasSurface(rawDepth) == 0.0)
                    return 0.0;
                float3 position = SurfacePosition(uv, rawDepth);
                float3 normal = SampleSceneNormals(uv);
                return step(abs(position.y - _OutlineGroundGuideHeight),
                    _OutlineGroundGuideHeightTolerance) * step(0.8, normal.y);
            }

            // 멀리 있는 바닥을 옅게 채우고 지면이 하늘에 닿는 곳에만 가는 선을 더한다.
            float3 GroundGuideColor(float2 uv, float2 firstUv, float2 secondUv,
                float2 thirdUv, float2 fourthUv)
            {
                if (_OutlineGroundGuideEnabled < 0.5)
                    return 0.0;
                float rawDepth = SampleSceneDepth(uv);
                float ground = GroundGuideCoverage(uv);
                float distanceFade = smoothstep(_OutlineGroundGuideNearDistance,
                    _OutlineGroundGuideFarDistance, EyeDepth(rawDepth));
                float3 fill = _OutlineGroundGuideFillColor.rgb
                    * _OutlineGroundGuideFillColor.a * ground * distanceFade;
                float firstGround = GroundGuideCoverage(firstUv);
                float secondGround = GroundGuideCoverage(secondUv);
                float thirdGround = GroundGuideCoverage(thirdUv);
                float fourthGround = GroundGuideCoverage(fourthUv);
                float firstSky = 1.0 - HasSurface(SampleSceneDepth(firstUv));
                float secondSky = 1.0 - HasSurface(SampleSceneDepth(secondUv));
                float thirdSky = 1.0 - HasSurface(SampleSceneDepth(thirdUv));
                float fourthSky = 1.0 - HasSurface(SampleSceneDepth(fourthUv));
                float border = max(max(firstGround * secondSky, secondGround * firstSky),
                    max(thirdGround * fourthSky, fourthGround * thirdSky));
                float3 borderColor = _OutlineGroundGuideEdgeColor.rgb
                    * _OutlineGroundGuideEdgeColor.a * border;
                return max(fill, borderColor);
            }

            // 불투명 경계와 투명 이펙트 및 개별 위협을 합성하고 나머지 면과 배경은 검게 출력한다.
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                float2 texelSize = rcp(_ScaledScreenParams.xy);
                float lowerOffset = floor(_LineWidth * 0.5);
                float upperOffset = ceil(_LineWidth * 0.5);
                float2 uv = input.texcoord;
                float2 firstUv = saturate(uv + float2(-lowerOffset, -lowerOffset) * texelSize);
                float2 secondUv = saturate(uv + float2(upperOffset, upperOffset) * texelSize);
                float2 thirdUv = saturate(uv + float2(-lowerOffset, upperOffset) * texelSize);
                float2 fourthUv = saturate(uv + float2(upperOffset, -lowerOffset) * texelSize);
                float3 edge = max(EdgeBetween(firstUv, secondUv), EdgeBetween(thirdUv, fourthUv));
                float beastBodyCoverage = max(
                    SAMPLE_TEXTURE2D_X(_OutlineBeastOutlineExclusionMask, sampler_PointClamp, uv).r,
                    max(max(
                        SAMPLE_TEXTURE2D_X(_OutlineBeastOutlineExclusionMask, sampler_PointClamp, firstUv).r,
                        SAMPLE_TEXTURE2D_X(_OutlineBeastOutlineExclusionMask, sampler_PointClamp, secondUv).r),
                        max(
                            SAMPLE_TEXTURE2D_X(_OutlineBeastOutlineExclusionMask, sampler_PointClamp, thirdUv).r,
                            SAMPLE_TEXTURE2D_X(_OutlineBeastOutlineExclusionMask, sampler_PointClamp, fourthUv).r)));
                edge *= 1.0 - beastBodyCoverage;
                edge *= 1.0 - TransparentCoverage(uv);
                float3 transparentEdge = max(TransparentEdgeBetween(firstUv, secondUv),
                    TransparentEdgeBetween(thirdUv, fourthUv));
                float3 warningCenterMask = SAMPLE_TEXTURE2D_X(
                    _OutlineMeleeWarningMask, sampler_PointClamp, uv).rgb;
                float3 warningFirstMask = SAMPLE_TEXTURE2D_X(
                    _OutlineMeleeWarningMask, sampler_PointClamp, firstUv).rgb;
                float3 warningSecondMask = SAMPLE_TEXTURE2D_X(
                    _OutlineMeleeWarningMask, sampler_PointClamp, secondUv).rgb;
                float3 warningThirdMask = SAMPLE_TEXTURE2D_X(
                    _OutlineMeleeWarningMask, sampler_PointClamp, thirdUv).rgb;
                float3 warningFourthMask = SAMPLE_TEXTURE2D_X(
                    _OutlineMeleeWarningMask, sampler_PointClamp, fourthUv).rgb;

                float warningAmount = saturate(max(
                    warningCenterMask.r,
                    max(max(warningFirstMask.r, warningSecondMask.r),
                        max(warningThirdMask.r, warningFourthMask.r))));
                // G 채널 대상(쌍둥이 늑대 덮치기)은 파동과 상관없이 모양의 경계를 붉은 테두리로 그린다.
                float alwaysWarningEdge = saturate(max(
                    abs(warningFirstMask.g - warningSecondMask.g),
                    abs(warningThirdMask.g - warningFourthMask.g)) * 1.5);

                float transparentBrightness = max(transparentEdge.r, max(transparentEdge.g, transparentEdge.b));

            // 연기에서 검출된 테두리 픽셀에만 전조 색을 적용한다.
                edge = max(edge, transparentEdge);
                float edgeBrightness = max(edge.r, max(edge.g, edge.b));
                float outlineTint = warningAmount * step(0.0001, transparentBrightness);
                edge = lerp(edge, _MeleeWarningColor.rgb * _MeleeWarningColor.a * edgeBrightness, outlineTint);
                edge *= 1.0 - AlwaysVisibleCoverage(uv);
                float foregroundEdge = saturate(max(
                    abs(AlwaysVisibleCoverage(firstUv) - AlwaysVisibleCoverage(secondUv)),
                    abs(AlwaysVisibleCoverage(thirdUv) - AlwaysVisibleCoverage(fourthUv))) * 1.5);
                edge = max(edge, foregroundEdge * _LineColor.rgb * _LineColor.a);
                edge = max(edge, GroundMarkColor(uv));
                edge = max(edge, GroundGuideColor(uv, firstUv, secondUv, thirdUv, fourthUv));
                edge = lerp(edge, _MeleeWarningColor.rgb * _MeleeWarningColor.a, alwaysWarningEdge);
                // 눈 섬광 전용 B 채널은 기본 윤곽선의 약 세 배 간격으로 표본을 읽어 두꺼운 하늘색 경계를 그린다.
                float glintLowerOffset = floor(_LineWidth * 1.5);
                float glintUpperOffset = ceil(_LineWidth * 1.5);
                float2 glintFirstUv = saturate(uv + float2(-glintLowerOffset, -glintLowerOffset) * texelSize);
                float2 glintSecondUv = saturate(uv + float2(glintUpperOffset, glintUpperOffset) * texelSize);
                float2 glintThirdUv = saturate(uv + float2(-glintLowerOffset, glintUpperOffset) * texelSize);
                float2 glintFourthUv = saturate(uv + float2(glintUpperOffset, -glintLowerOffset) * texelSize);
                float glintFirstMask = SAMPLE_TEXTURE2D_X(_OutlineMeleeWarningMask, sampler_PointClamp, glintFirstUv).b;
                float glintSecondMask = SAMPLE_TEXTURE2D_X(_OutlineMeleeWarningMask, sampler_PointClamp, glintSecondUv).b;
                float glintThirdMask = SAMPLE_TEXTURE2D_X(_OutlineMeleeWarningMask, sampler_PointClamp, glintThirdUv).b;
                float glintFourthMask = SAMPLE_TEXTURE2D_X(_OutlineMeleeWarningMask, sampler_PointClamp, glintFourthUv).b;
                float glintEdge = saturate(max(
                    abs(glintFirstMask - glintSecondMask),
                    abs(glintThirdMask - glintFourthMask)) * 1.5);
                edge = lerp(edge, _BeastGlintColor.rgb * _BeastGlintColor.a, glintEdge);
                // A 채널 눈 섬광(일반 Beast)은 같은 방식으로 표본 간격만 _BeastThickGlintWidthScale배 넓혀 더 두꺼운 경계를 그린다.
                float thickGlintLowerOffset = floor(_LineWidth * 1.5 * _BeastThickGlintWidthScale);
                float thickGlintUpperOffset = ceil(_LineWidth * 1.5 * _BeastThickGlintWidthScale);
                float2 thickGlintFirstUv = saturate(uv + float2(-thickGlintLowerOffset, -thickGlintLowerOffset) * texelSize);
                float2 thickGlintSecondUv = saturate(uv + float2(thickGlintUpperOffset, thickGlintUpperOffset) * texelSize);
                float2 thickGlintThirdUv = saturate(uv + float2(-thickGlintLowerOffset, thickGlintUpperOffset) * texelSize);
                float2 thickGlintFourthUv = saturate(uv + float2(thickGlintUpperOffset, -thickGlintLowerOffset) * texelSize);
                float thickGlintEdge = saturate(max(
                    abs(SAMPLE_TEXTURE2D_X(_OutlineMeleeWarningMask, sampler_PointClamp, thickGlintFirstUv).a
                        - SAMPLE_TEXTURE2D_X(_OutlineMeleeWarningMask, sampler_PointClamp, thickGlintSecondUv).a),
                    abs(SAMPLE_TEXTURE2D_X(_OutlineMeleeWarningMask, sampler_PointClamp, thickGlintThirdUv).a
                        - SAMPLE_TEXTURE2D_X(_OutlineMeleeWarningMask, sampler_PointClamp, thickGlintFourthUv).a)) * 1.5);
                edge = lerp(edge, _BeastGlintColor.rgb * _BeastGlintColor.a, thickGlintEdge);
                // 위협 실루엣은 경계를 선명하게, 내부는 반투명하게 다른 모든 선 위에 덮는다.
                float threatFirst = ThreatCoverage(firstUv);
                float threatSecond = ThreatCoverage(secondUv);
                float threatThird = ThreatCoverage(thirdUv);
                float threatFourth = ThreatCoverage(fourthUv);
                float threatEdge = saturate(max(abs(threatFirst - threatSecond),
                    abs(threatThird - threatFourth)) * 1.5);
                float threatFill = max(ThreatCoverage(uv),
                    max(max(threatFirst, threatSecond), max(threatThird, threatFourth)));
                float fillOpacity = lerp(_ThreatFillOpacity, _ThreatHighlightFillOpacity, saturate(_ThreatSilhouetteWeight));
                float threat = saturate(max(threatEdge, threatFill * fillOpacity));
                edge = lerp(edge, _ThreatColor.rgb * _ThreatColor.a, threat);
                edge = ApplyPresentation(edge, uv, firstUv, secondUv, thirdUv, fourthUv);
                return half4(edge, 1.0);
            }
            ENDHLSL
        }
    }
}

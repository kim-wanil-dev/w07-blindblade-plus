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
            CBUFFER_END

            TEXTURE2D_X(_OutlineOpaqueColor);
            TEXTURE2D_X(_OutlineAlwaysVisibleVfxColor);
            TEXTURE2D_X(_OutlineTransparentDepth);
            TEXTURE2D_X(_OutlineSceneryMask);
            TEXTURE2D_X(_OutlineThreatMask);
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
            TEXTURE2D(_OutlineTerrainMemory);
            SAMPLER(sampler_OutlineTerrainMemory);
            float4 _OutlineMemoryBounds;
            float4 _OutlineMemoryColor;
            float _OutlineMemoryEnabled;

            // 영역 반경의 안쪽 경계부터 가장자리까지 공간 가시성을 부드럽게 줄인다.
            float RegionVisibility(float3 position, float3 center, float radius)
            {
                if (radius <= 0.0)
                    return 0.0;
                float innerRadius = max(0.0, radius - max(_OutlineEdgeFade, 0.0001));
                return 1.0 - smoothstep(innerRadius, radius, distance(position, center));
            }

            // 현재 인지를 선 색상으로 표시하고 일반 지형에만 누적 기억 색상을 합성한다.
            float3 PerceptionColor(float2 uv, float rawDepth, float hasSurface, float includeMemory)
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
                float memoryVisibility = 0.0;
                if (includeMemory > 0.5 && _OutlineMemoryEnabled > 0.5)
                {
                    float2 memoryUv = (position.xz - _OutlineMemoryBounds.xy) / _OutlineMemoryBounds.zw;
                    if (all(memoryUv >= 0.0) && all(memoryUv <= 1.0))
                        memoryVisibility = SAMPLE_TEXTURE2D_LOD(_OutlineTerrainMemory,
                            sampler_OutlineTerrainMemory, memoryUv, 0).r;
                }
                float3 memoryColor = _OutlineMemoryColor.rgb * _OutlineMemoryColor.a * memoryVisibility;
                return memoryColor * (1.0 - visibility) + _LineColor.rgb * _LineColor.a * visibility;
            }

            // 항상 보이는 배경 표면은 인지 범위·편집 모드와 무관하게 고정된 배경 선 색상을 사용한다.
            float3 SurfaceColor(float2 uv, float rawDepth, float hasSurface)
            {
                if (hasSurface != 0.0
                    && SAMPLE_TEXTURE2D_X(_OutlineSceneryMask, sampler_PointClamp, uv).r > 0.5)
                    return _SceneryColor.rgb * _SceneryColor.a;
                return PerceptionColor(uv, rawDepth, hasSurface, 1.0);
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
                return edge * max(PerceptionColor(firstUv, firstDepth, first, 0.0),
                    PerceptionColor(secondUv, secondDepth, second, 0.0));
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
                edge *= 1.0 - TransparentCoverage(uv);
                float3 transparentEdge = max(TransparentEdgeBetween(firstUv, secondUv),
                    TransparentEdgeBetween(thirdUv, fourthUv));
                edge = max(edge, transparentEdge);
                edge *= 1.0 - AlwaysVisibleCoverage(uv);
                float foregroundEdge = saturate(max(
                    abs(AlwaysVisibleCoverage(firstUv) - AlwaysVisibleCoverage(secondUv)),
                    abs(AlwaysVisibleCoverage(thirdUv) - AlwaysVisibleCoverage(fourthUv))) * 1.5);
                edge = max(edge, foregroundEdge * _LineColor.rgb * _LineColor.a);
                // 위협 실루엣은 경계를 선명하게, 내부는 반투명하게 다른 모든 선 위에 덮는다.
                float threatFirst = ThreatCoverage(firstUv);
                float threatSecond = ThreatCoverage(secondUv);
                float threatThird = ThreatCoverage(thirdUv);
                float threatFourth = ThreatCoverage(fourthUv);
                float threatEdge = saturate(max(abs(threatFirst - threatSecond),
                    abs(threatThird - threatFourth)) * 1.5);
                float threatFill = max(ThreatCoverage(uv),
                    max(max(threatFirst, threatSecond), max(threatThird, threatFourth)));
                float threat = saturate(max(threatEdge, threatFill * _ThreatFillOpacity));
                edge = lerp(edge, _ThreatColor.rgb * _ThreatColor.a, threat);
                edge = ApplyPresentation(edge, uv, firstUv, secondUv, thirdUv, fourthUv);
                return half4(edge, 1.0);
            }
            ENDHLSL
        }
    }
}

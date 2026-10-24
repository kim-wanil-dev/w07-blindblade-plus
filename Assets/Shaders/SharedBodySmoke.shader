Shader "Hidden/BlindBlade/SharedBodySmoke"
{
    Properties
    {
        _BaseMap ("Base Map", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1, 1, 1, 1)
        _DepthBlendOp ("Depth Blend Operation", Float) = 4
        _TransparentThreshold ("Transparent Threshold", Float) = 0.015
    }

    SubShader
    {
        Tags { "RenderPipeline" = "UniversalPipeline" }

        HLSLINCLUDE
        #pragma target 4.5
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        TEXTURE2D(_BaseMap);
        SAMPLER(sampler_BaseMap);

        CBUFFER_START(UnityPerMaterial)
            float4 _BaseMap_ST;
            half4 _BaseColor;
            float _TransparentThreshold;
        CBUFFER_END

        // 입자마다 (로컬 위치, 현재 크기), 현재 색, (회전 라디안) 세 칸이며 템플릿 순서로 이어진다.
        StructuredBuffer<float4> _SharedSmokeParticles;
        // 적마다 몸 연기 행렬 세 행과 (템플릿 번호, 전조 세기) 한 칸이다.
        StructuredBuffer<float4> _SharedSmokeAnchors;
        int _SharedSmokeParticlesPerTemplate;
        float _SharedSmokeMaxParticleSize;

        struct Varyings
        {
            float4 positionCS : SV_POSITION;
            float2 uv : TEXCOORD0;
            half4 color : COLOR;
            float warning : TEXCOORD1;
        };

        static const float2 CORNERS[6] =
        {
            float2(-0.5, -0.5), float2(-0.5, 0.5), float2(0.5, 0.5),
            float2(-0.5, -0.5), float2(0.5, 0.5), float2(0.5, -0.5)
        };

        // 인스턴스는 적, 정점 여섯 개는 템플릿 입자 하나의 View 정렬 빌보드다.
        Varyings SmokeVert(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
        {
            Varyings output = (Varyings)0;
            uint particle = vertexID / 6;
            float2 corner = CORNERS[vertexID % 6];
            uint anchor = instanceID * 4;
            float4 row0 = _SharedSmokeAnchors[anchor];
            float4 row1 = _SharedSmokeAnchors[anchor + 1];
            float4 row2 = _SharedSmokeAnchors[anchor + 2];
            float4 info = _SharedSmokeAnchors[anchor + 3];
            uint index = ((uint)info.x * (uint)_SharedSmokeParticlesPerTemplate + particle) * 3;
            float4 shape = _SharedSmokeParticles[index];
            float4 color = _SharedSmokeParticles[index + 1];
            float rotation = _SharedSmokeParticles[index + 2].x;

            float4 local = float4(shape.xyz, 1.0);
            float3 center = float3(dot(row0, local), dot(row1, local), dot(row2, local));
            float scale = max(length(float3(row0.x, row1.x, row2.x)),
                max(length(float3(row0.y, row1.y, row2.y)), length(float3(row0.z, row1.z, row2.z))));
            float size = shape.w * scale;
            // Max Particle Size처럼 화면 높이 대비 크기를 제한한다.
            float viewDepth = max(-TransformWorldToView(center).z, 0.0);
            size = min(size, _SharedSmokeMaxParticleSize * 2.0 * viewDepth / abs(UNITY_MATRIX_P._m11));

            float s;
            float c;
            sincos(rotation, s, c);
            float2 rotated = float2(corner.x * c - corner.y * s, corner.x * s + corner.y * c);
            float3 right = UNITY_MATRIX_V._m00_m01_m02;
            float3 up = UNITY_MATRIX_V._m10_m11_m12;
            float3 positionWS = center + (right * rotated.x + up * rotated.y) * size;

            output.positionCS = TransformWorldToHClip(positionWS);
            output.uv = corner + 0.5;
            output.color = color;
            output.warning = info.y;
            return output;
        }
        ENDHLSL

        Pass
        {
            // URP Particles/Unlit 알파 합성과 같은 텍스처 × 기본색 × 입자색 출력이다.
            Name "SharedBodySmokeColor"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex SmokeVert
            #pragma fragment ColorFrag

            half4 ColorFrag(Varyings input) : SV_Target
            {
                float2 uv = input.uv * _BaseMap_ST.xy + _BaseMap_ST.zw;
                return SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, uv) * _BaseColor * input.color;
            }
            ENDHLSL
        }

        Pass
        {
            // OutlineTransparentDepth와 같이 불투명 표면 앞의 최근접 깊이를 누적한다.
            Name "SharedBodySmokeDepth"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One One
            BlendOp [_DepthBlendOp]
            ColorMask R

            HLSLPROGRAM
            #pragma vertex SmokeVert
            #pragma fragment DepthFrag

            float4 DepthFrag(Varyings input) : SV_Target
            {
                return float4(input.positionCS.z, 0, 0, 1);
            }
            ENDHLSL
        }

        Pass
        {
            // OutlineMeleeWarningMask의 파티클 경로와 같은 모양에 적별 전조 세기를 곱해 R에 기록한다.
            Name "SharedBodySmokeWarning"
            ZWrite Off
            ZTest LEqual
            Cull Off
            Blend One One
            BlendOp Max
            ColorMask RGBA

            HLSLPROGRAM
            #pragma vertex SmokeVert
            #pragma fragment WarningFrag

            half4 WarningFrag(Varyings input) : SV_Target
            {
                half4 texel = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half coverage = texel.a * input.color.a * _BaseColor.a;
                half3 visibleRgb = texel.rgb * input.color.rgb * _BaseColor.rgb * coverage;
                half brightness = max(visibleRgb.r, max(visibleRgb.g, visibleRgb.b));
                half shape = smoothstep(0.0, max(_TransparentThreshold, 0.0001), brightness);
                return half4(shape * saturate(input.warning), 0, 0, 0);
            }
            ENDHLSL
        }
    }
}

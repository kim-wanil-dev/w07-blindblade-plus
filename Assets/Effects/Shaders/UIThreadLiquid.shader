Shader "BlindBlade/UIThreadLiquid"
{
    Properties
    {
        [PerRendererData] _MainTex ("Density Texture", 2D) = "black" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _LiquidSurface ("Liquid Surface (Level, Amplitude, Wavelength, Phase)", Vector) = (-1, 0, 1.8, 0)
        _LiquidShape ("Liquid Shape (Tilt, Softness)", Vector) = (0, 0.03, 0, 0)
        _FillColor ("Fill Color", Color) = (0.85, 0.88, 0.95, 0.08)
        _Level1Color ("Level 1 Color", Color) = (0.42, 0.45, 0.52, 0.8)
        _Level2Color ("Level 2 Color", Color) = (0.66, 0.69, 0.75, 0.9)
        _Level3Color ("Level 3 Color", Color) = (0.86, 0.88, 0.92, 0.96)
        _Level4Color ("Level 4 Color", Color) = (1, 1, 1, 1)
        _LevelThresholds ("Level Thresholds", Vector) = (0.25, 1.15, 2.1, 3.05)
        _LevelSoftness ("Level Softness", Float) = 0.06
        _ClipRect ("Clip Rect", Vector) = (-32767,-32767,32767,32767)
        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
        [Toggle(UNITY_UI_ALPHACLIP)] _UseUIAlphaClip ("Use Alpha Clip", Float) = 0
    }

    SubShader
    {
        Tags
        {
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
            "RenderType" = "Transparent"
            "PreviewType" = "Plane"
            "CanUseSpriteAtlas" = "True"
        }

        Stencil
        {
            Ref [_Stencil]
            Comp [_StencilComp]
            Pass [_StencilOp]
            ReadMask [_StencilReadMask]
            WriteMask [_StencilWriteMask]
        }

        Cull Off
        Lighting Off
        ZWrite Off
        ZTest [unity_GUIZTestMode]
        Blend SrcAlpha OneMinusSrcAlpha
        ColorMask [_ColorMask]

        Pass
        {
            CGPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #pragma multi_compile __ UNITY_UI_ALPHACLIP

            #include "UnityCG.cginc"
            #include "UnityUI.cginc"

            sampler2D _MainTex;
            fixed4 _Color;
            float4 _ClipRect;
            float4 _LiquidSurface;
            float4 _LiquidShape;
            fixed4 _FillColor;
            fixed4 _Level1Color;
            fixed4 _Level2Color;
            fixed4 _Level3Color;
            fixed4 _Level4Color;
            float4 _LevelThresholds;
            float _LevelSoftness;

            struct AppData
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
            };

            struct VertexData
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float2 uv : TEXCOORD0;
                float4 worldPosition : TEXCOORD1;
            };

            // 액체 원을 감싸는 사각형의 위치·색·텍스처 좌표를 그대로 전달한다.
            VertexData Vert(AppData input)
            {
                VertexData output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.color = input.color * _Color;
                return output;
            }

            // 겹침 수 density가 threshold를 넘은 정도를 _LevelSoftness 폭으로 부드럽게 0~1로 반환한다.
            float LevelBlend(float density, float threshold)
            {
                float softness = max(0.0001, _LevelSoftness);
                return smoothstep(threshold - softness, threshold + softness, density);
            }

            // 실이 겹친 수에 따라 단계별 색을 고르고, 물결치는 수면 위와 액체 원 밖을 경계 폭 안에서 지운다.
            fixed4 Frag(VertexData input) : SV_Target
            {
                // 밀도 텍스처에는 실 한 겹이 1/8로 더해져 있다.
                float density = tex2D(_MainTex, input.uv).r * 8.0;
                fixed4 color = _FillColor;
                color = lerp(color, _Level1Color, LevelBlend(density, _LevelThresholds.x));
                color = lerp(color, _Level2Color, LevelBlend(density, _LevelThresholds.y));
                color = lerp(color, _Level3Color, LevelBlend(density, _LevelThresholds.z));
                color = lerp(color, _Level4Color, LevelBlend(density, _LevelThresholds.w));
                color *= input.color;

                float2 local = input.uv * 2.0 - 1.0;
                float softness = max(0.0001, _LiquidShape.y);
                float wave = 0.65 * sin(UNITY_TWO_PI * (local.x / _LiquidSurface.z - _LiquidSurface.w))
                    + 0.35 * sin(UNITY_TWO_PI * (local.x / (_LiquidSurface.z * 0.57) + _LiquidSurface.w * 0.73) + 1.3);
                float surface = _LiquidSurface.x + _LiquidSurface.y * wave + _LiquidShape.x * local.x;
                color.a *= saturate((surface - local.y) / softness);
                color.a *= saturate((1.0 - length(local)) / softness);

                #ifdef UNITY_UI_CLIP_RECT
                color.a *= UnityGet2DClipping(input.worldPosition.xy, _ClipRect);
                #endif

                #ifdef UNITY_UI_ALPHACLIP
                clip(color.a - 0.001);
                #endif

                return color;
            }
            ENDCG
        }
    }
}

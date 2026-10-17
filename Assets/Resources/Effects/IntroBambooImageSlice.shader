Shader "BlindBlade/Intro Bamboo Image Slice"
{
    Properties
    {
        [PerRendererData] _MainTex ("Sprite Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _SliceSide ("Slice Side", Float) = 1
        _SliceProgress ("Slice Progress", Range(0,1)) = 0
        _SliceGlowColor ("Slice Glow Color", Color) = (1,0.8,0.4,1)
        _SliceGlowWidth ("Slice Glow Width", Float) = 2
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
            fixed4 _SliceGlowColor;
            float4 _ClipRect;
            float _SliceSide;
            float _SliceProgress;
            float _SliceGlowWidth;

            struct AppData
            {
                float4 vertex : POSITION;
                fixed4 color : COLOR;
                float4 uv : TEXCOORD0;
            };

            struct VertexData
            {
                float4 vertex : SV_POSITION;
                fixed4 color : COLOR;
                float4 uv : TEXCOORD0;
                float slicePosition : TEXCOORD1;
                float4 worldPosition : TEXCOORD2;
            };

            VertexData Vert(AppData input)
            {
                VertexData output;
                output.worldPosition = input.vertex;
                output.vertex = UnityObjectToClipPos(input.vertex);
                output.uv = input.uv;
                output.slicePosition = input.uv.z;
                output.color = input.color * _Color;
                return output;
            }

            fixed4 Frag(VertexData input) : SV_Target
            {
                clip(input.slicePosition * _SliceSide);

                fixed4 color = tex2D(_MainTex, input.uv.xy) * input.color;
                float glow = (1 - smoothstep(0, max(0.001, _SliceGlowWidth), abs(input.slicePosition)))
                    * saturate(1 - _SliceProgress * 4);
                color.rgb = lerp(color.rgb, _SliceGlowColor.rgb, glow * _SliceGlowColor.a);
                color.a *= 1 - smoothstep(0.35, 1, _SliceProgress);

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

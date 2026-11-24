// 시작 연출 "먹 한 방울"(GameIntro.Style.Drop): 화면을 덮은 한지에 먹 자국 모양 구멍을 뚫어 뒤의 시작 화면을 보여 준다.
// _Hole: xy = 구멍 가운데(화면 0~1), z = 크기(화면 높이 기준, 0 이면 구멍 없음), w = 회전(라디안).
// 구멍 바로 바깥은 먹빛 테두리(_RimColor)로 칠해 먹이 번진 자국처럼 보이게 한다.
Shader "REDBOOL/IntroHole"
{
    Properties
    {
        [PerRendererData] _MainTex ("Texture", 2D) = "white" {}
        _Color ("Tint", Color) = (1,1,1,1)
        _MaskTex ("Hole Shape", 2D) = "black" {}
        _Hole ("Hole (center xy, scale z, rotation w)", Vector) = (0.5, 0.5, 0, 0)
        _RimColor ("Rim Color", Color) = (0.05, 0.04, 0.04, 1)
        _Rim ("Rim Width", Float) = 0.14
        _Aspect ("Screen Aspect", Float) = 1.7777

        _StencilComp ("Stencil Comparison", Float) = 8
        _Stencil ("Stencil ID", Float) = 0
        _StencilOp ("Stencil Operation", Float) = 0
        _StencilWriteMask ("Stencil Write Mask", Float) = 255
        _StencilReadMask ("Stencil Read Mask", Float) = 255
        _ColorMask ("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "Queue"="Transparent" "IgnoreProjector"="True" "RenderType"="Transparent" "PreviewType"="Plane" "CanUseSpriteAtlas"="True" }
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
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float4 color : COLOR; float2 uv : TEXCOORD0; };
            struct v2f { float4 pos : SV_POSITION; fixed4 color : COLOR; float2 uv : TEXCOORD0; };

            sampler2D _MainTex; float4 _MainTex_ST;
            sampler2D _MaskTex;
            fixed4 _Color, _RimColor;
            float4 _Hole;
            float _Rim, _Aspect;

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = TRANSFORM_TEX(v.uv, _MainTex);
                o.color = v.color * _Color;
                return o;
            }

            float HoleAt (float2 p, float scale)
            {
                if (scale <= 0.0001) return 0;
                float2 d = p - _Hole.xy;
                d.x *= _Aspect;
                float s = sin(_Hole.w), c = cos(_Hole.w);
                d = float2(c * d.x - s * d.y, s * d.x + c * d.y);
                float2 uv = d / scale + 0.5;
                if (uv.x < 0 || uv.y < 0 || uv.x > 1 || uv.y > 1) return 0;
                return tex2D(_MaskTex, uv).a;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                fixed4 col = tex2D(_MainTex, i.uv) * i.color;
                float hole = HoleAt(i.uv, _Hole.z);
                float rim = saturate(HoleAt(i.uv, _Hole.z * (1 + _Rim)) - hole);
                col.rgb = lerp(col.rgb, _RimColor.rgb, saturate(rim * 1.6));
                col.a *= 1 - hole;
                return col;
            }
            ENDCG
        }
    }
}

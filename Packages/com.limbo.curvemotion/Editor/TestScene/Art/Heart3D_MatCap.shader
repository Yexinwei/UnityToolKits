// Heart3D 用的无光照 MatCap：颜色与高光全部来自 _MatCap 贴图，不依赖场景灯光，
// 放进 UI 相机（只渲染 UI 层、通常没有灯）也能正常显示立体感。
// Queue 用 Transparent，便于和 Screen Space - Camera 的 Canvas 一起按 Sorting Order 排序。
Shader "CurveMotion/Test/Heart MatCap"
{
    Properties
    {
        _MatCap ("MatCap", 2D) = "white" {}
        _Color ("Tint", Color) = (1, 1, 1, 1)
    }
    SubShader
    {
        Tags { "Queue" = "Transparent" "RenderType" = "Opaque" "IgnoreProjector" = "True" }
        Pass
        {
            ZWrite On
            ZTest LEqual
            Cull Back

            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            sampler2D _MatCap;
            fixed4 _Color;

            struct appdata
            {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float3 viewNormal : TEXCOORD0;
            };

            v2f vert (appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.viewNormal = mul((float3x3)UNITY_MATRIX_IT_MV, v.normal);
                return o;
            }

            fixed4 frag (v2f i) : SV_Target
            {
                float2 uv = normalize(i.viewNormal).xy * 0.49 + 0.5;
                return tex2D(_MatCap, uv) * _Color;
            }
            ENDCG
        }
    }
}

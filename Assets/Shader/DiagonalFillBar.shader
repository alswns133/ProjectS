Shader "Custom/DiagonalFillBar"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _FillAmount ("Fill Amount", Range(0, 1)) = 1.0
        _Angle ("Angle (degrees)", Float) = 22.0
    }

    SubShader
    {
        Tags { "Queue"="Transparent" "RenderType"="Transparent" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off
        Cull Off

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float4 pos : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            sampler2D _MainTex;
            float _FillAmount;
            float _Angle;

            v2f vert(appdata v)
            {
                v2f o;
                o.pos = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                o.color = v.color;
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {
                // 수직선 기준 22도 → tan값으로 사선 경계 계산
                float rad = _Angle * 3.14159 / 180.0;
                float tanVal = tan(rad);

                // 사선 기준 x 임계값 (uv.y에 따라 x 경계가 달라짐)
                float threshold = _FillAmount + (i.uv.y - 0.5) * tanVal;

                if (i.uv.x > threshold)
                    discard;

                fixed4 col = tex2D(_MainTex, i.uv) * i.color;
                return col;
            }
            ENDCG
        }
    }
}
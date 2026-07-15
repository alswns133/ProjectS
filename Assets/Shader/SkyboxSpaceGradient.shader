// 6면(6 Sided) 스카이박스 위에 수직 그라데이션을 가산 블렌딩하는 셰이더.
// 우주 스카이박스가 전체적으로 너무 어두워, 아래쪽(지평선 이하)에 블루 계열 빛을 더하고
// 위로 갈수록 원본(검은 우주 + 별)이 그대로 보이게 하기 위해 만들었다.
// 프로퍼티 이름은 빌트인 Skybox/6 Sided와 동일하게 유지해 기존 머티리얼의
// 텍스처 연결이 끊기지 않도록 한다.
Shader "Custom/SkyboxSpaceGradient"
{
    Properties
    {
        _Tint ("Tint Color", Color) = (0.5, 0.5, 0.5, 0.5)
        [Gamma] _Exposure ("Exposure", Range(0, 8)) = 1.0
        _Rotation ("Rotation", Range(0, 360)) = 0

        [Header(Gradient)]
        _BottomColor ("Bottom Color", Color) = (0.09, 0.22, 0.55, 1)
        _TopColor ("Top Color", Color) = (0, 0, 0, 1)
        _GradientPower ("Gradient Power", Range(0.5, 8)) = 2.5
        _GradientIntensity ("Gradient Intensity", Range(0, 2)) = 1.0

        [NoScaleOffset] _FrontTex ("Front [+Z]", 2D) = "grey" {}
        [NoScaleOffset] _BackTex ("Back [-Z]", 2D) = "grey" {}
        [NoScaleOffset] _LeftTex ("Left [+X]", 2D) = "grey" {}
        [NoScaleOffset] _RightTex ("Right [-X]", 2D) = "grey" {}
        [NoScaleOffset] _UpTex ("Up [+Y]", 2D) = "grey" {}
        [NoScaleOffset] _DownTex ("Down [-Y]", 2D) = "grey" {}
    }

    SubShader
    {
        Tags { "Queue"="Background" "RenderType"="Background" "PreviewType"="Skybox" }
        Cull Off
        ZWrite Off

        CGINCLUDE
        #include "UnityCG.cginc"

        half4 _Tint;
        half _Exposure;
        float _Rotation;
        half4 _BottomColor;
        half4 _TopColor;
        half _GradientPower;
        half _GradientIntensity;

        float3 RotateAroundYInDegrees(float3 vertex, float degrees)
        {
            float alpha = degrees * UNITY_PI / 180.0;
            float sina, cosa;
            sincos(alpha, sina, cosa);
            float2x2 m = float2x2(cosa, -sina, sina, cosa);
            return float3(mul(m, vertex.xz), vertex.y).xzy;
        }

        struct appdata_t
        {
            float4 vertex : POSITION;
            float2 texcoord : TEXCOORD0;
            UNITY_VERTEX_INPUT_INSTANCE_ID
        };

        struct v2f
        {
            float4 vertex : SV_POSITION;
            float2 texcoord : TEXCOORD0;
            float3 dir : TEXCOORD1;
            UNITY_VERTEX_OUTPUT_STEREO
        };

        v2f SkyboxVert(appdata_t v)
        {
            v2f o;
            UNITY_SETUP_INSTANCE_ID(v);
            UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
            float3 rotated = RotateAroundYInDegrees(v.vertex.xyz, _Rotation);
            o.vertex = UnityObjectToClipPos(rotated);
            o.texcoord = v.texcoord;
            // 그라데이션은 회전과 무관하게 순수한 시선 높이(y)만 사용한다.
            o.dir = v.vertex.xyz;
            return o;
        }

        half4 SkyboxFrag(v2f i, sampler2D smp, half4 smpDecode)
        {
            half4 tex = tex2D(smp, i.texcoord);
            half3 c = DecodeHDR(tex, smpDecode);
            c = c * _Tint.rgb * unity_ColorSpaceDouble.rgb;
            c *= _Exposure;

            // y = -1(바닥) -> t = 0, y = +1(천정) -> t = 1.
            // pow 로 블루가 지평선 아래쪽에 몰리게 하고 위로 갈수록 TopColor(검정)로 수렴시킨다.
            float t = saturate(normalize(i.dir).y * 0.5 + 0.5);
            half3 gradient = lerp(_BottomColor.rgb, _TopColor.rgb, pow(t, _GradientPower));

            // 가산 블렌딩이라 TopColor가 검정이면 위쪽은 원본 별 텍스처가 그대로 남는다.
            c += gradient * _GradientIntensity;
            return half4(c, 1);
        }
        ENDCG

        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _FrontTex;
            half4 _FrontTex_HDR;
            half4 frag(v2f i) : SV_Target { return SkyboxFrag(i, _FrontTex, _FrontTex_HDR); }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _BackTex;
            half4 _BackTex_HDR;
            half4 frag(v2f i) : SV_Target { return SkyboxFrag(i, _BackTex, _BackTex_HDR); }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _LeftTex;
            half4 _LeftTex_HDR;
            half4 frag(v2f i) : SV_Target { return SkyboxFrag(i, _LeftTex, _LeftTex_HDR); }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _RightTex;
            half4 _RightTex_HDR;
            half4 frag(v2f i) : SV_Target { return SkyboxFrag(i, _RightTex, _RightTex_HDR); }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _UpTex;
            half4 _UpTex_HDR;
            half4 frag(v2f i) : SV_Target { return SkyboxFrag(i, _UpTex, _UpTex_HDR); }
            ENDCG
        }

        Pass
        {
            CGPROGRAM
            #pragma vertex SkyboxVert
            #pragma fragment frag
            #pragma target 2.0
            sampler2D _DownTex;
            half4 _DownTex_HDR;
            half4 frag(v2f i) : SV_Target { return SkyboxFrag(i, _DownTex, _DownTex_HDR); }
            ENDCG
        }
    }
    Fallback Off
}

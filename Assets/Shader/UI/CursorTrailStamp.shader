// 커서 흔적 버퍼 갱신용(Graphics.Blit 전용, 머티리얼로 직접 쓰지 않는다). (2026-10-07 TH 추가)
//
// 흔적 버퍼(R 채널 = 흔적 세기 0~1)를 매 프레임 핑퐁으로 한 번 통과시키며
//  ① 이전 값을 프레임 시간만큼 선형으로 깎고(_Params.z = dt / 사라지는 시간),
//  ② 직전 커서 위치 → 현재 위치 선분을 캡슐 모양으로 1까지 찍는다.
// 점이 아니라 선분으로 찍는 이유: 마우스를 빨리 휘두르면 프레임 사이 이동이 수십 px라, 점으로 찍으면 흔적이 끊긴다.
// 지수 감쇠(v *= k)가 아니라 선형 감쇠인 이유: "몇 초 뒤 완전히 사라진다"를 인스펙터 값 하나로 예측 가능하게 하기 위함.
// 버퍼는 RHalf라 8비트처럼 작은 값이 반올림에 걸려 영원히 남는 문제가 없다.
// 값은 CircuitCursorFx.cs가 매 프레임 넣는다.
Shader "Hidden/ProjectS/Cursor Trail Stamp"
{
    Properties
    {
        _MainTex ("Previous Trail", 2D) = "black" {}
    }

    SubShader
    {
        Cull Off
        ZWrite Off
        ZTest Always

        Pass
        {
            CGPROGRAM
            #pragma vertex vert_img
            #pragma fragment frag

            #include "UnityCG.cginc"

            sampler2D _MainTex;
            float4 _Seg;       // 선분 (ax, ay, bx, by) 화면 px
            float4 _Params;    // (반경 px — 0 이하면 찍지 않음, 가장자리 부드러움 px, 이번 프레임 감쇠량, -)
            float4 _ScreenPx;  // (화면 너비, 높이) px

            float4 frag(v2f_img i) : SV_Target
            {
                float v = max(tex2D(_MainTex, i.uv).r - _Params.z, 0.0);

                if (_Params.x > 0.0)
                {
                    // 버퍼 해상도와 무관하게 화면 px 기준으로 거리를 잰다(다운샘플 배율을 바꿔도 굵기가 같게).
                    float2 p = i.uv * _ScreenPx.xy;
                    float2 a = _Seg.xy;
                    float2 ab = _Seg.zw - a;
                    float t = saturate(dot(p - a, ab) / max(dot(ab, ab), 1e-4));
                    float d = length(p - a - ab * t);
                    v = max(v, 1.0 - smoothstep(_Params.x - _Params.y, _Params.x, d));
                }

                return float4(v, v, v, v);
            }
            ENDCG
        }
    }
}

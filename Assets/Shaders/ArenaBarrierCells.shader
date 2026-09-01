// 레이드 아레나 경계 배리어 셰이더 — 육각 셀 반응형.
//
// ArenaBarrier.shader(텍스처 육각 + 동심원 파문)의 대안. 컴포넌트 쪽 프로퍼티 이름이
// 같으므로 ProjectS.Effects.ArenaBarrier를 그대로 쓰면서 머티리얼만 갈아끼우면 된다.
//
// 설계 원칙: "이 방벽은 셀로 되어 있고, 모든 반응은 셀 단위로 일어난다."
//    동심원 파문은 물에 돌을 던진 그림이라 액체의 언어다. 육각 패널로 된 구조물에는
//    맞지 않는다. 그래서 격자를 텍스처가 아니라 절차적으로 만들어 "지금 이 픽셀이
//    몇 번 셀인가"를 알아내고, 발광을 셀 중심에서 한 번만 평가해 셀 전체에 균일하게
//    적용한다. 그 결과 셀이 통째로 켜졌다 꺼지고, 파문이 퍼지면 이웃 셀이 차례로
//    점등되어 "방벽이 순간적으로 경화되어 막아냈다"로 읽힌다.
//
// 접촉 반응은 "밝은 점이 격자를 타고 사방으로 흩어지는 것"으로 표현한다. (2026-09-01 변경)
//    원래는 격자를 바깥으로 밀어 막이 눌린 것처럼 보이게 했는데, 미는 세기가 접촉점에서
//    최대인 반면 미는 방향은 바로 그 지점에서 180도 뒤집혀서, 접촉점 정중앙에 격자가
//    반대로 찢어지는 특이점이 생겼다. 반경을 키우자 화면에 칼자국처럼 드러났고,
//    세기를 줄이는 것으로는 찢어진 폭만 좁아질 뿐 없어지지 않아 기법 자체를 버렸다.
//    지금은 파문 링을 "그리는 것"이 아니라 "점을 출발시키는 신호"로 쓴다. 링이 셀에 닿는
//    순간 그 셀의 변마다 점이 하나 출발해 접촉점 반대쪽으로 달리고, 링이 번지면서
//    이웃 셀이 차례로 같은 일을 하므로 릴레이처럼 퍼진다. 격자를 변형하지 않으므로
//    왜곡이 원리적으로 생길 수 없다.
//
// 상시 상태는 셀 내부를 채우지 않고 격자 선만 남긴다.
//    면을 연하게라도 채우면 그만큼 뒤의 도시가 뿌예진다. 선만 있으면 막혀 있다는
//    인상은 유지하면서 경관은 선 사이로 그대로 보인다.
//
// 포그를 계산하지 않는다. 씬 포그가 어두운 적갈색이라 청록 방벽에 섞이면 탁해진다.
Shader "ProjectS/Arena Barrier (Cells)"
{
    Properties
    {
        [MainColor][HDR] _BaseColor ("Base Color", Color) = (0.10, 0.85, 1.0, 1.0)

        [Header(Grid)]
        // x = 가로 셀 수, y = 세로 셀 수. 가로는 정수로 둘 것(원통 UV 이음매).
        _HexTiling ("Hex Tiling (XY)", Vector) = (18, 5, 0, 0)
        _LineWidth ("Line Width", Range(0.001, 0.3)) = 0.045
        // 아무도 근처에 없을 때의 격자 선 밝기. 낮게 둘수록 "평소엔 잘 안 보인다"가 된다.
        _LineLevel ("Line Level (Far)", Range(0, 1)) = 0.08
        // 정면에서 볼 때 남는 비율. 0이면 정면에서 완전히 사라지고 1이면 각도와 무관해진다.
        _HeadOnLevel ("Head On Level", Range(0, 1)) = 0.25
        _FresnelPower ("Fresnel Power", Range(0.5, 8)) = 2.5

        [Header(Approach)]
        // 다가갈수록 선이 진해지는 범위. 셀 점등(Glow Radius)보다 훨씬 넓게 잡아야
        // "가까워지면서 서서히 드러난다"가 되고, 좁으면 갑자기 켜지는 것처럼 보인다.
        _NearRadius ("Near Radius", Float) = 14
        // 바짝 붙었을 때의 격자 선 밝기.
        _LineNearLevel ("Line Level (Near)", Range(0, 2)) = 0.7

        [Header(Traveling Dots)]
        // 격자의 변을 따라 밝은 점이 흘러간다. "이 방벽이 지금 가동 중"이라는 인상을 준다.
        // 접촉 반응은 셀 내부가 차오르지만 이쪽은 선 위에만 머무르므로 둘이 헷갈리지 않는다.
        _DotSpeed ("Dot Speed", Range(0, 2)) = 0.22
        // 변 길이에 대한 비율. 키우면 점이 아니라 짧은 선처럼 보인다.
        _DotSize ("Dot Size", Range(0.02, 0.5)) = 0.12
        // 선 기본 밝기에 얼마나 얹을지. 접촉 반응보다 밝으면 주객이 바뀐다.
        _DotStrength ("Dot Strength", Range(0, 4)) = 1.4
        // 한 변이 한 번의 주기 동안 점을 가질 확률. 매 주기마다 다시 뽑으므로
        // 같은 자리에 계속 있지 않고 여기저기서 나타났다 사라진다.
        // 0.05만 되어도 화면 전체로 보면 꽤 많다. 작게 시작할 것.
        _DotDensity ("Dot Density", Range(0, 0.5)) = 0.06
        // 변마다 속도를 얼마나 다르게 할지. 0이면 전부 같은 속도로 움직여 기계적으로 보인다.
        _DotSpeedVariation ("Dot Speed Variation", Range(0, 0.9)) = 0.5

        [Header(Emitter Glow)]
        // 아랫변이 밝으면 방벽이 공중에 뜬 게 아니라 바닥에서 생성된 것으로 보인다.
        _BaseGlowLevel ("Base Glow Level", Range(0, 2)) = 0.5
        _BaseGlowHeight ("Base Glow Height (V)", Range(0.01, 0.6)) = 0.18

        // ★ 아래 반응 값들은 전부 "셀 한 칸의 월드 크기"를 기준으로 잡아야 한다.
        //    발광을 셀 중심에서 한 번만 평가하는 구조라, 반경이 셀보다 작으면 마주친 셀조차
        //    켜지지 않고 지나간다(셀 중심이 반경 밖에 있으면 그 셀은 없는 것과 같다).
        //    셀 크기 = 면의 둘레 / _HexTiling.x. 원통 반지름 56~75인 아레나에서는 약 8.6유닛이라
        //    반경·링 두께·파문 이동거리를 모두 그 배수로 잡아 놓았다. _HexTiling을 바꾸면
        //    여기도 같이 바꿔야 반응이 보인다.

        [Header(Cell Response)]
        // 접촉점에서 이 반경 안의 셀이 점등된다. 셀 하나보다 커야 접촉한 셀이 확실히 켜진다.
        _GlowRadius ("Glow Radius", Float) = 12
        // 점등된 셀의 내부가 얼마나 차오르는가. 이 값이 "순간적으로 불투명해진다"를 만든다.
        _FillStrength ("Cell Fill Strength", Range(0, 2)) = 1.4
        // 셀이 어중간하게 반쯤 켜지지 않도록 문턱을 세운다. 클수록 딱딱 켜진다.
        // 파문의 수명 감쇠까지 여기서 함께 거듭제곱되므로 과하게 올리면 파문이 순식간에 죽는다.
        _FillSharpness ("Cell Fill Sharpness", Range(1, 8)) = 2.2

        [Header(Ripple)]
        // 속도 x 수명이 파문이 퍼지는 총 거리다. 이 거리가 셀 한 칸보다 짧으면
        // 파문이 이웃 셀에 닿기 전에 죽어서, 부딪힌 자리만 깜빡이고 번지지 않는다.
        _RippleSpeed ("Ripple Speed", Float) = 32
        // 링 두께도 셀 한 칸 정도는 되어야 링이 셀 중심을 놓치지 않고 지나간다.
        _RingWidth ("Ring Width", Float) = 7
        // ArenaBarrier.cs가 이 값을 읽어 파문 수명을 판단한다. 여기가 단일 기준점이다.
        _RippleLife ("Ripple Life", Float) = 1.1

        [Header(Scatter Dots)]
        // 파문 링이 셀을 지나는 순간, 그 셀의 변마다 밝은 점이 하나 출발해 접촉점 반대쪽으로 달린다.
        // 링이 바깥으로 번지면서 셀이 차례로 점을 뱉으므로 "부딪힌 자리에서 점이 사방으로 흩어진다"가 된다.
        //
        // 예전에 여기 있던 _PushAmount(격자를 밀어 출렁이게 하던 값)는 제거했다.
        // 미는 세기는 접촉점에서 최대인데 미는 방향은 바로 그 지점에서 180도 뒤집히기 때문에,
        // 접촉점 정중앙에서 격자가 서로 반대로 찢어지는 특이점이 있었다. 세기를 줄여도
        // 찢어진 폭만 좁아질 뿐 없어지지 않는다. 반경을 키우자 화면에 칼자국처럼 드러났다.

        // 점이 변 하나를 훑는 데 걸리는 시간(초). 링이 셀 하나를 건너는 시간(셀 크기 / _RippleSpeed)과
        // 비슷하게 잡아야 이웃 셀로 끊김 없이 이어진다. 길게 주면 여러 겹이 동시에 달린다.
        _ScatterLife ("Scatter Life", Range(0.05, 1)) = 0.3
        _ScatterStrength ("Scatter Strength", Range(0, 6)) = 3.0
        // 링이 지나갈 때 실제로 점을 뱉는 변의 비율. 1이면 여섯 변이 전부 켜져 눈꽃처럼 규칙적으로 보인다.
        _ScatterDensity ("Scatter Density", Range(0, 1)) = 0.55

        [Header(Shape)]
        _TopFadeStart ("Top Fade Start (V)", Range(0, 1)) = 0.6
        // 원통 메시의 위아래 뚜껑을 지운다. 뚜껑은 노말이 수직이라 이 값으로 걸러진다.
        // 가산 블렌딩이라 바닥 뚜껑이 남으면 아레나 바닥 전체가 밝아진다.
        // 1로 두면 아무것도 걸러내지 않으므로, 뚜껑 없는 메시라면 1로 둬도 된다.
        _CapCutoff ("Cap Cutoff", Range(0, 1)) = 0.55

        [Header(Camera Fade)]
        // 카메라가 배리어 면을 뚫고 들어가면 가산 블렌딩이라 화면 절반이 발광으로 덮인다.
        // 카메라에 가까운 조각을 지워 그 사고를 막는다. 카메라 충돌 처리가 없는 동안의 안전망이다.
        _CameraFadeStart ("Camera Fade Start", Float) = 0.4
        _CameraFadeEnd ("Camera Fade End", Float) = 3.0
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "ArenaBarrierCells"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha One
            ZWrite Off
            Cull Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // ArenaBarrier.cs의 MaxPoints와 반드시 같아야 한다.
            #define MAX_POINTS 8

            // 육각 격자의 세로 비율. sqrt(3).
            #define HEX_S float2(1.0, 1.7320508)

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                float3 normalWS   : TEXCOORD2;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _HexTiling;
                float _LineWidth;
                float _LineLevel;
                float _HeadOnLevel;
                float _FresnelPower;
                float _NearRadius;
                float _LineNearLevel;
                float _DotSpeed;
                float _DotSize;
                float _DotStrength;
                float _DotDensity;
                float _DotSpeedVariation;
                float _BaseGlowLevel;
                float _BaseGlowHeight;
                float _GlowRadius;
                float _FillStrength;
                float _FillSharpness;
                float _RippleSpeed;
                float _RingWidth;
                float _RippleLife;
                float _ScatterLife;
                float _ScatterStrength;
                float _ScatterDensity;
                float _TopFadeStart;
                float _CapCutoff;
                float _CameraFadeStart;
                float _CameraFadeEnd;
            CBUFFER_END

            // 배열은 CBUFFER 밖에 둔다. UnityPerMaterial 안에는 배열을 넣을 수 없어
            // SRP Batcher 대상에서 빠지지만, 배리어는 씬에 하나뿐이라 문제되지 않는다.
            float4 _ProximityPoints[MAX_POINTS];  // xyz = 월드 좌표, w = 세기(0~1)
            float4 _RipplePoints[MAX_POINTS];     // xyz = 월드 좌표, w = 발생 시각
            int _ProximityCount;
            int _RippleCount;

            // 육각 격자. xy = 셀 중심 기준 로컬 좌표, zw = 셀 고유 번호.
            // 어긋난 두 사각 격자 중 가까운 쪽을 고르는 표준 기법이다.
            float4 GetHex(float2 p)
            {
                float4 hC = floor(float4(p, p - float2(0.5, 1.0)) / HEX_S.xyxy) + 0.5;
                float4 h = float4(p - hC.xy * HEX_S, p - (hC.zw + 0.5) * HEX_S);
                return dot(h.xy, h.xy) < dot(h.zw, h.zw)
                    ? float4(h.xy, hC.xy)
                    : float4(h.zw, hC.zw + 0.5);
            }

            // 육각형 거리장. 변에서 0.5가 된다.
            float HexDist(float2 p)
            {
                p = abs(p);
                return max(dot(p, HEX_S * 0.5), p.x);
            }

            // 셀 안에서 가장 가까운 변을 찾아, 변 번호(0~5)와 그 변 위에서의 위치(0~1)를 돌려준다.
            // 육각형은 세 개의 대칭축으로 정의되므로, 투영값이 가장 큰 축이 곧 가장 가까운 변이고
            // 그 부호가 여섯 변 중 어느 쪽인지를 가른다. 점이 변을 "따라" 흐르려면 이 두 값이 필요하다.
            // tangent도 함께 돌려준다. 흩어지는 점이 "접촉점 반대쪽"으로 달리려면
            // 변이 뻗은 방향과 바깥 방향의 부호를 비교해야 하기 때문이다.
            void GetHexEdge(float2 p, out float edgeId, out float alongEdge, out float2 edgeTangent)
            {
                float2 n0 = float2(1.0, 0.0);
                float2 n1 = float2(0.5, 0.8660254);
                float2 n2 = float2(0.5, -0.8660254);

                float d0 = dot(p, n0);
                float d1 = dot(p, n1);
                float d2 = dot(p, n2);
                float a0 = abs(d0), a1 = abs(d1), a2 = abs(d2);

                float2 axis;
                float projected;
                float index;

                if (a0 >= a1 && a0 >= a2)  { axis = n0; projected = d0; index = 0.0; }
                else if (a1 >= a2)         { axis = n1; projected = d1; index = 1.0; }
                else                       { axis = n2; projected = d2; index = 2.0; }

                edgeId = index * 2.0 + (projected < 0.0 ? 1.0 : 0.0);

                // 변이 뻗은 방향은 법선의 수직 방향이다.
                // 내접원이 0.5인 정육각형에서 변의 반길이는 0.2887(= 0.5 / sqrt(3))이다.
                float2 tangent = float2(-axis.y, axis.x);
                alongEdge = saturate(dot(p, tangent) / 0.5773503 + 0.5);
                edgeTangent = tangent;
            }

            float Hash21(float2 p)
            {
                p = frac(p * float2(123.34, 456.21));
                p += dot(p, p + 45.32);
                return frac(p.x * p.y);
            }

            // 주어진 월드 좌표에서의 접촉 영향력. 가장 강하게 기여한 접촉점도 함께 돌려준다.
            float GlowAt(float3 p, out float3 strongestPoint, out float strongest)
            {
                float glow = 0;
                strongestPoint = p;
                strongest = 0;

                [loop]
                for (int i = 0; i < _ProximityCount; i++)
                {
                    float d = distance(p, _ProximityPoints[i].xyz);
                    float f = 1.0 - saturate(d / max(_GlowRadius, 1e-4));
                    f = f * f * _ProximityPoints[i].w;

                    if (f > strongest) { strongest = f; strongestPoint = _ProximityPoints[i].xyz; }
                    glow = max(glow, f);
                }

                [loop]
                for (int r = 0; r < _RippleCount; r++)
                {
                    float age = _Time.y - _RipplePoints[r].w;
                    float d = distance(p, _RipplePoints[r].xyz);

                    float ring = saturate(1.0 - abs(d - age * _RippleSpeed) / max(_RingWidth, 1e-4));

                    // 수명 감쇠를 선형으로 깎으면 안 된다. 이 값이 나중에 셀 채움에서
                    // pow(_FillSharpness)로 한 번 더 눌리기 때문에, 선형으로 깎으면 실제로는
                    // 세제곱으로 사그라들어 파문이 태어나자마자 죽는다(수명의 3분의 1도 못 산다).
                    // 수명의 대부분은 세기를 유지하고 마지막에만 떨어뜨려, 거듭제곱을 먹어도
                    // 링이 벽을 가로지르는 동안 살아 있게 한다.
                    float lifeT = saturate(age / max(_RippleLife, 1e-4));
                    ring *= 1.0 - smoothstep(0.55, 1.0, lifeT);
                    ring *= step(0.0, age);

                    if (ring > strongest) { strongest = ring; strongestPoint = _RipplePoints[r].xyz; }
                    glow = max(glow, ring);
                }

                return glow;
            }

            // 파문 링이 이 지점을 "이미 지나갔는지"와, 지나간 뒤 얼마나 흘렀는지를 구한다.
            // 링은 원점에서 _RippleSpeed로 퍼지므로 도달 시각은 원점 거리 / 속도로 바로 나온다.
            // 링 자체의 밝기(GlowAt)와 달리 이 값은 셀마다 "언제 반응을 시작할지"를 정하는 신호다.
            // 덕분에 셀이 링이 닿는 순서대로 점을 뱉어, 접촉점에서 바깥으로 번지는 릴레이가 된다.
            //
            // 아직 링이 닿지 않았거나 살아있는 파문이 없으면 -1을 돌려준다.
            float RippleWakeAt(float3 p, out float3 origin, out float energy)
            {
                float best = 1e9;
                origin = p;
                energy = 0;

                [loop]
                for (int r = 0; r < _RippleCount; r++)
                {
                    float age = _Time.y - _RipplePoints[r].w;
                    if (age < 0.0 || age > _RippleLife) continue;

                    float d = distance(p, _RipplePoints[r].xyz);
                    float since = age - d / max(_RippleSpeed, 1e-4);
                    if (since < 0.0) continue;

                    // 여러 파문이 겹치면 가장 최근에 도달한 쪽을 쓴다. 방금 맞은 쪽이 더 중요하다.
                    if (since < best)
                    {
                        best = since;
                        origin = _RipplePoints[r].xyz;
                        // 파문이 늙을수록 뱉는 점도 약해진다. 멀리 갈수록 잦아드는 그림이 된다.
                        energy = saturate(1.0 - age / max(_RippleLife, 1e-4));
                    }
                }

                return best < 1e8 ? best : -1.0;
            }

            // 다가감 정도. 0이면 아무도 근처에 없고, 1이면 벽에 바짝 붙었다.
            // 셀 점등과 달리 셀 단위로 끊지 않고 프래그먼트마다 연속으로 계산한다.
            // 다가가는 동안은 부드러운 그라데이션으로 드러나야 자연스럽기 때문이다.
            float NearAt(float3 p)
            {
                float near = 0;

                [loop]
                for (int i = 0; i < _ProximityCount; i++)
                {
                    // 표면을 따라 퍼지는 거리와, 플레이어가 벽에서 얼마나 떨어져 있는지(w)를 함께 본다.
                    float d = distance(p, _ProximityPoints[i].xyz);
                    float f = 1.0 - saturate(d / max(_NearRadius, 1e-4));
                    near = max(near, f * _ProximityPoints[i].w);
                }

                return near;
            }

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                VertexPositionInputs pos = GetVertexPositionInputs(IN.positionOS.xyz);
                OUT.positionCS = pos.positionCS;
                OUT.positionWS = pos.positionWS;
                OUT.normalWS = TransformObjectToWorldNormal(IN.normalOS);
                OUT.uv = IN.uv;
                return OUT;
            }

            half4 Frag(Varyings IN) : SV_Target
            {
                float3 positionWS = IN.positionWS;

                // 0단계: "UV 1칸당 월드 변위"를 화면 미분으로 구한다.
                // 정점에서 로컬 축으로 넘기면 평면(Quad)에서만 맞다. 원통은 UV가 원주를 따라
                // 감기므로 UV 방향이 위치마다 달라져서, 셀 중심 좌표가 엉뚱한 곳으로 간다.
                // 미분으로 구하면 원통이든 타원이든 어떤 메시든 그 자리의 실제 값이 나온다.
                // 분기 밖에서 계산해야 한다. 미분은 2x2 픽셀 블록 단위라 분기 안에서는 값이 깨진다.
                float3 dpdx = ddx(positionWS);
                float3 dpdy = ddy(positionWS);
                float2 duvdx = ddx(IN.uv);
                float2 duvdy = ddy(IN.uv);

                float det = duvdx.x * duvdy.y - duvdy.x * duvdx.y;
                // UV가 이어지는 이음매에서는 미분이 폭주하므로 0으로 접어 안전하게 만든다.
                float safe = abs(det) > 1e-9 ? 1.0 : 0.0;
                det = det + (1.0 - safe);

                float3 dPdu = (dpdx * duvdy.y - dpdy * duvdx.y) / det * safe;
                float3 dPdv = (dpdy * duvdx.x - dpdx * duvdy.x) / det * safe;

                // 격자는 변형하지 않는다. 프래그먼트 위치에서 영향력을 잴 일도 없어졌으므로
                // (격자 밀기 제거) 프래그먼트마다 돌던 GlowAt 루프 하나가 통째로 빠졌다.
                float2 hp = IN.uv * _HexTiling.xy;

                // 1단계: 셀을 구하고, 그 셀 중심의 월드 좌표를 되찾는다.
                float4 hex = GetHex(hp);
                float2 cellUV = (hp - hex.xy) / _HexTiling.xy;
                float2 duv = cellUV - IN.uv;
                float3 cellWS = positionWS + duv.x * dPdu + duv.y * dPdv;

                // 2단계: 셀 중심에서 한 번만 평가한다. 그래야 셀 전체가 한 덩어리로 켜진다.
                float3 ignoredPoint;
                float ignoredStrength;
                float cellGlow = GlowAt(cellWS, ignoredPoint, ignoredStrength);
                cellGlow = saturate(pow(cellGlow, _FillSharpness) * _FillStrength);

                // --- 상시: 격자 선 ---
                float edge = 0.5 - HexDist(hex.xy);
                float lineShape = 1.0 - smoothstep(0.0, _LineWidth, edge);

                // 다가갈수록 선이 진해진다. 이게 "평소엔 잘 안 보이다가 가까이 가면 드러난다"의 본체다.
                float near = NearAt(positionWS);
                float lineLevel = lerp(_LineLevel, _LineNearLevel, near);

                // 변을 따라 흐르는 점.
                float edgeId, alongEdge;
                float2 edgeTangent;
                GetHexEdge(hex.xy, edgeId, alongEdge, edgeTangent);

                // 변마다 고유한 씨앗 두 개. 하나는 출발 시점, 하나는 속도와 방향에 쓴다.
                float seedA = Hash21(hex.zw + edgeId * 37.0);
                float seedB = Hash21(hex.zw + edgeId * 91.0 + 5.7);

                // 속도와 진행 방향을 변마다 달리한다. 전부 같으면 한 덩어리로 움직여 기계적으로 보인다.
                float dotSpeed = _DotSpeed * lerp(1.0 - _DotSpeedVariation, 1.0 + _DotSpeedVariation, seedB);
                float dotDirection = seedB < 0.5 ? -1.0 : 1.0;

                float cycle = _Time.y * dotSpeed + seedA * 17.3;
                float travel = frac(cycle);

                // 주기가 바뀔 때마다 난수를 다시 뽑는다. 같은 변에 점이 늘 붙어 있는 게 아니라
                // 한 번 지나간 뒤에는 다른 변에서 나타나므로 자리가 고정돼 보이지 않는다.
                float pick = Hash21(hex.zw + edgeId * 13.0 + floor(cycle) * 57.1);
                float active = step(pick, _DotDensity);

                // 방향에 따라 변의 어느 끝에서 출발할지 뒤집는다.
                float head = dotDirection > 0.0 ? travel : 1.0 - travel;

                // 양 끝에서 부드럽게 나타나고 사라지게 한다. 없으면 툭 튀어나왔다 툭 꺼진다.
                float endFade = smoothstep(0.0, 0.18, travel) * smoothstep(1.0, 0.82, travel);

                float travelingDot = smoothstep(_DotSize, 0.0, abs(alongEdge - head)) * active * endFade;

                // --- 접촉 반응: 격자를 타고 흩어지는 점 ---
                // 링이 이 셀에 도달한 뒤 흐른 시간. 아직 안 닿았으면 음수다.
                float3 wakeOrigin;
                float wakeEnergy;
                float wake = RippleWakeAt(cellWS, wakeOrigin, wakeEnergy);

                // 접촉점에서 바깥으로 향하는 방향을 격자 좌표계로 옮긴다.
                // dPdu/dPdv가 이 자리의 실제 UV 축이므로 원통이든 타원이든 이 변환 하나로 맞는다.
                //
                // normalize(dPdu)에 내적하면 안 된다. 그건 "UV 1칸당 몇 유닛인가"를 곱한 값이라
                // 가로 축(둘레 413유닛)이 세로 축(높이 60유닛)보다 7배 크게 잡히고, 그러면
                // 방향 판정이 가로로 쏠려 흩어짐이 옆으로만 번지는 것처럼 보인다.
                // 축 길이의 제곱으로 나눠야 제대로 된 UV 성분이 나온다.
                float3 awayWS = cellWS - wakeOrigin;
                float2 awayHp = float2(dot(awayWS, dPdu) / max(dot(dPdu, dPdu), 1e-6),
                                       dot(awayWS, dPdv) / max(dot(dPdv, dPdv), 1e-6))
                              * _HexTiling.xy;

                // 변이 뻗은 방향과 바깥 방향의 부호를 맞춘다. 이 한 줄이 "사방으로 흩어진다"를 만든다.
                // 부호를 안 맞추면 점이 접촉점 쪽으로 되돌아가는 변이 절반쯤 생겨 흐름이 읽히지 않는다.
                float outward = dot(edgeTangent, awayHp) >= 0.0 ? 1.0 : -1.0;

                // 셀·변마다, 그리고 파문마다 다른 변이 켜지도록 씨앗에 파문 원점을 섞는다.
                // 안 섞으면 늘 같은 변만 뱉어서 두 번째 충돌이 첫 번째의 복사본처럼 보인다.
                float scatterPick = Hash21(hex.zw + edgeId * 23.0 + wakeOrigin.xz * 0.31);

                float scatterT = saturate(wake / max(_ScatterLife, 1e-4));
                // 훑기가 끝나면 끈다. 안 끄면 점이 변 끝에 박힌 채 파문이 죽을 때까지 남는다.
                float scatterOn = step(scatterPick, _ScatterDensity)
                                * step(0.0, wake)
                                * step(wake, _ScatterLife);

                float scatterHead = outward > 0.0 ? scatterT : 1.0 - scatterT;
                float scatterFade = smoothstep(1.0, 0.7, scatterT);
                float scatterDot = smoothstep(_DotSize, 0.0, abs(alongEdge - scatterHead))
                                 * scatterOn * scatterFade * wakeEnergy;

                // 점은 기본 밝기에 얹는다. 곱하지 않으므로 멀리서도 흐름이 보인다.
                // 흩어지는 점도 lineShape를 타므로 변 위에만 올라간다 — 격자를 타고 퍼지는 그림이 된다.
                float gridLine = lineShape * (lineLevel
                                            + travelingDot * _DotStrength
                                            + scatterDot * _ScatterStrength);

                // 발생 장치에서 나온 느낌을 주는 아랫변 발광.
                float baseGlow = (1.0 - smoothstep(0.0, _BaseGlowHeight, IN.uv.y)) * _BaseGlowLevel;

                // 상시 요소만 각도에 따라 죽인다. 정면에서 도시를 가리지 않기 위함이다.
                // 접촉 반응은 정면에서도 또렷해야 하므로 여기서 제외한다.
                float3 normalWS = normalize(IN.normalWS);
                float3 viewWS = normalize(GetWorldSpaceViewDir(positionWS));
                float fresnel = pow(1.0 - saturate(abs(dot(normalWS, viewWS))), _FresnelPower);
                // 멀리서는 정면일수록 사라지지만, 다가가면 각도와 무관하게 보이게 한다.
                // 코앞의 벽이 정면이라는 이유로 안 보이면 오히려 어색하다.
                float headOn = lerp(_HeadOnLevel, 1.0, near);
                float rest = (gridLine + baseGlow) * lerp(headOn, 1.0, fresnel);

                float topFade = 1.0 - smoothstep(_TopFadeStart, 1.0, IN.uv.y);

                // 벽면은 노말이 수평이고 원통 뚜껑은 수직이다. 뚜껑을 지워 바닥이 밝아지는 것을 막는다.
                float wallFacing = 1.0 - smoothstep(_CapCutoff, 1.0, abs(normalWS.y));

                // 카메라가 면을 뚫고 들어와도 화면이 발광으로 덮이지 않게 가까운 조각을 지운다.
                float camFade = smoothstep(_CameraFadeStart, _CameraFadeEnd,
                                           distance(positionWS, GetCameraPositionWS()));

                float strength = saturate(rest + cellGlow) * topFade * wallFacing * camFade;

                return half4(_BaseColor.rgb, strength * _BaseColor.a);
            }
            ENDHLSL
        }
    }

    Fallback Off
}

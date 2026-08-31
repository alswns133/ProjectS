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

        [Header(Cell Response)]
        // 접촉점에서 이 반경 안의 셀이 점등된다.
        _GlowRadius ("Glow Radius", Float) = 3.5
        // 점등된 셀의 내부가 얼마나 차오르는가. 이 값이 "순간적으로 불투명해진다"를 만든다.
        _FillStrength ("Cell Fill Strength", Range(0, 2)) = 1.0
        // 셀이 어중간하게 반쯤 켜지지 않도록 문턱을 세운다. 클수록 딱딱 켜진다.
        _FillSharpness ("Cell Fill Sharpness", Range(1, 8)) = 3

        [Header(Ripple)]
        _RippleSpeed ("Ripple Speed", Float) = 9
        _RingWidth ("Ring Width", Float) = 1.3
        // ArenaBarrier.cs가 이 값을 읽어 파문 수명을 판단한다. 여기가 단일 기준점이다.
        _RippleLife ("Ripple Life", Float) = 0.8

        [Header(Push)]
        // 접촉점 주변의 격자를 바깥으로 민다. 빛이 아니라 형태로 반응해서
        // "막이 눌렸다"가 읽힌다. 크게 주면 셀이 헤엄치는 것처럼 보이니 조금만.
        _PushAmount ("Grid Push", Range(0, 1.5)) = 0.45

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
                float _PushAmount;
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
            void GetHexEdge(float2 p, out float edgeId, out float alongEdge)
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
                    ring *= saturate(1.0 - age / max(_RippleLife, 1e-4));
                    ring *= step(0.0, age);

                    if (ring > strongest) { strongest = ring; strongestPoint = _RipplePoints[r].xyz; }
                    glow = max(glow, ring);
                }

                return glow;
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

                // 1단계: 프래그먼트 위치에서 영향력을 재고, 격자를 밀 방향을 얻는다.
                float3 pushFrom;
                float pushStrength;
                float fragGlow = GlowAt(positionWS, pushFrom, pushStrength);

                float2 hp = IN.uv * _HexTiling.xy;

                // 2단계: 접촉점 반대 방향으로 격자를 민다. 막이 눌린 것처럼 보인다.
                if (pushStrength > 0.001)
                {
                    float3 away = positionWS - pushFrom;
                    float2 dir = float2(dot(away, normalize(dPdu)), dot(away, normalize(dPdv)));
                    if (dot(dir, dir) > 1e-6)
                    {
                        hp += normalize(dir) * pushStrength * _PushAmount;
                    }
                }

                // 3단계: 셀을 구하고, 그 셀 중심의 월드 좌표를 되찾는다.
                float4 hex = GetHex(hp);
                float2 cellUV = (hp - hex.xy) / _HexTiling.xy;
                float2 duv = cellUV - IN.uv;
                float3 cellWS = positionWS + duv.x * dPdu + duv.y * dPdv;

                // 4단계: 셀 중심에서 한 번만 평가한다. 그래야 셀 전체가 한 덩어리로 켜진다.
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
                GetHexEdge(hex.xy, edgeId, alongEdge);

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

                // 점은 기본 밝기에 얹는다. 곱하지 않으므로 멀리서도 흐름이 보인다.
                float gridLine = lineShape * (lineLevel + travelingDot * _DotStrength);

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

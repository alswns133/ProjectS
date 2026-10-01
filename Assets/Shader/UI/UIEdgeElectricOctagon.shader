// RVFX EdgeEffect_Electric_Additive 의 복사본 (TH).
// 원본은 테두리가 사각형/원형뿐이라 8각형 슬롯에 맞추려면 UI Mask 가 필요했는데, Mask 는 경계 바깥 글로우를
// 잘라내고 머티리얼을 스텐실 복사본으로 바꿔 값 변경이 안 먹는다. 그래서 테두리 모양 자체를 깎는 _CornerCut 을 추가했다.
// 원본이 깃 제외 폴더(ExternalAssets)에 있어 직접 고치지 않고 복사했다.
Shader "ProjectS/UI Edge Electric Octagon"
{
    Properties
    {
        [HideInInspector] _MainTex("MainTex", 2D) = "white"{}
        [Header(Edge Type)]
        [Toggle(_IsCircular)] _IsCircular("IsCircular", float) = 0
        // 0 = 사각형, 커질수록 모서리가 대각선으로 깎인다(Rect 절반 변 길이 대비 비율). IsCircular 가 켜지면 무시.
        _CornerCut("CornerCut", Range(0, 1)) = 0.2
        // 선이 놓이는 위치(0 = Rect 중심, 1 = Rect 가장자리). 원본은 0.5 고정이라 선이 Rect 절반 크기로 그려졌다.
        // 1 에 붙이면 바깥쪽 글로우가 Rect 밖으로 잘리므로 여유를 조금 남긴다.
        _EdgePosition("EdgePosition", Range(0, 1)) = 0.9
        // Additive 는 밝은 배경 위에서 색이 포화돼 안 보인다. 밝은 배경에 얹을 때는 AlphaBlend 를 쓴다.
        [Enum(Additive,1,AlphaBlend,10)] _DstBlend("BlendMode", Float) = 1

        [Header(Electric)]
        [NoScaleOffset] _NoiseTex("NoiseTex", 2D) = "gray"{}
        _NoiseTex_ScaleScrollSpeed("NoiseTex_ScaleScrollSpeed", vector) = (1,1,0,0)
    

        _DistortionIntensity("DistortionIntensity", float) = 1.0
        _Thickness("Thickness", range(0.0, 1)) = 0.01
        _XThicknessScaleFactor("XThicknessScaleFactor", Range(0,10)) = 1.0

        _EdgeBlur("EdgeBlur", range(0.0, 1)) = 0.0

        [Header(UV Control)]
        _RadialUV_Power("RadialUV_Power", range(0,5)) = 1.0


        [Header(Color Control)]
        _ColorPower("ColorPower", float) = 1.0
        _ColorIntensity("ColorIntensity", float) = 1.0


        [Header(Mask)]
        [Toggle(_Use_Mask)] _Use_Mask("UseMask", float) = 0
        [NoScaleOffset] _MaskTex("MaskTex", 2D) = "white"{}
        _MaskTexScaleOffset("MaskTexScaleOffset", vector) = (1,1,0,0)
        _MaskTex_ScrollSpeed_X("MaskTex_ScrollSpeed_X", float) = 0.0
        _MaskTex_ScrollSpeed_Y("MaskTex_ScrollSpeed_Y", float) = 0.0
        _Mask_Power("Mask_Power", float) = 1.0
        _Mask_Intensity("Mask_Intensity", float) = 1.0

        [Header(Mask2)]
        [Toggle(_Use_Mask2)] _Use_Mask2("UseMask2", float) = 0
        [NoScaleOffset] _MaskTex2("MaskTex2", 2D) = "white"{}
        _MaskTex2ScaleOffset("MaskTex2ScaleOffset", vector) = (1,1,0,0)
        _MaskTex2_ScrollSpeed_X("MaskTex2_ScrollSpeed_X", float) = 0.0
        _MaskTex2_ScrollSpeed_Y("MaskTex2_ScrollSpeed_Y", float) = 0.0
        _Mask2_Power("Mask2_Power", float) = 1.0
        _Mask2_Intensity("Mask2_Intensity", float) = 1.0

   



        [HideInInspector] _StencilComp("Stencil Comparison", Float) = 8
        [HideInInspector] _Stencil("Stencil ID", Float) = 0
        [HideInInspector] _StencilOp("Stencil Operation", Float) = 0
        [HideInInspector] _StencilWriteMask("Stencil Write Mask", Float) = 255
        [HideInInspector] _StencilReadMask("Stencil Read Mask", Float) = 255
        [HideInInspector] _ColorMask("Color Mask", Float) = 15
    }
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent"}
        ZTest[unity_GUIZTestMode]
        Blend One [_DstBlend]
        Cull Off
        ZWrite Off

        Stencil
        {
            Ref[_Stencil]
            Comp[_StencilComp]
        }
    

        Pass
        {
            CGPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma shader_feature_local _Use_Mask
            #pragma shader_feature_local _Use_Mask2


            #pragma shader_feature_local _IsCircular
            #pragma multi_compile __ UNITY_UI_CLIP_RECT
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct v2f
            {
                float2 uv : TEXCOORD0;
 
                float4 vertex : SV_POSITION;
                float4 color : COLOR;

                #if UNITY_UI_CLIP_RECT
                    float4	rectMask    : TEXCOORD2;
                #endif
            };

 
            float _RadialUV_Power;
            float _ColorIntensity;
            float _ColorPower;

 

            #ifdef _Use_Mask
                sampler2D _MaskTex;
                float4 _MaskTexScaleOffset;
                float _MaskTex_ScrollSpeed_X;
                float _MaskTex_ScrollSpeed_Y;
                float _Mask_Power;
                float _Mask_Intensity;
            #endif

            #ifdef _Use_Mask2
                sampler2D _MaskTex2;
                float4 _MaskTex2ScaleOffset;
                float _MaskTex2_ScrollSpeed_X;
                float _MaskTex2_ScrollSpeed_Y;
                float _Mask2_Power;
                float _Mask2_Intensity;
            #endif


            #ifdef UNITY_UI_CLIP_RECT
                float4 _ClipRect;
                float _UIMaskSoftnessX, _UIMaskSoftnessY;
            #endif


            float _InitialRotation;
            float _RotationSpeed;


            //Electric
            float _Thickness;
            float _XThicknessScaleFactor;
            float _EdgeBlur;
            float _DistortionIntensity;
            float _CornerCut;
            float _EdgePosition;
  

            sampler2D _NoiseTex;
            float4 _NoiseTex_ScaleScrollSpeed;


          


   

            v2f vert (appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);


                o.uv = v.uv;
                o.color = v.color;

                #ifdef UNITY_UI_CLIP_RECT
                    float4 clampedRect = clamp(_ClipRect, -2e10, 2e10);
                    o.rectMask = half4(v.vertex.xy * 2 - clampedRect.xy - clampedRect.zw, 0.25 / (0.25 * float2((_UIMaskSoftnessX + 1), (_UIMaskSoftnessY + 1))));
                #endif
              
                return o;
            }

            fixed4 frag(v2f i) : SV_Target
            {

                float u = 0;
                float v = 0;


                
                float PI = 3.1415926535;
                float2 centered_uv = (i.uv - 0.5) * 2.0;
 
                #ifdef _IsCircular
                    u = length(centered_uv);
                #else
                    float2 a = abs(centered_uv);
                    a.x = pow(a.x, _XThicknessScaleFactor);
                    u = max(max(a.x, a.y), (a.x + a.y) / (2.0 - _CornerCut));
                #endif

                u = saturate(pow(u, _RadialUV_Power));
                v = (atan2(centered_uv.x, centered_uv.y) + PI) / (PI * 2.0);

             




     
















                float2 uv = float2(u, v);
                float2 uv_dx = ddx(i.uv.xy);
                float2 uv_dy = ddy(i.uv.xy);
            
             
 

                //Electrics
                float noise = tex2D(_NoiseTex, uv * _NoiseTex_ScaleScrollSpeed.xy + _NoiseTex_ScaleScrollSpeed.zw * _Time.y, uv_dx, uv_dy).r;
                float noise_2 = tex2D(_NoiseTex, 3.0 * uv * _NoiseTex_ScaleScrollSpeed.xy + _NoiseTex_ScaleScrollSpeed.zw * _Time.y, uv_dx, uv_dy).r;
                noise = (noise - 0.5) * 2 * 0.03 * _DistortionIntensity;
                noise_2 = (noise_2 - 0.5) * 2 * 0.03 * _DistortionIntensity;

                float2 centered_uv_electric = (uv + float2(noise*noise_2, noise * noise_2 * 1.19382) - float2(_EdgePosition, 0.5)) * 2.0;

                float thickness = _Thickness;
                float outerline_mask = 1.0 - smoothstep(thickness, thickness + _EdgeBlur, abs(centered_uv_electric.x));
                float core_mask = pow(outerline_mask, 100.0);

                float4 col = i.color * outerline_mask;
                col.rgb += i.color.rgb * core_mask * 20.0;
                col.rgb *= col.a;

    

                col.rgb = pow(col.rgb, _ColorPower);
                col.rgb *= i.color.rgb* _ColorIntensity;
 
                col.rgb *= i.color.a;



                #ifdef _Use_Mask
                    // 원본은 R 채널만 읽어, 모양이 알파에 들어 있는 스프라이트(투명 배경 + 흰 도형)는 마스크가 안 먹었다.
                    // 알파를 곱해 흑백 마스크와 알파 스프라이트 둘 다 받는다.
                    float4 maskSample = tex2D(_MaskTex, i.uv * _MaskTexScaleOffset.xy + _MaskTexScaleOffset.zw +
                    float2(_MaskTex_ScrollSpeed_X, _MaskTex_ScrollSpeed_Y) * _Time.y);
                    float mask = maskSample.r * maskSample.a;
                    mask = saturate(pow(mask, _Mask_Power) * _Mask_Intensity);
                    col *= mask;
                #endif

                #ifdef _Use_Mask2
                    float4 mask2Sample = tex2D(_MaskTex2, i.uv * _MaskTex2ScaleOffset.xy + _MaskTex2ScaleOffset.zw +
                    float2(_MaskTex2_ScrollSpeed_X, _MaskTex2_ScrollSpeed_Y) * _Time.y);
                    float mask2 = mask2Sample.r * mask2Sample.a;
                    mask2 = saturate(pow(mask2, _Mask2_Power) * _Mask2_Intensity);
                    col *= mask2;
                #endif



                #if UNITY_UI_CLIP_RECT	
                    half2 m = saturate((_ClipRect.zw - _ClipRect.xy - abs(i.rectMask.xy)) * i.rectMask.zw);
                    col *= m.x * m.y;
                #endif


                return col;
            }
            ENDCG
        }
    }
}

// References:
// https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/renderer-features/how-to-fullscreen-blit.html

// 流体效果着色器，用于实现类似液体的视觉效果。
// 使用之前模糊过的纹理作为输入，结合透明度裁剪和颜色调整来模拟液体边缘和内部的视觉特性。
Shader "Custom/URP/2D/Liquid2DEffect"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Cutoff ("Alpha cutoff", Range(0,1)) = 0.2
        _AlphaOffset ("Alpha cutoff", Range(0,1)) = 0
        
//        _EdgeClip("Edge Clip", Range(0,1)) = 0.2
//        _Color ("Main Color", Color) = (0.2, 0.6, 1, 1)
//        _Stroke ("Stroke Alpha", Range(0,1)) = 0.4
//        _StrokeColor ("Stroke Color", Color) = (0.6, 0.2, 1, 1)
//        _Stroke2 ("Stroke Alpha", Range(0,1)) = 0.6
//        _StrokeColor2 ("Stroke Color", Color) = (0.2, 0.2, 1, 1)
    }
    
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        Cull Off
        ZWrite Off
        Blend SrcAlpha OneMinusSrcAlpha
        
        Pass
        {
            HLSLPROGRAM

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            #pragma vertex vert
            #pragma fragment frag

            struct  Attribute
            {
                uint vertexID : SV_VertexID;
            };

            struct Varying
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D_X(_MainTex);
            // https://docs.unity3d.com/Manual/SL-SamplerStates.html
            SAMPLER(sampler_linear_clamp_MainTex);
            TEXTURE2D_X(_OcclusionTex);
            SAMPLER(sampler_linear_clamp_OcclusionTex);

            half _Cutoff;
            half _AlphaOffset;
            half _HardEdges;
            
            // half _EdgeClip;
            // half4 _Color;
            //
            // half _Stroke;
            // half4 _StrokeColor;
            //
            // half _Stroke2;
            // half4 _StrokeColor2;

            Varying vert(Attribute IN)
            {
                Varying OUT;
                
                // 使用方法库函数生成全屏三角形顶点位置和UV坐标。
                OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
                OUT.uv = GetFullScreenTriangleTexCoord(IN.vertexID);
                
                return OUT;
            }

            half3 Saturation(half3 color, half saturation)
            {
                half gray = dot(color, half3(0.299, 0.587, 0.114));
                return lerp(half3(gray, gray, gray), color, saturation);
            }

            half4 frag(Varying IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv);
                clip(col.a - _Cutoff);
                half4 colOcclusionTex = SAMPLE_TEXTURE2D_X(_OcclusionTex, sampler_linear_clamp_OcclusionTex, IN.uv);
                clip(1 - colOcclusionTex.a);

                // 按照遮挡纹理 alpha 混合
                col = lerp(col, colOcclusionTex, colOcclusionTex.a);

                col.a = saturate(col.a + _AlphaOffset);
                // TODO: 允许添加一个颜色并设置混合权重。
                return col;
                
				// half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv);
				// clip(col.a - _Cutoff);
                //
                //col.a = saturate(col.a + _AlphaOffset);

                // if (col.a < _Stroke) col = _StrokeColor;
				// else if (col.a < _Stroke2) col = _StrokeColor2;
                // else col = _Color;

                // // 计算边缘权重
                // half edgeWeight = saturate((col.a - _EdgeClip) / (1.0 - _EdgeClip));
                //
                // // 以原始透明度为基础，边缘更高，中心更低
                // half edgeAlpha = col.a * 1.2; // 边缘增强
                // half centerAlpha = col.a * 0.7; // 中心降低
                //
                // if (edgeWeight < 0.5)
                //     col.a = lerp(edgeAlpha, centerAlpha, edgeWeight * 2.0);
                // else
                //     col.a = lerp(centerAlpha, edgeAlpha, (edgeWeight - 0.5) * 2.0);
                //
                // // 增加饱和度，saturation > 1 表示增强
                // col.rgb = Saturation(col.rgb, 10);
                
				// return col;
            }
            ENDHLSL
        }
    }
}
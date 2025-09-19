// References:
// https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/renderer-features/how-to-fullscreen-blit.html

// 流体效果着色器，用于实现类似液体的视觉效果。
// 使用之前模糊过的纹理作为输入，结合透明度裁剪和颜色调整来模拟液体边缘和内部的视觉特性。
Shader "Custom/URP/2D/Liquid2DEffect"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        Pass
        {
            Name "Liquid2DEffect"
            
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            
            HLSLPROGRAM
            // ---- Keywords ------------------------------------- Start
            #pragma shader_feature_local _OPACITY_REPLACE // 透明度倍率使用覆盖方式，而不是乘法方式。
            // ---- Keywords ------------------------------------- End

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

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
            TEXTURE2D_X(_ObstructionTex);
            SAMPLER(sampler_linear_clamp_ObstructionTex);

            half _Cutoff; // 透明度裁剪阈值。
            half _OpacityValue; // 透明度值，用法和模式相关。
            half4 _CoverColor; // 叠加颜色。
            half _EdgeIntensity; // 边缘颜色强度。越强边缘越宽。
            half4 _EdgeColor; // 边缘颜色。

            Varying Vert(Attribute IN)
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

            half4 Frag(Varying IN) : SV_Target
            {
                half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv);

                // ---- 透明度裁剪 ---- //
                // 裁剪掉透明度低于阈值的像素，形成流体边缘效果。
                clip(col.a - _Cutoff);

                // ---- 颜色处理 ---- //
                // 覆盖颜色，用于整体调节流体颜色。
                col.rgb = lerp(col.rgb, _CoverColor.rgb, _CoverColor.a);

                // ---- 边缘颜色-计算lerp值 ---- //
                half edgeRange = _EdgeIntensity * (1 - _Cutoff);
                half edgeStart = _Cutoff;

                // 归一化 t，超出范围自动截断到 [0,1]。
                half t = saturate((col.a - edgeStart) / max(edgeRange, 1e-5));


                // ---- 透明度处理 ---- //
                #if defined(_OPACITY_REPLACE)
                col.a = _OpacityValue; // 直接覆盖透明度。
                #else
                col.a = col.a * _OpacityValue;
                #endif

                // ---- 边缘颜色-应用边缘色 ---- //
                col = lerp(_EdgeColor, col, t);

                // ---- 阻挡纹理处理 ---- //
                // 阻挡纹理完全阻挡流体颜色。一般是挡板、管道、瓶子等。
                half4 colObstructionTex = SAMPLE_TEXTURE2D_X(_ObstructionTex, sampler_linear_clamp_ObstructionTex, IN.uv);
                clip(1 - colObstructionTex.a);
                // 阻挡物挡住流体颜色。
                col = lerp(col, colObstructionTex, step(0.001, colObstructionTex.a));
                
                // TODO: 可以正面遮挡流体的颜色处理，比如玻璃瓶的正面，给流体盖上一层透明颜色。Occluder层。
                
                return col;
            }
            ENDHLSL
        }
    }
}
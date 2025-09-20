// References:
// https://docs.unity3d.com/Packages/com.unity.render-pipelines.universal@14.0/manual/renderer-features/how-to-fullscreen-blit.html

// 流体效果着色器，用于实现类似液体的视觉效果。
// 使用之前模糊过的纹理作为输入，结合透明度裁剪和颜色调整来模拟液体边缘和内部的视觉特性。
Shader "Custom/URP/2D/Liquid2DEffect"
{
    Properties
    {
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.3 // 透明度裁剪阈值。低于此值的像素将被裁剪掉，形成流体边缘效果。
        _OpacityValue ("Opacity Value", Range(0,1)) = 0.6 // 透明度值，用法和模式相关。会相乘或覆盖原有透明度。
        _CoverColor ("Cover Color", Color) = (0,0,0,0) // 叠加颜色，用于整体调节流体颜色。透明度为0时不影响颜色。
        _EdgeIntensity ("Edge Intensity", Range(0,1)) = 0.2 // 边缘颜色强度。越强边缘越宽。
        _EdgeColor ("Edge Color", Color) = (1,1,1,1) // 边缘颜色。

        // 背景扰动相关参数。在流体为透明时会看到背景被扰动扭曲的效果。
        _Magnitude ("Distort Magnitude", Range(0, 1)) = 0.1 // 扰动采样缩放。值越大，扰动越频繁。
        _Frequency ("Noise Frequency", Range(1, 500)) = 380 // 扰动频率。值越大，扰动越密集。
        _Amplitude ("Distort Amplitude", Range(0, 0.1)) = 0.008 // 扰动强度。值越大，扰动越明显。
        _DistortSpeed ("Distort Speed", Vector) = (0.1, 1, 0, 0) // 扰动速度。x分量控制x方向扰动速度，y分量控制y方向扰动速度。
        _DistortTimeFactors ("Distort Time Factors", Vector) = (0.3, -0.4, 0.1, 0.5) // 扰动时间系数。用于控制不同噪点的运动速度和方向。
        _NoiseCoordOffset ("Noise Coord Offset", Float) = 4.0 // 噪点坐标偏移。用于增加噪点的复杂度，防止重复模式。
    }
    
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
            #pragma shader_feature_local _DISTORT // 开启水体扰动。
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
            // 水体扰动背景相关参数。

            #if defined(_DISTORT)
            TEXTURE2D_X(_BackgroundTex);
            SAMPLER(sampler_linear_clamp_BackgroundTex);
            #endif
            
CBUFFER_START(UnityPerMaterial)
            
            half _Cutoff; // 透明度裁剪阈值。
            half _OpacityValue; // 透明度值，用法和模式相关。
            half4 _CoverColor; // 叠加颜色。
            half _EdgeIntensity; // 边缘颜色强度。越强边缘越宽。
            half4 _EdgeColor; // 边缘颜色。
            
            #if defined(_DISTORT)
            // 计算扰动方式。
            float _Magnitude; // 扰动采样缩放。
            float _Frequency; // 噪点频率。
            float _Amplitude; // 扰动强度。
            float2 _DistortSpeed; // 扰动速度。
            float4 _DistortTimeFactors; // 扰动时间系数。
            float _NoiseCoordOffset; // 噪点坐标偏移。

            // 如果使用贴图控制扰动效果，可以启用下面的代码。
            // 噪声纹理采样方式。
            // TEXTURE2D_X(_DistortTex);
            // SAMPLER(sampler_linear_repeat_DistortTex); // 重复采样，防止边缘断裂。注意是 repeat 不是 clamp。
            // half _DistortIntensity; // 扰动强度。
            // half _AspectRatio; // 视口宽高比，用于修正UV坐标。
            #endif
CBUFFER_END
            
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

            float random (float2 uv)
            {
                return frac(sin(dot(uv,float2(12.9898,78.233)))*43758.5453123);
            }
            
            float noise(float2 coord)
            {
                float2 i = floor(coord);
                float2 f = frac(coord);

                float a = random(i);
                float b = random(i + float2(1.0, 0.0));
                float c = random(i + float2(0.0, 1.0));
                float d = random(i + float2(1.0, 1.0));

                float2 cubic = f * f * (3.0 - 2.0 * f);

                return lerp(a, b, cubic.x) + (c - a) * cubic.y * (1.0 - cubic.x) + (d - b) * cubic.x * cubic.y;
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
                // 阻挡纹理完全阻挡流体颜色。一般是挡板、管道、瓶子的横截面等。
                half4 colObstructionTex = SAMPLE_TEXTURE2D_X(_ObstructionTex, sampler_linear_clamp_ObstructionTex, IN.uv);
                clip(1 - colObstructionTex.a);
                // 阻挡物挡住流体颜色。
                col = lerp(col, colObstructionTex, step(0.001, colObstructionTex.a));
                
                // Tips:
                // 注意，我们只是裁剪掉了阻挡纹理的透明部分，阻挡纹理本身的颜色并没有参与混合。
                // 因为流体系统只专注于处理流体效果自身。
                // 关于遮挡流体的物体，比如一个玻璃瓶。你应当将玻璃瓶的横截面的 Rendering Layer 设置为阻挡层。
                // 但是正面盖在流体上的玻璃瓶部分的渲染，应当由用户自行处理。比如创建新的 Renderer Feature 来渲染玻璃瓶。
                // 这个玻璃瓶的 Renderer Feature 应当在流体 Renderer Feature 之后执行。
                
                // ---- 水体扰动背景 ---- //
                #if defined(_DISTORT)
                
                float time = _Time.y;
                float2 noisecoord1 = IN.uv * _Frequency * (_Magnitude);
                float2 noisecoord2 = IN.uv * _Frequency * (_Magnitude) + _NoiseCoordOffset;
                float2 motion1 = float2(time * _DistortTimeFactors.x, time * _DistortTimeFactors.y) * _DistortSpeed.x;
                float2 motion2 = float2(time * _DistortTimeFactors.z, time * _DistortTimeFactors.w) * _DistortSpeed.y;

                // - float2(0.5,0.5) 是为了让噪点图的值在 -0.5 到 0.5 之间波动，而不是 0 到 1 之间。
                float2 distort1 = float2(noise(noisecoord1 + motion1), noise(noisecoord2 + motion1)) - float2(0.5,0.5);
                float2 distort2 = float2(noise(noisecoord1 + motion2), noise(noisecoord2 + motion2)) - float2(0.5,0.5);
            
                // 计算最终采样偏移。
                float2 distort_sum = (distort1 + distort2) * _Amplitude;
                //return half4(distort_sum,0,1);
                float2 uvDistorted = saturate(IN.uv + distort_sum);
                
                // 采样背景纹理。
				half4 colBg = SAMPLE_TEXTURE2D_X(_BackgroundTex, sampler_linear_clamp_BackgroundTex, uvDistorted);
                // 混合流体颜色和背景颜色。
                half4 finalColor;
                finalColor.rgb = col.rgb * col.a + colBg.rgb * (1 - col.a);
                // 正常混合是 col.a + colBg.a * (1 - col.a) 但是 colBg.a 是1，所以简化了。
                finalColor.a = 1.0; // 混合以后已经包含了背景色，不需要和原有背景混合了，直接不透明。
                return finalColor;

                // ---- 使用贴图控制扰动效果的方式 ---- //
                // float2 distortUv = IN.uv;
                // distortUv.x *= _AspectRatio;
                //
                // float2 _DistortSpeed = float2(1,1);
                // float _DistortScale = 1;
                // _DistortIntensity = 0.01;
                // distortUv = distortUv * _DistortScale + _Time.y * _DistortSpeed;
                // half2 distort = SAMPLE_TEXTURE2D_X(_DistortTex, sampler_linear_repeat_DistortTex, distortUv) * 2 - 1;
                // float2 uvDistorted = saturate(IN.uv + distort * _DistortIntensity);
                // half4 bg = SAMPLE_TEXTURE2D_X(_BackgroundTex, sampler_linear_clamp_BackgroundTex, uvDistorted);
                // half4 finalCol = lerp(bg, col, col.a);
                // return finalCol;
                
                #else
                return col;
                #endif
            }
            ENDHLSL
        }
    }
}
// 流体模糊着色器，用于将所有粒子进行多次模糊处理，让颜色连接在一起，然后供之后的处理。
Shader "Custom/URP/2D/Liquid2DBlur"
{
    Properties
    {
        _BlurOffset ("Blur Size", Float) = 1.0
        _AlphaThresholdMin("Alpha Threshold Min", Float) = 0.7
        _AlphaThresholdMax("Alpha Threshold Max", Float) = 0.95
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
            Name "Liquid2DBlur"
            
            Blend One Zero
            Cull Off
            ZWrite Off
        
            HLSLPROGRAM

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
            SAMPLER(sampler_linear_clamp_MainTex);
            half2 _MainTex_TexelSize;
            
            half _BlurOffset;
            float _AlphaThresholdMin;
            float _AlphaThresholdMax;

            Varying Vert(Attribute IN)
            {
                Varying OUT;
                
                // 使用方法库函数生成全屏三角形顶点位置和UV坐标。
                OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
                OUT.uv = GetFullScreenTriangleTexCoord(IN.vertexID);
                
                return OUT;
            }

            half4 Frag(Varying IN) : SV_Target
            {
                // ---- Describe ---- //
                // 1. 采样根据当前像素，向四个斜方向偏移 _BlurOffset 个像素进行采样，然后取平均值作为模糊后的颜色。
                // 2. _BlurOffset 过大会导致本身有颜色的区域采样四个方向时获得的颜色都是无色的，从而导致颜色变淡甚至消失。
                // 3. 0.5 半个像素偏移是为了让采样点落在像素中心，防止采样到边缘颜色。

                // https://zhuanlan.zhihu.com/p/632957274
                // Kawase Blur 进行简单的模糊处理，采样周围像素并平均。

                // 采样中心。将自身颜色也加入混合，更好的保持自身颜色。
                half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv) * 4;
                // 四个斜角。
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv + float2(_BlurOffset + 0.5, _BlurOffset + 0.5) * _MainTex_TexelSize);
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv + float2(-_BlurOffset - 0.5, _BlurOffset + 0.5) * _MainTex_TexelSize);
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv + float2(-_BlurOffset - 0.5, -_BlurOffset - 0.5) * _MainTex_TexelSize);
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv + float2(_BlurOffset + 0.5, -_BlurOffset - 0.5) * _MainTex_TexelSize);
                
                // 权重归一化。
                return col * 0.125;
            }
            
            ENDHLSL
        }
    }
}
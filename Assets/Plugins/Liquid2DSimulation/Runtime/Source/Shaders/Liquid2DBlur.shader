// 流体模糊着色器，用于将所有粒子进行多次模糊处理，让有颜色的区域连接在一起，然后供之后的Effect处理。
Shader "Custom/URP/2D/Liquid2DBlur"
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
            Name "Liquid2DBlur"
            
            Blend One Zero
            Cull Off
            ZWrite Off
        
            HLSLPROGRAM
            // ---- Keywords ------------------------------------- Start
            #pragma shader_feature_local _IGNORE_BG_COLOR // 忽略背景色。
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
                half4 uv01 : TEXCOORD0; // xy: uv0, zw: uv1
                half4 uv23 : TEXCOORD1; // xy: uv2, zw: uv3
                half2 uv4  : TEXCOORD2; // uv4
            };

            TEXTURE2D_X(_MainTex);
            SAMPLER(sampler_linear_clamp_MainTex);
            half2 _MainTex_TexelSize;
            
            half _BlurOffset;

            Varying Vert(Attribute IN)
            {
                Varying OUT;
                half2 uv = GetFullScreenTriangleTexCoord(IN.vertexID);
                
                // 使用方法库函数生成全屏三角形顶点位置和UV坐标。
                OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
                OUT.uv01.xy = uv;
                OUT.uv01.zw = uv + half2(_BlurOffset + 0.5, _BlurOffset + 0.5) * _MainTex_TexelSize;   // 右上
                OUT.uv23.xy = uv + half2(-_BlurOffset - 0.5, _BlurOffset + 0.5) * _MainTex_TexelSize;  // 左上
                OUT.uv23.zw = uv + half2(-_BlurOffset - 0.5, -_BlurOffset - 0.5) * _MainTex_TexelSize; // 左下
                OUT.uv4     = uv + half2(_BlurOffset + 0.5, -_BlurOffset - 0.5) * _MainTex_TexelSize;  // 右下
                
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
                
                #if defined(_IGNORE_BG_COLOR)
                
                half4 col0 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv01.xy);
                half4 col1 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv01.zw);
                half4 col2 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv23.xy);
                half4 col3 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv23.zw);
                half4 col4 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv4);

                // 中心点权重4，其余为1
                half w0 = col0.a * 4;
                half w1 = col1.a;
                half w2 = col2.a;
                half w3 = col3.a;
                half w4 = col4.a;

                half totalWeight = w0 + w1 + w2 + w3 + w4 + 1e-5;
                half3 rgb = (col0.rgb * w0 + col1.rgb * w1 + col2.rgb * w2 + col3.rgb * w3 + col4.rgb * w4) / totalWeight;
                half a = (col0.a * 4 + col1.a + col2.a + col3.a + col4.a) * 0.125;

                return half4(rgb, a);
                
                #else
                // 采样中心。将自身颜色也加入混合，更好的保持自身颜色。中心权重更高。
                half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv01.xy) * 4;
                // 四个斜角。
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv01.zw);
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv23.xy);
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv23.zw);
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv4);
                // 权重归一化。
                return col * 0.125;
                #endif
            }
            
            ENDHLSL
        }
    }
}
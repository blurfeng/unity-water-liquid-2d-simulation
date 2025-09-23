// 流体模糊着色器，用于将所有粒子进行多次模糊处理，让有颜色的区域连接在一起，然后供之后的Effect处理。
// Liquid blur shader, used to perform multiple blurring of all particles, connecting colored areas together, and then providing for subsequent Effect processing.
// 流体のぼかしシェーダーで、すべての粒子を複数回ぼかし処理し、色の付いた領域を一緒に接続し、その後のエフェクト処理に提供します。
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
            // 忽略背景色。 // Ignore background color. // 背景色を無視します。
            #pragma shader_feature_local _IGNORE_BG_COLOR
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
                // Use the utility library function to generate full-screen triangle vertex positions and UV coordinates.
                // ユーティリティライブラリ関数を使用して、フルスクリーントライアングルの頂点位置とUV座標を生成します。
                OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
                OUT.uv01.xy = uv;
                OUT.uv01.zw = uv + half2(_BlurOffset + 0.5, _BlurOffset + 0.5) * _MainTex_TexelSize;   // 右上 // Top right // 右上
                OUT.uv23.xy = uv + half2(-_BlurOffset - 0.5, _BlurOffset + 0.5) * _MainTex_TexelSize;  // 左上 // Top left // 左上
                OUT.uv23.zw = uv + half2(-_BlurOffset - 0.5, -_BlurOffset - 0.5) * _MainTex_TexelSize; // 左下 // Bottom left // 左下
                OUT.uv4     = uv + half2(_BlurOffset + 0.5, -_BlurOffset - 0.5) * _MainTex_TexelSize;  // 右下 // Bottom right // 右下
                
                return OUT;
            }

            half4 Frag(Varying IN) : SV_Target
            {
                // ---- 描述 // Description // 説明 ---- //
                // 1. 采样根据当前像素，向四个斜方向偏移 _BlurOffset 个像素进行采样，然后取平均值作为模糊后的颜色。
                // 2. _BlurOffset 过大会导致本身有颜色的区域采样四个方向时获得的颜色都是无色的，从而导致颜色变淡甚至消失。
                // 3. 0.5 半个像素偏移是为了让采样点落在像素中心，防止采样到边缘颜色。

                // 1. Sample by offsetting _BlurOffset pixels in four diagonal directions from the current pixel, then take the average as the blurred color.
                // 2. If _BlurOffset is too large, the area with its own color will sample colorless colors in four directions, resulting in color fading or even disappearance.
                // 3. The 0.5 half-pixel offset is to let the sampling point fall in the center of the pixel to prevent sampling edge colors.
                
                // 1. 現在のピクセルから4つの斜め方向に_BlurOffsetピクセルオフセットしてサンプリングし、平均をぼかした色として取得します。
                // 2. _BlurOffsetが大きすぎると、自身の色がある領域が4つの方向で無色の色をサンプリングし、色が薄くなったり消えたりします。
                // 3. 0.5の半ピクセルオフセットは、サンプリングポイントをピクセルの中心に落とし、エッジカラーのサンプリングを防ぐためです。

                // https://zhuanlan.zhihu.com/p/632957274
                // Kawase Blur 进行简单的模糊处理，采样周围像素并平均。
                // Simple blur processing using Kawase Blur, sampling surrounding pixels and averaging.
                // Kawase Blurを使用したシンプルなぼかし処理で、周囲のピクセルをサンプリングして平均化します。
                
                #if defined(_IGNORE_BG_COLOR)
                // 忽略背景色模式。会根据透明度进行加权平均，透明度高的颜色权重更大。
                // Ignore background color mode. Weighted average based on transparency, higher transparency means greater weight.
                // 背景色を無視するモード。透明度に基づいて加重平均を行い、透明度が高いほど重みが大きくなります。
                
                half4 col0 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv01.xy);
                half4 col1 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv01.zw);
                half4 col2 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv23.xy);
                half4 col3 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv23.zw);
                half4 col4 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv4);

                // 中心点权重4，其余为1。 // Center point weight 4, others 1. // 中心点の重みは4、他は1。
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
                // 普通模糊模式。直接平均采样的颜色。
                // Normal blur mode. Directly average the sampled colors.
                // 通常のぼかしモード。サンプリングした色を直接平均化します。

                // 采样中心。将自身颜色也加入混合，更好的保持自身颜色。中心权重更高。
                // Sample the center. Include its own color in the mix to better maintain its own color. Center weight is higher.
                // 中心をサンプリングします。自身の色をミックスに含めて、自身の色をよりよく維持します。中心の重みは高くなります。
                half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv01.xy) * 4;
                // 四个斜角。 // Four diagonals. // 4つの斜め。
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv01.zw);
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv23.xy);
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv23.zw);
                col += SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv4);
                // 权重归一化。 // Weight normalization. // 重みの正規化。
                return col * 0.125;
                #endif
            }
            
            ENDHLSL
        }
    }
}
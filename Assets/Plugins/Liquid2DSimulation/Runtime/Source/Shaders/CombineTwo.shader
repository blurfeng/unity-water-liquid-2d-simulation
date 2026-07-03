// 合并两张纹理。主要用于合并模糊后的和模糊过程中核心更清晰的纹理，这样可以保持核心范围的清晰度。
// Combine two textures. Mainly used to combine the blurred and the core clearer texture during the blurring process, which can maintain the clarity of the core area.
// 2つのテクスチャを組み合わせます。主に、ぼかし処理中にぼかされたテクスチャとコアのより鮮明なテクスチャを組み合わせるために使用され、コア領域の鮮明さを維持できます。
Shader "Custom/URP/2D/CombineTwo"
{
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        Pass
        {
            Name "CombineTwo"
            
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

            TEXTURE2D_X(_SecondTex);
            SAMPLER(sampler_linear_clamp_SecondTex);
            
            Varying Vert(Attribute IN)
            {
                Varying OUT;
                
                // 使用方法库函数生成全屏三角形顶点位置和UV坐标。
                // Use the utility library function to generate full-screen triangle vertex positions and UV coordinates.
                // ユーティリティライブラリ関数を使用して、フルスクリーントライアングルの頂点位置とUV座標を生成します。
                OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
                OUT.uv = GetFullScreenTriangleTexCoord(IN.vertexID);
                
                return OUT;
            }

            half4 Frag(Varying IN) : SV_Target
            {
                float2 uv = IN.uv;
                
                half4 col1 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, uv);
                half4 col2 = SAMPLE_TEXTURE2D_X(_SecondTex, sampler_linear_clamp_SecondTex, uv);
                
                // 用_SecondTex的 alpha 做权重，核心更清晰。
                // Use the alpha of _SecondTex as weight, the core is clearer.
                // _SecondTexのアルファを重みとして使用し、コアはより鮮明です。
                half4 color;
                color.rgb = lerp(col1.rgb, col2.rgb, col2.a);
                color.a   = max(col1.a, col2.a);
                return color;
            }
            
            ENDHLSL
        }
    }
}
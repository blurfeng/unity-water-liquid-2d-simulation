// 抓取当前相机渲染结果作为背景。
// Grab the current camera rendering result as the background.
// 現在のカメラのレンダリング結果を背景としてキャプチャします。
Shader "Custom/URP/2D/GrabAsBg"
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
            Name "GrabAsBg"
            
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

            struct Output
            {
                half4 color0 : SV_Target0;
                half4 color1 : SV_Target1;
            };

            TEXTURE2D_X(_MainTex);
            SAMPLER(sampler_linear_clamp_MainTex);

            half4 _Color;
            float _ColorIntensity;

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
            
            Output Frag(Varying IN)
            {
                Output OUT;
                
                float2 uv = IN.uv;
                
                // Blitter.BlitTexture方法中直接传入的 source 参数会被转换为_BlitTexture。
                // The source parameter passed directly in the Blitter.BlitTexture method will be converted to _BlitTexture.
                // Blitter.BlitTextureメソッドで直接渡されるsourceパラメーターは、_BlitTextureに変換されます。
                half4 color = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, uv);

                // 输出原始颜色。 // Output the original color. // 元の色を出力します。
                OUT.color0 = color;
                
                // 只保留颜色，丢弃alpha。允许自定义背景颜色，否则使用当前相机纹理作为背景色。
                // Keep only the color and discard alpha. Allow custom background color, otherwise use current camera texture as background color.
                // 色のみを保持し、アルファを破棄します。カスタム背景色を許可します。そうしないと、現在のカメラテクスチャが背景色として使用されます。
                OUT.color1 = half4(lerp(color.rgb, _Color.rgb, _ColorIntensity), 0);

                return OUT;
            }
            
            ENDHLSL
        }
    }
}
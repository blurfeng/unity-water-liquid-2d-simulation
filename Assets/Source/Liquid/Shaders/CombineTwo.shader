Shader "Custom/URP/2D/CombineTwo"
{
    Properties
    {
        _TexFirst ("First Blur", 2D) = "white" {}
        _TexLast  ("Last Blur", 2D)  = "white" {}
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline" = "UniversalPipeline" }
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
            SAMPLER(sampler_linear_clamp_MainTex);

            TEXTURE2D_X(_SecondTex);
            SAMPLER(sampler_linear_clamp_SecondTex);
            
            Varying vert(Attribute IN)
            {
                Varying OUT;
                
                // 使用方法库函数生成全屏三角形顶点位置和UV坐标。
                OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
                OUT.uv = GetFullScreenTriangleTexCoord(IN.vertexID);
                
                return OUT;
            }

            half4 frag(Varying IN) : SV_Target
            {
                float2 uv = IN.uv;
                
                half4 col1 = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, uv);
                half4 col2 = SAMPLE_TEXTURE2D_X(_SecondTex, sampler_linear_clamp_SecondTex, uv);
                
                // 用_SecondTex的alpha做权重，核心更清晰
                half4 color;
                color.rgb = lerp(col1.rgb, col2.rgb, col2.a);
                color.a   = max(col1.a, col2.a);
                return color;
            }
            
            ENDHLSL
        }
    }
}
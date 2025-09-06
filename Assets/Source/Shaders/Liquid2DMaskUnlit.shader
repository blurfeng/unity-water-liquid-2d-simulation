Shader "Custom/Liquid2DMaskUnlit"
{
    Properties
    {
        _MainTex ("Texture", 2D) = "white" {}
        _Color ("Main Color", Color) = (0.2, 0.6, 1, 1)
        _Cutoff("Alpha cutoff", Range(0,1)) = 0.25
        _Stroke ("Stroke Alpha", Range(0,1)) = 0.48
        _StrokeColor ("Stroke Color", Color) = (0.2, 0.6, 1, 1)
        _Stroke2 ("Stroke Alpha", Range(0,1)) = 0.1
        _StrokeColor2 ("Stroke Color", Color) = (0.2, 0.6, 1, 1)
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
                half2 taps[4] : TEXCOORD1;
            };

            TEXTURE2D_X(_MainTex);
            SAMPLER(sampler_linear_clamp_MainTex);

            half _Cutoff;

			half4 _Color;

			half _Stroke;
			half4 _StrokeColor;

			half _Stroke2;
			half4 _StrokeColor2;

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
				half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, IN.uv);
				clip(col.a - _Cutoff);

				if (col.a < _Stroke) col = _StrokeColor;
				else if (col.a < _Stroke2) col = _StrokeColor2;
                else col = _Color;
				return col;
            }
            
            ENDHLSL
        }
    }
}
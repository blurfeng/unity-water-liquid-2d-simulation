Shader "Custom/URP/2D/Clone"
{
    SubShader
    {
        Tags
        {
            "RenderType"="Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        ZTest Off ZWrite Off Cull Off
        Pass
        {
            Name "Clone"
            
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/Runtime/Utilities/Blit.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag
            
            half4 Frag(Varyings input) : SV_Target0
            {
                float2 uv = input.texcoord.xy;
                
                // Blitter.BlitTexture方法中直接传入的 source 参数会被转换为_BlitTexture。
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return color;
            }
            
            ENDHLSL
        }
    }
}
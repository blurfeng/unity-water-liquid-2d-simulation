// 使用 Blend SrcAlpha OneMinusSrcAlpha 模式克隆纹理到目标纹理。
// Use Blend SrcAlpha OneMinusSrcAlpha mode to clone the texture to the target texture.
// Blend SrcAlpha OneMinusSrcAlpha モードを使用して、テクスチャをターゲットテクスチャにクローンします。
Shader "Custom/URP/2D/Clone"
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
                // The source parameter passed directly in the Blitter.BlitTexture method will be converted to _BlitTexture.
                // Blitter.BlitTextureメソッドで直接渡されるsourceパラメーターは、_BlitTextureに変換されます。
                half4 color = SAMPLE_TEXTURE2D_X(_BlitTexture, sampler_LinearClamp, uv);
                return color;
            }
            
            ENDHLSL
        }
    }
}
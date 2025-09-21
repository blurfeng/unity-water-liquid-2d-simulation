// 流体粒子着色器，用于将所有流体粒子通过 GPU Instancing 一次性渲染到纹理上。
Shader "Custom/URP/2D/Liquid2DParticle"
{
    SubShader
    {
        Tags
        {
            "RenderType"="Transparent"
            "Queue"="Transparent"
            "RenderPipeline" = "UniversalPipeline"
        }

        Pass
        {
            Name "Liquid2DParticle"
            
            // alpha 使用此混合方式能使粒子在更远就开始融合。虽然 alpha 可能会超过1，但视觉上没问题。
            // 并且防止 SrcAlpha OneMinusSrcAlpha 混合方式在 alpha 过低时反而 alpha 降低。
            // 关于配置的 sprite: 如果你想使粒子在更远时黏连，可以使用更大的甚至超出尺寸范围的圆形扩散图形。
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
            UNITY_INSTANCING_BUFFER_END(UnityPerMaterial)
            
            struct Attribute
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;

                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varying
            {
                float2 uv : TEXCOORD0;
                float4 positionCS : SV_POSITION;

                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            Varying Vert(Attribute IN)
            {
                Varying OUT = (Varying)0;

                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);

                OUT.uv = IN.uv;
                OUT.positionCS = TransformObjectToHClip(IN.positionOS);
                return OUT;
            }

            half4 Frag(Varying IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);

                float4 color = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Color);
                half4 texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half4 col = texCol * color;
                return col;
            }
            
            ENDHLSL
        }
    }
}
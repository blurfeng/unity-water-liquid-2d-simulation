// 流体粒子着色器，用于将所有流体粒子通过 GPU Instancing 一次性渲染到纹理上。
// Liquid particle shader, used to render all liquid particles to a texture at once through GPU Instancing.
// 流体粒子シェーダー。GPUインスタンシングを使用して、すべての流体粒子を一度にテクスチャにレンダリングします。
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
            
            // alpha 使用 One OneMinusSrcAlpha 能使粒子在更远就开始融合。虽然 alpha 可能会超过1，但视觉上没问题。
            // 并且防止 SrcAlpha OneMinusSrcAlpha 混合方式在 alpha 过低时反而 alpha 降低。
            // 关于配置的 sprite: 如果你想使粒子在更远时黏连，可以使用更大的甚至超出尺寸范围的圆形扩散图形。
            
            // Use "One OneMinusSrcAlpha" blending mode to make particles start to merge from a farther distance. Although alpha may exceed 1, it looks fine visually.
            // It also prevents the issue where "SrcAlpha OneMinusSrcAlpha" blending mode reduces alpha when it's too low.
            // Regarding the sprite configuration: If you want particles to stick together from a farther distance, you can use a larger circular diffusion graphic, even exceeding the size range.
            
            // 「One OneMinusSrcAlpha」ブレンドモードを使用すると、粒子がより遠くから融合し始めます。アルファが1を超えることがありますが、視覚的には問題ありません。
            // また、「SrcAlpha OneMinusSrcAlpha」のブレンドモードがアルファが低すぎるとアルファを減少させる問題を防ぎます。
            // スプライトの設定について：粒子がより遠くから結合するようにしたい場合は、サイズ範囲を超える大きな円形の拡散グラフィックを使用できます。
            
            // RT0：颜色(RGB) + 覆盖度(A)，与原先一致。RT1（仅 _OPACITY_FIELD 开启时绑定）：独立透明度场，
            // 加性累加 R=Σ(覆盖×O)、G=Σ覆盖，合成阶段 O=R/G 得到覆盖度加权的最终透明度倍率（与形状解耦）。
            // RT1 用加性混合 One One，故不依赖目标自身的 src-alpha，单通道对累加无影响；RT1 每帧须清零（由 Pass 端保证）。
            // RT0: color(RGB) + coverage(A), unchanged. RT1 (bound only when _OPACITY_FIELD): the independent opacity field,
            // additively accumulating R=Σ(coverage×O), G=Σcoverage; the composite computes O=R/G (coverage-weighted final opacity,
            // decoupled from shape). RT1 uses additive One One (no dependence on its own dst src-alpha); Pass clears RT1 each frame.
            Blend 0 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Blend 1 One One
            Cull Off
            ZWrite Off

            HLSLPROGRAM

            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #pragma instancing_options assumeuniformscaling
            // 独立透明度场开关（由 C# 运行时按「是否有非恒1透明度曲线的粒子」切换；随之绑定第二渲染目标）。
            // Independent opacity field toggle (switched at runtime by C# based on whether any drawn particle has a non-constant-1
            // opacity curve; the second render target is bound together with it). // 独立透明度場スイッチ（C# が実行時に切替）。
            #pragma multi_compile_local _ _OPACITY_FIELD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            UNITY_INSTANCING_BUFFER_START(UnityPerMaterial)
                UNITY_DEFINE_INSTANCED_PROP(float4, _Color)
                // 逐实例最终透明度倍率 O（0..1）。仅 _OPACITY_FIELD 开启时由 C# 逐粒子填充；否则未使用。
                // Per-instance final opacity multiplier O (0..1). Filled per particle by C# only when _OPACITY_FIELD; unused otherwise.
                UNITY_DEFINE_INSTANCED_PROP(float, _Opacity)
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
                OUT.positionCS = TransformObjectToHClip(IN.positionOS.xyz);
                return OUT;
            }

            // 采样出 RT0 颜色（tex×color），并取出逐实例透明度倍率 O（供 RT1 使用）。
            // Sample the RT0 color (tex×color) and fetch the per-instance opacity multiplier O (for RT1).
            half4 SampleParticle(Varying IN, out half opacityMul)
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                float4 color = UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Color);
                opacityMul = (half)UNITY_ACCESS_INSTANCED_PROP(UnityPerMaterial, _Opacity);
                half4 texCol = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return texCol * color;
            }

            #if defined(_OPACITY_FIELD)
            struct FragOut
            {
                half4 color : SV_Target0; // RGB=颜色, A=覆盖度。 // RGB=color, A=coverage.
                half2 op    : SV_Target1; // R=覆盖×O, G=覆盖。 // R=coverage×O, G=coverage.
            };
            FragOut Frag(Varying IN)
            {
                half o;
                half4 col = SampleParticle(IN, o);
                FragOut OUT;
                OUT.color = col;
                half cov = col.a;
                OUT.op = half2(cov * o, cov);
                return OUT;
            }
            #else
            half4 Frag(Varying IN) : SV_Target
            {
                half o;
                return SampleParticle(IN, o);
            }
            #endif

            ENDHLSL
        }
    }
}
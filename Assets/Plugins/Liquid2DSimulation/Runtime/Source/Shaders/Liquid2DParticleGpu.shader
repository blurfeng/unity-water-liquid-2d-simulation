// GPU 常驻流体粒子着色器（程序化绘制）。配合 SphGpuSolver 的常驻 GPU 缓冲，通过 DrawProcedural 直接绘制：
// 每个实例 = 一个粒子，6 顶点拼出一个面向屏幕的四边形；位置/颜色/半径/类型从 StructuredBuffer 按 slot 读取
// （slot = _ActiveIndices[instanceID]），类型不匹配 _TargetType 的实例输出到裁剪域外被剔除。
// 视觉与 CPU 路径（Liquid2DParticle + DrawMeshInstanced）一致：直径 = 半径 × 2 × renderScale，四边形 [-0.5,0.5]。
//
// GPU-resident fluid particle shader (procedural). Paired with SphGpuSolver's resident GPU buffers, drawn via
// DrawProcedural: each instance is one particle, 6 vertices form a screen quad; position/color/radius/type are read from
// StructuredBuffers by slot (slot = _ActiveIndices[instanceID]); instances whose type != _TargetType are culled offscreen.
// Matches the CPU path visually: diameter = radius × 2 × renderScale, quad spans [-0.5,0.5].
//
// GPU 常駐流体粒子シェーダー（プロシージャル描画）。SphGpuSolver の常駐 GPU バッファと組で DrawProcedural で描画。
Shader "Custom/URP/2D/Liquid2DParticleGpu"
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
            Name "Liquid2DParticleGpu"

            // 与 Liquid2DParticle 一致的混合，使粒子远距离即开始融合。 // Same blend as Liquid2DParticle for far-distance merging. // 同一ブレンド。
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float2> _Positions;
            StructuredBuffer<float4> _Colors;
            StructuredBuffer<float>  _Radii;
            StructuredBuffer<int>    _TypeIds;
            StructuredBuffer<int>    _ActiveIndices;
            int   _TargetType;
            float _RenderScale;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            struct Varying
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
            };

            // 两个三角形拼出四边形（与 CPU quad 同尺寸 [-0.5,0.5]）。 // Two triangles form the quad. // 四角形。
            static const float2 QCorner[6] =
            {
                float2(-0.5, -0.5), float2(0.5, -0.5), float2(0.5, 0.5),
                float2(-0.5, -0.5), float2(0.5,  0.5), float2(-0.5, 0.5)
            };
            static const float2 QUV[6] =
            {
                float2(0, 0), float2(1, 0), float2(1, 1),
                float2(0, 0), float2(1, 1), float2(0, 1)
            };

            Varying Vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varying OUT = (Varying)0;
                int slot = _ActiveIndices[iid];

                // 类型不匹配的实例输出到裁剪域外（剔除）。 // Cull instances of non-matching type offscreen. // 不一致タイプを画面外へ。
                if (_TypeIds[slot] != _TargetType)
                {
                    OUT.positionCS = float4(2, 2, 2, 1);
                    return OUT;
                }

                float2 center = _Positions[slot];
                float diameter = _Radii[slot] * 2.0 * _RenderScale;
                float2 world = center + QCorner[vid] * diameter;

                OUT.positionCS = TransformWorldToHClip(float3(world, 0));
                OUT.uv = QUV[vid];
                OUT.color = _Colors[slot];
                return OUT;
            }

            half4 Frag(Varying IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return tex * IN.color;
            }
            ENDHLSL
        }
    }
}

// 流体粒子独立显示着色器。两条路径均为 DrawProcedural（无 mesh），shader 内程序化生成 quad，片元按 UV 裁出圆形：
//   · CPU 模式：缓冲为密实数组，SV_InstanceID 直接索引；
//   · GPU 模式（关键字 _GPU_PROCEDURAL）：缓冲为常驻 GPU（slot 索引），slot = _ActiveIndices[SV_InstanceID]，
//     与正式 Feature 的 Liquid2DParticleGpu 流程一致（全程 GPU、零回读、每帧最新）。
// 两路径均支持速度渐变与模拟颜色两种模式。
// Standalone fluid particle display shader. Both paths use DrawProcedural (no mesh); the quad is generated in-shader and
// the fragment carves a circle from it by UV:
//   · CPU mode: dense buffers indexed directly by SV_InstanceID;
//   · GPU mode (keyword _GPU_PROCEDURAL): resident GPU buffers (slot-indexed),
//     slot = _ActiveIndices[SV_InstanceID], mirroring the official Feature's Liquid2DParticleGpu path (fully GPU, zero readback).
// Both paths support simulation-colour and velocity-gradient modes.
// 流体パーティクル独立表示シェーダー。両パスとも DrawProcedural（mesh 不要）、quad はシェーダー内生成、フラグメントで UV により円形を切り出す。
Shader "Custom/URP/2D/Liquid2DParticleDisplay"
{
    Properties
    {
    }
    SubShader
    {
        Tags
        {
            "RenderType"      = "Transparent"
            "Queue"           = "Transparent"
            "RenderPipeline"  = "UniversalPipeline"
        }

        Pass
        {
            Name "Liquid2DParticleDisplay"
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex   Vert
            #pragma fragment Frag
            #pragma target 4.5
            #pragma multi_compile_local _ _GPU_PROCEDURAL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 每粒子结构化缓冲。 // Per-particle structured buffers. // 粒子ごとのストラクチャードバッファ。
            StructuredBuffer<float2> _Positions;   // 世界坐标。 // World position. // ワールド座標。
            StructuredBuffer<float2> _Velocities;  // 世界速度。 // World velocity. // ワールド速度。
            StructuredBuffer<float4> _Colors;      // RGBA（模拟色）。 // RGBA (simulation colour). // RGBA（シミュレーション色）。

            TEXTURE2D(_ColourMap);  SAMPLER(sampler_ColourMap);

            float _VelocityMax;
            int   _ColourMode;  // 0 = 速度渐变, 1 = 模拟颜色。 // 0 = velocity gradient, 1 = simulation colour. // 0 = 速度グラデーション、1 = シミュレーション色。
            

            // 两个三角形拼出四边形（[-0.5,0.5]）+ [0,1] UV。两路径共用。
            // Two triangles form a quad ([-0.5,0.5]) with [0,1] UV. Shared by both paths.
            // 2 つの三角形で四角形（[-0.5,0.5]）+ [0,1] UV。両パス共用。
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

#if defined(_GPU_PROCEDURAL)
            // GPU 常驻模式额外缓冲/参数。 // Extra buffers/params for GPU resident mode. // GPU 常駐モード用。
            StructuredBuffer<float>  _Radii;          // 物理半径（slot 索引）。 // Physics radius (slot-indexed). // 物理半径。
            StructuredBuffer<int>    _TypeIds;        // 粒子类型（slot 索引）。 // Particle type (slot-indexed). // 粒子タイプ。
            StructuredBuffer<int>    _ActiveIndices;  // 密实 instanceID → slot。 // dense instanceID → slot. // 密実 → slot。
            int   _TargetType;    // 仅绘制此类型，其余剔除。 // Draw only this type; cull others. // このタイプのみ描画。
            float _RenderScale;   // 当前类型的 renderScale。 // renderScale of the current type. // 当該タイプの renderScale。
            float _DisplayScale;  // 全局可视倍率（组件 scale）。 // Global visual scale (component scale). // 全体倍率。
#else
            StructuredBuffer<float>  _Scales;      // 可视直径（世界单位，已含 renderScale×scale）。 // Visual diameter. // 可視直径。
#endif

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : TEXCOORD1;
            };

            // 速度→渐变色采样。 // Sample velocity gradient. // 速度グラデーション採取。
            float4 ColourFromVelocity(float2 vel)
            {
                float speed = length(vel);
                float t     = saturate(speed / max(_VelocityMax, 1e-4));
                return SAMPLE_TEXTURE2D_LOD(_ColourMap, sampler_ColourMap, float2(t, 0.5), 0);
            }

#if defined(_GPU_PROCEDURAL)
            Varyings Vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings OUT = (Varyings)0;
                int slot = _ActiveIndices[iid];

                // 类型不匹配的实例输出到裁剪域外（剔除）。 // Cull non-matching type offscreen. // 不一致タイプを画面外へ。
                if (_TypeIds[slot] != _TargetType)
                {
                    OUT.positionCS = float4(2, 2, 2, 1);
                    return OUT;
                }

                float2 center   = _Positions[slot];
                // 可视直径 = 物理半径 × renderScale × 全局 scale（与 CPU 路径一致）。
                // Visual diameter = radius × renderScale × global scale (matches CPU path).
                // 可視直径 = 半径 × renderScale × 全体 scale。
                float  diameter = _Radii[slot] * _RenderScale * _DisplayScale;
                float2 world    = center + QCorner[vid] * diameter;

                OUT.positionCS = TransformWorldToHClip(float3(world, 0.0));
                OUT.uv         = QUV[vid];
                OUT.color      = (_ColourMode == 0) ? ColourFromVelocity(_Velocities[slot]) : _Colors[slot];
                return OUT;
            }
#else
            Varyings Vert(uint vid : SV_VertexID, uint iid : SV_InstanceID)
            {
                Varyings OUT = (Varyings)0;

                // 密实数组直接用 SV_InstanceID 索引；程序化生成 quad 顶点（z=0 Billboard）。
                // Dense arrays indexed directly by SV_InstanceID; quad vertices generated procedurally (z=0 billboard).
                // 密実配列を SV_InstanceID で直接索引；quad 頂点をプロシージャル生成（z=0 ビルボード）。
                float2 center   = _Positions[iid];
                float  diameter = _Scales[iid];   // 已含 renderScale×scale。 // already includes renderScale×scale. // renderScale×scale 込み。
                float2 world    = center + QCorner[vid] * diameter;

                OUT.positionCS = TransformWorldToHClip(float3(world, 0.0));
                OUT.uv         = QUV[vid];
                OUT.color      = (_ColourMode == 0) ? ColourFromVelocity(_Velocities[iid]) : _Colors[iid];
                return OUT;
            }
#endif

            float4 Frag(Varyings IN) : SV_Target
            {
                // 在方形 quad 的 UV 空间内按到中心距离裁出圆形（严格参照 Instanced/Particle2D）：
                // UV [0,1] 映射到 [-1,1]，到中心距离 ≤1 保留、四角裁掉，fwidth+smoothstep 做抗锯齿边。
                // Carve a circle from the square quad in UV space (mirrors Instanced/Particle2D):
                // map UV [0,1]→[-1,1], keep pixels within radius 1, drop the corners; fwidth+smoothstep for AA edge.
                // 四角形 quad の UV 空間で中心からの距離により円形を切り出す（Instanced/Particle2D 準拠）。
                float2 centreOffset = (IN.uv - 0.5) * 2.0;
                float  sqrDst       = dot(centreOffset, centreOffset);
                float  delta        = fwidth(sqrt(sqrDst));
                float  alpha        = 1.0 - smoothstep(1.0 - delta, 1.0 + delta, sqrDst);

                // rgb 取实例颜色，alpha 叠加模拟颜色自带透明度，保证本项目两种模式可用。
                // rgb from the instance colour; alpha multiplied by the simulation colour's own alpha.
                // rgb はインスタンス色、alpha はシミュレーション色の alpha を乗算。
                return float4(IN.color.rgb, IN.color.a * alpha);
            }
            ENDHLSL
        }
    }
}

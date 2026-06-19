// 流体粒子独立显示着色器。两条路径：
//   · CPU 模式：DrawMeshInstancedIndirect，缓冲为密实数组，SV_InstanceID 直接索引；
//   · GPU 模式（关键字 _GPU_PROCEDURAL）：DrawProcedural，缓冲为常驻 GPU（slot 索引），slot = _ActiveIndices[SV_InstanceID]，
//     与正式 Feature 的 Liquid2DParticleGpu 流程一致（全程 GPU、零回读、每帧最新）。
// 两路径均支持模拟颜色与速度渐变两种模式。
// Standalone fluid particle display shader. Two paths:
//   · CPU mode: DrawMeshInstancedIndirect, dense buffers indexed directly by SV_InstanceID;
//   · GPU mode (keyword _GPU_PROCEDURAL): DrawProcedural over resident GPU buffers (slot-indexed),
//     slot = _ActiveIndices[SV_InstanceID], mirroring the official Feature's Liquid2DParticleGpu path (fully GPU, zero readback).
// Both paths support simulation-colour and velocity-gradient modes.
// 流体パーティクル独立表示シェーダー。CPU=DrawMeshInstancedIndirect、GPU(_GPU_PROCEDURAL)=DrawProcedural（常駐 GPU 直読）。
Shader "Custom/URP/2D/Liquid2DParticleDisplay"
{
    Properties
    {
        _MainTex ("Sprite Texture", 2D) = "white" {}
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

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_ColourMap);  SAMPLER(sampler_ColourMap);

            float _VelocityMax;
            int   _ColourMode;  // 0 = 模拟颜色, 1 = 速度渐变。 // 0 = simulation, 1 = velocity gradient. // 0 = シミュレーション色, 1 = 速度グラデーション。

#if defined(_GPU_PROCEDURAL)
            // GPU 常驻模式额外缓冲/参数。 // Extra buffers/params for GPU resident mode. // GPU 常駐モード用。
            StructuredBuffer<float>  _Radii;          // 物理半径（slot 索引）。 // Physics radius (slot-indexed). // 物理半径。
            StructuredBuffer<int>    _TypeIds;        // 粒子类型（slot 索引）。 // Particle type (slot-indexed). // 粒子タイプ。
            StructuredBuffer<int>    _ActiveIndices;  // 密实 instanceID → slot。 // dense instanceID → slot. // 密実 → slot。
            int   _TargetType;    // 仅绘制此类型，其余剔除。 // Draw only this type; cull others. // このタイプのみ描画。
            float _RenderScale;   // 当前类型的 renderScale。 // renderScale of the current type. // 当該タイプの renderScale。
            float _DisplayScale;  // 全局可视倍率（组件 scale）。 // Global visual scale (component scale). // 全体倍率。

            // 两个三角形拼出四边形（[-0.5,0.5]）。 // Two triangles form a quad ([-0.5,0.5]). // 四角形。
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
#else
            StructuredBuffer<float>  _Scales;      // 可视半径（世界单位，已含 renderScale×scale）。 // Visual radius. // 可視半径。
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
                OUT.color      = (_ColourMode == 1) ? ColourFromVelocity(_Velocities[slot]) : _Colors[slot];
                return OUT;
            }
#else
            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                uint   instanceID : SV_InstanceID;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                uint id = IN.instanceID;

                float2 center = _Positions[id];
                float  radius = _Scales[id];

                // 以粒子世界坐标为中心，用 OS 顶点坐标缩放成 Billboard（z=0）。
                // Billboard centred on the particle world position; OS vertex offsets scaled by visual radius (z=0).
                // 粒子ワールド座標を中心に OS 頂点を可視半径でスケールしたビルボード（z=0）。
                float3 worldPos = float3(center + IN.positionOS.xy * radius, 0.0);
                OUT.positionCS  = TransformWorldToHClip(worldPos);
                OUT.uv          = IN.uv;
                OUT.color       = (_ColourMode == 1) ? ColourFromVelocity(_Velocities[id]) : _Colors[id];
                return OUT;
            }
#endif

            float4 Frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return tex * IN.color;
            }
            ENDHLSL
        }
    }
}

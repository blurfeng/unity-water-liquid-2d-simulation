// 流体粒子独立显示着色器（DrawMeshInstancedIndirect）。支持模拟颜色与速度渐变两种模式。
// Standalone fluid particle display shader (DrawMeshInstancedIndirect). Supports simulation colour and velocity-gradient modes.
// 流体パーティクル独立表示シェーダー（DrawMeshInstancedIndirect）。シミュレーション色と速度グラデーション両モード対応。
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

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            // 每粒子结构化缓冲。 // Per-particle structured buffers. // 粒子ごとのストラクチャードバッファ。
            StructuredBuffer<float2> _Positions;   // 世界坐标。 // World position. // ワールド座標。
            StructuredBuffer<float2> _Velocities;  // 世界速度。 // World velocity. // ワールド速度。
            StructuredBuffer<float4> _Colors;      // RGBA（模拟色）。 // RGBA (simulation colour). // RGBA（シミュレーション色）。
            StructuredBuffer<float>  _Scales;      // 可视半径（世界单位）。 // Visual radius (world units). // 可視半径（ワールド単位）。

            TEXTURE2D(_MainTex);    SAMPLER(sampler_MainTex);
            TEXTURE2D(_ColourMap);  SAMPLER(sampler_ColourMap);

            float _VelocityMax;
            int   _ColourMode;  // 0 = 模拟颜色, 1 = 速度渐变。 // 0 = simulation, 1 = velocity gradient. // 0 = シミュレーション色, 1 = 速度グラデーション。

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                uint   instanceID : SV_InstanceID;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                float4 color      : TEXCOORD1;
            };

            Varyings Vert(Attributes IN)
            {
                Varyings OUT;
                uint id = IN.instanceID;

                float2 center = _Positions[id];
                float  radius = _Scales[id];

                // 以粒子世界坐标为中心，用 OS 顶点坐标缩放成 Billboard（z=0）。
                // Billboard centred on the particle world position; OS vertex offsets are scaled by visual radius (z=0).
                // 粒子ワールド座標を中心に OS 頂点オフセットを可視半径でスケールしたビルボード（z=0）。
                float3 worldPos = float3(center + IN.positionOS.xy * radius, 0.0);
                OUT.positionCS  = TransformWorldToHClip(worldPos);
                OUT.uv          = IN.uv;

                if (_ColourMode == 1)
                {
                    // 速度渐变：speed → [0,1] → 渐变贴图采样。
                    // Velocity gradient: speed → [0,1] → sample colour map.
                    // 速度グラデーション：speed → [0,1] → カラーマップサンプリング。
                    float speed = length(_Velocities[id]);
                    float t     = saturate(speed / max(_VelocityMax, 1e-4));
                    OUT.color   = SAMPLE_TEXTURE2D_LOD(_ColourMap, sampler_ColourMap, float2(t, 0.5), 0);
                }
                else
                {
                    OUT.color = _Colors[id];
                }

                return OUT;
            }

            float4 Frag(Varyings IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return tex * IN.color;
            }
            ENDHLSL
        }
    }
}


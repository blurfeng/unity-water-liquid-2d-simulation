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

            // 与 Liquid2DParticle 一致的混合，使粒子远距离即开始融合。RT1（仅 _OPACITY_FIELD 开启时绑定）为独立透明度场，
            // 加性累加 R=Σ(覆盖×O)、G=Σ覆盖，合成阶段 O=R/G。 // Same blend as Liquid2DParticle. RT1 (bound only when _OPACITY_FIELD)
            // is the independent opacity field: additive R=Σ(coverage×O), G=Σcoverage; the composite computes O=R/G. // 同一ブレンド + 独立透明度場。
            Blend 0 SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha
            Blend 1 One One
            Cull Off
            ZWrite Off

            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma target 4.5
            // 独立透明度场开关（与 CPU 路径同名，由 C# 运行时按需切换并绑定第二渲染目标）。 // Independent opacity field toggle (same as CPU path). // 独立透明度場スイッチ。
            #pragma multi_compile_local _ _OPACITY_FIELD
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            StructuredBuffer<float2> _Positions;
            StructuredBuffer<float4> _Colors;
            StructuredBuffer<float>  _Radii;
            StructuredBuffer<int>    _TypeIds;
            StructuredBuffer<int>    _ActiveIndices;
            int   _TargetType;
            float _RenderScale;

            // 渐变颜色映射（_UseGradient!=0 时启用）。按每粒子标量（速度或密度亏空）采样 _GradientLut，替代 _Colors。
            // Gradient color mapping (enabled when _UseGradient!=0). Samples _GradientLut by a per-particle scalar
            // (speed or density deficit), replacing _Colors. // 渐変マッピング（_UseGradient!=0）。
            StructuredBuffer<float3> _RenderScalars; // 逐粒子平滑标量 (x=密度→Foam, y=速度→Speed, z=Impact 冲击泡沫累加器 F)。 // per-particle scalars (x=density→Foam, y=speed→Speed, z=Impact accumulator F). // 平滑スカラー。
            TEXTURE2D(_GradientLut);
            SAMPLER(sampler_GradientLut);
            // 透明度 LUT（仅 _OPACITY_FIELD + 渐变时使用）：按同一标量 t 采样得每粒子最终透明度倍率 O(t)。 // Opacity LUT (used only with _OPACITY_FIELD + gradient): O(t) by the same scalar t. // 透明度 LUT。
            TEXTURE2D(_OpacityLut);
            SAMPLER(sampler_OpacityLut);
            int   _UseGradient;     // 0=用 _Colors；非0=用渐变。 // 0=use _Colors; nonzero=gradient. // 0=_Colors、非0=渐変。
            int   _GradientSource;  // 0=Speed，1=Density，2=DensityWithImpact，3=DensityWithSpeed，4=Impact。 // 0/1/2/3/4。
            float _SpeedMin;        // 速度归一化下限（低于视作 0）。 // speed lower bound (below → 0). // 速度下限。
            float _GradientSpeedMax;
            float _RestDensity;     // 该类粒子静止密度（把 SPH 密度归一化为密度比）。 // rest density (normalizes SPH density to a ratio). // 静止密度。
            float _FoamStart;
            float _FoamEnd;

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);

            // sRGB→linear（逐通道，含 HDR>1 分支）。曲线与 C# Mathf.GammaToLinearSpace / Color.linear / Material.SetColor 完全一致，
            // 使 _Colors（store 的手调 sRGB 工作色）在绘制读取的边界转换后，与 CPU 路径及 SetColor 上传的材质颜色表现一致。仅线性色彩空间需要。
            // sRGB→linear (per channel, with an HDR >1 branch). The curve matches C# Mathf.GammaToLinearSpace / Color.linear /
            // Material.SetColor exactly, so _Colors (the store's authored sRGB working color) — converted at this draw-time read
            // boundary — matches the CPU path and SetColor-uploaded material colors. Only needed in linear color space.
            // sRGB→linear（チャンネルごと、HDR>1 分岐あり）。曲線は C# の Mathf.GammaToLinearSpace / Color.linear / Material.SetColor と一致。
            float3 Liquid2DSRGBToLinear(float3 c)
            {
                float3 lo   = c / 12.92;                                 // c <= 0.04045
                float3 mid  = pow(max((c + 0.055) / 1.055, 0.0), 2.4);   // 0.04045 < c < 1
                float3 hi   = pow(max(c, 0.0), 2.2);                     // c >= 1（HDR，与 GammaToLinearSpace 一致用 2.2）
                float3 midHi = (c < 1.0) ? mid : hi;
                return (c <= 0.04045) ? lo : midHi;
            }

            struct Varying
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : TEXCOORD1;
                float opacity : TEXCOORD2; // 逐粒子最终透明度倍率 O（_OPACITY_FIELD 用；否则恒 1）。 // per-particle opacity multiplier O. // 透明度倍率 O。
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
                OUT.opacity = 1.0; // 默认不透明倍率；渐变+_OPACITY_FIELD 时下方按 t 采样覆盖。 // default; overwritten by LUT below when gradient+_OPACITY_FIELD. // 既定 1。

                // 颜色：渐变模式按每粒子标量采 LUT（顶点纹理取样，同实例 6 顶点结果一致）；否则用 store 颜色。
                // Color: gradient mode samples the LUT by a per-particle scalar (vertex texture fetch; identical across the
                // instance's 6 verts); otherwise the store color. // 色：渐変は LUT サンプル、否則は store 色。
                if (_UseGradient != 0)
                {
                    float3 rscal = _RenderScalars[slot]; // x=平滑密度, y=平滑速度, z=动态泡沫累加器 F（DensityWith*）。 // x=smoothed density, y=smoothed speed, z=dynamic-foam accumulator F. // x=密度, y=速度, z=泡F。
                    float t;
                    if (_GradientSource == 0) // Speed：速度重映射，低于 _SpeedMin 视作 0。 // Speed: remap; below _SpeedMin → 0. // 速度リマップ。
                    {
                        t = saturate((rscal.y - _SpeedMin) / max(1e-4, _GradientSpeedMax - _SpeedMin));
                    }
                    else if (_GradientSource == 1) // Density：纯密度亏空静态贴图。 // Density: pure density-deficit static map. // 純密度不足。
                    {
                        float ratio = rscal.x / max(1e-4, _RestDensity);
                        t = saturate((_FoamStart - ratio) / max(1e-4, _FoamStart - _FoamEnd));
                    }
                    else // DensityWithImpact（=2）/ DensityWithSpeed（=3）/ Impact（=4）：求解器算好的动态泡沫累加器 F。 // dynamic-foam accumulator F. // 動的泡累加器 F。
                    {
                        t = rscal.z;
                    }
                    t = saturate(t);
                    OUT.color = SAMPLE_TEXTURE2D_LOD(_GradientLut, sampler_GradientLut, float2(t, 0.5), 0);
                    #if defined(_OPACITY_FIELD)
                    // 按同一标量 t 采样透明度 LUT，得到该粒子的最终透明度倍率。 // Sample the opacity LUT by the same t. // 同じ t で透明度 LUT をサンプリング。
                    OUT.opacity = SAMPLE_TEXTURE2D_LOD(_OpacityLut, sampler_OpacityLut, float2(t, 0.5), 0).r;
                    #endif
                }
                else
                {
                    // store 的手调 sRGB 工作色；线性项目下在此绘制读取边界转 linear（与 CPU 路径对齐）。渐变分支采样的 LUT 已烘焙为上传值，不在此转换。
                    // The store's authored sRGB working color; convert to linear at this draw-time read boundary in linear projects (aligned with the CPU path). The gradient branch's sampled LUT is already baked to upload values and is not converted here.
                    // store の手調整 sRGB 作業色。線形項目ではこの描画読み取り境界で linear へ。
                    float4 sc = _Colors[slot];
                    #ifndef UNITY_COLORSPACE_GAMMA
                    sc.rgb = Liquid2DSRGBToLinear(sc.rgb);
                    #endif
                    OUT.color = sc;
                }
                return OUT;
            }

            #if defined(_OPACITY_FIELD)
            struct FragOut
            {
                half4 color : SV_Target0; // RGB=颜色, A=覆盖度。 // RGB=color, A=coverage.
                half2 op    : SV_Target1; // R=覆盖×O, G=覆盖。 // R=coverage×O, G=coverage.
            };
            FragOut Frag(Varying IN)
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                half4 col = tex * IN.color;
                FragOut OUT;
                OUT.color = col;
                half cov = col.a;
                OUT.op = half2(cov * (half)IN.opacity, cov);
                return OUT;
            }
            #else
            half4 Frag(Varying IN) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, IN.uv);
                return tex * IN.color;
            }
            #endif
            ENDHLSL
        }
    }
}

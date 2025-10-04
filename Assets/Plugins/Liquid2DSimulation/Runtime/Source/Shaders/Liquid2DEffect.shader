// 流体效果着色器，用于实现类似液体的视觉效果。
// Fluid effect shader for implementing liquid-like visual effects.
// 液体のような視覚効果を実現するための流体エフェクトシェーダー。
Shader "Custom/URP/2D/Liquid2DEffect"
{
    SubShader
    {
        Tags
        {
            "RenderType" = "Opaque"
            "RenderPipeline" = "UniversalPipeline"
        }
        
        Pass
        {
            Name "Liquid2DEffect"
            
            Blend SrcAlpha OneMinusSrcAlpha
            Cull Off
            ZWrite Off
            
            HLSLPROGRAM
            
            // ---- Keywords ------------------------------------- Start
            // 开启遮挡纹理功能。 // Enable occluder texture feature. // 遮蔽テクスチャ機能を有効化。
            #pragma shader_feature_local _OCCLUDER_ENABLE
            
            // 透明度倍率使用乘法方式。 // Opacity multiplier using multiplication method. // 透明度倍率は乗算方式を使用。
            #pragma shader_feature_local _OPACITY_MULTIPLY
            // 透明度倍率使用覆盖方式，而不是乘法方式。 // Opacity multiplier using override method instead of multiplication. // 透明度倍率は乗算ではなく上書き方式を使用。
            #pragma shader_feature_local _OPACITY_REPLACE
            
            // 开启水体扰动。 // Enable water distortion. // 水体の歪みを有効化。
            #pragma shader_feature_local _DISTORT_ENABLE

            // 开启边缘颜色效果。 // Enable edge color effect. // エッジカラー効果を有効化。
            #pragma shader_feature_local _EDGE_ENABLE
            // 边缘颜色使用 SrcAlpha OneMinusSrcAlpha 混合方式。 // Edge color using SrcAlpha OneMinusSrcAlpha blending mode. // エッジカラーはSrcAlpha OneMinusSrcAlphaブレンドモードを使用。
            #pragma shader_feature_local _EDGE_BLEND_SA_OMSA
            // 边缘颜色使用lerp混合方式。 // Edge color using lerp blending mode. // エッジカラーはlerpブレンドモードを使用。
            #pragma shader_feature_local _EDGE_BLEND_LERP

            // 开启像素化水体效果。 // Enable pixelated water effect. // ピクセル化水体効果を有効化。
            #pragma shader_feature_local _PIXEL_ENABLE
            // 开启像素化背景。 // Enable pixelated background. // ピクセル化背景を有効化。
            #pragma shader_feature_local _PIXEL_BG
            // ---- Keywords ------------------------------------- End

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.core/ShaderLibrary/Common.hlsl"
            #include "./ShaderLibrary/MathUtils.hlsl"

            #pragma vertex Vert
            #pragma fragment Frag

            struct  Attribute
            {
                uint vertexID : SV_VertexID;
            };

            struct Varying
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
            };

            TEXTURE2D_X(_MainTex);
            // https://docs.unity3d.com/Manual/SL-SamplerStates.html
            SAMPLER(sampler_linear_clamp_MainTex);
            
            TEXTURE2D_X(_ObstructionTex);
            SAMPLER(sampler_linear_clamp_ObstructionTex);
            
            // 背景纹理。当需要扰动或像素化背景时使用。将处理后的背景作为流体的背景。
            // 否则流体是透明的，可以看到真正的背景。
            // Background texture. Used when distortion or pixelated background is needed. Uses the processed background as the fluid's background.
            // Otherwise the fluid is transparent and you can see the real background.
            // 背景テクスチャ。歪みまたはピクセル化背景が必要な場合に使用。処理された背景を流体の背景として使用。
            // そうでなければ流体は透明で、実際の背景が見えます。
            #if defined(_DISTORT_ENABLE) || defined(_PIXEL_BG)
            TEXTURE2D_X(_BackgroundTex);
            SAMPLER(sampler_linear_clamp_BackgroundTex);
            #endif

            #if defined(_OCCLUDER_ENABLE)
            TEXTURE2D_X(_OccluderTex);
            SAMPLER(sampler_linear_clamp_OccluderTex);
            #endif
            
            
CBUFFER_START(UnityPerMaterial)

            half _Cutoff; // 透明度裁剪阈值。 // Transparency cutoff threshold. // 透明度クリップ閾値。
            half _OpacityValue; // 透明度值，用法和模式相关。 // Opacity value, usage depends on mode. // 透明度値、使用方法はモードに依存。
            half4 _CoverColor; // 叠加颜色。 // Overlay color. // オーバーレイカラー。
            
            // 水体扰动相关参数。 // Water distortion related parameters. // 水体歪み関連パラメータ。
            #if defined(_DISTORT_ENABLE)
            // 计算扰动方式。 // Calculate distortion method. // 歪み計算方式。
            float _Magnitude; // 扰动采样缩放。 // Distortion sampling scale. // 歪みサンプリングスケール。
            float _Frequency; // 噪点频率。 // Noise frequency. // ノイズ周波数。
            float _Amplitude; // 扰动强度。 // Distortion intensity. // 歪み強度。
            float2 _DistortSpeed; // 扰动速度。 // Distortion speed. // 歪み速度。
            float4 _DistortTimeFactors; // 扰动时间系数。 // Distortion time factors. // 歪み時間係数。
            float _NoiseCoordOffset; // 噪点坐标偏移。 // Noise coordinate offset. // ノイズ座標オフセット。

            // 如果使用方式控制扰动效果，可以启用下面的代码。
            // If using texture to control distortion effects, you can enable the code below.
            // もしテクスチャを使用して歪み効果を制御する場合、以下のコードを有効にできます。
            // TEXTURE2D_X(_DistortTex);
            // 重复采样，防止边缘断裂。注意是 repeat 不是 clamp。 // Repeat sampling to prevent edge breaks. Note it's repeat not clamp. // エッジの破綻を防ぐためリピートサンプリング。clampではなくrepeatであることに注意。
            // SAMPLER(sampler_linear_repeat_DistortTex);
            // half _DistortIntensity; // 扰动强度。 // Distortion intensity. // 歪み強度。
            // half _AspectRatio; // 视口宽高比，用于修正UV坐标。 // Viewport aspect ratio for UV coordinate correction. // UV座標補正用のビューポートアスペクト比。
            #endif

            #if defined(_EDGE_ENABLE)
            half _EdgeStart; // 边缘颜色起始位置。 // Edge color start position. // エッジカラー開始位置。
            half _EdgeEnd; // 边缘颜色结束位置。 // Edge color end position. // エッジカラー終了位置。
            half _EdgeMixStart; // 边缘颜色混合起始位置。 // Edge color blend start position. // エッジカラーブレンド開始位置。
            half4 _EdgeColor; // 边缘颜色。 // Edge color. // エッジカラー。
            #endif

            // 像素化相关参数。 // Pixelation related parameters. // ピクセル化関連パラメータ。
            #if defined(_PIXEL_ENABLE)
            float2 _PixelSize;
            #endif
            
CBUFFER_END
            
            Varying Vert(Attribute IN)
            {
                Varying OUT;
                
                // 使用方法库函数生成全屏三角形顶点位置和UV坐标。
                // Use utility library functions to generate full-screen triangle vertex positions and UV coordinates.
                // ユーティリティライブラリ関数を使用してフルスクリーン三角形の頂点位置とUV座標を生成。
                OUT.positionCS = GetFullScreenTriangleVertexPosition(IN.vertexID);
                OUT.uv = GetFullScreenTriangleTexCoord(IN.vertexID);
                
                return OUT;
            }
            
            half4 Frag(Varying IN) : SV_Target
            {
                float2 uvDefault = IN.uv;
                float2 uvProcess = IN.uv;

                // ---- 像素化处理 // Pixelation processing // ピクセル化処理 ---- //
                #if defined(_PIXEL_ENABLE)
                // 计算像素化后的UV坐标。 // Calculate pixelated UV coordinates. // ピクセル化後のUV座標を計算。
                uvProcess.xy = floor(uvProcess.xy * _PixelSize) / _PixelSize;
                // return half4(uvPixel,0,1);
                #endif
                
                half4 col = SAMPLE_TEXTURE2D_X(_MainTex, sampler_linear_clamp_MainTex, uvProcess);
                
                // ---- 透明度裁剪 // Transparency clipping // 透明度クリップ ---- //
                // 裁剪掉透明度低于阈值的像素，形成流体边缘效果。
                // Clip pixels with transparency below threshold to create fluid edge effects.
                // 閾値以下の透明度のピクセルをクリップして流体エッジ効果を作成。
                clip(col.a - _Cutoff);

                // ---- 颜色处理 // Color processing // カラー処理 ---- //
                // 覆盖颜色，用于整体调节流体颜色。 // Overlay color for overall fluid color adjustment. // 流体カラー全体調整用のオーバーレイカラー。
                col.rgb = lerp(col.rgb, _CoverColor.rgb, _CoverColor.a);

                // ---- 边缘颜色-计算lerp值 // Edge color - calculate lerp value // エッジカラー - lerp値計算 ---- //
                #if defined(_EDGE_ENABLE)
                // 计算边缘颜色的混合值。使用透明度处理之前的透明度值才能分辨出边缘。
                // Calculate edge color blend value. Use transparency value before processing to distinguish edges.
                // エッジカラーのブレンド値を計算。処理前の透明度値を使用してエッジを識別。
                half edge = smoothstep(_EdgeMixStart, _EdgeEnd, col.a);
                #endif

                // ---- 透明度处理 // Transparency processing // 透明度処理 ---- //
                #if defined(_OPACITY_MULTIPLY)
                col.a = col.a * _OpacityValue; // 透明度乘以一个值，整体调节透明度。 // Multiply transparency by a value for overall transparency adjustment. // 透明度に値を乗算して全体的な透明度を調整。
                #elif defined(_OPACITY_REPLACE)
                col.a = _OpacityValue; // 直接覆盖透明度。 // Directly override transparency. // 透明度を直接上書き。
                #endif
                // 注意：如果不启用任何透明度模式，则保持原有透明度不变。
                // Note: If no transparency mode is enabled, keep original transparency unchanged.
                // 注意：透明度モードが有効でない場合、元の透明度を変更せず保持。

                // ---- 边缘颜色-应用边缘色 // Edge color - apply edge color // エッジカラー - エッジカラー適用 ---- //
                #if defined(_EDGE_ENABLE)
                
                #if defined(_EDGE_BLEND_SA_OMSA)
                // 计算边缘混合权重。 // Calculate edge blend weight. // エッジブレンド重みを計算。
                half edgeAlpha = _EdgeColor.a * (1 - edge);
                // 按 Alpha 混合公式叠加边缘色。
                // Overlay edge color according to Alpha blending formula.
                // アルファブレンド公式に従ってエッジカラーをオーバーレイ。
                col.rgb = col.rgb * (1 - edgeAlpha) + _EdgeColor.rgb * edgeAlpha;
                col.a   = col.a   * (1 - edgeAlpha) + _EdgeColor.a   * edgeAlpha;
                #elif defined(_EDGE_BLEND_LERP)
                // 使用lerp混合边缘色。 // Use lerp to blend edge color. // lerpを使用してエッジカラーをブレンド。
                col = lerp(col, _EdgeColor, (1 - edge));
                #endif

                #endif

                // ---- 阻挡纹理处理 // Obstruction texture processing // 阻害テクスチャ処理 ---- //
                // 阻挡纹理完全阻挡流体颜色。一般是挡板、管道、瓶子的横截面等。
                // Obstruction texture completely blocks fluid color. Usually cross-sections of barriers, pipes, bottles, etc.
                // 阻害テクスチャは流体カラーを完全にブロック。通常はバリア、パイプ、ボトルの断面など。
                half4 colObstructionTex = SAMPLE_TEXTURE2D_X(_ObstructionTex, sampler_linear_clamp_ObstructionTex, uvDefault);
                clip(1 - colObstructionTex.a);
                // 阻挡物挡住流体颜色。 // Obstructions block fluid color. // 阻害物が流体カラーをブロック。
                col = lerp(col, colObstructionTex, step(0.001, colObstructionTex.a));
                
                // Tips:
                // 我们只是裁剪掉了阻挡纹理的透明部分，阻挡纹理本身的颜色并没有参与混合。
                // 因为流体系统只专注于处理流体效果自身。
                // 关于遮挡流体的物体，比如一个玻璃瓶。你应当将玻璃瓶的横截面的 Rendering Layer 设置为阻挡层。
                // 但是正面盖在流体上的玻璃瓶部分的渲染，应当由用户自行处理。比如创建新的 Renderer Feature 来渲染玻璃瓶。
                // 这个玻璃瓶的 Renderer Feature 应当在流体 Renderer Feature 之后执行。

                // We only clipped the transparent parts of the obstruction texture, the obstruction texture's color itself doesn't participate in blending.
                // Because the fluid system only focuses on processing fluid effects themselves.
                // For objects that obstruct fluid, like a glass bottle, you should set the glass bottle's cross-section Rendering Layer to the obstruction layer.
                // But the rendering of the glass bottle parts that cover the fluid should be handled by the user. For example, create a new Renderer Feature to render the glass bottle.
                // This glass bottle Renderer Feature should execute after the fluid Renderer Feature.

                // 阻害テクスチャの透明部分のみクリップし、阻害テクスチャ自体の色はブレンドに参加しません。
                // 流体システムは流体エフェクト自体の処理にのみ集中するためです。
                // ガラス瓶のような流体を阻害するオブジェクトについては、ガラス瓶の断面のRendering Layerを阻害レイヤーに設定する必要があります。
                // しかし流体を覆うガラス瓶部分のレンダリングはユーザーが自分で処理する必要があります。例えば、ガラス瓶をレンダリングする新しいRenderer Featureを作成します。
                // このガラス瓶のRenderer Featureは流体Renderer Featureの後に実行される必要があります。
                
                // ---- 水体扰动背景 // Water distortion background // 水体歪み背景 ---- //
                #if defined(_DISTORT_ENABLE)

                // 像素化背景时，扰动使用像素化后的UV，否则使用默认UV。
                // When pixelating background, distortion uses pixelated UV, otherwise uses default UV.
                // 背景をピクセル化する場合、歪みはピクセル化されたUVを使用し、そうでなければデフォルトUVを使用。
                #if defined(_PIXEL_BG)
                float2 uvDistortGet = uvProcess;
                #else
                float2 uvDistortGet = uvDefault;
                #endif
                
                float time = _Time.y;
                float2 noisecoord1 = uvDistortGet * _Frequency * (_Magnitude);
                float2 noisecoord2 = uvDistortGet * _Frequency * (_Magnitude) + _NoiseCoordOffset;
                float2 motion1 = float2(time * _DistortTimeFactors.x, time * _DistortTimeFactors.y) * _DistortSpeed.x;
                float2 motion2 = float2(time * _DistortTimeFactors.z, time * _DistortTimeFactors.w) * _DistortSpeed.y;

                // - float2(0.5,0.5) 是为了让噪点图的值在 -0.5 到 0.5 之间波动，而不是 0 到 1 之间。
                // - float2(0.5,0.5) is to make noise values fluctuate between -0.5 to 0.5, not 0 to 1.
                // - float2(0.5,0.5) はノイズ値を0から1ではなく-0.5から0.5の間で変動させるため。
                float2 distort1 = float2(noise(noisecoord1 + motion1), noise(noisecoord2 + motion1)) - float2(0.5,0.5);
                float2 distort2 = float2(noise(noisecoord1 + motion2), noise(noisecoord2 + motion2)) - float2(0.5,0.5);
            
                // 计算最终采样偏移。 // Calculate final sampling offset. // 最終サンプリングオフセットを計算。
                float2 distort_sum = (distort1 + distort2) * _Amplitude;
                //return half4(distort_sum,0,1);
                float2 uvDistorted = saturate(uvDistortGet + distort_sum);
                
                // 采样背景纹理。 // Sample background texture. // 背景テクスチャをサンプリング。
				half4 colBg = SAMPLE_TEXTURE2D_X(_BackgroundTex, sampler_linear_clamp_BackgroundTex, uvDistorted);
                // 混合流体颜色和背景颜色。 // Blend fluid color and background color. // 流体カラーと背景カラーをブレンド。
                half4 distortBlendColor;
                distortBlendColor.rgb = col.rgb * col.a + colBg.rgb * (1 - col.a);
                // 正常混合是 col.a + colBg.a * (1 - col.a) 但是 colBg.a 是1，所以简化了。
                // Normal blending is col.a + colBg.a * (1 - col.a) but colBg.a is 1, so it's simplified.
                // 通常のブレンドはcol.a + colBg.a * (1 - col.a)ですが、colBg.aは1なので簡略化されました。
                distortBlendColor.a = 1.0; // 混合以后已经包含了背景色，不需要和原有背景混合了，直接不透明。 // After blending already contains background color, no need to blend with original background, directly opaque. // ブレンド後は既に背景色が含まれているため、元の背景とブレンドする必要はなく、直接不透明。
                col = distortBlendColor;
                
                // ---- 使用贴图控制扰动效果的方式 // Method using texture to control distortion effects // テクスチャを使用して歪み効果を制御する方法 ---- //
                // float2 distortUv = uvDefault;
                // distortUv.x *= _AspectRatio;
                //
                // float2 _DistortSpeed = float2(1,1);
                // float _DistortScale = 1;
                // _DistortIntensity = 0.01;
                // distortUv = distortUv * _DistortScale + _Time.y * _DistortSpeed;
                // half2 distort = SAMPLE_TEXTURE2D_X(_DistortTex, sampler_linear_repeat_DistortTex, distortUv) * 2 - 1;
                // float2 uvDistorted = saturate(uvDefault + distort * _DistortIntensity);
                // half4 bg = SAMPLE_TEXTURE2D_X(_BackgroundTex, sampler_linear_clamp_BackgroundTex, uvDistorted);
                // half4 finalCol = lerp(bg, col, col.a);
                
                #else

                // 开启像素化背景时，使用像素化后的UV采样背景，否则使用默认UV。
                // When pixelated background is enabled, use pixelated UV to sample background, otherwise use default UV.
                // ピクセル化背景が有効な場合、ピクセル化されたUVを使用して背景をサンプリング、そうでなければデフォルトUVを使用。
                #if defined(_PIXEL_BG)
                // 采样背景纹理。 // Sample background texture. // 背景テクスチャをサンプリング。
                half4 colBg = SAMPLE_TEXTURE2D_X(_BackgroundTex, sampler_linear_clamp_BackgroundTex, uvProcess);
                // 混合流体颜色和背景颜色。 // Blend fluid color and background color. // 流体カラーと背景カラーをブレンド。
                half4 pixelBlendColor;
                pixelBlendColor.rgb = col.rgb * col.a + colBg.rgb * (1 - col.a);
                // 正常混合是 col.a + colBg.a * (1 - col.a) 但是 colBg.a 是1，所以简化了。
                // 混合以后已经包含了背景色，不需要和原有背景混合了，直接不透明。 
                // Normal blending is col.a + colBg.a * (1 - col.a) but colBg.a is 1, so it's simplified.
                // After blending already contains background color, no need to blend with original background, directly opaque. 
                // 通常のブレンドはcol.a + colBg.a * (1 - col.a)ですが、colBg.aは1なので簡略化されました。
                // ブレンド後は既に背景色が含まれているため、元の背景とブレンドする必要はなく、直接不透明。
                pixelBlendColor.a = 1.0;
                col = pixelBlendColor;
                #else
                // 直接返回流体颜色。此时水体是透明的，可以看到真正的背景。
                // Directly return fluid color. At this time the water is transparent and you can see the real background.
                // 流体カラーを直接返します。この時水体は透明で、実際の背景が見えます。
                #endif
                
                #endif

                // ---- 遮挡纹理处理 // Occluder texture processing // 遮蔽テクスチャ処理 ---- //
                #if defined(_OCCLUDER_ENABLE)
                half4 colOccluder = SAMPLE_TEXTURE2D_X(_OccluderTex, sampler_linear_clamp_OccluderTex, uvDefault);
                half4 occluderBlendColor;
                occluderBlendColor.rgb = colOccluder.rgb * colOccluder.a + col.rgb * (1 - colOccluder.a);
                occluderBlendColor.a = colOccluder.a + col.a * (1 - colOccluder.a);
                col = occluderBlendColor;
                #endif

                return col;
            }
            ENDHLSL
        }
    }
}
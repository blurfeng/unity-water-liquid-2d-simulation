using Unity.Mathematics;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 颜色空间上传辅助。粒子基础色（<see cref="Liquid2DParticleRenderSettings.Color"/> / 渐变）以「手调 sRGB 值」存放于 store 与
    /// 渐变 LUT，而整体覆盖色 <see cref="Liquid2DRenderFeatureSettings.CoverColor"/> 等是经 MaterialPropertyBlock.SetColor 上传的——
    /// 在线性色彩空间项目下，SetColor 会自动做 sRGB→linear，而 SetVectorArray / ComputeBuffer 不会。这会导致同一取值的粒子色与
    /// CoverColor 表现不一致（CoverColor 偏深）。为对齐两者，粒子色在「上传 GPU 的边界」按同一规则转换：线性项目做 sRGB→linear，
    /// Gamma 项目原样返回；alpha 不转换。转换曲线采用 <see cref="Color.linear"/>（即 <see cref="Mathf.GammaToLinearSpace"/>），
    /// 与 SetColor 一致。
    /// Color-space upload helper. Particle base colors (<see cref="Liquid2DParticleRenderSettings.Color"/> / gradient) are stored
    /// as authored sRGB values in the store and the gradient LUT, whereas the whole-body cover color
    /// (<see cref="Liquid2DRenderFeatureSettings.CoverColor"/>) is uploaded via MaterialPropertyBlock.SetColor. In a linear
    /// color-space project SetColor applies sRGB→linear automatically, but SetVectorArray / ComputeBuffer do not — so the same
    /// authored value renders differently for the particle color and the cover color (the cover color looks darker). To align
    /// them, particle colors are converted at the GPU-upload boundary by the same rule: sRGB→linear in linear projects, passed
    /// through in Gamma projects; alpha is never converted. The curve matches <see cref="Color.linear"/>
    /// (i.e. <see cref="Mathf.GammaToLinearSpace"/>), identical to SetColor.
    /// カラースペースのアップロード補助。粒子の基礎色（<see cref="Liquid2DParticleRenderSettings.Color"/> / グラデーション）は
    /// 手調整の sRGB 値として store とグラデーション LUT に保持されますが、全体カバー色
    /// （<see cref="Liquid2DRenderFeatureSettings.CoverColor"/>）は MaterialPropertyBlock.SetColor で上传されます。リニア色空間の
    /// プロジェクトでは SetColor が自動で sRGB→linear を行う一方、SetVectorArray / ComputeBuffer は行いません。そのため同じ値でも
    /// 粒子色とカバー色の見えが食い違います（カバー色が暗く見える）。両者を揃えるため、粒子色も「GPU アップロードの境界」で同じ
    /// 規則で変換します：リニア項目では sRGB→linear、Gamma 項目ではそのまま。alpha は変換しません。曲線は
    /// <see cref="Color.linear"/>（＝<see cref="Mathf.GammaToLinearSpace"/>）で、SetColor と一致します。
    /// </summary>
    public static class Liquid2DColorSpace
    {
        /// <summary>
        /// 项目是否以线性色彩空间渲染（运行时固定，与 SetColor 的判定口径一致）。
        /// Whether the project renders in linear color space (fixed at runtime; matches the check SetColor uses).
        /// プロジェクトがリニア色空間で描画するか（実行時固定、SetColor と同じ判定）。
        /// </summary>
        public static bool IsLinear => QualitySettings.activeColorSpace == ColorSpace.Linear;

        /// <summary>
        /// 单颜色转换（非热路径，如渐变 LUT 烘焙）。线性项目做 sRGB→linear，Gamma 项目原样；alpha 不变。
        /// Single-color conversion (non-hot path, e.g. gradient LUT bake). sRGB→linear in linear projects, pass-through in Gamma
        /// projects; alpha unchanged.
        /// 単色変換（非ホットパス、例：グラデーション LUT 焼き込み）。リニア項目では sRGB→linear、Gamma 項目ではそのまま。alpha は不変。
        /// </summary>
        public static Color ToGpuUpload(Color c) => IsLinear ? c.linear : c;

        /// <summary>
        /// 热路径转换：调用方先缓存 <paramref name="isLinear"/>（= <see cref="IsLinear"/>）再逐粒子调用，避免每粒子查询色彩空间。
        /// 逐通道用 <see cref="Mathf.GammaToLinearSpace"/>，与 <see cref="Color.linear"/> / SetColor 完全一致；alpha 不变。
        /// Hot-path conversion: the caller caches <paramref name="isLinear"/> (= <see cref="IsLinear"/>) and calls this per
        /// particle, avoiding a per-particle color-space query. Per channel via <see cref="Mathf.GammaToLinearSpace"/>, identical
        /// to <see cref="Color.linear"/> / SetColor; alpha unchanged.
        /// ホットパス変換：呼び出し側が <paramref name="isLinear"/>（= <see cref="IsLinear"/>）をキャッシュして粒子ごとに呼び、
        /// 色空間の毎回の問い合わせを回避します。チャンネルごとに <see cref="Mathf.GammaToLinearSpace"/> を使用（<see cref="Color.linear"/> /
        /// SetColor と一致）、alpha は不変。
        /// </summary>
        public static Vector4 ToGpuUpload(float4 c, bool isLinear)
        {
            if (!isLinear) return new Vector4(c.x, c.y, c.z, c.w);
            return new Vector4(
                Mathf.GammaToLinearSpace(c.x),
                Mathf.GammaToLinearSpace(c.y),
                Mathf.GammaToLinearSpace(c.z),
                c.w);
        }
    }
}

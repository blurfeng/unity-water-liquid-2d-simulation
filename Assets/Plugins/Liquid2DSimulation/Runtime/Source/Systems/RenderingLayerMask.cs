#if !UNITY_6000_0_OR_NEWER
using System;
using UnityEngine;

namespace Fs.Liquid2D
{
    /// <summary>
    /// 渲染层遮罩（Unity 2022.3 兼容垫片）。
    /// Unity 6（URP16 / 2023.1+）内置 UnityEngine.RenderingLayerMask 结构体，并自带 Inspector 遮罩下拉；
    /// Unity 2022.3 没有该类型。本结构体是单源移植用的最小替身：内部就是一个 uint 位掩码，与 uint 隐式互转，
    /// 可直接用作 FilteringSettings.renderingLayerMask；配套的 RenderingLayerMaskDrawer 从 URP Global Settings
    /// 读取已配置的渲染层名称并绘制成勾选式下拉。U6 不编译本文件（改用引擎内置类型）。
    ///
    /// Rendering layer mask (Unity 2022.3 compatibility shim).
    /// Unity 6 (URP16 / 2023.1+) ships UnityEngine.RenderingLayerMask with a built-in Inspector mask popup;
    /// Unity 2022.3 has no such type. This struct is the minimal single-source stand-in: internally a single uint
    /// bit mask with implicit uint conversions, usable directly as FilteringSettings.renderingLayerMask. The
    /// companion RenderingLayerMaskDrawer reads the configured rendering-layer names from URP Global Settings and
    /// draws a mask popup. This file is not compiled on U6 (which uses the engine's built-in type).
    ///
    /// レンダリングレイヤーマスク（Unity 2022.3 互換シム）。
    /// Unity 6（URP16 / 2023.1+）は UnityEngine.RenderingLayerMask 構造体と組み込みのマスクポップアップを備えますが、
    /// Unity 2022.3 には存在しません。本構造体は単一ソース移植のための最小の代替で、内部は uint のビットマスクであり、
    /// uint と暗黙的に相互変換でき、FilteringSettings.renderingLayerMask にそのまま使用できます。付属の
    /// RenderingLayerMaskDrawer は URP Global Settings から設定済みのレンダリングレイヤー名を読み取り、
    /// マスクポップアップを描画します。U6 では本ファイルはコンパイルされません（エンジン組み込み型を使用）。
    /// </summary>
    [Serializable]
    public struct RenderingLayerMask
    {
        // 位掩码本体。字段名 _bits 供 RenderingLayerMaskDrawer 通过 FindPropertyRelative("_bits") 访问。
        // The bit mask itself. The name _bits is read by RenderingLayerMaskDrawer via FindPropertyRelative("_bits").
        // ビットマスク本体。名前 _bits は RenderingLayerMaskDrawer が FindPropertyRelative("_bits") で参照します。
        [SerializeField] private uint _bits;

        public RenderingLayerMask(uint value) { _bits = value; }

        /// <summary>位掩码值。 // The bit mask value. // ビットマスク値。</summary>
        public uint Value { get => _bits; set => _bits = value; }

        // 与 uint 隐式互转：让本类型能无缝喂给 FilteringSettings(...) 并与 renderingLayerMask(uint) 直接比较。
        // ⚠ 故意不定义 == / != 运算符——一旦定义，"uint != RenderingLayerMask" 会在「内置 uint 比较」与
        //   「自定义运算符（uint 隐式转 RenderingLayerMask）」之间产生二义（CS0034）。仅保留隐式转换，
        //   编译器就会把混合比较解析为内置 uint 比较，行为正确且无歧义。
        // Implicit uint conversions let this type feed straight into FilteringSettings(...) and compare with
        // renderingLayerMask (uint). Intentionally NO == / != operators: defining them makes
        // "uint != RenderingLayerMask" ambiguous (CS0034) between the built-in uint comparison and the
        // user-defined operator. With only the conversions, the compiler resolves mixed comparisons to the
        // built-in uint comparison — correct and unambiguous.
        public static implicit operator uint(RenderingLayerMask mask) => mask._bits;
        public static implicit operator RenderingLayerMask(uint value) => new RenderingLayerMask(value);

        public override string ToString() => _bits.ToString();
    }
}
#endif

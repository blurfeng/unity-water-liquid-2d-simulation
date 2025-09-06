using System;

/// <summary>
/// 渲染层枚举。
/// 因为 UniversalRenderPipelineGlobalSettings 并不支持直接获取到 RenderingLayerMask 的名字列表。
/// 所以这里手动定义一个枚举，方便在代码中使用和管理渲染层。
/// </summary>
[Flags]
public enum ERenderingLayerMask
{
    Default = 1 << 0,
    Liquid = 1 << 1,
    Layer2 = 1 << 2,
    Layer3 = 1 << 3,
}

public static class RenderingLayerMaskUtil
{
    public static readonly string[] LayerNames;
    public static bool IsHaveLayer => LayerNames.Length > 0;
    
    static RenderingLayerMaskUtil()
    {
        LayerNames = typeof(ERenderingLayerMask).GetEnumNames();
    }
}
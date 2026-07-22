namespace MettlerDataCollection.Device;

/// <summary>
///     仪器的工作模式。决定 IDevice 如何解析每行数据。
/// </summary>
public enum CollectMode
{
    /// <summary>双工模式：仪器同时发 pH 消息和电导率消息，pH 消息带时间戳，电导率消息借前一条 pH 的时间戳</summary>
    PH_AND_COND,

    /// <summary>单工模式：仅 pH（暂未实现）</summary>
    PH_ONLY,

    /// <summary>单工模式：仅电导率（暂未实现）</summary>
    COND_ONLY,
}

namespace MettlerDataCollection.Services;

/// <summary>
///     把每次采集的原始记录实时落盘到本地 txt 文件。
///     实现必须保证每条记录都强制 flush 到底层（防断电丢失）。
/// </summary>
public interface IDataPersistenceService
{
    /// <summary>
    ///     当前正在写入的文件路径。未开始采集或已停止时为 <c>null</c>。
    /// </summary>
    string? CurrentFilePath { get; }

    /// <summary>
    ///     开始一个新的样本数据文件。如果已有打开的文件，会先关闭。
    ///     文件存放在 <see cref="IDataPersistenceService" /> 构造时指定的目录。
    /// </summary>
    /// <param name="sampleNo">样品编号，会作为文件名后缀。</param>
    void StartNewFile(string sampleNo);

    /// <summary>
    ///     写入一条原始记录。实现会加上时间戳前缀。
    ///     未开始采集时调用会被静默忽略（不抛异常）。
    /// </summary>
    void WriteRecord(string rawRecord);

    /// <summary>
    ///     显式关闭当前文件。窗口退出或开始新一轮采集前应调用。
    /// </summary>
    void Stop();
}

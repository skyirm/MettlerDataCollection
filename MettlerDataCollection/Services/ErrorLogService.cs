using System;
using System.Threading;
using Serilog.Core;
using Serilog.Events;

namespace MettlerDataCollection.Services;

/// <summary>
///     错误日志条目：UI 上展示的一行。
///     <see cref="FormattedText" /> 已经把时间、级别、消息、异常拼成单行（异常在第二行缩进），
///     直接绑给 ListBox.ItemsSource 即可。
/// </summary>
public sealed record ErrorLogEntry(
    DateTime Timestamp,
    LogEventLevel Level,
    string Message,
    string? ExceptionText)
{
    /// <summary>UI 显示用的两行格式：首行带时间戳 + 级别，异常另起一行缩进。</summary>
    public string FormattedText => ExceptionText is null
        ? $"[{Timestamp:HH:mm:ss.fff}] [{Level,-5}] {Message}"
        : $"[{Timestamp:HH:mm:ss.fff}] [{Level,-5}] {Message}\n        {ExceptionText.Replace("\n", "\n        ")}";
}

/// <summary>
///     全局错误日志收集器：Serilog 的自定义 <see cref="ILogEventSink" />。
///     <para>
///         App.OnStartup 把这个实例注册到 <c>LoggerConfiguration.WriteTo.Sink(...)</c>，
///         之后任何地方 <c>Log.Error / Log.Warning</c> 都会流到这里。
///     </para>
///     <para>
///         UI 层（<c>ErrorLog</c> UserControl）订阅 <see cref="EntryAdded" /> 实时显示；
///         MainWindow 订阅 <see cref="FirstErrorOccurred" /> 弹一次提示。
///     </para>
/// </summary>
/// <remarks>
///     过滤策略：只向上推送 <see cref="LogEventLevel.Warning" /> 及以上的条目，
///     Info / Debug 直接忽略（否则应用启动那一堆 Log.Information 会刷爆抽屉）。
///     全部级别的日志仍走 file sink 写盘，需要看完整日志去 log/ 目录。
/// </remarks>
public sealed class ErrorLogService : ILogEventSink
{
    public static ErrorLogService Instance { get; } = new();

    private ErrorLogService() { }

    /// <summary>每条 Warning+ 日志都会触发。订阅者在自己的线程上下文里处理（Serilog 调用 Emit 时所在线程）。</summary>
    public event Action<ErrorLogEntry>? EntryAdded;

    /// <summary>进程生命周期内只触发一次：第一次出现 Warning+ 日志时触发。
    /// MainWindow 用它弹一次"请查看错误日志"提示，之后的错误只进抽屉不弹窗。</summary>
    public event Action<ErrorLogEntry>? FirstErrorOccurred;

    /// <summary>0=未通知，1=已通知。Interlocked 保护跨线程安全。</summary>
    private int _firstErrorNotified;

    public void Emit(LogEvent logEvent)
    {
        if (logEvent.Level < LogEventLevel.Warning) return;

        var entry = new ErrorLogEntry(
            Timestamp: logEvent.Timestamp.LocalDateTime,
            Level: logEvent.Level,
            Message: logEvent.RenderMessage(),
            ExceptionText: logEvent.Exception?.ToString());

        EntryAdded?.Invoke(entry);

        // 全局"首次错误"：进程级别只弹一次
        if (Interlocked.Exchange(ref _firstErrorNotified, 1) == 0)
            FirstErrorOccurred?.Invoke(entry);
    }
}

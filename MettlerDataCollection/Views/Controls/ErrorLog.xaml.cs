using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MettlerDataCollection.Services;
using Serilog.Events;

namespace MettlerDataCollection.Views.Controls;

/// <summary>
///     全局错误日志抽屉：常驻底部的滚动日志条。
///     订阅 <see cref="ErrorLogService.Instance" /> 的 <see cref="ErrorLogService.EntryAdded" /> 事件，
///     把 Serilog 的 Warning+ 日志实时显示到 UI。
/// </summary>
/// <remarks>
///     <para>
///         任何地方调 <c>Log.Error / Log.Warning / Log.Fatal</c> 都会进这里。
///         Info / Debug 被 sink 过滤掉，避免应用启动那一堆 Log.Information 刷屏。
///     </para>
///     <para>
///         跨线程安全：Serilog sink 在任意线程触发 <see cref="ErrorLogService.Emit" />，
///         内部 marshal 到 UI 线程再加 ListBox。
///     </para>
///     <para>
///         FIFO 上限 <see cref="MaxEntries" /> 条。折叠状态只控制列表显隐。
///     </para>
/// </remarks>
public partial class ErrorLog : UserControl
{
    /// <summary>抽屉内最多保留的条目数。超出按 FIFO 丢弃最早的。</summary>
    private const int MaxEntries = 200;

    private bool _isCollapsed;
    private bool _subscribed;

    public ErrorLog()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed) return;
        ErrorLogService.Instance.EntryAdded += OnEntryAdded;
        _subscribed = true;
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) return;
        ErrorLogService.Instance.EntryAdded -= OnEntryAdded;
        _subscribed = false;
    }

    private void OnEntryAdded(ErrorLogEntry entry)
    {
        // Serilog sink 可能在任意线程触发，统一 marshal 回 UI 线程
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => AddEntryInternal(entry));
            return;
        }
        AddEntryInternal(entry);
    }

    private void AddEntryInternal(ErrorLogEntry entry)
    {
        var item = new ListBoxItem
        {
            Content = entry.FormattedText,
            Foreground = ColorForLevel(entry.Level),
            Padding = new Thickness(4, 2, 4, 2),
            ToolTip = entry.ExceptionText ?? entry.Message,
        };
        EntryList.Items.Add(item);

        // FIFO：超出上限丢最早的
        while (EntryList.Items.Count > MaxEntries)
            EntryList.Items.RemoveAt(0);

        // 自动滚到最后一条
        if (EntryList.Items.Count > 0)
            EntryList.ScrollIntoView(EntryList.Items[EntryList.Items.Count - 1]);
    }

    private static Brush ColorForLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Error => Brushes.Firebrick,
        LogEventLevel.Fatal => Brushes.DarkRed,
        LogEventLevel.Warning => Brushes.DarkOrange,
        _ => Brushes.Black,
    };

    private void ClearButton_Click(object sender, RoutedEventArgs e)
    {
        EntryList.Items.Clear();
    }

    private void ToggleButton_Click(object sender, RoutedEventArgs e)
    {
        _isCollapsed = !_isCollapsed;
        EntryList.Visibility = _isCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ToggleButton.Content = _isCollapsed ? "展开" : "折叠";
    }
}

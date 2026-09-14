using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MettlerDataCollection.Services;
using Serilog.Events;

namespace MettlerDataCollection.Views.Controls;

public partial class ErrorLog : UserControl
{
    private bool _subscribed;
    private ErrorLogWindow? _window;

    public ErrorLog()
    {
        InitializeComponent();
        // 在代码中设置引用，避免 XAML 将名称字面量误解析为 PlacementTarget 值。
        LogPopup.PlacementTarget = BadgeButton;
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed) return;
        ErrorLogService.Instance.EntryAdded += OnEntryAdded;
        ErrorLogService.Instance.EntriesCleared += OnEntriesCleared;
        _subscribed = true;
        RefreshBadge();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        if (!_subscribed) return;
        ErrorLogService.Instance.EntryAdded -= OnEntryAdded;
        ErrorLogService.Instance.EntriesCleared -= OnEntriesCleared;
        _subscribed = false;
    }

    private void OnEntryAdded(ErrorLogEntry entry)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => { AddEntry(entry); RefreshBadge(); });
            return;
        }
        AddEntry(entry);
        RefreshBadge();
    }

    private void OnEntriesCleared()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(ClearListAndRefresh);
            return;
        }
        ClearListAndRefresh();
    }

    private void ClearListAndRefresh()
    {
        EntryList.Items.Clear();
        RefreshBadge();
    }

    private void AddEntry(ErrorLogEntry entry)
    {
        EntryList.Items.Add(CreateItem(entry));
        while (EntryList.Items.Count > 200)
            EntryList.Items.RemoveAt(0);
        EntryList.ScrollIntoView(EntryList.Items[EntryList.Items.Count - 1]);
    }

    private void RefreshBadge()
    {
        var count = ErrorLogService.Instance.GetEntries().Count;
        BadgeButton.Content = count == 0 ? "错误日志" : $"错误日志（{count}）";
        BadgeButton.Foreground = count == 0 ? Brushes.DimGray : Brushes.Firebrick;
    }

    private static ListBoxItem CreateItem(ErrorLogEntry entry) => new()
    {
        Content = CreateCopyableText(entry),
        Padding = new Thickness(0),
    };

    private static TextBox CreateCopyableText(ErrorLogEntry entry)
    {
        var textBox = new TextBox
        {
            Text = entry.FormattedText,
            IsReadOnly = true,
            IsReadOnlyCaretVisible = true,
            TextWrapping = TextWrapping.Wrap,
            AcceptsReturn = true,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Foreground = ColorForLevel(entry.Level),
            Padding = new Thickness(4, 2, 4, 2),
            ToolTip = entry.ExceptionText ?? entry.Message,
        };

        var copyMenuItem = new MenuItem { Header = "复制" };
        copyMenuItem.Click += (_, _) =>
        {
            textBox.Focus();
            textBox.Copy();
        };
        textBox.ContextMenu = new ContextMenu();
        textBox.ContextMenu.Items.Add(copyMenuItem);
        return textBox;
    }

    private static Brush ColorForLevel(LogEventLevel level) => level switch
    {
        LogEventLevel.Error => Brushes.Firebrick,
        LogEventLevel.Fatal => Brushes.DarkRed,
        LogEventLevel.Warning => Brushes.DarkOrange,
        _ => Brushes.Black,
    };

    private void BadgeButton_Click(object sender, RoutedEventArgs e)
    {
        if (LogPopup.IsOpen)
        {
            LogPopup.IsOpen = false;
            return;
        }

        EntryList.Items.Clear();
        foreach (var entry in ErrorLogService.Instance.GetEntries())
            EntryList.Items.Add(CreateItem(entry));
        if (EntryList.Items.Count > 0)
            EntryList.ScrollIntoView(EntryList.Items[EntryList.Items.Count - 1]);
        LogPopup.IsOpen = true;
    }

    private void ClearButton_Click(object sender, RoutedEventArgs e) => ErrorLogService.Instance.ClearEntries();

    private void ClosePopup_Click(object sender, RoutedEventArgs e) => LogPopup.IsOpen = false;

    private void OpenWindow_Click(object sender, RoutedEventArgs e)
    {
        if (_window is null)
        {
            _window = new ErrorLogWindow { Owner = Window.GetWindow(this) };
            _window.Closed += (_, _) => _window = null;
            _window.Show();
        }
        else
        {
            if (_window.WindowState == WindowState.Minimized)
                _window.WindowState = WindowState.Normal;
            _window.Activate();
        }
        LogPopup.IsOpen = false;
    }
}

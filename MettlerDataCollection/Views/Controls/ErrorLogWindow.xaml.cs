using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MettlerDataCollection.Services;
using Serilog.Events;

namespace MettlerDataCollection.Views.Controls;

public partial class ErrorLogWindow : Window
{
    private bool _subscribed;

    public ErrorLogWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Closed += OnClosed;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        if (_subscribed) return;
        ErrorLogService.Instance.EntryAdded += OnEntryAdded;
        ErrorLogService.Instance.EntriesCleared += OnEntriesCleared;
        _subscribed = true;
        RefreshList();
    }

    private void OnClosed(object? sender, EventArgs e)
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
            Dispatcher.BeginInvoke(() => AddEntry(entry));
            return;
        }
        AddEntry(entry);
    }

    private void AddEntry(ErrorLogEntry entry)
    {
        EntryList.Items.Add(CreateItem(entry));
        while (EntryList.Items.Count > 200)
            EntryList.Items.RemoveAt(0);
        EntryList.ScrollIntoView(EntryList.Items[EntryList.Items.Count - 1]);
    }

    private void OnEntriesCleared()
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.BeginInvoke(() => EntryList.Items.Clear());
            return;
        }
        EntryList.Items.Clear();
    }

    private void RefreshList()
    {
        EntryList.Items.Clear();
        foreach (var entry in ErrorLogService.Instance.GetEntries())
            EntryList.Items.Add(CreateItem(entry));
        if (EntryList.Items.Count > 0)
            EntryList.ScrollIntoView(EntryList.Items[EntryList.Items.Count - 1]);
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
            Foreground = entry.Level switch
            {
                LogEventLevel.Error => Brushes.Firebrick,
                LogEventLevel.Fatal => Brushes.DarkRed,
                LogEventLevel.Warning => Brushes.DarkOrange,
                _ => Brushes.Black,
            },
            Padding = new Thickness(4, 3, 4, 3),
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

    private void ClearButton_Click(object sender, RoutedEventArgs e) => ErrorLogService.Instance.ClearEntries();

    private void TopmostChanged(object sender, RoutedEventArgs e) => Topmost = TopmostCheckBox.IsChecked == true;
}

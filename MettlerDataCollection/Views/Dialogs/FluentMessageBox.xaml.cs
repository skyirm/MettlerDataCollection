using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MettlerDataCollection.Views.Dialogs;

/// <summary>
///     FluentMessageBox.xaml 的交互逻辑
/// </summary>
public partial class FluentMessageBox : Window
{
    public FluentMessageBox(string message, string title,
        MessageBoxButton buttons, MessageBoxImage icon)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;

        // 设置图标
        IconImage.Source = icon switch
        {
            MessageBoxImage.Information => SystemIcons.Information.ToImageSource(),
            MessageBoxImage.Warning => SystemIcons.Warning.ToImageSource(),
            MessageBoxImage.Error => SystemIcons.Error.ToImageSource(),
            _ => null
        };

        // 设置按钮
        AddButtons(buttons);
    }

    public FluentMessageBox(string message, string title, string buttonText)
    {
        InitializeComponent();

        Title = title;
        MessageText.Text = message;
        IconImage.Visibility = Visibility.Collapsed;

        AddButton(buttonText, MessageBoxResult.OK);
        AddButton("取消", MessageBoxResult.Cancel);
    }

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private void AddButton(string text, MessageBoxResult result)
    {
        var btn = new Button
        {
            Content = text,
            Width = 80,
            Margin = new Thickness(5),
            Padding = new Thickness(6)
        };
        btn.Click += (_, _) =>
        {
            Result = result;
            Close();
        };
        ButtonPanel.Children.Add(btn);
    }

    private void AddButtons(MessageBoxButton buttons)
    {
        switch (buttons)
        {
            case MessageBoxButton.OK:
                AddButton("确定", MessageBoxResult.OK);
                break;

            case MessageBoxButton.OKCancel:
                AddButton("确定", MessageBoxResult.OK);
                AddButton("取消", MessageBoxResult.Cancel);
                break;

            case MessageBoxButton.YesNo:
                AddButton("是", MessageBoxResult.Yes);
                AddButton("否", MessageBoxResult.No);
                break;

            case MessageBoxButton.YesNoCancel:
                AddButton("是", MessageBoxResult.Yes);
                AddButton("否", MessageBoxResult.No);
                AddButton("取消", MessageBoxResult.Cancel);
                break;
        }
    }

    public static MessageBoxResult Show(string message, string title,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage icon = MessageBoxImage.None,
        Window owner = null)
    {
        var msg = new FluentMessageBox(message, title, buttons, icon);
        if (owner != null)
        {
            try
            {
                msg.Owner = owner;
            }
            catch (InvalidOperationException)
            {
                // WPF 不允许将 Owner 设到"未显示/正在关闭"的窗口（MainWindow_Closing 弹确认框时会触发）。
                // 失败时降级为无 Owner 弹窗，对话框照常能用，只是不会居中到 owner。
            }
        }

        msg.ShowDialog();
        return msg.Result;
    }

    public static MessageBoxResult Show(string message, string title,
        string buttonText, Window owner = null)
    {
        var msg = new FluentMessageBox(message, title, buttonText);
        if (owner != null)
        {
            try
            {
                msg.Owner = owner;
            }
            catch (InvalidOperationException)
            {
                // 同上：Owner 设置失败时降级为无 Owner 弹窗
            }
        }

        msg.ShowDialog();
        return msg.Result;
    }
}

public static class IconExtensions
{
    public static BitmapSource ToImageSource(this Icon icon)
    {
        return Imaging.CreateBitmapSourceFromHIcon(
            icon.Handle,
            Int32Rect.Empty,
            BitmapSizeOptions.FromEmptyOptions());
    }
}
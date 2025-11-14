using System.Drawing;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace MettlerDataCollection;

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

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    private void AddButtons(MessageBoxButton buttons)
    {
        void Add(string text, MessageBoxResult result)
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

        switch (buttons)
        {
            case MessageBoxButton.OK:
                Add("确定", MessageBoxResult.OK);
                break;

            case MessageBoxButton.OKCancel:
                Add("确定", MessageBoxResult.OK);
                Add("取消", MessageBoxResult.Cancel);
                break;

            case MessageBoxButton.YesNo:
                Add("是", MessageBoxResult.Yes);
                Add("否", MessageBoxResult.No);
                break;

            case MessageBoxButton.YesNoCancel:
                Add("是", MessageBoxResult.Yes);
                Add("否", MessageBoxResult.No);
                Add("取消", MessageBoxResult.Cancel);
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
            msg.Owner = owner;

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
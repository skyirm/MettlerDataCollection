using System.Diagnostics;
using System.Windows;
using System.Windows.Input;

namespace MettlerDataCollection;

/// <summary>
///     About.xaml 的交互逻辑
/// </summary>
public partial class About : Window
{
    public About()
    {
        InitializeComponent();
    }

    private void OnLinkClick(object sender, MouseButtonEventArgs e)
    {
        var url = "https://github.com/skyirm/MettlerDataCollection";
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }
        catch
        {
            MessageBox.Show("无法打开链接：" + url);
        }
    }

    // 关闭窗口
    private void OnCloseClick(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
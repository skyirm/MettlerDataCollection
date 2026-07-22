using System.Windows;

namespace MettlerDataCollection.Views.Dialogs;

/// <summary>
///     InputSampleNo.xaml 的交互逻辑
/// </summary>
public partial class InputSampleNo : Window
{
    public InputSampleNo(string tips)
    {
        InitializeComponent();
        tipsTextBlock.Text = tips;
    }

    public string InputText => InputBox.Text;

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true; // 标记用户点击了“确定”
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
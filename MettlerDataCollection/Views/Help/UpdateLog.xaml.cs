using System.IO;
using System.Windows;

namespace MettlerDataCollection.Views.Help;

public partial class UpdateLog : Window
{
    public UpdateLog()
    {
        InitializeComponent();
        LoadChangelog();
    }

    private void LoadChangelog()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "CHANGELOG.md");
        try
        {
            ChangelogTextBox.Text = File.Exists(path)
                ? File.ReadAllText(path)
                : "未找到更新日志文件。\n\n请确认 CHANGELOG.md 已随程序一起发布。";
        }
        catch (Exception ex)
        {
            ChangelogTextBox.Text = $"读取更新日志失败：{ex.Message}";
        }
    }

    private void Close_Click(object sender, RoutedEventArgs e) => Close();
}

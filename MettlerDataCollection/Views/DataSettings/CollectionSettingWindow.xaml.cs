using System.IO;
using System.Windows;
using MettlerDataCollection.Properties;
using Microsoft.Win32;
using Serilog;

namespace MettlerDataCollection.Views.DataSettings;

public partial class CollectionSettingWindow : Window
{
    private string _selectedPath;

    public CollectionSettingWindow()
    {
        InitializeComponent();
        _selectedPath = Settings.Default.DataPath;
        DataPathText.Text = string.IsNullOrWhiteSpace(_selectedPath) ? "未设置" : _selectedPath;
    }

    private void SelectFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "请选择数据存放路径",
            Multiselect = false,
            FolderName = _selectedPath
        };

        if (dialog.ShowDialog() == true)
        {
            _selectedPath = dialog.FolderName;
            DataPathText.Text = _selectedPath;
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Settings.Default.DataPath = _selectedPath;
        Settings.Default.Save();
        Directory.CreateDirectory(_selectedPath);
        Log.Information($"数据存放路径已修改为: {_selectedPath}");
        DialogResult = true;
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}

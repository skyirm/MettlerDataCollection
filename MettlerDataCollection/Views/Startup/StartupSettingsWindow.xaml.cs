using System.IO;
using System.Windows;
using MettlerDataCollection.Device;
using MettlerDataCollection.Properties;
using MettlerDataCollection.Views.Dialogs;
using Microsoft.Win32;
using Serilog;

namespace MettlerDataCollection.Views.Startup;

/// <summary>
///     启动设置对话框：选择数据存放目录 + 选择设备。
///     3 行布局：第 1 行目录输入、第 2 行设备下拉、第 3 行确认/取消。
///     通过反射发现所有 <see cref="IDevice" /> 实现列在下拉框。
/// </summary>
public partial class StartupSettingsWindow : Window
{
    public string DataPath => DataPathTextBox.Text.Trim();

    /// <summary>用户在确认时选中的设备实例（按 DeviceComboBox.SelectedItem 的类型创建）。</summary>
    public IDevice? SelectedDevice { get; private set; }

    /// <summary>
    ///     true（默认）= 显示"数据存放路径"行（首次启动 OnStartup 走这个模式）；
    ///     false = 隐藏路径行，只让用户选设备（菜单"重新选择设备"走这个模式，
    ///             因为运行时路径由"采集设置"窗口管理，路径已经存好）。
    /// </summary>
    public bool ShowDataPath { get; set; } = true;

    public StartupSettingsWindow()
    {
        InitializeComponent();

        // 注意：ShowDataPath 是用对象初始化器在构造函数返回后设的，构造函数里读还是默认值。
        // 所以 UI 调整（隐藏路径行 / 改 Title / 缩高度）挪到 Loaded 里做，那时值已生效。
        Loaded += OnLoadedAdjustUi;

        // 记住上次的目录（即使隐藏也填值，避免 ShowDataPath 切换时的边角 case）
        DataPathTextBox.Text = Settings.Default.DataPath ?? string.Empty;

        // 反射发现所有 IDevice 实现
        var deviceTypes = DeviceCatalog.DiscoverDeviceTypes();
        if (deviceTypes.Count == 0)
        {
            Log.Error("反射未发现任何 IDevice 实现类");
            FluentMessageBox.Show("程序初始化失败：未找到任何可用的设备驱动。", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error, this);
            // 让窗口显示但确认按钮始终 disable
        }
        else
        {
            // 找已存设备类型的下标，作为默认选中
            var savedType = FindSavedDeviceType(deviceTypes);
            int defaultIndex = 0;

            foreach (var type in deviceTypes)
            {
                // 用一个临时实例拿 Name/Description
                var instance = DeviceCatalog.CreateDevice(type);
                DeviceComboBox.Items.Add(new DeviceOption(type, instance.Name, instance.Description));
                if (savedType != null && type == savedType)
                    defaultIndex = DeviceComboBox.Items.Count - 1;
            }
            DeviceComboBox.DisplayMemberPath = nameof(DeviceOption.DisplayText);
            DeviceComboBox.SelectedIndex = defaultIndex;
        }

        UpdateOkButtonState();
    }

    private void OnLoadedAdjustUi(object sender, RoutedEventArgs e)
    {
        // 一次性调整（Loaded 只触发一次，所以不需要 -= ）
        if (!ShowDataPath)
        {
            DataPathRow.Visibility = Visibility.Collapsed;
            Height = 180;
            Title = "选择设备";
        }
    }

    private static Type? FindSavedDeviceType(IReadOnlyList<Type> types)
    {
        var savedName = Settings.Default.SelectedDeviceTypeName;
        if (string.IsNullOrWhiteSpace(savedName)) return null;
        return types.FirstOrDefault(t => t.FullName == savedName);
    }

    private void BrowseButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "选择数据存放目录",
            Multiselect = false
        };

        if (!string.IsNullOrWhiteSpace(DataPathTextBox.Text) && Directory.Exists(DataPathTextBox.Text))
            dialog.InitialDirectory = DataPathTextBox.Text;

        if (dialog.ShowDialog() == true)
        {
            DataPathTextBox.Text = dialog.FolderName;
            UpdateOkButtonState();
        }
    }

    private void DataPathTextBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
    {
        UpdateOkButtonState();
    }

    private void DeviceComboBox_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        UpdateOkButtonState();
    }

    private void OkButton_Click(object sender, RoutedEventArgs e)
    {
        if (!ValidateInputs()) return;

        var option = (DeviceOption)DeviceComboBox.SelectedItem;
        try
        {
            SelectedDevice = DeviceCatalog.CreateDevice(option.DeviceType);
        }
        catch (Exception ex)
        {
            FluentMessageBox.Show($"创建设备失败：{ex.Message}", "错误",
                MessageBoxButton.OK, MessageBoxImage.Error, this);
            return;
        }

        // 只选设备模式下不写 DataPath（路径由"采集设置"窗口管理，保留原值）
        if (ShowDataPath)
            Settings.Default.DataPath = DataPath;
        Settings.Default.SelectedDeviceTypeName = option.DeviceType.FullName;
        Settings.Default.Save();

        Log.Information($"启动设置完成：路径={DataPath}, 设备={SelectedDevice.Name}");
        DialogResult = true;
        Close();
    }

    private void CancelButton_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private bool ValidateInputs()
    {
        if (string.IsNullOrWhiteSpace(DataPath))
        {
            FluentMessageBox.Show("请选择或输入数据存放路径。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return false;
        }

        if (!Directory.Exists(DataPath))
        {
            FluentMessageBox.Show($"目录不存在：\n{DataPath}\n\n请重新选择。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return false;
        }

        if (DeviceComboBox.SelectedItem is not DeviceOption)
        {
            FluentMessageBox.Show("请选择仪器设备。", "提示",
                MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return false;
        }

        return true;
    }

    private void UpdateOkButtonState()
    {
        var pathOk = !ShowDataPath
                     || (!string.IsNullOrWhiteSpace(DataPathTextBox.Text)
                         && Directory.Exists(DataPathTextBox.Text.Trim()));
        var deviceOk = DeviceComboBox.SelectedItem is DeviceOption;
        OkButton.IsEnabled = pathOk && deviceOk;
    }

    private sealed class DeviceOption
    {
        public Type DeviceType { get; }
        public string Name { get; }
        public string Description { get; }
        public string DisplayText => $"{Name}（{Description}）";

        public DeviceOption(Type type, string name, string description)
        {
            DeviceType = type;
            Name = name;
            Description = description;
        }
    }
}

using Microsoft.Win32;
using MettlerDataCollection.Device;
using MettlerDataCollection.Properties;
using ScottPlot;
using ScottPlot.Plottables;
using Serilog;
using System.IO;
using System.Text;
using System.Windows;

namespace MettlerDataCollection;

/// <summary>
///     RecoverData.xaml 的交互逻辑
/// </summary>
public partial class RecoverData : Window
{
    private readonly S470K _s470K = new();
    private DataLogger _conductivityLogger;
    private DataLogger _phLogger;

    public RecoverData()
    {
        InitializeComponent();
        InintalPlot();

        // 用 S470K 解析历史数据：每行触发 ParseData，OnDataProduced 把数据点加到 plot
        _s470K.OnDataProduced += OnDataProduced;
        _s470K.OnParseError += OnParseError;
    }

    private void InintalPlot()
    {
        MainPlot.Plot.XLabel("Time (s)");
        MainPlot.Plot.Axes.Bottom.TickLabelStyle.FontSize = 22;

        MainPlot.Plot.Axes.Left.Label.Text = "pH";
        MainPlot.Plot.Axes.Left.Label.FontSize = 22;
        MainPlot.Plot.Axes.Left.Label.ForeColor = Colors.Red;
        MainPlot.Plot.Axes.Left.TickLabelStyle.FontSize = 22;
        MainPlot.Plot.Axes.Left.FrameLineStyle.Color = Colors.Red;
        MainPlot.Plot.Axes.Left.MajorTickStyle.Color = Colors.Red;
        MainPlot.Plot.Axes.Left.MinorTickStyle.Color = Colors.Red;
        MainPlot.Plot.Axes.Left.TickLabelStyle.ForeColor = Colors.Red;

        MainPlot.Plot.Axes.Right.Label.Text = "Conductivity (µS/cm)";
        MainPlot.Plot.Axes.Right.Label.FontSize = 22;
        MainPlot.Plot.Axes.Right.Label.ForeColor = Colors.Blue;
        MainPlot.Plot.Axes.Right.TickLabelStyle.FontSize = 22;
        MainPlot.Plot.Axes.Right.FrameLineStyle.Color = Colors.Blue;
        MainPlot.Plot.Axes.Right.MajorTickStyle.Color = Colors.Blue;
        MainPlot.Plot.Axes.Right.MinorTickStyle.Color = Colors.Blue;
        MainPlot.Plot.Axes.Right.TickLabelStyle.ForeColor = Colors.Blue;

        MainPlot.Plot.Grid.MajorLineColor = Colors.Green.WithOpacity(.3);
        MainPlot.Plot.Grid.MajorLineWidth = 2;

        MainPlot.Plot.Grid.MinorLineColor = Colors.Gray.WithOpacity(.1);
        MainPlot.Plot.Grid.MinorLineWidth = 1;

        MainPlot.Plot.Legend.FontSize = 22;

        _phLogger = MainPlot.Plot.Add.DataLogger();
        _conductivityLogger = MainPlot.Plot.Add.DataLogger();
        _phLogger.Color = Colors.Red;
        _phLogger.LineWidth = 2;
        _conductivityLogger.Color = Colors.Blue;
        _conductivityLogger.LineWidth = 2;
        _phLogger.Axes.YAxis = MainPlot.Plot.Axes.Left;
        _conductivityLogger.Axes.YAxis = MainPlot.Plot.Axes.Right;


        MainPlot.Refresh();
    }

    private void OnDataProduced(MeasureData data)
    {
        // 已经在 UI 线程（ReadFile await ReadAllLinesAsync 之后），不需要 Dispatcher
        _phLogger.Add(data.Time, data.Ph);
        _conductivityLogger.Add(data.Time, data.Conductivity);
    }

    private void OnParseError(string error)
    {
        Log.Error($"[RecoverData] 解析错误: {error}");
    }

    private async void ReadFile(object sender, RoutedEventArgs e)
    {
        try
        {
            var openFileDialog = new OpenFileDialog
            {
                Title = "打开文件",
                InitialDirectory = Settings.Default.DataPath,
                DefaultExt = ".txt"
            };

            var result = openFileDialog.ShowDialog();

            if (result == false) return;

            _conductivityLogger.Clear();
            _phLogger.Clear();

            var fileName = openFileDialog.FileName;
            if (!File.Exists(fileName))
            {
                FluentMessageBox.Show("文件不存在。", "错误", MessageBoxButton.OK, MessageBoxImage.Error, this);
                return;
            }

            this.FileNameText.Text = Path.GetFileName(fileName);

            // 根据用户 UI 选的模式设置 S470K 解析模式
            _s470K.CurrentMode = CollectModeCombox.SelectedIndex switch
            {
                0 => CollectMode.PH_AND_COND,
                1 => CollectMode.PH_ONLY,
                2 => CollectMode.COND_ONLY,
                _ => CollectMode.PH_AND_COND,
            };

            foreach (var line in await File.ReadAllLinesAsync(fileName, Encoding.UTF8))
            {
                var parts = line.Split('|');
                if (parts.Length >= 2)
                {
                    // 写入格式是 "yyyy-MM-dd HH:mm:ss.fff| 原始消息"
                    // parts[1] 是 | 之后的原始消息，跟实时采集时 S470K.ParseData 收到的格式一致
                    _s470K.ParseData(parts[1].Trim());
                }
            }

            MainPlot.Refresh();
        }
        catch (Exception ex)
        {
            Log.Error($"读取文件时发生错误: {ex.Message}");
            FluentMessageBox.Show($"读取文件时发生错误: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error, this);
        }
    }

    private void ExportData(object sender, RoutedEventArgs e)
    {
        var saveFileDialog = new SaveFileDialog();

        // **配置属性**
        var contentString = new StringBuilder();

        // 1. 设置默认的文件名
        saveFileDialog.FileName = "";

        // 2. 设置默认的文件扩展名
        saveFileDialog.DefaultExt = ".txt";

        // 3. 设置文件过滤器 (用于限制文件类型)
        // 格式: "描述|*.扩展名|描述|*.扩展名"
        saveFileDialog.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";

        // **显示对话框**
        var result = saveFileDialog.ShowDialog();

        // **处理对话框结果**
        if (result == true)
        {
            // 获取用户选择的文件路径（包含文件名）
            var filename = saveFileDialog.FileName;

            switch (CollectModeCombox.SelectedIndex)
            {
                case 1:
                    contentString.AppendLine("Time(s) pH");
                    foreach (var record in _phLogger.Data.Coordinates)
                    {
                        var time = record.X;
                        var pH = record.Y;
                        contentString.AppendLine($"{time,5} {pH,6}");
                    }

                    break;
                case 2:
                    contentString.AppendLine("Time(s) Conductivity(µS/cm)");
                    foreach (var record in _conductivityLogger.Data.Coordinates)
                    {
                        var time = record.X;
                        var conductivity = record.Y;
                        contentString.AppendLine($"{time,5} {conductivity,7}");
                    }

                    break;
                case 0:
                    contentString.AppendLine("Time(s) pH Conductivity(µS/cm)");
                    foreach (var record in _phLogger.Data.Coordinates)
                    {
                        var time = record.X;
                        var pH = record.Y;
                        var conductivityRecord = _conductivityLogger.Data.Coordinates.FirstOrDefault(r => r.X == time);
                        var conductivity = conductivityRecord != null ? conductivityRecord.Y : 0;
                        contentString.AppendLine($"{time,6} {pH,6} {conductivity,7}");
                    }

                    break;
            }

            // 实际保存文件的代码（例如使用 System.IO.File.WriteAllText）
            try
            {
                File.WriteAllText(filename, contentString.ToString(), Encoding.UTF8);
                Log.Information($"数据已导出到文件: {filename}");
            }
            catch (Exception ex)
            {
                FluentMessageBox.Show($"导出数据时发生错误，请重试。{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error,
                    this);
                Log.Error($"导出数据到文件 {filename} 时发生错误。{ex.Message}");
            }
        }
    }
}

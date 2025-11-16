using Microsoft.Win32;
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
    private DataLogger _conductivityLogger;
    private PartialDataRecord? _partialDataRecord;
    private DataLogger _phLogger;

    public RecoverData()
    {
        InitializeComponent();
        InintalPlot();
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

    private async void ReadFile(object sender, RoutedEventArgs e)
    {
        var openFileDialog = new OpenFileDialog
        {
            Title = "打开文件",
            InitialDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "origindata"),
            DefaultExt = ".txt"
        };

        var result = openFileDialog.ShowDialog();

        if (result == false) return;

        var fileName = openFileDialog.FileName;

        foreach (var line in await File.ReadAllLinesAsync(fileName, Encoding.UTF8))
        {
            var parts = line.Split('|');
            ProcessRecord(parts[1]);
        }

        MainPlot.Refresh();
    }

    private void ProcessRecord(string completeRecord)
    {
        switch (CollectModeCombox.SelectedIndex)
        {
            case 0:
            {
                var parts = completeRecord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return;

                if (parts.Length >= 3 && parts[1] == "1")
                {
                    var time = int.TryParse(parts[0].Replace("s", ""), out var t) ? t : 0;
                    var pHValue = double.TryParse(parts[2], out var pH) ? pH : 0;
                    if (_partialDataRecord == null)
                        _partialDataRecord = new PartialDataRecord { Time = time, PHValue = pHValue };
                }
                else if (parts[0] == "2" && parts.Length >= 2)
                {
                    var conductivityValue = double.TryParse(parts[1], out var cond) ? cond : 0;
                    if (_partialDataRecord != null)
                    {
                        _phLogger.Add(_partialDataRecord.Time, _partialDataRecord.PHValue);
                        _conductivityLogger.Add(_partialDataRecord.Time, conductivityValue);
                        _partialDataRecord = null;
                    }
                }

                break;
            }
            case 1:
            {
                var parts = completeRecord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return;
                var time = int.TryParse(parts[0].Replace("s", ""), out var _t) ? _t : 0;
                var pHValue = double.TryParse(parts[1], out var pH) ? pH : 0;
                _phLogger.Add(time, pHValue);
                break;
            }
            case 2:
            {
                var parts = completeRecord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return;
                var time = int.TryParse(parts[0].Replace("s", ""), out var _t) ? _t : 0;
                var condValue = double.TryParse(parts[1], out var cond) ? cond : 0;
                _conductivityLogger.Add(time, condValue);
                break;
            }
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

        // 4. 设置初始目录（可选）
        // saveFileDialog.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);

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
                    contentString.AppendLine("Time(s),pH");
                    foreach (var record in _phLogger.Data.Coordinates)
                    {
                        var time = record.X;
                        var pH = record.Y;
                        contentString.AppendLine($"{time},{pH},");
                    }

                    break;
                case 2:
                    contentString.AppendLine("Time(s),Conductivity(µS/cm)");
                    foreach (var record in _conductivityLogger.Data.Coordinates)
                    {
                        var time = record.X;
                        var conductivity = record.Y;
                        contentString.AppendLine($"{time},{conductivity}");
                    }

                    break;
                case 0:
                    contentString.AppendLine("Time(s),pH,Conductivity(µS/cm)");
                    foreach (var record in _phLogger.Data.Coordinates)
                    {
                        var time = record.X;
                        var pH = record.Y;
                        var conductivityRecord = _conductivityLogger.Data.Coordinates.FirstOrDefault(r => r.X == time);
                        var conductivity = conductivityRecord != null ? conductivityRecord.Y : 0;
                        contentString.AppendLine($"{time},{pH},{conductivity}");
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
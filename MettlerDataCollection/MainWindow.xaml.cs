using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MettlerDataCollection.Properties;
using Microsoft.Win32;
using ScottPlot;
using ScottPlot.DataGenerators;
using ScottPlot.Plottables;
using Serilog;

namespace MettlerDataCollection;

public partial class MainWindow : Window, IDisposable
{
    private bool _disposed;
    private const string RecordDelimiter = "\r\n";
    private readonly object _bufferLock = new();

    private readonly StringBuilder _receiveBuffer = new();

    private readonly ComPortWatcher _watcher;
    private readonly object _fileLock = new();
    private readonly SerialPort _serialPort = new();
    private readonly DispatcherTimer _dispatcherTimer = new();
    private readonly object _logFilePathLock = new();
    private string _logFilePath = $"./log/{DateTime.Now:yyyyMMdd_HHmmss}.txt";

    private CollectMode _currentMode = CollectMode.PH_AND_COND;
    private volatile int _dataCount;
    private volatile bool _isCollecting;
    private DataLogger _conductivityLogger;
    private PartialDataRecord? _partialDataRecord;

    public string LogFilePath
    {
        get
        {
            lock (_logFilePathLock)
            {
                return _logFilePath;
            }
        }
        set
        {
            lock (_logFilePathLock)
            {
                _logFilePath = value;
            }
        }
    }

    private DataLogger _phLogger;
    private string _sampleNo = string.Empty;
    private LegendItem _timeLegendItem;

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;

        _dispatcherTimer.Interval = TimeSpan.FromSeconds(1);
        _dispatcherTimer.Tick += DispatcherTimerTick;

        _watcher = new ComPortWatcher();
        _watcher.ComPortsChanged += HandleComPortsChanged;
        _watcher.Start();
        HandleComPortsChanged(new List<string>(SerialPort.GetPortNames()));



        InitPlot();
        InitSerialPort();
        //Create_data(new object(), new RoutedEventArgs());
    }

    public string SelectedComport { get; set; }

    private void CreateOriginDirectory()
    {
        if (!Directory.Exists(Settings.Default.DataPath)) Directory.CreateDirectory(Settings.Default.DataPath);
    }

    private void InitPlot()
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
        _timeLegendItem = new LegendItem()
        {
            LabelText = "Current Time: 0s",
        };
        MainPlot.Plot.Legend.ManualItems.Add(_timeLegendItem);


        _phLogger = MainPlot.Plot.Add.DataLogger();
        _conductivityLogger = MainPlot.Plot.Add.DataLogger();
        _phLogger.Color = Colors.Red;
        _phLogger.LineWidth = 2;
        _conductivityLogger.Color = Colors.Blue;
        _conductivityLogger.LineWidth = 2;
        _phLogger.Axes.YAxis = MainPlot.Plot.Axes.Left;
        _conductivityLogger.Axes.YAxis = MainPlot.Plot.Axes.Right;
        _phLogger.LegendText = "Current pH: 0";
        _conductivityLogger.LegendText = "Current Cond: 0";


        MainPlot.Refresh();
    }

    private void InitSerialPort()
    {
        _serialPort.BaudRate = 9600;
        _serialPort.DataBits = 8;
        _serialPort.Parity = Parity.None;
        _serialPort.StopBits = StopBits.One;
        _serialPort.Handshake = Handshake.XOnXOff;
        _serialPort.DataReceived += SerialPort_DataReceived;
        _serialPort.ReceivedBytesThreshold = 1;
    }

    private void DispatcherTimerTick(object? sender, EventArgs e)
    {
        if (showFull.IsChecked == true)
        {
            _phLogger.ViewFull();
            _conductivityLogger.ViewFull();
        }
        else if (showSlide.IsChecked == true)
        {
            _phLogger.ViewSlide(200);
            _conductivityLogger.ViewSlide(200);
        }

        MainPlot.Refresh();
    }

    private void HandleComPortsChanged(List<string> list)
    {
        Dispatcher.Invoke(() => { ComportCombox.ItemsSource = new ObservableCollection<string>(list); });
    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var newData = _serialPort.ReadExisting();

            lock (_bufferLock)
            {
                _receiveBuffer.Append(newData);

                var bufferStr = _receiveBuffer.ToString();
                int delimiterIndex;

                // 不断查找完整的记录
                while ((delimiterIndex = bufferStr.IndexOf(RecordDelimiter)) >= 0)
                {
                    var completeRecord = bufferStr[..delimiterIndex].Trim();
                    bufferStr = bufferStr[(delimiterIndex + RecordDelimiter.Length)..];

                    if (!string.IsNullOrEmpty(completeRecord))
                    {
                        // 在 UI 线程处理完整记录
                        if (_isCollecting) Dispatcher.BeginInvoke(() => ProcessCompleteRecord(completeRecord));
                        WriteDataToFile(completeRecord);
                    }
                }

                // 剩下不完整的数据保留
                _receiveBuffer.Clear();
                _receiveBuffer.Append(bufferStr);
            }
        }
        catch (Exception ex)
        {
            Log.Error($"串口接收错误: {ex.Message}");
        }
    }

    private void ProcessCompleteRecord(string completeRecord)
    {
        try
        {
            switch (_currentMode)
            {
                case CollectMode.PH_AND_COND:
                    ProcessPhAndCondRecord(completeRecord);
                    break;
                case CollectMode.PH_ONLY:
                    ProcessPhRecord(completeRecord);
                    break;
                case CollectMode.COND_ONLY:
                    ProcessCondRecord(completeRecord);
                    break;
            }
        }
        catch (Exception ex)
        {
            Log.Error($"数据解析异常: {ex.Message}");
        }
    }

    private void ProcessCondRecord(string completeRecord)
    {
        var parts = completeRecord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return;
        var time = int.TryParse(parts[0].Replace("s", ""), out var _t) ? _t : 0;
        var CondValue = double.TryParse(parts[1], out var cond) ? cond : 0;
        _conductivityLogger.Add(time, CondValue);
        _conductivityLogger.LegendText = $"Current Cond: {CondValue}";
        _timeLegendItem.LabelText = $"Current Time: {time}s";
        _dataCount++;
        dataCountLabel.Content = $"已接收数据: {_dataCount}个";
    }

    private void ProcessPhRecord(string completeRecord)
    {
        var parts = completeRecord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2)
            return;
        var time = int.TryParse(parts[0].Replace("s", ""), out var _t) ? _t : 0;
        var pHValue = double.TryParse(parts[1], out var pH) ? pH : 0;
        _phLogger.Add(time, pHValue);
        _phLogger.LegendText = $"Current pH: {pHValue}";
        _timeLegendItem.LabelText = $"Current Time: {time}s";
        _dataCount++;
        dataCountLabel.Content = $"已接收数据: {_dataCount}个";
    }

    private void ProcessPhAndCondRecord(string completeRecord)
    {
        try
        {
            var parts = completeRecord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
                return;

            if (parts.Length >= 3 && parts[1] == "1")
            {
                var time = int.TryParse(parts[0].Replace("s", ""), out var t) ? t : 0;
                var pHValue = double.TryParse(parts[2], out var pH) ? pH : 0;
                if (_partialDataRecord == null) _partialDataRecord = new PartialDataRecord { Time = time, PHValue = pHValue };
            }
            else if (parts[0] == "2" && parts.Length >= 2)
            {
                var conductivityValue = double.TryParse(parts[1], out var cond) ? cond : 0;
                if (_partialDataRecord != null)
                {
                    _phLogger.Add(_partialDataRecord.Time, _partialDataRecord.PHValue);
                    _phLogger.LegendText = $"Current pH: {_partialDataRecord.PHValue}";
                    _conductivityLogger.Add(_partialDataRecord.Time, conductivityValue);
                    _conductivityLogger.LegendText = $"Current Cond: {conductivityValue}";
                    _timeLegendItem.LabelText = $"Current Time: {_partialDataRecord.Time}s";
                    _dataCount++;
                    dataCountLabel.Content = $"已接收数据: {_dataCount}个";
                    _partialDataRecord = null;
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error($"数据解析异常: {ex.Message}");
        }
    }

    private void WriteDataToFile(string completeRecord)
    {
        lock (_fileLock)
        {
            try
            {
                var logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}| {completeRecord}{Environment.NewLine}";

                // FileStream 确保我们可以控制 Flush
                // FileMode.Append: 如果文件存在则追加，否则创建
                using (var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.None))
                using (var writer = new StreamWriter(stream, Encoding.UTF8))
                {
                    writer.Write(logEntry);

                    // 强制将 StreamWriter 缓冲区数据写入 FileStream 缓冲区
                    writer.Flush();

                    // 强制将 FileStream 缓冲区数据写入到操作系统文件系统缓冲区 (最关键的防断电步骤)
                    // 参数 true 表示同时刷新底层操作系统缓冲区
                    stream.Flush(true);
                }
            }
            catch (IOException ex)
            {
                Log.Error($"写入数据文件时发生错误: {ex.Message}");
            }
        }
    }

    private void Button_OpenPort(object sender, RoutedEventArgs e)
    {
        if (_serialPort.IsOpen) return;
        if (string.IsNullOrEmpty(SelectedComport))
        {
            FluentMessageBox.Show("请选择一个串口！", "提示", icon: MessageBoxImage.Error, owner: this);
            return;
        }

        _serialPort.PortName = SelectedComport;

        try
        {
            _serialPort.Open();
            LogFilePath = Path.Combine(Settings.Default.DataPath, $"{DateTime.Now:yyyyMMdd_HHmmss}.txt");
            ComportLabel.Content = $"串口{SelectedComport}已连接。";
            Log.Information($"串口 {SelectedComport} 已打开。");
            ComportCombox.IsEnabled = false;
            Btn_ClosePort.IsEnabled = true;
            Btn_OpenPort.IsEnabled = false;
        }
        catch (Exception ex)
        {
            Log.Error($"打开串口失败: {ex.Message}");
            ComportLabel.Content = $"错误：无法打开串口。{ex.Message}";
        }
    }

    private void Button_ClosePort(object sender, RoutedEventArgs e)
    {
        if (!_serialPort.IsOpen) return;

        try
        {
            _serialPort.Close();
            ComportLabel.Content = $"串口{SelectedComport}已断开。";
            ComportCombox.IsEnabled = true;
            Log.Information($"串口 {SelectedComport} 已关闭。");
            Btn_OpenPort.IsEnabled = true;
            Btn_ClosePort.IsEnabled = false;
        }
        catch (Exception ex)
        {
            Log.Error($"关闭串口失败: {ex.Message}");
            ComportLabel.Content = $"错误：无法关闭串口。{ex.Message}";
        }
    }

    private void Button_StartCollect(object sender, RoutedEventArgs e)
    {
        CreateOriginDirectory();
        if (!_serialPort.IsOpen)
        {
            FluentMessageBox.Show("请先连接串口", "提示", MessageBoxButton.OK, MessageBoxImage.Warning, this);
            return;
        }

        var inputSampleDialog = new InputSampleNo("开始采集前确保设备已停止实验，旧数据将被清除，输入样品编号后继续")
            { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (inputSampleDialog.ShowDialog() != true) return;
        _sampleNo = inputSampleDialog.InputText;
        SampleNoLabel.Content = $"样品编号: {_sampleNo}";


        if (CollectModeCombox.SelectedIndex == 0)
            _currentMode = CollectMode.PH_AND_COND;
        else if (CollectModeCombox.SelectedIndex == 1)
            _currentMode = CollectMode.PH_ONLY;
        else if (CollectModeCombox.SelectedIndex == 2)
            _currentMode = CollectMode.COND_ONLY;

        try
        {
            // === 1️⃣ 清理串口残留数据 ===
            _serialPort.DiscardOutBuffer(); // 清发送缓冲
            var remainingData = string.Empty;

            // 尝试读取残留接收缓冲区
            if (_serialPort.BytesToRead > 0) remainingData = _serialPort.ReadExisting();

            // 如果还有内存缓冲里的数据
            lock (_bufferLock)
            {
                if (_receiveBuffer.Length > 0)
                {
                    remainingData += _receiveBuffer.ToString();
                    _receiveBuffer.Clear();
                }
            }

            // 如果有未处理的数据，写入硬盘保存
            if (!string.IsNullOrWhiteSpace(remainingData))
            {
                var records = remainingData.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                foreach (var record in records)
                {
                    var trimmed = record.Trim();
                    if (!string.IsNullOrEmpty(trimmed))
                        WriteDataToFile(trimmed);
                }

                Log.Information("已保存停止期间积累的残留数据。");
            }
        }
        catch (Exception ex)
        {
            Log.Error($"清空串口残留数据时发生错误: {ex.Message}");
        }

        _phLogger.Clear();
        _conductivityLogger.Clear();
        dataCountLabel.Content = "已接收数据: 0个";
        _dispatcherTimer.Start();
        LogFilePath = Path.Combine(Settings.Default.DataPath, $"{DateTime.Now:yyyyMMdd_HHmmss}-{_sampleNo}.txt");
        MainPlot.Refresh();
        _dataCount = 0;
        _isCollecting = true;
        Log.Information("数据采集开始。");
        Btn_StopCollect.IsEnabled = true;
        Btn_StartCollect.IsEnabled = false;
    }

    private void Button_StopCollect(object sender, RoutedEventArgs e)
    {
        _isCollecting = false;
        Log.Information("数据采集停止。");
        _dispatcherTimer.Stop();
        _partialDataRecord = null;
        Btn_StartCollect.IsEnabled = true;
        Btn_StopCollect.IsEnabled = false;
    }


    private void MainWindow_Closing(object sender, CancelEventArgs e)
    {
        if (FluentMessageBox.Show("数据导出了吗？", "确认退出",
                MessageBoxButton.OKCancel, MessageBoxImage.Warning, this) != MessageBoxResult.OK)
        {
            e.Cancel = true;
        }
        else
        {
            if (_serialPort.IsOpen)
            {
                _serialPort.Close();
                Log.Information($"串口 {SelectedComport} 已关闭。");
            }
            _watcher.Stop();
        }
    }

    private void Button_ExportData(object sender, RoutedEventArgs e)
    {
        var saveFileDialog = new SaveFileDialog();

        // **配置属性**
        var contentString = new StringBuilder();

        // 1. 设置默认的文件名
        saveFileDialog.FileName = _sampleNo;

        // 2. 设置默认的文件扩展名
        saveFileDialog.DefaultExt = ".txt";

        // 3. 设置文件过滤器 (用于限制文件类型)
        // 格式: "描述|*.扩展名|描述|*.扩展名"
        saveFileDialog.Filter = "文本文件 (*.txt)|*.txt|所有文件 (*.*)|*.*";

        // 4. 设置初始目录
        if (!string.IsNullOrWhiteSpace(Settings.Default.ExportDataPath) && Directory.Exists(Settings.Default.ExportDataPath))
        {
            saveFileDialog.InitialDirectory = Settings.Default.ExportDataPath;
        }

        // **显示对话框**
        var result = saveFileDialog.ShowDialog();

        // **处理对话框结果**
        if (result == true)
        {
            // 获取用户选择的文件路径（包含文件名）
            var filename = saveFileDialog.FileName;

            // 更新导出数据目录
            Settings.Default.ExportDataPath = Path.GetDirectoryName(filename);
            Settings.Default.Save();

            switch (_currentMode)
            {
                case CollectMode.PH_ONLY:
                    contentString.AppendLine("Time(s) pH");
                    foreach (var record in _phLogger.Data.Coordinates)
                    {
                        var time = record.X;
                        var pH = record.Y;
                        contentString.AppendLine($"{time,5} {pH,7}");
                    }

                    break;
                case CollectMode.COND_ONLY:
                    contentString.AppendLine("Time(s) Conductivity(µS/cm)");
                    foreach (var record in _conductivityLogger.Data.Coordinates)
                    {
                        var time = record.X;
                        var conductivity = record.Y;
                        contentString.AppendLine($"{time,5} {conductivity,7}");
                    }

                    break;
                case CollectMode.PH_AND_COND:
                    contentString.AppendLine("Time(s) pH Conductivity(µS/cm)");
                    foreach (var record in _phLogger.Data.Coordinates)
                    {
                        var time = record.X;
                        var pH = record.Y;
                        var conductivityRecord = _conductivityLogger.Data.Coordinates.FirstOrDefault(r => r.X == time);
                        var conductivity = conductivityRecord != null ? conductivityRecord.Y : 0;
                        contentString.AppendLine($"{time,6} {pH,7} {conductivity,7}");
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

    private void Button_OpenTestPage(object sender, RoutedEventArgs e)
    {
        var testWindow = new TestConnection { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        testWindow.ShowDialog();
    }

    private void Button_OpenAbout(object sender, RoutedEventArgs e)
    {
        var aboutWindow = new About { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        aboutWindow.Show();
    }

    private void Button_OpenPortSetting(object sender, RoutedEventArgs e)
    {
        var settingWindow = new SerialportSetting(_serialPort)
            { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        settingWindow.ShowDialog();
    }

    private void Button_ModifySampleNo(object sender, RoutedEventArgs e)
    {
        var inputSampleDialog = new InputSampleNo("输入新编号")
            { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        if (inputSampleDialog.ShowDialog() != true) return;
        _sampleNo = inputSampleDialog.InputText;
        SampleNoLabel.Content = $"样品编号: {_sampleNo}";
    }

    private void Button_ReadOrigindata(object sender, RoutedEventArgs e)
    {
        var recoverDataWindow = new RecoverData();
        recoverDataWindow.Show();
    }

    private void Button_SampleSetting(object sender, RoutedEventArgs e)
    {
        var settingWindow = new CollectionSettingWindow
        {
            Owner = this,
            WindowStartupLocation = WindowStartupLocation.CenterOwner
        };
        settingWindow.ShowDialog();
    }

    private void Button_OpenHelp(object sender, RoutedEventArgs e)
    {
        var helpWindow = new HelpWindow { Owner = this, WindowStartupLocation = WindowStartupLocation.CenterOwner };
        helpWindow.Show();
    }

    private async void Create_data(object sender, RoutedEventArgs e)
    {
        int time = 0;
        double ph = 7.0;
        double cond = 3.5;
        _dispatcherTimer.Start();
        var random = new Random();
        while (true)
        {
            time += 5;
            ph += random.Next(-5, 5) * -1;
            cond += random.Next(-10, 10) * -50;
            _phLogger.Add(time, ph);
            _conductivityLogger.Add(time, cond);
            await Task.Delay(100);
            MainPlot.Refresh();
            _timeLegendItem.LabelText = $"Current Time: {time}s";
        }
    }

    protected virtual void Dispose(bool disposing)
    {
        if (!_disposed)
        {
            if (disposing)
            {
                _dispatcherTimer.Stop();
                _watcher.Stop();
                _watcher.Dispose();
                if (_serialPort.IsOpen) _serialPort.Close();
                _serialPort.Dispose();
            }
            _disposed = true;
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MainWindow()
    {
        Dispose(false);
    }
}

public enum CollectMode
{
    PH_AND_COND,
    PH_ONLY,
    COND_ONLY
}

public class PartialDataRecord
{
    public int Time { get; set; }
    public double PHValue { get; set; }
}
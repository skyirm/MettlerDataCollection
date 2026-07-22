using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Threading;
using MettlerDataCollection.Device;
using MettlerDataCollection.Properties;
using MettlerDataCollection.Services;
using Microsoft.Win32;
using ScottPlot;
using ScottPlot.DataGenerators;
using ScottPlot.Plottables;
using Serilog;

namespace MettlerDataCollection;

public partial class MainWindow : Window, IDisposable
{
    private bool _disposed;

    private readonly ComPortWatcher _watcher;
    private readonly SerialPort _serialPort = new();
    private readonly DispatcherTimer _dispatcherTimer = new();
    private readonly IDataPersistenceService _persistenceService;
    private readonly S470K _s470K = new();

    private volatile int _dataCount;
    private volatile bool _isCollecting;
    private DataLogger _conductivityLogger;

    private DataLogger _phLogger;
    private string _sampleNo = string.Empty;
    private LegendItem _timeLegendItem;

    public MainWindow(IDataPersistenceService persistenceService)
    {
        _persistenceService = persistenceService;

        InitializeComponent();
        DataContext = this;

        _dispatcherTimer.Interval = TimeSpan.FromSeconds(1);
        _dispatcherTimer.Tick += DispatcherTimerTick;

        _watcher = new ComPortWatcher();
        _watcher.ComPortsChanged += HandleComPortsChanged;
        _watcher.Start();
        HandleComPortsChanged(new List<string>(SerialPort.GetPortNames()));

        _s470K.OnLinePreprocessed += OnLinePreprocessed;
        _s470K.OnDataProduced += OnDataProduced;
        _s470K.OnParseError += OnParseError;

        InitPlot();
        InitSerialPort();
        //Create_data(new object(), new RoutedEventArgs());
    }

    private void OnLinePreprocessed(string line)
    {
        // 写盘永远做（写盘逻辑在 _persistenceService 内部已做 Flush 防断电）
        _persistenceService.WriteRecord(line);
        // 解析：S470K.ParseData 根据 CurrentMode 分支，成功触发 OnDataProduced 更新 plot。
        // _isCollecting=false（清残留阶段）时不调 ParseData，避免给上份实验的残留数据配对、污染 plot。
        if (_isCollecting) _s470K.ParseData(line);
    }

    private void OnDataProduced(MeasureData data)
    {
        Dispatcher.BeginInvoke(() => AddDataPoint(data));
    }

    private void OnParseError(string error)
    {
        Log.Error($"数据解析错误: {error}");
    }

    private void AddDataPoint(MeasureData data)
    {
        _phLogger.Add(data.Time, data.Ph);
        _phLogger.LegendText = $"Current pH: {data.Ph}";
        _conductivityLogger.Add(data.Time, data.Conductivity);
        _conductivityLogger.LegendText = $"Current Cond: {data.Conductivity}";
        _timeLegendItem.LabelText = $"Current Time: {data.Time}s";
        _dataCount++;
        dataCountLabel.Content = $"已接收数据: {_dataCount}个";
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
            // S470K.PreprocessData 内部按 \r\n 切行，每切出一行就触发 OnLinePreprocessed，
            // handler (OnLinePreprocessed) 负责写盘和更新 plot。
            _s470K.PreprocessData(newData);
        }
        catch (Exception ex)
        {
            Log.Error($"串口接收错误: {ex.Message}");
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
            _s470K.CurrentMode = CollectMode.PH_AND_COND;
        else if (CollectModeCombox.SelectedIndex == 1)
            _s470K.CurrentMode = CollectMode.PH_ONLY;
        else if (CollectModeCombox.SelectedIndex == 2)
            _s470K.CurrentMode = CollectMode.COND_ONLY;

        try
        {
            // === 1️⃣ 清理串口残留数据 ===
            // 把硬件 FIFO 里的数据抢救出来，S470K.PreprocessData 会按 \r\n 切完整行，
            // 通过 OnLinePreprocessed → 写盘到当前文件（上一份实验）。
            // 注意：必须在 _persistenceService.StartNewFile(...) 之前执行，
            //      否则残留数据会污染新实验的文件。
            // _isCollecting 此时仍为 false，handler 只写盘不更新 plot。
            if (_serialPort.BytesToRead > 0)
            {
                var chunk = _serialPort.ReadExisting();
                _s470K.PreprocessData(chunk);
                Log.Information("已尝试抢救停止期间积累的残留数据。");
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
        _persistenceService.StartNewFile(_sampleNo, Settings.Default.DataPath);
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

            switch (_s470K.CurrentMode)
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
                _persistenceService.Stop();
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
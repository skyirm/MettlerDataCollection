using Microsoft.Win32;
using ScottPlot.Plottables;
using Serilog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace MettlerDataCollection
{
    public partial class MainWindow : Window
    {
        private ComPortWatcher _watcher;
        private int _dataCount = 0;
        private bool _isCollecting = false;
        object FileLock = new object();

        public bool IsCollecting { get => _isCollecting; }

        DataLogger PhLogger;
        DataLogger ConductivityLogger;
        private DataRecord1? dataRecord1 = null;
        DispatcherTimer timer = new DispatcherTimer();

        private readonly StringBuilder _receiveBuffer = new StringBuilder();
        private const string RecordDelimiter = "\r\n";
        private readonly object _bufferLock = new object();

        private string _selectedComport;
        public string SelectedComport
        {
            get => _selectedComport;
            set => _selectedComport = value;
        }

        public string LogFilePath = $"./log/{DateTime.Now:yyyyMMdd_HHmmss}.txt";
        SerialPort serialPort = new SerialPort();

        private CollectMode _currentMode = CollectMode.PH_AND_COND;

        public MainWindow()
        {
            InitializeComponent();
            this.DataContext = this;

            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += Timer_Tick;

            _watcher = new ComPortWatcher();
            _watcher.ComPortsChanged += HandleComPortsChanged;
            _watcher.Start();
            HandleComPortsChanged(new List<string>(SerialPort.GetPortNames()));

            CreateOriginDirectory();

            InitPlot();
            InitSerialPort();
        }

        private void CreateOriginDirectory()
        {
            if(!Directory.Exists("./origindata"))
            {
                Directory.CreateDirectory("./origindata");
            }
        }

        private void InitPlot()
        {
            MainPlot.Plot.XLabel("Time (s)");
            MainPlot.Plot.Axes.Bottom.TickLabelStyle.FontSize = 16;

            MainPlot.Plot.Axes.Left.Label.Text = "pH";
            MainPlot.Plot.Axes.Left.Label.ForeColor = ScottPlot.Colors.Red;
            MainPlot.Plot.Axes.Left.TickLabelStyle.FontSize = 16;
            MainPlot.Plot.Axes.Left.FrameLineStyle.Color = ScottPlot.Colors.Red;
            MainPlot.Plot.Axes.Left.MajorTickStyle.Color = ScottPlot.Colors.Red;
            MainPlot.Plot.Axes.Left.MinorTickStyle.Color = ScottPlot.Colors.Red;
            MainPlot.Plot.Axes.Left.TickLabelStyle.ForeColor = ScottPlot.Colors.Red;

            MainPlot.Plot.Axes.Right.Label.Text = "Conductivity (µS/cm)";
            MainPlot.Plot.Axes.Right.Label.ForeColor = ScottPlot.Colors.Blue;
            MainPlot.Plot.Axes.Right.TickLabelStyle.FontSize = 16;
            MainPlot.Plot.Axes.Right.FrameLineStyle.Color = ScottPlot.Colors.Blue;
            MainPlot.Plot.Axes.Right.MajorTickStyle.Color = ScottPlot.Colors.Blue;
            MainPlot.Plot.Axes.Right.MinorTickStyle.Color = ScottPlot.Colors.Blue;
            MainPlot.Plot.Axes.Right.TickLabelStyle.ForeColor = ScottPlot.Colors.Blue;

            MainPlot.Plot.Legend.FontSize = 16;
            

            PhLogger = MainPlot.Plot.Add.DataLogger();
            ConductivityLogger = MainPlot.Plot.Add.DataLogger();
            PhLogger.Color = ScottPlot.Colors.Red;
            PhLogger.LineWidth = 2;
            ConductivityLogger.Color = ScottPlot.Colors.Blue;
            ConductivityLogger.LineWidth = 2;
            PhLogger.Axes.YAxis = MainPlot.Plot.Axes.Left;
            ConductivityLogger.Axes.YAxis = MainPlot.Plot.Axes.Right;
            PhLogger.LegendText = $"Current pH: 0";
            ConductivityLogger.LegendText = $"Current Cond: 0";


            MainPlot.Refresh();
        }

        private void InitSerialPort()
        {
            serialPort.BaudRate = 9600;
            serialPort.DataBits = 8;
            serialPort.Parity = Parity.None;
            serialPort.StopBits = StopBits.One;
            serialPort.Handshake = Handshake.XOnXOff;
            serialPort.DataReceived += SerialPort_DataReceived;
            serialPort.ReceivedBytesThreshold = 1;
        }

        private void Timer_Tick(object? sender, EventArgs e)
        {
            if (PhLogger.HasNewData || ConductivityLogger.HasNewData)
            {
                if (this.showFull.IsChecked == true)
                {
                    PhLogger.ViewFull();
                    ConductivityLogger.ViewFull();
                }
                else if (this.showSlide.IsChecked == true)
                {
                    PhLogger.ViewSlide();
                    ConductivityLogger.ViewSlide();
                }
                
                MainPlot.Refresh();
            }
                
        }

        private void HandleComPortsChanged(List<string> list)
        {
            Dispatcher.Invoke(() =>
            {
                ComportCombox.ItemsSource = new ObservableCollection<string>(list);
            });
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (!_isCollecting) return;

            try
            {
                string newData = serialPort.ReadExisting();

                lock (_bufferLock)
                {
                    _receiveBuffer.Append(newData);

                    string bufferStr = _receiveBuffer.ToString();
                    int delimiterIndex;

                    // 不断查找完整的记录
                    while ((delimiterIndex = bufferStr.IndexOf(RecordDelimiter)) >= 0)
                    {
                        string completeRecord = bufferStr[..delimiterIndex].Trim();
                        bufferStr = bufferStr[(delimiterIndex + RecordDelimiter.Length)..];

                        if (!string.IsNullOrEmpty(completeRecord))
                        {
                            // 在 UI 线程处理完整记录
                            Dispatcher.BeginInvoke(() => ProcessCompleteRecord(completeRecord));
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
            throw new NotImplementedException();
            int CondValue = 0;
            double time = _dataCount;
            ConductivityLogger.Add(time, CondValue);
            ConductivityLogger.LegendText = $"Current Cond: {CondValue}";
            _dataCount++;
            dataCountLabel.Content = $"已接收数据: {_dataCount}个";
        }

        private void ProcessPhRecord(string completeRecord)
        {
            throw new NotImplementedException();
            int pHValue = 0;
            double time = _dataCount; 
            PhLogger.Add(time, pHValue);
            PhLogger.LegendText = $"Current pH: {pHValue}";
            _dataCount++;
            dataCountLabel.Content = $"已接收数据: {_dataCount}个";
        }

        private void ProcessPhAndCondRecord(string completeRecord)
        {
            try
            {
                string[] parts = completeRecord.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length < 2)
                    return;

                if (parts.Length >= 3 && parts[1] == "1")
                {
                    int time = int.TryParse(parts[0].Replace("s", ""), out int t) ? t : 0;
                    double pHValue = double.TryParse(parts[2], out double pH) ? pH : 0;
                    if (dataRecord1 == null)
                    {
                        dataRecord1 = new DataRecord1 { Time = time, PHValue = pHValue };
                    }
                }
                else if (parts[0] == "2" && parts.Length >= 2)
                {
                    double conductivityValue = double.TryParse(parts[1], out double cond) ? cond : 0;
                    if (dataRecord1 != null)
                    {
                        PhLogger.Add(dataRecord1.Time, dataRecord1.PHValue);
                        PhLogger.LegendText = $"Current pH: {dataRecord1.PHValue}";
                        ConductivityLogger.Add(dataRecord1.Time, conductivityValue);
                        ConductivityLogger.LegendText = $"Current Cond: {conductivityValue}";
                        _dataCount++;
                        dataCountLabel.Content = $"已接收数据: {_dataCount}个";
                        dataRecord1 = null;
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
            lock (FileLock)
            {
                try
                {
                    string logEntry = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}| {completeRecord}{Environment.NewLine}";

                    // FileStream 确保我们可以控制 Flush
                    // FileMode.Append: 如果文件存在则追加，否则创建
                    using (var stream = new FileStream(LogFilePath, FileMode.Append, FileAccess.Write, FileShare.None))
                    using (var writer = new StreamWriter(stream, System.Text.Encoding.UTF8))
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
            if (serialPort.IsOpen) return;
            if (string.IsNullOrEmpty(SelectedComport))
            {
                MessageBox.Show("请选择一个串口！");
                return;
            }
            serialPort.PortName = SelectedComport;

            try
            {
                serialPort.Open();
                ComportLabel.Content = $"串口{SelectedComport}已连接。";
                Log.Information($"串口 {SelectedComport} 已打开。");
                ComportCombox.IsEnabled = false;
            }
            catch (Exception ex)
            {
                Log.Error($"打开串口失败: {ex.Message}");
                ComportLabel.Content = $"错误：无法打开串口。{ex.Message}";
            }
        }

        private void Button_ClosePort(object sender, RoutedEventArgs e)
        {
            if (!serialPort.IsOpen) return;

            try
            {
                serialPort.Close();
                ComportLabel.Content = $"串口{SelectedComport}已断开。";
                ComportCombox.IsEnabled = true;
                Log.Information($"串口 {SelectedComport} 已关闭。");
            }
            catch (Exception ex)
            {
                Log.Error($"关闭串口失败: {ex.Message}");
                ComportLabel.Content = $"错误：无法关闭串口。{ex.Message}";
            }
        }

        private void Button_StartCollect(object sender, RoutedEventArgs e)
        {
            if (!serialPort.IsOpen)
            {
                MessageBox.Show("请先连接串口！");
                return;
            }

            if (MessageBox.Show("开始采集前确保设备已停止实验，旧数据将被清除，是否继续？", "数据采集", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                == MessageBoxResult.No)
                return;

            if (CollectModeCombox.SelectedIndex == 0)
            {
                _currentMode = CollectMode.PH_AND_COND;
            }
            else if (CollectModeCombox.SelectedIndex == 1)
            {
                _currentMode = CollectMode.PH_ONLY;
            }
            else if (CollectModeCombox.SelectedIndex == 2)
            {
                _currentMode = CollectMode.COND_ONLY;
            }

            try
            {
                // === 1️⃣ 清理串口残留数据 ===
                serialPort.DiscardOutBuffer(); // 清发送缓冲
                string remainingData = string.Empty;

                // 尝试读取残留接收缓冲区
                if (serialPort.BytesToRead > 0)
                {
                    remainingData = serialPort.ReadExisting();
                }

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
                    string[] records = remainingData.Split(new string[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);
                    foreach (var record in records)
                    {
                        string trimmed = record.Trim();
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

            PhLogger.Clear();
            ConductivityLogger.Clear();
            dataCountLabel.Content = $"已接收数据: 0个";
            timer.Start();
            LogFilePath = $"./origindata/{DateTime.Now:yyyyMMdd_HHmmss}.txt";
            MainPlot.Refresh();
            _dataCount = 0;
            _isCollecting = true;
            Log.Information("数据采集开始。");
        }

        private void Button_StopCollect(object sender, RoutedEventArgs e)
        {
            _isCollecting = false;
            Log.Information("数据采集停止。");
            MessageBox.Show("数据采集已停止","数据采集",MessageBoxButton.OK,MessageBoxImage.Information);
            timer.Stop();
        }


        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            if (MessageBox.Show("未导出的数据可能丢失", "确认退出",
                MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
            {
                if (serialPort.IsOpen)
                {
                    serialPort.Close();
                    Log.Information($"串口 {SelectedComport} 已关闭。");
                }
                e.Cancel = true;
                return;
            }
        }

        private void Button_ExportData(object sender, RoutedEventArgs e)
        {
            SaveFileDialog saveFileDialog = new SaveFileDialog();

            // **配置属性**
            var contentString = new StringBuilder();
            contentString.AppendLine("Time(s),pH,Conductivity(µS/cm)");

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
            bool? result = saveFileDialog.ShowDialog();

            // **处理对话框结果**
            if (result == true)
            {
                // 获取用户选择的文件路径（包含文件名）
                string filename = saveFileDialog.FileName;

                if (PhLogger.Data.Coordinates.Count != ConductivityLogger.Data.Coordinates.Count || PhLogger.Data.Coordinates.Count == 0)
                {
                    System.IO.File.WriteAllText(filename, contentString.ToString(), Encoding.UTF8);
                    return;
                }
                foreach (var record in PhLogger.Data.Coordinates)
                {
                    var time = record.X;
                    var pH = record.Y;
                    var conductivityRecord = ConductivityLogger.Data.Coordinates.FirstOrDefault(r => r.X == time);
                    var conductivity = conductivityRecord != null ? conductivityRecord.Y : 0;
                    contentString.AppendLine($"{time},{pH},{conductivity}");
                }

                // 实际保存文件的代码（例如使用 System.IO.File.WriteAllText）
                try
                {
                    System.IO.File.WriteAllText(filename, contentString.ToString(), Encoding.UTF8);
                    Log.Information($"数据已导出到文件: {filename}");
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"导出数据时发生错误，请重试。{ex.Message}");
                    Log.Error($"导出数据到文件 {filename} 时发生错误。{ex.Message}");
                }

            }
        }

        private void Button_OpenTestPage(object sender, RoutedEventArgs e)
        {
            var testWindow = new TestConnection();
            testWindow.ShowDialog();
        }

        private void Button_OpenAbout(object sender, RoutedEventArgs e)
        {
            var aboutWindow = new About();
            aboutWindow.Show();
        }

        private void Button_OpenPortSetting(object sender, RoutedEventArgs e)
        {
            var settingWindow = new SerialportSetting(serialPort);
            settingWindow.ShowDialog();
        }
    }

    public enum CollectMode
    {
        PH_AND_COND,
        PH_ONLY,
        COND_ONLY
    }
    public class DataRecord1
    {
        public int Time { get; set; }
        public double PHValue { get; set; }
    }
}

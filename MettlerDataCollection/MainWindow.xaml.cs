using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;
using ScottPlot.Plottables;
using Serilog;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.IO.Ports;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace MettlerDataCollection
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        private ComPortWatcher _watcher;

        
        private int _dataCount = 0;
        private bool _isCollecting = false;

        DataLogger PhLogger;
        DataLogger ConductivityLogger;
        private DataRecord1? dataRecord1 = null;

        string _dataBuffer = string.Empty;
        private const char RecordDelimiter = '\n';

        private string _selectedComport;
        public string SelectedComport
        {
            get { return _selectedComport; }
            set
            {
                _selectedComport = value;
                // 可以在这里处理选中事件或触发通知
            }
        }

        public string LogFilePath = $"/log/{DateTime.Now.ToString("yyyyMMdd_HHmmss")}";

        SerialPort serialPort = new SerialPort();
        private object FileLock = new object();

        public MainWindow()
        {
            InitializeComponent();

            this.DataContext = this;

            _watcher = new ComPortWatcher();
            _watcher.ComPortsChanged += HandleComPortsChanged;
            _watcher.Start();

            HandleComPortsChanged(new List<string>(SerialPort.GetPortNames()));

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

            PhLogger = MainPlot.Plot.Add.DataLogger();
            ConductivityLogger = MainPlot.Plot.Add.DataLogger();
            PhLogger.Color = ScottPlot.Colors.Red;
            ConductivityLogger.Color = ScottPlot.Colors.Blue;
            PhLogger.Axes.YAxis = MainPlot.Plot.Axes.Left;
            ConductivityLogger.Axes.YAxis = MainPlot.Plot.Axes.Right;

            serialPort.BaudRate = 9600;   // 波特率 (Baud Rate)
            serialPort.DataBits = 8;      // 数据位 (Data Bits)
            serialPort.Parity = Parity.None; // 奇偶校验 (Parity.None 表示无校验)
            serialPort.StopBits = StopBits.One; // 停止位 (StopBits.One 表示 1 位)
            serialPort.Handshake = Handshake.XOnXOff;
            serialPort.DataReceived += SerialPort_DataReceived;

            MainPlot.Refresh();
        }

        private void HandleComPortsChanged(List<string> list)
        {
            this.Dispatcher.Invoke(() =>
            {
                ComportCombox.ItemsSource = new ObservableCollection<string>(list);
            });
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (!_isCollecting)
            {
                return;
            }

            Dispatcher.Invoke(() =>
            {
                try
                {
                    // 读取所有现有数据并追加到缓冲区
                    _dataBuffer += serialPort.ReadExisting();

                    // 检查缓冲区中是否包含结束符
                    if (_dataBuffer.Contains(RecordDelimiter))
                    {
                        // 分割出完整的记录
                        string[] records = _dataBuffer.Split(new char[] { RecordDelimiter }, StringSplitOptions.RemoveEmptyEntries);

                        // 最后一块通常是不完整的，留给下次接收
                        // 如果最后一块不包含结束符，它就是下一轮的开始
                        _dataBuffer = records.LastOrDefault() ?? string.Empty;

                        // 处理所有完整的记录
                        for (int i = 0; i < records.Length - 1; i++) // 处理除最后一块之外的所有记录
                        {
                            string completeRecord = records[i].Trim();

                            if (!string.IsNullOrEmpty(completeRecord))
                            {
                                // ！！！ 在这里调用您的解析函数 ！！！
                                ProcessCompleteRecord(completeRecord);
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    // 忽略或记录读取错误
                }
            });
        }

        private void ProcessCompleteRecord(string completeRecord)
        {
            WriteDataToFile(completeRecord);
            int time = 0;
            double pHValue = 0;
            double conductivityValue = 0;
            string[] parts = completeRecord.Split(' ');
            if (parts.Length < 3)
            {
                return;
            }
            
            if (parts[1] == "1")
            {
                time = int.TryParse(parts[0].Replace("s",""), out int t) ? t : 0;
                pHValue = double.TryParse(parts[2], out double pH) ? pH : 0;
                if (dataRecord1 == null && pHValue != 0)
                {
                    dataRecord1 = new DataRecord1
                    {
                        Time = time,
                        PHValue = pHValue
                    };
                }
            }
            if (parts[0] == "2")
            {
                conductivityValue = double.TryParse(parts[1], out double cond) ? cond : 0;
                if (dataRecord1 is not null && conductivityValue != 0)
                {
                    PhLogger.Add(dataRecord1.Time, dataRecord1.PHValue);
                    ConductivityLogger.Add(dataRecord1.Time, conductivityValue);
                    _dataCount++;
                    this.dataCountLabel.Content = $"已接收数据: {_dataCount}个";
                    dataRecord1 = null;
                    MainPlot.Refresh();
                }
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
            if (serialPort.IsOpen)
            {
                return;
            }
            serialPort.PortName = SelectedComport;

            try
            {
                serialPort.Open();
                this.ComportLabel.Content = $"串口{SelectedComport}已连接。";
                Log.Information($"串口 {SelectedComport} 已打开。");
                this.ComportCombox.IsEnabled = false;
            }
            catch (Exception ex)
            {
                Log.Error($"打开串口 {SelectedComport} 时发生错误: {ex.Message}");
                this.ComportLabel.Content = $"错误：无法打开串口。请检查端口号是否正确或是否已被占用。详细信息: {ex.Message}";
                return;
            }
        }


        private void Button_ClosePort(object sender, RoutedEventArgs e)
        {
            if (!serialPort.IsOpen)
            {
                return;
            }
            try
            {
                serialPort.Close();
                this.ComportLabel.Content = $"串口{SelectedComport}已断开。";
                Log.Information($"串口 {SelectedComport} 已关闭。");
                this.ComportCombox.IsEnabled = true;
            }
            catch (Exception ex)
            {
                Log.Error($"关闭串口 {SelectedComport} 时发生错误: {ex.Message}");
                this.ComportLabel.Content = $"错误：无法关闭串口。详细信息: {ex.Message}";
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

                if(PhLogger.Data.Coordinates.Count != ConductivityLogger.Data.Coordinates.Count || PhLogger.Data.Coordinates.Count == 0)
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
                    MessageBox.Show("导出数据时发生错误，请重试。");
                    Log.Error($"导出数据到文件 {filename} 时发生错误。{ex.Message}");
                }
                
            }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // 弹出提示框，询问用户是否确定退出
            MessageBoxResult result = MessageBox.Show(
                "未导出的数据可能丢失",
                "确认退出",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            // 根据用户选择决定是否取消关闭
            if (result == MessageBoxResult.Yes)
            {
                Log.Information("应用程序关闭。");
                // 用户选择“是”，继续关闭程序
                e.Cancel = false;
            }
            else
            {
                // 用户选择“否”，取消关闭操作
                e.Cancel = true;
            }
        }

        private void Button_StartCollect(object sender, RoutedEventArgs e)
        {
            if (!serialPort.IsOpen)
            {
                MessageBox.Show("请先连接串口！");
                return;
            }
            MessageBoxResult result = MessageBox.Show(
                "当前数据将被清除",
                "确认",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (result == MessageBoxResult.No)
            {
                return;
            }
            PhLogger.Clear();
            ConductivityLogger.Clear();
            Log.Debug("开始采集数据。");
            LogFilePath = $"/origindata/{DateTime.Now.ToString("yyyyMMdd_HHmmss")}.txt";
            MainPlot.Refresh();
            _dataCount = 0;
            _isCollecting = true;
        }

        private void Button_StopCollect(object sender, RoutedEventArgs e)
        {
            Log.Debug("停止采集数据。");
            _isCollecting = false;
        }

        private void Button_ShowAllData(object sender, RoutedEventArgs e)
        {
            PhLogger.ViewFull();
            ConductivityLogger.ViewFull();
        }

        private void Button_ShowNewestData(object sender, RoutedEventArgs e)
        {
            PhLogger.ViewSlide(100);
            ConductivityLogger.ViewSlide(100);
        }
    }

    public class DataRecord1
    {
        public int Time { get; set; }
        public double PHValue { get; set; }
        public double TemperatureValue { get; set; }
    }
    public class DataRecord2
    {
        public double ConductivityValue { get; set; }
        public double TemperatureValue { get; set; }
    }
}
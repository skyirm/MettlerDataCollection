using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Text;
using System.Windows;
using MettlerDataCollection.Properties;
using Serilog;

namespace MettlerDataCollection;

/// <summary>
///     TestConnection.xaml 的交互逻辑
/// </summary>
public partial class TestConnection : Window
{
    private const string RecordDelimiter = "\r\n";
    private readonly StringBuilder _receiveBuffer = new();
    private readonly object _bufferLock = new();

    private readonly SerialPort serialPort = new();
    private readonly Settings settings;

    public TestConnection()
    {
        InitializeComponent();
        DataContext = this;
        settings = Settings.Default;
        InitSerialPort();
        ComportCombox.ItemsSource = new ObservableCollection<string>(SerialPort.GetPortNames());
    }

    public string SelectedComport { get; set; }

    private void Button_OpenPort(object sender, RoutedEventArgs e)
    {
        if (string.IsNullOrEmpty(SelectedComport))
        {
            MessageBox.Show("请选择一个串口！");
            return;
        }

        serialPort.PortName = SelectedComport;

        try
        {
            serialPort.Open();
            Log.Information($"TestConnection-串口 {SelectedComport} 已打开。");
            ComportCombox.IsEnabled = false;
            Btn_OpenPort.IsEnabled = false;
            Btn_ClosePort.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error($"TestConnection-打开串口失败: {ex.Message}");
            MessageBox.Show($"打开串口失败: {ex.Message}");
        }
    }

    private void Button_ClosePort(object sender, RoutedEventArgs e)
    {
        try
        {
            serialPort.Close();
            ComportCombox.IsEnabled = true;
            Log.Information($"TestConnection-串口 {SelectedComport} 已关闭。");
            Btn_ClosePort.IsEnabled = false;
            Btn_OpenPort.IsEnabled = true;
        }
        catch (Exception ex)
        {
            Log.Error($"TestConnection-关闭串口失败: {ex.Message}");
            MessageBox.Show($"关闭串口失败: {ex.Message}");
        }
    }

    private void InitSerialPort()
    {
        serialPort.BaudRate = settings.BaudRate;
        serialPort.DataBits = settings.DataBits;
        serialPort.Parity = (Parity)settings.Parity;
        serialPort.StopBits = (StopBits)settings.StopBits;
        serialPort.Handshake = (Handshake)settings.Handshake;
        serialPort.DataReceived += SerialPort_DataReceived;
        serialPort.ReceivedBytesThreshold = 1;
    }

    private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
    {
        try
        {
            var newData = serialPort.ReadExisting();

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
                        // 在 UI 线程处理完整记录
                        Dispatcher.BeginInvoke(() => LogBox.AppendText(completeRecord + "\n"));
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
}
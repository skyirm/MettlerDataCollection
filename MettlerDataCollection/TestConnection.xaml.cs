using Serilog;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace MettlerDataCollection
{
    /// <summary>
    /// TestConnection.xaml 的交互逻辑
    /// </summary>
    public partial class TestConnection : Window
    {

        private string _selectedComport;
        public string SelectedComport
        {
            get => _selectedComport;
            set => _selectedComport = value;
        }

        SerialPort serialPort = new SerialPort();
        private object _bufferLock = new object();
        private readonly StringBuilder _receiveBuffer = new StringBuilder();
        private const string RecordDelimiter = "\r\n";

        public TestConnection()
        {
            InitializeComponent();
            this.DataContext = this;
            InitSerialPort();
            this.ComportCombox.ItemsSource = new ObservableCollection<string>(SerialPort.GetPortNames());
        }

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
            serialPort.BaudRate = 9600;
            serialPort.DataBits = 8;
            serialPort.Parity = Parity.None;
            serialPort.StopBits = StopBits.One;
            serialPort.Handshake = Handshake.XOnXOff;
            serialPort.DataReceived += SerialPort_DataReceived;
            serialPort.ReceivedBytesThreshold = 1;
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
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
                            Dispatcher.BeginInvoke(() => this.LogBox.AppendText(completeRecord +"\n"));
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
    }
}

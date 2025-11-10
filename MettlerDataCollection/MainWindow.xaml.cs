using ScottPlot.Plottables;
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
        private List<string> _comList;

        private string _selectedComport;
        private int _dataCount = 0;

        DataLogger PhLogger;
        DataLogger ConductivityLogger;
        public string SelectedComport
        {
            get { return _selectedComport; }
            set
            {
                _selectedComport = value;
                // 可以在这里处理选中事件或触发通知
            }
        }
        SerialPort serialPort = new SerialPort();

        public MainWindow()
        {
            InitializeComponent();

            string[] portNames = SerialPort.GetPortNames();
            _comList = new List<string>(portNames);
            this.ComportCombox.ItemsSource = _comList;

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

            PhLogger.Add(1, 5);
            PhLogger.Add(2, 5);
            ConductivityLogger.Add(1, 200);
            ConductivityLogger.Add(2, 250);
            MainPlot.Refresh();
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            Dispatcher.Invoke(() =>
            {
                serialPort.ReadLine();
            });
        }
    }
}
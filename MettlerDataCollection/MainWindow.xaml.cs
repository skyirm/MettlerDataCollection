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
        public string SelectedComport
        {
            get { return _selectedComport; }
            set
            {
                _selectedComport = value;
                // 可以在这里处理选中事件或触发通知
            }
        }
        public MainWindow()
        {
            InitializeComponent();

            string[] portNames = SerialPort.GetPortNames();
            _comList = new List<string>(portNames);
            this.ComportCombox.ItemsSource = _comList;

            double[] dataX = { 1, 2, 3, 4, 5 };
            double[] dataY = { 1, 4, 9, 16, 25 };
            WpfPlot1.Plot.Add.Scatter(dataX, dataY);
            WpfPlot1.Plot.XLabel("Time (s)");
            WpfPlot1.Refresh();
        }
    }
}
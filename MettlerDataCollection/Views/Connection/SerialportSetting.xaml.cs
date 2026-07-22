using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using MettlerDataCollection.Properties;

namespace MettlerDataCollection.Views.Connection;

/// <summary>
///     SerialportSetting.xaml 的交互逻辑
/// </summary>
public partial class SerialportSetting : Window
{
    private readonly Dictionary<string, Handshake> _handshakeMap = new()
    {
        { "无", Handshake.None },
        { "XOn/XOff", Handshake.XOnXOff },
        { "硬件RTS/CTS", Handshake.RequestToSend },
        { "RTS/XOnXOff", Handshake.RequestToSendXOnXOff }
    };

    private readonly Dictionary<string, Parity> _parityMap = new()
    {
        { "无校验", Parity.None },
        { "奇校验", Parity.Odd },
        { "偶校验", Parity.Even },
        { "标志校验", Parity.Mark },
        { "空白校验", Parity.Space }
    };

    private readonly SerialPort _serialPort;

    private readonly Dictionary<string, StopBits> _stopBitsMap = new()
    {
        { "1", StopBits.One },
        { "1.5", StopBits.OnePointFive },
        { "2", StopBits.Two }
    };

    public SerialportSetting(SerialPort serialPort)
    {
        InitializeComponent();
        _serialPort = serialPort;
        LoadSettings();
    }

    private void LoadSettings()
    {
        var s = Settings.Default;

        // 填充枚举下拉框
        ParityBox.ItemsSource = _parityMap.Keys;
        StopBitsBox.ItemsSource = _stopBitsMap.Keys;
        HandshakeBox.ItemsSource = _handshakeMap.Keys;

        // 载入数值设置
        BaudRateBox.SelectedItem = BaudRateBox.Items
            .Cast<ComboBoxItem>()
            .FirstOrDefault(i => i.Content.ToString() == s.BaudRate.ToString());

        DataBitsBox.SelectedItem = DataBitsBox.Items
            .Cast<ComboBoxItem>()
            .FirstOrDefault(i => i.Content.ToString() == s.DataBits.ToString());

        // 载入枚举映射
        ParityBox.SelectedItem = _parityMap.FirstOrDefault(x => x.Value == (Parity)s.Parity).Key;
        StopBitsBox.SelectedItem = _stopBitsMap.FirstOrDefault(x => x.Value == (StopBits)s.StopBits).Key;
        HandshakeBox.SelectedItem = _handshakeMap.FirstOrDefault(x => x.Value == (Handshake)s.Handshake).Key;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        if (BaudRateBox.SelectedItem == null || DataBitsBox.SelectedItem == null ||
            ParityBox.SelectedItem == null || StopBitsBox.SelectedItem == null ||
            HandshakeBox.SelectedItem == null)
        {
            MessageBox.Show("请确保所有设置项都已选择。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (_serialPort.IsOpen)
        {
            MessageBox.Show("请先关闭串口再修改设置。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        var s = Settings.Default;

        s.BaudRate = int.Parse(((ComboBoxItem)BaudRateBox.SelectedItem).Content.ToString());
        s.DataBits = int.Parse(((ComboBoxItem)DataBitsBox.SelectedItem).Content.ToString());

        // 保存枚举映射
        s.Parity = (byte)_parityMap[ParityBox.SelectedItem.ToString()];
        s.StopBits = (byte)_stopBitsMap[StopBitsBox.SelectedItem.ToString()];
        s.Handshake = (byte)_handshakeMap[HandshakeBox.SelectedItem.ToString()];

        s.Save();

        // 更新主串口对象
        _serialPort.BaudRate = s.BaudRate;
        _serialPort.DataBits = s.DataBits;
        _serialPort.Parity = (Parity)s.Parity;
        _serialPort.StopBits = (StopBits)s.StopBits;
        _serialPort.Handshake = (Handshake)s.Handshake;

        MessageBox.Show("串口设置已保存。", "成功", MessageBoxButton.OK, MessageBoxImage.Information);
        Close();
    }

    private void Cancel_Click(object sender, RoutedEventArgs e)
    {
        Close();
    }
}
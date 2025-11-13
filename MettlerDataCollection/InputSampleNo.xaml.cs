using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
    /// InputSampleNo.xaml 的交互逻辑
    /// </summary>
    public partial class InputSampleNo : Window
    {
        public string InputText => InputBox.Text;
        public InputSampleNo(string tips)
        {
            InitializeComponent();
            this.tipsTextBlock.Text = tips;
        }

        private void Ok_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true; // 标记用户点击了“确定”
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}

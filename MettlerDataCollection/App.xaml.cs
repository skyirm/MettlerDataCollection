using Serilog;
using System.Configuration;
using System.Data;
using System.Windows;

namespace MettlerDataCollection
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            // 配置日志记录器
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug() // 设置最低日志级别
                .WriteTo.File("log/app_log.txt", // 将日志写入文件
                              rollingInterval: RollingInterval.Day, // 每天生成一个新文件
                              outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("应用程序已启动。");
            PowerManagement.PreventSleepAndDisplayTurnOff(); // 启动时阻止睡眠和关闭屏幕
            base.OnStartup(e);
            // 在应用程序启动时执行的代码
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("应用程序即将退出。");
            Log.CloseAndFlush(); // 确保所有日志都已写入
            PowerManagement.AllowSleepAndDisplayTurnOff(); // 退出时恢复电源管理设置
            base.OnExit(e);
            // 在应用程序退出时执行的代码
        }
    }

}

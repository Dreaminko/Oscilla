using Oscilla.Core; // 【新增】必须引入它，才能在这里调用 AudioEngine
using System;
using System.Configuration;
using System.Data;
using System.Windows;

namespace Oscilla.UI
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        // ==========================================
        // 【防闪退】：全局异常捕获，死也要死个明白
        // ==========================================
        protected override void OnStartup(StartupEventArgs e)
        {
            // 捕获 UI 线程异常
            this.DispatcherUnhandledException += (s, args) =>
            {
                MessageBox.Show($"UI线程崩溃: {args.Exception.Message}\n{args.Exception.StackTrace}", "致命错误");
                args.Handled = true;
            };

            // 捕获非 UI 线程（后台线程）异常
            AppDomain.CurrentDomain.UnhandledException += (s, args) =>
            {
                var ex = args.ExceptionObject as Exception;
                MessageBox.Show($"后台线程崩溃: {ex?.Message}\n{ex?.StackTrace}", "致命错误");
            };

            base.OnStartup(e);
        }

        // ==========================================
        // 【防炸音】：在应用进程被彻底杀死前，安全拔掉 ASIO 插头
        // ==========================================
        protected override void OnExit(ExitEventArgs e)
        {
            try
            {
                // 强制调用音频引擎的释放方法
                // 这会让底层的 ASIO 驱动停止工作，并安全释放声卡独占权
                AudioEngine.Instance.Dispose();
            }
            catch
            {
                // 退出时的异常直接吞掉，保证软件能顺利关闭，不卡死
            }

            base.OnExit(e);
        }
    }
}
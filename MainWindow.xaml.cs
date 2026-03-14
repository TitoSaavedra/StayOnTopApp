using System;
using System.Diagnostics;
using System.Drawing.Imaging;
using Drawing = System.Drawing;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace StayOnTopApp
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")] public static extern bool ReleaseCapture();
        [DllImport("user32.dll")] public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);
        [DllImport("user32.dll")] static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);
        [DllImport("kernel32.dll", SetLastError = true)] static extern bool GetSystemTimes(out long lpIdleTime, out long lpKernelTime, out long lpUserTime);

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HT_CAPTION = 0x2;
        List<String> _lastProcesses = new List<string>();


        long _lastIdleTime, _lastKernelTime, _lastUserTime;
        DispatcherTimer timer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();
            InitializeAsync();

            GetSystemTimes(out _lastIdleTime, out _lastKernelTime, out _lastUserTime);

            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (s, e) => {
                AutoScanProcesses();
                UpdateCpuUsage();
            };
            timer.Start();
        }

        async void InitializeAsync()
        {
            await webView.EnsureCoreWebView2Async();
            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html");
            webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        }

        void UpdateCpuUsage()
        {
            if (GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
            {
                long idleDelta = idleTime - _lastIdleTime;
                long kernelDelta = kernelTime - _lastKernelTime;
                long userDelta = userTime - _lastUserTime;
                long totalDelta = (kernelDelta + userDelta);

                if (totalDelta > 0)
                {
                    float cpuUsage = 100.0f * (totalDelta - idleDelta) / totalDelta;
                    cpuUsage = Math.Max(0, Math.Min(100, cpuUsage));
                    webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { command = "CPU_UPDATE", value = Math.Round(cpuUsage) }));
                }

                _lastIdleTime = idleTime;
                _lastKernelTime = kernelTime;
                _lastUserTime = userTime;
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            var json = JsonDocument.Parse(args.WebMessageAsJson);
            string command = json.RootElement.GetProperty("command").GetString();

            switch (command)
            {
                case "DRAG":
                    IntPtr hwnd = new WindowInteropHelper(this).Handle;
                    ReleaseCapture();
                    SendMessage(hwnd, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
                    break;
                case "CLOSE": this.Close(); break;
                case "MINIMIZE": this.WindowState = WindowState.Minimized; break;
                case "GET_PROCESSES": EnviarProcesos(); break;
                case "SET_PIN": TogglePin(json.RootElement.GetProperty("name").GetString(), true); break;
                case "SET_UNPIN": TogglePin(json.RootElement.GetProperty("name").GetString(), false); break;
            }
        }

        void EnviarProcesos()
        {
            var procs = Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                .Select(p => new {
                    name = p.ProcessName + ".exe",
                    icon = GetIcon(p)
                })
                .GroupBy(p => p.name).Select(g => g.First())
                .OrderBy(p => p.name).ToList();

            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { command = "PROCESS_LIST", data = procs }));
        }

        string GetIcon(Process p)
        {
            try
            {
                string path = p.MainModule.FileName;
                using (var icon = Drawing.Icon.ExtractAssociatedIcon(path))
                using (var bmp = icon.ToBitmap())
                using (var ms = new MemoryStream())
                {
                    bmp.Save(ms, ImageFormat.Png);
                    return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                }
            }
            catch { return ""; }
        }

        void TogglePin(string name, bool pin)
        {
            var procs = Process.GetProcessesByName(name.Replace(".exe", ""));
            foreach (var p in procs)
                SetWindowPos(p.MainWindowHandle, pin ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }

        void AutoScanProcesses()
        {
            try
            {
                var currentRunning = Process.GetProcesses()
                    .Where(p => {
                        try
                        {
                            return p.MainWindowHandle != IntPtr.Zero && p.Id != 0;
                        }
                        catch { return false; }
                    })
                    .Select(p => p.ProcessName + ".exe")
                    .Distinct()
                    .OrderBy(p => p)
                    .ToList();

                if (!_lastProcesses.SequenceEqual(currentRunning))
                {
                    _lastProcesses = currentRunning;
                    EnviarProcesos();
                }
            }
            catch
            {
            }
        }
    }
}
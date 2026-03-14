using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using Microsoft.Web.WebView2.Core;
using System.Text.Json;

namespace StayOnTopApp
{
    public partial class MainWindow : Window
    {
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();
        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, int wParam, int lParam);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        static readonly IntPtr HWND_NOTOPMOST = new IntPtr(-2);
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOMOVE = 0x0002;
        const int WM_NCLBUTTONDOWN = 0xA1;
        const int HT_CAPTION = 0x2;

        public MainWindow()
        {
            InitializeComponent();
            InitializeAsync();
        }

        async void InitializeAsync()
        {
            await webView.EnsureCoreWebView2Async();
            string htmlPath = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html");
            webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
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
                .Select(p => p.ProcessName + ".exe").Distinct().OrderBy(p => p).ToList();
            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(procs));
        }

        void TogglePin(string name, bool pin)
        {
            var procs = Process.GetProcessesByName(name.Replace(".exe", ""));
            foreach (var p in procs)
                SetWindowPos(p.MainWindowHandle, pin ? HWND_TOPMOST : HWND_NOTOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE);
        }
    }
}
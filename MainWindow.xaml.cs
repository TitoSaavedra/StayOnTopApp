using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing.Imaging;
using Drawing = System.Drawing;
using System.IO;
using System.Linq;
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
        List<string> _processList = new List<string>();
        List<IntPtr> _selectedWindows = new List<IntPtr>();
        bool _isPicking = false;
        long _lastIdleTime, _lastKernelTime, _lastUserTime;
        DispatcherTimer timer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();
            InitializeAsync();
            NativeMethods.GetSystemTimes(out _lastIdleTime, out _lastKernelTime, out _lastUserTime);

            timer.Interval = TimeSpan.FromSeconds(1);
            timer.Tick += (s, e) => { AutoScanProcesses(); UpdateCpuUsage(); };
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
            if (NativeMethods.GetSystemTimes(out long idleTime, out long kernelTime, out long userTime))
            {
                long totalDelta = (kernelTime - _lastKernelTime) + (userTime - _lastUserTime);
                if (totalDelta > 0)
                {
                    float cpuUsage = 100.0f * (totalDelta - (idleTime - _lastIdleTime)) / totalDelta;
                    webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { command = "CPU_UPDATE", value = Math.Round(Math.Clamp(cpuUsage, 0, 100)) }));
                }
                _lastIdleTime = idleTime; _lastKernelTime = kernelTime; _lastUserTime = userTime;
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            var json = JsonDocument.Parse(args.WebMessageAsJson).RootElement;
            string command = json.GetProperty("command").GetString();

            switch (command)
            {
                case "DRAG":
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(new WindowInteropHelper(this).Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HT_CAPTION, 0);
                    break;
                case "CLOSE": Close(); break;
                case "MINIMIZE": WindowState = WindowState.Minimized; break;
                case "GET_PROCESSES": EnviarProcesos(); break;
                case "SET_PIN": TogglePin(json.GetProperty("name").GetString(), true); break;
                case "SET_UNPIN": TogglePin(json.GetProperty("name").GetString(), false); break;
                case "START_PICKER": _isPicking = true; Mouse.OverrideCursor = Cursors.Cross; StartPickingTimer(); break;
            }
        }

        void TogglePin(string name, bool pin)
        {
            if (name.Contains("HWND 0x"))
            {
                IntPtr hWnd = new IntPtr(Convert.ToInt64(name.Split("0x")[1], 16));
                ApplyWindowEffects(hWnd, pin);
            }
            else
            {
                var procs = Process.GetProcessesByName(name.Replace(".exe", ""));
                foreach (var p in procs) ApplyWindowEffects(p.MainWindowHandle, pin);
            }
        }

        void ApplyWindowEffects(IntPtr hWnd, bool pin)
        {
            if (hWnd == IntPtr.Zero) return;
            NativeMethods.SetWindowPos(hWnd, pin ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
            NativeMethods.MakeClickThrough(hWnd, pin); 
        }

        void EnviarProcesos()
        {
            var procs = Process.GetProcesses().Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                .Select(p => new { name = p.ProcessName + ".exe", icon = GetIcon(p) }).ToList();

            var selected = _selectedWindows.Where(h => NativeMethods.IsWindow(h))
                .Select(h => {
                    NativeMethods.GetWindowThreadProcessId(h, out uint pid);
                    try { var p = Process.GetProcessById((int)pid); return new { name = $"{p.ProcessName}.exe | HWND 0x{h.ToInt64():X}", icon = GetIcon(p) }; }
                    catch { return null; }
                }).Where(x => x != null).ToList();

            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { command = "PROCESS_LIST", data = procs.Concat(selected).GroupBy(p => p.name).Select(g => g.First()).OrderBy(p => p.name).ToList() }));
        }

        string GetIcon(Process p)
        {
            try
            {
                using var icon = Drawing.Icon.ExtractAssociatedIcon(p.MainModule.FileName);
                using var ms = new MemoryStream();
                icon.ToBitmap().Save(ms, ImageFormat.Png);
                return "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
            }
            catch { return ""; }
        }

        void AutoScanProcesses()
        {
            var current = Process.GetProcesses().Where(p => p.MainWindowHandle != IntPtr.Zero && p.Id != 0).Select(p => p.ProcessName + ".exe").Distinct().OrderBy(p => p).ToList();
            if (!_processList.SequenceEqual(current)) { _processList = current; EnviarProcesos(); }
        }

        void StartPickingTimer()
        {
            DispatcherTimer pickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            pickTimer.Tick += (s, e) =>
            {
                if (!_isPicking) return;
                NativeMethods.GetCursorPos(out Drawing.Point pt);
                IntPtr hWnd = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(pt), NativeMethods.GA_ROOT);
                if (hWnd == IntPtr.Zero) return;

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                try
                {
                    var p = Process.GetProcessById((int)pid);
                    webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { command = "HOVER_PROCESS", name = $"{p.ProcessName}.exe | HWND 0x{hWnd.ToInt64():X}", pid = pid }));

                    if ((NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0)
                    {
                        _isPicking = false; Mouse.OverrideCursor = null; pickTimer.Stop();
                        if (!_selectedWindows.Contains(hWnd)) _selectedWindows.Add(hWnd);
                        webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new { command = "PICKED_PROCESS", name = $"{p.ProcessName}.exe | HWND 0x{hWnd.ToInt64():X}" }));
                    }
                }
                catch { }
            };
            pickTimer.Start();
        }
    }
}
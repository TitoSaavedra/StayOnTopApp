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
        private List<string> _processList = new List<string>();
        private readonly List<IntPtr> _selectedWindows = new List<IntPtr>();
        private readonly Dictionary<string, string> _iconCache = new Dictionary<string, string>();
        private bool _isPicking = false;
        private long _lastIdleTime, _lastKernelTime, _lastUserTime;
        private readonly DispatcherTimer _scanTimer = new DispatcherTimer();

        public MainWindow()
        {
            InitializeComponent();
            InitializeAsync();

            NativeMethods.GetSystemTimes(out _lastIdleTime, out _lastKernelTime, out _lastUserTime);

            _scanTimer.Interval = TimeSpan.FromSeconds(1);
            _scanTimer.Tick += (s, e) => { AutoScanProcesses(); UpdateCpuUsage(); };
            _scanTimer.Start();
        }

        private async void InitializeAsync()
        {
            await webView.EnsureCoreWebView2Async();
            string htmlPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "index.html");
            webView.CoreWebView2.Navigate(new Uri(htmlPath).AbsoluteUri);
            webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
        }

        private void UpdateCpuUsage()
        {
            if (!NativeMethods.GetSystemTimes(out long idleTime, out long kernelTime, out long userTime)) return;

            long totalDelta = (kernelTime - _lastKernelTime) + (userTime - _lastUserTime);
            if (totalDelta > 0)
            {
                float cpuUsage = 100.0f * (totalDelta - (idleTime - _lastIdleTime)) / totalDelta;
                SendJson(new { command = "CPU_UPDATE", value = Math.Round(Math.Clamp(cpuUsage, 0, 100)) });
            }
            _lastIdleTime = idleTime; _lastKernelTime = kernelTime; _lastUserTime = userTime;
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs args)
        {
            using var json = JsonDocument.Parse(args.WebMessageAsJson);
            var root = json.RootElement;
            string command = root.GetProperty("command").GetString();

            switch (command)
            {
                case "DRAG":
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(new WindowInteropHelper(this).Handle, NativeMethods.WM_NCLBUTTONDOWN, NativeMethods.HT_CAPTION, 0);
                    break;
                case "CLOSE": Close(); break;
                case "MINIMIZE": WindowState = WindowState.Minimized; break;
                case "GET_PROCESSES": SendProcesses(); break;
                case "SET_PIN": TogglePin(root.GetProperty("name").GetString(), true); break;
                case "SET_UNPIN": TogglePin(root.GetProperty("name").GetString(), false); break;
                case "START_PICKER":
                    _isPicking = true;
                    Mouse.OverrideCursor = Cursors.Cross;
                    StartPickingTimer();
                    break;
            }
        }

        private void TogglePin(string name, bool pin)
        {
            if (!pin) SendJson(new { command = "SET_UNPIN", name = name });

            if (name.Contains("HWND 0x"))
            {
                IntPtr hWnd = new IntPtr(Convert.ToInt64(name.Split("0x")[1], 16));
                ApplyWindowEffects(hWnd, pin);
                if (pin && !_selectedWindows.Contains(hWnd)) _selectedWindows.Add(hWnd);
            }
            else
            {
                var cleanName = name.Replace(".exe", "");
                foreach (var p in Process.GetProcessesByName(cleanName))
                {
                    ApplyWindowEffects(p.MainWindowHandle, pin);
                    if (pin && !_selectedWindows.Contains(p.MainWindowHandle))
                        _selectedWindows.Add(p.MainWindowHandle);
                }
            }

            SetScanTimerInterval(pin);
        }

        private void ApplyWindowEffects(IntPtr hWnd, bool pin)
        {
            if (hWnd == IntPtr.Zero || !NativeMethods.IsWindow(hWnd)) return;
            NativeMethods.SetWindowPos(hWnd, pin ? NativeMethods.HWND_TOPMOST : NativeMethods.HWND_NOTOPMOST, 0, 0, 0, 0, NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE);
            NativeMethods.MakeClickThrough(hWnd, pin);
        }

        private void SendProcesses()
        {
            var activeProcs = Process.GetProcesses()
                .Where(p => !string.IsNullOrEmpty(p.MainWindowTitle))
                .Select(p => new { name = p.ProcessName + ".exe", icon = GetIconCached(p) });

            var customWindows = _selectedWindows
                .Where(h => NativeMethods.IsWindow(h))
                .Select(h => {
                    NativeMethods.GetWindowThreadProcessId(h, out uint pid);
                    try
                    {
                        var p = Process.GetProcessById((int)pid);
                        return new { name = $"{p.ProcessName}.exe | HWND 0x{h.ToInt64():X}", icon = GetIconCached(p) };
                    }
                    catch { return null; }
                }).Where(x => x != null);

            SendJson(new
            {
                command = "PROCESS_LIST",
                data = activeProcs.Concat(customWindows).GroupBy(p => p.name).Select(g => g.First()).OrderBy(p => p.name)
            });
        }

        private string GetIconCached(Process p)
        {
            try
            {
                string fileName = p.MainModule.FileName;
                if (_iconCache.TryGetValue(fileName, out string cachedIcon)) return cachedIcon;

                using var icon = Drawing.Icon.ExtractAssociatedIcon(fileName);
                using var ms = new MemoryStream();
                icon.ToBitmap().Save(ms, ImageFormat.Png);
                string base64 = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());

                _iconCache[fileName] = base64;
                return base64;
            }
            catch { return ""; }
        }

        private void AutoScanProcesses()
        {
            var current = Process.GetProcesses()
                .Where(p => p.MainWindowHandle != IntPtr.Zero && p.Id != 0)
                .Select(p => p.ProcessName + ".exe")
                .Distinct().OrderBy(p => p).ToList();

            if (!_processList.SequenceEqual(current))
            {
                _processList = current;
                SendProcesses();
            }
        }

        private void StartPickingTimer()
        {
            DispatcherTimer pickTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(50) };
            pickTimer.Tick += (s, e) =>
            {
                if (!_isPicking) return;

                // Cancel with ESC (0x1B)
                if ((NativeMethods.GetAsyncKeyState(0x1B) & 0x8000) != 0)
                {
                    StopPicking(pickTimer, "PICK_CANCELLED");
                    return;
                }

                NativeMethods.GetCursorPos(out Drawing.Point pt);
                IntPtr hWnd = NativeMethods.GetAncestor(NativeMethods.WindowFromPoint(pt), NativeMethods.GA_ROOT);
                if (hWnd == IntPtr.Zero) return;

                NativeMethods.GetWindowThreadProcessId(hWnd, out uint pid);
                try
                {
                    var p = Process.GetProcessById((int)pid);
                    SendJson(new { command = "HOVER_PROCESS", name = $"{p.ProcessName}.exe | HWND 0x{hWnd.ToInt64():X}", pid });

                    // Left Click (0x01) to select
                    if ((NativeMethods.GetAsyncKeyState(0x01) & 0x8000) != 0)
                    {
                        if (!_selectedWindows.Contains(hWnd)) _selectedWindows.Add(hWnd);
                        StopPicking(pickTimer, "PICKED_PROCESS", $"{p.ProcessName}.exe | HWND 0x{hWnd.ToInt64():X}");
                    }
                }
                catch { }
            };
            pickTimer.Start();
        }

        private void StopPicking(DispatcherTimer timer, string command, string name = null)
        {
            _isPicking = false;
            Mouse.OverrideCursor = null;
            timer.Stop();
            SendJson(new { command, name });
        }

        private void SetScanTimerInterval(bool isPinned)
        {
            _scanTimer.Interval = isPinned ? TimeSpan.FromSeconds(50) : TimeSpan.FromSeconds(1);
        }

        private void SendJson(object data) =>
            webView.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(data));

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            Mouse.OverrideCursor = null;

            foreach (IntPtr hWnd in _selectedWindows)
            {
                if (NativeMethods.IsWindow(hWnd))
                {
                    NativeMethods.MakeClickThrough(hWnd, false);
                }
            }
            base.OnClosing(e);
        }
    }
}
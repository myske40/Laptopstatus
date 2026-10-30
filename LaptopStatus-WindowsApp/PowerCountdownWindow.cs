using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Threading.Tasks;

namespace LaptopStatus
{
    internal sealed class PowerCountdownWindow : Window
    {
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const int WmHotKey = 0x0312;
        private const int VkEscape = 0x1B;
        private const int EscapeHotKeyId = 0x4C53;
        private static readonly TimeSpan CountdownDuration = TimeSpan.FromSeconds(10);
        private readonly DispatcherTimer _timer = new();
        private readonly Stopwatch _elapsed = new();
        private readonly TextBlock _countText;
        private readonly TextBlock _statusText;
        private readonly Func<string, bool> _verifyCancellationPassword;
        private readonly bool _passwordConfigured;
        private readonly bool _animationsEnabled;
        private HwndSource? _windowSource;
        private TimeSpan _remaining = CountdownDuration;
        private bool _allowClose;
        private bool _passwordPromptOpen;
        private bool _escapeHotKeyRegistered;
        private PowerCancellationPasswordDialog? _activePasswordPrompt;

        public PowerCountdownWindow(string action, string customMessage, Func<string, bool> verifyCancellationPassword, bool passwordConfigured, bool animationsEnabled = true)
        {
            _verifyCancellationPassword = verifyCancellationPassword;
            _passwordConfigured = passwordConfigured;
            _animationsEnabled = animationsEnabled;

            Title = action == "restart" ? "Restart Windows" : action == "sleep" ? "Sleep Windows" : "Matikan Windows";
            Width = 430;
            MaxWidth = Math.Min(430, Math.Max(300, SystemParameters.WorkArea.Width - 32));
            SizeToContent = SizeToContent.Height;
            MaxHeight = Math.Max(260, SystemParameters.WorkArea.Height - 32);
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Focusable = true;

            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = action == "restart" ? "Restart Windows dijadwalkan" : action == "sleep" ? "Sleep Windows dijadwalkan" : "Shutdown Windows dijadwalkan",
                Foreground = Brushes.White, FontSize = 20, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(customMessage) ? "Perintah dikirim dari perangkat yang dipasangkan." : customMessage,
                Foreground = new SolidColorBrush(Color.FromRgb(167, 181, 197)), FontSize = 13,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 18)
            });
            _countText = new TextBlock
            {
                Text = "10", Foreground = new SolidColorBrush(Color.FromRgb(114, 228, 191)),
                FontSize = 44, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 12)
            };
            panel.Children.Add(_countText);
            _statusText = new TextBlock
            {
                Text = "Tekan Esc lalu masukkan password LaptopStatus untuk membatalkan.",
                Foreground = new SolidColorBrush(Color.FromRgb(167, 181, 197)),
                FontSize = 12, TextWrapping = TextWrapping.Wrap,
                HorizontalAlignment = HorizontalAlignment.Center,
                TextAlignment = TextAlignment.Center
            };
            panel.Children.Add(_statusText);

            var chrome = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 25, 35)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 66, 82)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(18),
                Padding = new Thickness(24), Child = panel, Focusable = true
            };
            chrome.MouseLeftButtonDown += (_, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed) DragMove();
            };
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = chrome
            };

            SourceInitialized += (_, _) =>
            {
                _windowSource = HwndSource.FromHwnd(new WindowInteropHelper(this).Handle);
                _windowSource?.AddHook(HandleWindowMessage);
                RegisterEscapeHotKey();
            };
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key != Key.Escape || _passwordPromptOpen || _allowClose) return;
                e.Handled = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(VerifyCancellationPassword));
            };

            Closing += (_, e) =>
            {
                if (!_allowClose) e.Cancel = true;
            };

            _timer.Interval = TimeSpan.FromMilliseconds(100);
            _timer.Tick += (_, _) =>
            {
                UpdateRemaining();
                if (_remaining > TimeSpan.Zero) return;
                _timer.Stop();
                _activePasswordPrompt?.CloseForCountdownCompleted();
                _allowClose = true;
                DialogResult = true;
            };
            Loaded += (_, _) =>
            {
                Activate();
                RegisterEscapeHotKey();
                Focus();
                chrome.Focus();
                Keyboard.Focus(chrome);
                _elapsed.Start();
                _timer.Start();
            };
            Closed += (_, _) =>
            {
                _timer.Stop();
                UnregisterEscapeHotKey();
                _windowSource?.RemoveHook(HandleWindowMessage);
                _windowSource = null;
            };
        }

        private IntPtr HandleWindowMessage(IntPtr hwnd, int message, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (message == WmHotKey && wParam.ToInt32() == EscapeHotKeyId && !_passwordPromptOpen && !_allowClose)
            {
                handled = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(VerifyCancellationPassword));
                return IntPtr.Zero;
            }
            if ((message == WmKeyDown || message == WmSysKeyDown) && wParam.ToInt32() == VkEscape && !_passwordPromptOpen && !_allowClose)
            {
                handled = true;
                Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(VerifyCancellationPassword));
            }
            return IntPtr.Zero;
        }

        private void UpdateRemaining()
        {
            _remaining = CountdownDuration - _elapsed.Elapsed;
            if (_remaining < TimeSpan.Zero) _remaining = TimeSpan.Zero;
            _countText.Text = Math.Ceiling(_remaining.TotalSeconds).ToString();
        }

        private void VerifyCancellationPassword()
        {
            if (_passwordPromptOpen) return;
            _passwordPromptOpen = true;
            UnregisterEscapeHotKey();
            UpdateRemaining();
            _timer.Stop();
            _elapsed.Stop();

            try
            {
                var prompt = new PowerCancellationPasswordDialog(
                    this, _passwordConfigured, _verifyCancellationPassword, _animationsEnabled,
                    ResumeCountdownFromPrompt, PauseCountdownFromPrompt);
                _activePasswordPrompt = prompt;
                var accepted = prompt.ShowDialog() == true;
                _activePasswordPrompt = null;
                if (_remaining <= TimeSpan.Zero) return;
                if (accepted && prompt.PasswordVerified)
                {
                    _allowClose = true;
                    DialogResult = false;
                    return;
                }

                if (prompt.PasswordRejected)
                    _statusText.Text = "Password salah. Hitung mundur dilanjutkan.";
                else if (!_passwordConfigured)
                    _statusText.Text = "Atur password akses layar di Pengaturan Windows agar dapat membatalkan.";
                else
                    _statusText.Text = "Pembatalan tidak dikonfirmasi. Hitung mundur dilanjutkan.";

                RegisterEscapeHotKey();
                _elapsed.Start();
                _timer.Start();
            }
            finally
            {
                _passwordPromptOpen = false;
            }
        }

        private void PauseCountdownFromPrompt()
        {
            if (_allowClose || _remaining <= TimeSpan.Zero) return;
            UpdateRemaining();
            _timer.Stop();
            _elapsed.Stop();
        }

        private void ResumeCountdownFromPrompt()
        {
            if (_allowClose || _remaining <= TimeSpan.Zero) return;
            _elapsed.Start();
            _timer.Start();
        }

        private void RegisterEscapeHotKey()
        {
            if (_escapeHotKeyRegistered || !IsLoaded) return;
            var handle = new WindowInteropHelper(this).Handle;
            _escapeHotKeyRegistered = handle != IntPtr.Zero && RegisterHotKey(handle, EscapeHotKeyId, 0, VkEscape);
        }

        private void UnregisterEscapeHotKey()
        {
            if (!_escapeHotKeyRegistered) return;
            UnregisterHotKey(new WindowInteropHelper(this).Handle, EscapeHotKeyId);
            _escapeHotKeyRegistered = false;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint modifiers, int virtualKey);

        [System.Runtime.InteropServices.DllImport("user32.dll", SetLastError = true)]
        [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }

}

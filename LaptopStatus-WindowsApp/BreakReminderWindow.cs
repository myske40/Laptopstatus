using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LaptopStatus
{
    internal sealed class BreakReminderWindow : Window
    {
        private readonly Action _onTakeBreak;
        private readonly Action _onIgnore;
        private readonly Action _onCancelShutdown;
        private readonly Action _onShutdown;
        private readonly bool _shutdownWarning;
        private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1) };
        private readonly TextBlock _countdown = new();
        private readonly Border _progress = new();
        private int _remaining;
        private bool _finished;

        public BreakReminderWindow(int promptNumber, bool animationsEnabled, Action onTakeBreak, Action onIgnore,
            Action onCancelShutdown, Action onShutdown)
        {
            _shutdownWarning = promptNumber >= 4;
            _remaining = _shutdownWarning ? 10 : 30;
            _onTakeBreak = onTakeBreak;
            _onIgnore = onIgnore;
            _onCancelShutdown = onCancelShutdown;
            _onShutdown = onShutdown;

            Title = _shutdownWarning ? "Peringatan shutdown" : "Pengingat istirahat";
            Width = 440;
            MaxWidth = Math.Max(320, SystemParameters.WorkArea.Width - 32);
            SizeToContent = SizeToContent.Height;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = true;

            var accent = new SolidColorBrush(Color.FromRgb(91, 224, 184));
            var muted = new SolidColorBrush(Color.FromRgb(167, 181, 197));
            var panel = new StackPanel();
            panel.Children.Add(new TextBlock
            {
                Text = _shutdownWarning ? "Laptop akan dimatikan" : "Sudah waktunya istirahat",
                Foreground = Brushes.White, FontSize = 22, FontWeight = FontWeights.SemiBold,
                TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 10)
            });
            panel.Children.Add(new TextBlock
            {
                Text = _shutdownWarning
                    ? "Pengingat istirahat sudah diabaikan tiga kali. Windows akan dimatikan dalam 10 detik. Tekan Batal shutdown untuk menghentikannya."
                    : $"Anda sudah menggunakan LaptopStatus cukup lama (pengingat {promptNumber} dari 3). Istirahat sejenak membantu menjaga kenyamanan.",
                Foreground = muted, FontSize = 14, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 18)
            });

            if (_shutdownWarning)
            {
                _countdown.Text = _remaining.ToString();
                _countdown.Foreground = accent;
                _countdown.FontSize = 46;
                _countdown.FontWeight = FontWeights.Bold;
                _countdown.HorizontalAlignment = HorizontalAlignment.Center;
                _countdown.Margin = new Thickness(0, 0, 0, 8);
                panel.Children.Add(_countdown);
                _progress.Height = 4;
                _progress.HorizontalAlignment = HorizontalAlignment.Left;
                _progress.Background = accent;
                _progress.Margin = new Thickness(0, 0, 0, 18);
                panel.Children.Add(new Border { Height = 4, Background = new SolidColorBrush(Color.FromRgb(48, 66, 82)), Child = _progress });
            }
            else
            {
                _countdown.Text = "Jika diabaikan, pengingat kembali dalam 10 menit · 30 detik";
                _countdown.Foreground = muted;
                _countdown.FontSize = 11;
                _countdown.Margin = new Thickness(0, 0, 0, 14);
                panel.Children.Add(_countdown);
            }

            var actions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right };
            if (!_shutdownWarning)
                actions.Children.Add(MakeButton("Ingatkan 10 menit lagi", false, Ignore, 164));
            actions.Children.Add(MakeButton(_shutdownWarning ? "Batal shutdown" : "Saya istirahat", true,
                _shutdownWarning ? CancelShutdown : TakeBreak, _shutdownWarning ? 142 : 128));
            panel.Children.Add(actions);

            var chrome = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 25, 35)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(48, 66, 82)),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(16),
                Padding = new Thickness(24), Child = panel
            };
            chrome.MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };
            Content = chrome;
            Closing += (_, e) =>
            {
                if (_finished) return;
                if (_shutdownWarning)
                {
                    e.Cancel = true;
                    return;
                }
                e.Cancel = true;
                Ignore();
            };
            Closed += (_, _) => _timer.Stop();

            _timer.Tick += (_, _) =>
            {
                _remaining--;
                if (_shutdownWarning)
                {
                    _countdown.Text = Math.Max(0, _remaining).ToString();
                    _progress.Width = Math.Max(0, (ActualWidth - 52) * _remaining / 10d);
                    if (_remaining > 0) return;
                    Finish(_onShutdown);
                    return;
                }
                _countdown.Text = $"Jika diabaikan, pengingat kembali dalam 10 menit · {_remaining} detik";
                if (_remaining <= 0) Ignore();
            };
            Loaded += (_, _) =>
            {
                if (_shutdownWarning) _progress.Width = Math.Max(0, (ActualWidth - 52));
                _timer.Start();
            };
        }

        private Button MakeButton(string text, bool primary, Action action, double width)
        {
            var button = new Button
            {
                Content = text,
                Width = width,
                Height = 40,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(12, 0, 12, 0),
                BorderThickness = new Thickness(1),
                BorderBrush = new SolidColorBrush(primary ? Color.FromRgb(91, 224, 184) : Color.FromRgb(72, 91, 108)),
                Background = new SolidColorBrush(primary ? Color.FromRgb(91, 224, 184) : Color.FromRgb(27, 42, 58)),
                Foreground = primary ? new SolidColorBrush(Color.FromRgb(7, 19, 28)) : Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Cursor = System.Windows.Input.Cursors.Hand
            };
            button.Click += (_, _) => action();
            return button;
        }

        private void TakeBreak() => Finish(_onTakeBreak);
        private void Ignore() => Finish(_onIgnore);
        private void CancelShutdown() => Finish(_onCancelShutdown);

        public void Dismiss()
        {
            _finished = true;
            _timer.Stop();
            Close();
        }

        private void Finish(Action callback)
        {
            if (_finished) return;
            _finished = true;
            _timer.Stop();
            Close();
            callback();
        }
    }
}

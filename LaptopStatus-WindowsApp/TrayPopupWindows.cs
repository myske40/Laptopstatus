using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using Forms = System.Windows.Forms;
using Color = System.Windows.Media.Color;
using Pen = System.Drawing.Pen;
using Brushes = System.Windows.Media.Brushes;
using Point = System.Windows.Point;
using Size = System.Windows.Size;
using Matrix = System.Windows.Media.Matrix;
using PixelFormat = System.Drawing.Imaging.PixelFormat;

namespace LaptopStatus
{
    internal static class TrayWindowPlacement
    {
        [DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(System.Drawing.Point point, uint flags);

        [DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr monitor, int dpiType, out uint dpiX, out uint dpiY);

        public static double DipScale(Forms.Screen screen)
        {
            try
            {
                var bounds = screen.Bounds;
                var center = new System.Drawing.Point(bounds.Left + bounds.Width / 2, bounds.Top + bounds.Height / 2);
                var monitor = MonitorFromPoint(center, 2);
                if (monitor != IntPtr.Zero && GetDpiForMonitor(monitor, 0, out var dpiX, out _) == 0 && dpiX > 0)
                    return 96d / dpiX;
            }
            catch (DllNotFoundException) { }
            catch (EntryPointNotFoundException) { }
            return 1d;
        }
    }

    internal sealed class TrayFlyoutWindow : Window
    {
        private static readonly Color Panel = Color.FromRgb(17, 25, 35);
        private static readonly Color PanelRaised = Color.FromRgb(24, 34, 48);
        private static readonly Color Mint = Color.FromRgb(114, 228, 191);
        private static readonly Color Dim = Color.FromRgb(167, 181, 197);
        private readonly TextBlock _connectionTitle;
        private readonly TextBlock _connectionSub;
        private readonly Border _connectionIcon;
        private readonly Border _schedule;
        private readonly TextBlock _scheduleText;
        private readonly TextBlock[] _metricValues = new TextBlock[3];
        private readonly Border[] _metricBars = new Border[3];
        private readonly double[] _metricPercentages = new double[3];
        private readonly Action _openMain;
        private readonly Action _exitApp;
        private readonly Action _enableFallback;
        private bool _closing;
        private bool _motionEnabled = true;

        public TrayFlyoutWindow(Action openMain, Action exitApp, Action? enableFallback = null)
        {
            _openMain = openMain;
            _exitApp = exitApp;
            _enableFallback = enableFallback ?? (() => { });
            Width = 332;
            SizeToContent = SizeToContent.Height;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;

            var root = new StackPanel();
            root.Children.Add(BuildHeader());

            _connectionTitle = new TextBlock { Text = "Menunggu Android", FontSize = 13.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White };
            _connectionSub = new TextBlock { Text = "Belum ada perangkat yang terhubung.", FontSize = 12, Foreground = Brush(Dim), Margin = new Thickness(0, 2, 0, 0) };
            _connectionIcon = new Border
            {
                Width = 30, Height = 30, CornerRadius = new CornerRadius(9),
                Background = Brush(Color.FromRgb(35, 47, 62)),
                Child = MakeGlyph("M7 3 H17 Q19 3 19 5 V19 Q19 21 17 21 H7 Q5 21 5 19 V5 Q5 3 7 3 M10 18 H14", Dim, 1.6)
            };
            var connectionTexts = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            connectionTexts.Children.Add(_connectionTitle);
            connectionTexts.Children.Add(_connectionSub);
            var connectionRow = new DockPanel { LastChildFill = true };
            _connectionIcon.Margin = new Thickness(0, 0, 10, 0);
            DockPanel.SetDock(_connectionIcon, Dock.Left);
            connectionRow.Children.Add(_connectionIcon);
            connectionRow.Children.Add(connectionTexts);
            root.Children.Add(new Border
            {
                Background = Brush(PanelRaised), BorderBrush = Brush(Color.FromArgb(18, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 10, 12, 10), Margin = new Thickness(0, 14, 0, 0), Child = connectionRow
            });

            _scheduleText = new TextBlock { FontSize = 12.5, Foreground = Brush(Color.FromRgb(243, 215, 155)), TextWrapping = TextWrapping.Wrap };
            _schedule = new Border
            {
                Background = Brush(Color.FromArgb(24, 242, 195, 107)),
                BorderBrush = Brush(Color.FromArgb(55, 242, 195, 107)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(12), Padding = new Thickness(12, 9, 12, 9), Margin = new Thickness(0, 8, 0, 0),
                Child = _scheduleText, Visibility = Visibility.Collapsed
            };
            root.Children.Add(_schedule);

            var metrics = new Grid { Margin = new Thickness(0, 15, 0, 0) };
            for (var i = 0; i < 3; i++) metrics.ColumnDefinitions.Add(new ColumnDefinition());
            AddMetric(metrics, 0, "CPU");
            AddMetric(metrics, 1, "RAM");
            AddMetric(metrics, 2, "Baterai");
            root.Children.Add(metrics);

            var actions = new StackPanel { Margin = new Thickness(0, 14, 0, 0) };
            actions.Children.Add(new Border { Height = 1, Background = Brush(Color.FromArgb(20, 255, 255, 255)), Margin = new Thickness(0, 0, 0, 4) });
            actions.Children.Add(MakeAction("M3.5 4.5 H20.5 V19.5 H3.5 Z M3.5 9 H20.5 M12 13.5 V16.5 M10.5 15 H13.5", "Buka Status Perangkat", false, () => { CloseAnimated(); _openMain(); }));
            actions.Children.Add(MakeAction("M4 7 H20 M7 4 V10 M17 4 V10 M5 12 H19 M7 16 H17", "Gunakan menu tray sederhana", false, () => { CloseAnimated(); _enableFallback(); }));
            actions.Children.Add(MakeAction("M12 3.5 V11.5 M7 6.8 A7.5 7.5 0 1 0 17 6.8", "Keluar", true, () => { CloseAnimated(); _exitApp(); }));
            root.Children.Add(actions);

            Content = new Border
            {
                Background = Brush(Panel), BorderBrush = Brush(Color.FromArgb(36, 255, 255, 255)), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(18), Padding = new Thickness(16),
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = .48, BlurRadius = 28, ShadowDepth = 4 },
                Child = new ScrollViewer
                {
                    VerticalScrollBarVisibility = ScrollBarVisibility.Hidden,
                    HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                    Content = root
                },
                RenderTransformOrigin = new Point(.92, 1), RenderTransform = new ScaleTransform(.94, .94)
            };
            Opacity = 0;
            Deactivated += (_, _) => CloseAnimated();
            PreviewKeyDown += (_, e) =>
            {
                if (e.Key == Key.Escape) { CloseAnimated(); e.Handled = true; }
                if (e.Key == Key.Down && IsVisible) { MoveActionFocus(1); e.Handled = true; }
                if (e.Key == Key.Up && IsVisible) { MoveActionFocus(-1); e.Handled = true; }
            };
        }

        public void UpdateState(string connectionState, string scheduledAction, DateTimeOffset? scheduledAt, double cpu, double ram, int? battery)
        {
            switch (connectionState)
            {
                case "connected":
                    _connectionTitle.Text = "Android terhubung";
                    _connectionSub.Text = "Koneksi aman dari Android.";
                    _connectionIcon.Background = Brush(Color.FromArgb(35, 114, 228, 191));
                    _connectionIcon.Child = MakeGlyph("M4 12.5 L9 17 L20 6.5", Mint, 2);
                    break;
                case "rejected":
                    _connectionTitle.Text = "Koneksi Android ditolak";
                    _connectionSub.Text = "Pairing atau token ditolak.";
                    _connectionIcon.Background = Brush(Color.FromArgb(38, 232, 93, 106));
                    _connectionIcon.Child = MakeGlyph("M12 4 L21 20 H3 Z M12 10 V14 M12 17 H12.02", Color.FromRgb(232, 93, 106), 1.7);
                    break;
                default:
                    _connectionTitle.Text = "Menunggu Android";
                    _connectionSub.Text = "Belum ada perangkat yang terhubung.";
                    _connectionIcon.Background = Brush(Color.FromRgb(35, 47, 62));
                    _connectionIcon.Child = MakeGlyph("M7 3 H17 Q19 3 19 5 V19 Q19 21 17 21 H7 Q5 21 5 19 V5 Q5 3 7 3 M10 18 H14", Dim, 1.6);
                    break;
            }
            _schedule.Visibility = scheduledAt.HasValue ? Visibility.Visible : Visibility.Collapsed;
            if (scheduledAt.HasValue)
            {
                var actionName = scheduledAction switch { "shutdown" => "Shutdown", "restart" => "Restart", _ => "Sleep" };
                _scheduleText.Text = $"{actionName} terjadwal pada {scheduledAt.Value.ToLocalTime():dd MMM yyyy, HH:mm}.";
            }
            UpdateMetric(0, cpu, "%");
            UpdateMetric(1, ram, "%");
            UpdateMetric(2, battery ?? 0, battery.HasValue ? "%" : "—");
            if (!battery.HasValue) _metricBars[2].Width = 0;
        }

        public void ShowAt(Forms.NotifyIcon trayIcon, bool animate = true)
        {
            var cursor = Forms.Cursor.Position;
            var screen = Forms.Screen.FromPoint(cursor);
            var area = screen.WorkingArea;
            var dipScale = TrayWindowPlacement.DipScale(screen);
            var point = new Point(cursor.X * dipScale, cursor.Y * dipScale);
            var workLeft = area.Left * dipScale;
            var workTop = area.Top * dipScale;
            var workRight = area.Right * dipScale;
            var workBottom = area.Bottom * dipScale;
            Width = Math.Max(250, Math.Min(332, area.Width * dipScale - 16));
            MaxHeight = Math.Max(200, area.Height * dipScale - 16);
            _motionEnabled = animate;
            Left = workLeft + 8;
            Top = workTop + 8;
            Show();
            UpdateLayout();
            var popupWidth = ActualWidth > 0 ? ActualWidth : Width;
            var popupHeight = ActualHeight > 0 ? ActualHeight : MaxHeight;
            Left = Math.Clamp(point.X - popupWidth + 20, workLeft + 8, Math.Max(workLeft + 8, workRight - popupWidth - 8));
            Top = Math.Clamp(point.Y - popupHeight - 12, workTop + 8, Math.Max(workTop + 8, workBottom - popupHeight - 8));
            UpdateLayout();
            Activate();
            if (!animate)
            {
                Opacity = 1;
                if (Content is Border contentPanel && contentPanel.RenderTransform is ScaleTransform still) { still.ScaleX = 1; still.ScaleY = 1; }
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    for (var i = 0; i < _metricPercentages.Length; i++) ApplyMetricBar(i, animate: false);
                }), DispatcherPriority.Loaded);
                _ = Dispatcher.BeginInvoke(new Action(() =>
                {
                    var first = FindFirstAction(Content as DependencyObject);
                    if (first is not null) Keyboard.Focus(first);
                    else Keyboard.Focus(this);
                }), DispatcherPriority.Input);
                return;
            }
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                for (var i = 0; i < _metricPercentages.Length; i++) ApplyMetricBar(i, animate: true);
            }), DispatcherPriority.Loaded);
            BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(190)));
            if (Content is Border panel && panel.RenderTransform is ScaleTransform scaleTransform)
            {
                scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.94, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = .14, EasingMode = EasingMode.EaseOut } });
                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.94, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { Amplitude = .14, EasingMode = EasingMode.EaseOut } });
            }
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                var firstAction = FindFirstAction(Content as DependencyObject);
                if (firstAction is not null) Keyboard.Focus(firstAction);
                else Keyboard.Focus(this);
            }), DispatcherPriority.Input);
        }

        public void CloseAnimated()
        {
            if (_closing || !IsVisible) return;
            _closing = true;
            if (!_motionEnabled) { Close(); return; }
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        private void AddMetric(Grid grid, int index, string label)
        {
            var stack = new StackPanel { Margin = new Thickness(index == 0 ? 0 : 7, 0, index == 2 ? 0 : 7, 0) };
            stack.Children.Add(new TextBlock { Text = label, FontSize = 12, Foreground = Brush(Dim) });
            _metricValues[index] = new TextBlock { Text = "0%", FontSize = 19, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, Margin = new Thickness(0, 1, 0, 6) };
            stack.Children.Add(_metricValues[index]);
            var track = new Border { Height = 5, CornerRadius = new CornerRadius(9), Background = Brush(Color.FromArgb(18, 255, 255, 255)), HorizontalAlignment = HorizontalAlignment.Stretch, ClipToBounds = true };
            _metricBars[index] = new Border { Width = 0, Height = 5, CornerRadius = new CornerRadius(9), HorizontalAlignment = HorizontalAlignment.Left, Background = Brush(Color.FromRgb(53, 170, 135)), RenderTransformOrigin = new Point(0, .5), RenderTransform = new ScaleTransform(1, 1) };
            track.Child = _metricBars[index];
            stack.Children.Add(track);
            Grid.SetColumn(stack, index);
            grid.Children.Add(stack);
        }

        private void UpdateMetric(int index, double value, string suffix)
        {
            value = Math.Clamp(value, 0, 100);
            _metricPercentages[index] = value;
            _metricValues[index].Text = suffix == "—" ? "N/A" : $"{Math.Round(value):0}{suffix}";
            var color = index == 2 && value < 20 || index != 2 && value > 85 ? Color.FromRgb(242, 195, 107) : Color.FromRgb(114, 228, 191);
            _metricBars[index].Background = Brush(color);
            ApplyMetricBar(index, animate: IsVisible);
        }

        private void ApplyMetricBar(int index, bool animate)
        {
            if (_metricBars[index].Parent is not FrameworkElement host || host.ActualWidth <= 0) return;
            var target = host.ActualWidth * _metricPercentages[index] / 100;
            var animation = animate
                ? new DoubleAnimation(_metricBars[index].Width, target, TimeSpan.FromMilliseconds(650)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } }
                : null;
            _metricBars[index].BeginAnimation(FrameworkElement.WidthProperty, animation);
            if (!animate) _metricBars[index].Width = target;
        }

        private static FrameworkElement BuildHeader()
        {
            var glyph = new Grid { Width = 40, Height = 40, Margin = new Thickness(0, 0, 12, 0) };
            glyph.Children.Add(new Border { CornerRadius = new CornerRadius(12), Background = Brush(Color.FromRgb(17, 28, 40)), BorderBrush = Brush(Color.FromRgb(54, 82, 99)), BorderThickness = new Thickness(1.4) });
            glyph.Children.Add(MakeGlyph("M3 6 H21 Q24 6 24 9 V20 Q24 23 21 23 H3 Q0 23 0 20 V9 Q0 6 3 6 M8 27 V29 M16 27 V29 M5 31 H19", Mint, 2.2, 28, 28));
            glyph.Children.Add(new Ellipse { Width = 7, Height = 7, Fill = Brush(Mint), HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 4, 4, 0) });
            var text = new StackPanel { VerticalAlignment = VerticalAlignment.Center };
            text.Children.Add(new TextBlock { Text = "Status Perangkat", FontSize = 15.5, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White });
            var state = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            state.Children.Add(new Ellipse { Width = 8, Height = 8, Fill = Brush(Mint), Margin = new Thickness(1, 0, 8, 0) });
            state.Children.Add(new TextBlock { Text = "Layanan aktif", FontSize = 12.5, Foreground = Brush(Dim) });
            text.Children.Add(state);
            var row = new StackPanel { Orientation = Orientation.Horizontal };
            row.Children.Add(glyph);
            row.Children.Add(text);
            return row;
        }

        private static Button MakeAction(string geometry, string label, bool danger, Action action)
        {
            var button = new Button { Height = 42, Padding = new Thickness(10, 0, 10, 0), Background = Brushes.Transparent, BorderThickness = new Thickness(0), HorizontalContentAlignment = HorizontalAlignment.Stretch, Cursor = Cursors.Hand, FocusVisualStyle = null };
            var row = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
            row.Children.Add(new Border
            {
                Width = 30, Height = 22, Margin = new Thickness(0, 0, 0, 0),
                Child = MakeGlyph(geometry, danger ? Color.FromRgb(232, 93, 106) : Mint, 1.8, 18, 18)
            });
            row.Children.Add(new TextBlock { Text = label, FontSize = 13.5, FontWeight = FontWeights.Medium, Foreground = Brush(Color.FromRgb(241, 246, 252)), VerticalAlignment = VerticalAlignment.Center });
            button.Content = row;
            button.Click += (_, _) => action();
            button.MouseEnter += (_, _) => button.Background = Brush(danger ? Color.FromArgb(30, 232, 93, 106) : Color.FromArgb(18, 255, 255, 255));
            button.MouseLeave += (_, _) => button.Background = Brushes.Transparent;
            button.PreviewKeyDown += (_, e) =>
            {
                if (e.Key is not (Key.Down or Key.Up)) return;
                var direction = e.Key == Key.Down ? 1 : -1;
                if (button.Parent is Panel panel)
                {
                    var buttons = panel.Children.OfType<Button>().ToArray();
                    var index = Array.IndexOf(buttons, button);
                    if (buttons.Length > 0) buttons[(index + direction + buttons.Length) % buttons.Length].Focus();
                }
                e.Handled = true;
            };
            return button;
        }

        private void MoveActionFocus(int direction)
        {
            if (Content is not Border border || border.Child is not StackPanel root) return;
            var actions = root.Children.OfType<StackPanel>().LastOrDefault();
            if (actions is null) return;
            var buttons = actions.Children.OfType<Button>().ToArray();
            if (buttons.Length == 0) return;
            var index = Array.IndexOf(buttons, Keyboard.FocusedElement as Button);
            buttons[(index + direction + buttons.Length) % buttons.Length].Focus();
        }

        private static Button? FindFirstAction(DependencyObject? root)
        {
            if (root is null) return null;
            if (root is Button button) return button;
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
            {
                var result = FindFirstAction(VisualTreeHelper.GetChild(root, i));
                if (result is not null) return result;
            }
            return null;
        }

        private static Path MakeGlyph(string geometry, Color color, double thickness, double width = 18, double height = 18) => new()
        {
            Data = Geometry.Parse(geometry), Stroke = Brush(color), StrokeThickness = thickness,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round, Width = width, Height = height,
            Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };

        private static SolidColorBrush Brush(Color color) => new(color);
    }

    internal sealed class TrayToastWindow : Window
    {
        private readonly DispatcherTimer _lifetime;
        private bool _dismissed;
        private readonly Border _progress;
        private readonly List<TrayToastWindow> _openToasts;
        private readonly Action _openHistory;

        public TrayToastWindow(string title, string message, string kind, int durationMilliseconds, int stackIndex, List<TrayToastWindow> openToasts, Action openHistory, bool animate = true)
        {
            _openToasts = openToasts;
            _openHistory = openHistory;
            var screen = Forms.Screen.FromPoint(Forms.Cursor.Position);
            var area = screen.WorkingArea;
            var dipScale = TrayWindowPlacement.DipScale(screen);
            Width = Math.Max(250, Math.Min(390, area.Width * dipScale - 24));
            Height = 112;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            Topmost = true;
            Left = area.Right * dipScale - Width - 16;
            var minimumTop = area.Top * dipScale + 8;
            var maximumTop = Math.Max(minimumTop, area.Bottom * dipScale - Height - 16);
            Top = Math.Clamp(area.Bottom * dipScale - Height - 16 - stackIndex * (Height + 8), minimumTop, maximumTop);
            var accent = kind switch { "warn" => Color.FromRgb(232, 93, 106), "time" => Color.FromRgb(242, 195, 107), _ => Color.FromRgb(114, 228, 191) };
            var borderColor = kind switch { "warn" => Color.FromArgb(128, 232, 93, 106), "time" => Color.FromArgb(115, 242, 195, 107), _ => Color.FromRgb(57, 91, 91) };
            var tint = kind switch { "warn" => Color.FromArgb(38, 232, 93, 106), "time" => Color.FromArgb(36, 242, 195, 107), _ => Color.FromArgb(36, 114, 228, 191) };
            var root = new Grid();
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
            root.ColumnDefinitions.Add(new ColumnDefinition());
            root.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(40) });
            root.ColumnDefinitions[1].MinWidth = 0;
            var glyph = kind switch { "warn" => "M12 3.8 L21 19.5 H3 Z M12 10 V14.5 M12 17.4 H12.02", "time" => "M12 3.5 A8.5 8.5 0 1 1 12 20.5 A8.5 8.5 0 1 1 12 3.5 M12 7.5 V12 L15 14", _ => "M4 12.5 L9 17 L20 6.5" };
            var icon = new Border { Width = 34, Height = 34, CornerRadius = new CornerRadius(10), Background = new SolidColorBrush(tint), Child = MakeToastGlyph(glyph, accent) };
            Grid.SetColumn(icon, 0);
            root.Children.Add(icon);
            var text = new StackPanel { Margin = new Thickness(12, 0, 0, 0), VerticalAlignment = VerticalAlignment.Top };
            text.Children.Add(new TextBlock { Text = title, FontSize = 15, FontWeight = FontWeights.SemiBold, Foreground = Brushes.White, TextTrimming = TextTrimming.CharacterEllipsis });
            text.Children.Add(new TextBlock { Text = message, FontSize = 13, Foreground = new SolidColorBrush(Color.FromRgb(194, 207, 220)), TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 3, 0, 0), MaxHeight = 60 });
            Grid.SetColumn(text, 1);
            root.Children.Add(text);
            var close = new Button
            {
                Content = "×", Width = 36, Height = 36, FontSize = 23, FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White, Background = new SolidColorBrush(Color.FromArgb(100, 232, 93, 106)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(190, 232, 93, 106)), BorderThickness = new Thickness(1),
                VerticalAlignment = VerticalAlignment.Top, HorizontalAlignment = HorizontalAlignment.Center,
                Cursor = Cursors.Hand, ToolTip = "Tutup notifikasi"
            };
            close.Click += (_, _) => Dismiss();
            Grid.SetColumn(close, 2);
            root.Children.Add(close);
            _progress = new Border { Height = 2.5, Background = new SolidColorBrush(accent), Opacity = .7, HorizontalAlignment = HorizontalAlignment.Stretch, VerticalAlignment = VerticalAlignment.Bottom, RenderTransformOrigin = new Point(0, .5), RenderTransform = new ScaleTransform(1, 1) };
            Grid.SetColumnSpan(_progress, 3);
            root.Children.Add(_progress);
            var content = new Grid { Margin = new Thickness(14, 14, 10, 15) };
            content.Children.Add(root);
            var toastBorder = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 26, 37)), BorderBrush = new SolidColorBrush(borderColor), BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(15), Child = content,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Colors.Black, Opacity = .4, BlurRadius = 25, ShadowDepth = 4 },
                RenderTransform = new TranslateTransform(36, 0), Cursor = Cursors.Hand
            };
            toastBorder.MouseLeftButtonUp += (_, e) =>
            {
                if (close.IsMouseOver) return;
                e.Handled = true;
                Dismiss();
                _openHistory();
            };
            Content = toastBorder;
            Opacity = 0;
            _lifetime = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(durationMilliseconds) };
            MouseEnter += (_, _) => _lifetime.Stop();
            MouseLeave += (_, _) => { if (IsVisible) _lifetime.Start(); };
            _lifetime.Tick += (_, _) => Dismiss();
            Loaded += (_, _) =>
            {
                if (animate && Content is Border border && border.RenderTransform is TranslateTransform translate)
                    translate.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(36, 0, TimeSpan.FromMilliseconds(460)) { EasingFunction = new BackEase { Amplitude = .12, EasingMode = EasingMode.EaseOut } });
                if (animate) BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250)));
                else Opacity = 1;
                _lifetime.Start();
                if (animate && _progress.RenderTransform is ScaleTransform scale)
                    scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(durationMilliseconds)));
            };
            Closed += (_, _) => { _lifetime.Stop(); _openToasts.Remove(this); };
        }

        public void Dismiss()
        {
            if (_dismissed) return;
            _dismissed = true;
            _lifetime.Stop();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(240));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        private static Path MakeToastGlyph(string geometry, Color color) => new()
        {
            Data = Geometry.Parse(geometry), Stroke = new SolidColorBrush(color), StrokeThickness = 1.9,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round,
            Width = 18, Height = 18, Stretch = Stretch.Uniform, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center
        };
    }

    internal sealed class TrayIconAnimator : IDisposable
    {
        private readonly Forms.NotifyIcon _icon;
        private readonly Forms.Timer _timer = new() { Interval = 160 };
        private readonly Stopwatch _clock = Stopwatch.StartNew();
        private System.Drawing.Icon? _currentFrame;
        private string _state = "idle";
        private long _motionUntil;
        private long _motionStarted;
        private bool _shake;
        private bool _motionEnabled = true;
        private bool _disposed;

        public TrayIconAnimator(Forms.NotifyIcon icon)
        {
            _icon = icon;
            _timer.Tick += (_, _) => RenderFrame();
            RenderFrame();
        }

        public void SetState(string connectionState, bool scheduled)
        {
            _state = connectionState == "rejected" ? "rejected" : scheduled ? "schedule" : connectionState == "connected" ? "connected" : "idle";
            UpdateTimerState();
            RenderFrame();
        }

        public void SetMotionEnabled(bool enabled)
        {
            _motionEnabled = enabled;
            if (!enabled) { _motionUntil = 0; _shake = false; }
            UpdateTimerState();
            RenderFrame();
        }

        public void Pulse(bool warning = false)
        {
            if (!_motionEnabled) return;
            _shake = warning;
            _motionStarted = _clock.ElapsedMilliseconds;
            _motionUntil = _motionStarted + (warning ? 700 : 850);
            UpdateTimerState();
            RenderFrame();
        }

        private void UpdateTimerState()
        {
            var animateState = _motionEnabled && (_state != "idle" || _clock.ElapsedMilliseconds < _motionUntil);
            if (animateState) _timer.Start();
            else _timer.Stop();
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _timer.Stop();
            _timer.Dispose();
            _currentFrame?.Dispose();
        }

        private void RenderFrame()
        {
            if (_disposed) return;
            var elapsed = _clock.Elapsed.TotalSeconds;
            var baseColor = _state switch
            {
                "rejected" => System.Drawing.Color.FromArgb(232, 93, 106),
                "schedule" => System.Drawing.Color.FromArgb(242, 195, 107),
                _ => System.Drawing.Color.FromArgb(114, 228, 191)
            };
            var icon = new Bitmap(32, 32, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(icon))
            using (var monitorPen = new Pen(System.Drawing.Color.FromArgb(235, baseColor), 2.1f))
            using (var detailPen = new Pen(System.Drawing.Color.FromArgb(225, baseColor), 1.8f))
            using (var dotBrush = new SolidBrush(baseColor))
            using (var ripplePen = new Pen(System.Drawing.Color.FromArgb(80, baseColor), 1.4f))
            {
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var motionProgress = _clock.ElapsedMilliseconds - _motionStarted;
                var activeMotion = _motionEnabled && motionProgress < _motionUntil - _motionStarted;
                var jitter = activeMotion && _shake ? (int)(Math.Sin(elapsed * 42) * 2.0) : 0;
                var offsetY = activeMotion && !_shake
                    ? motionProgress < 260 ? -3.0f * MathF.Sin((float)(Math.PI * motionProgress / 260))
                    : motionProgress < 520 ? -1.5f * MathF.Sin((float)(Math.PI * (motionProgress - 260) / 260)) : 0
                    : 0;
                graphics.DrawRoundedRectangle(monitorPen, 3 + jitter, 7 + offsetY, 19, 13, 3);
                graphics.DrawLine(detailPen, 11 + jitter, 20 + offsetY, 11 + jitter, 24 + offsetY);
                graphics.DrawLine(detailPen, 8 + jitter, 25 + offsetY, 16 + jitter, 25 + offsetY);
                var phase = elapsed % (_state == "rejected" ? 1.4 : _state == "schedule" ? 3.2 : 2.4);
                if (_motionEnabled && _state is ("connected" or "schedule" or "rejected"))
                {
                    var period = _state == "rejected" ? 1.4 : _state == "schedule" ? 3.2 : 2.4;
                    var scale = (float)(phase / period);
                    var radius = 3.1f + scale * 7.5f;
                    ripplePen.Color = System.Drawing.Color.FromArgb(Math.Max(0, (int)(150 * (1 - scale))), baseColor);
                    graphics.DrawEllipse(ripplePen, 24 - radius, 6 - radius + offsetY, radius * 2, radius * 2);
                }
                var alpha = _motionEnabled && _state == "rejected" && Math.Sin(elapsed * Math.PI * 2) < 0 ? 90 :
                    _motionEnabled && _state == "idle" ? (int)(150 + 105 * (0.5 + 0.5 * Math.Sin(elapsed * Math.PI * 2 / 3.2))) : 255;
                dotBrush.Color = System.Drawing.Color.FromArgb(alpha, baseColor);
                graphics.FillEllipse(dotBrush, 21 + jitter, 3 + offsetY, 6, 6);
            }
            var handle = icon.GetHicon();
            try
            {
                var frame = System.Drawing.Icon.FromHandle(handle);
                _currentFrame?.Dispose();
                _currentFrame = (System.Drawing.Icon)frame.Clone();
                _icon.Icon = _currentFrame;
            }
            finally
            {
                DestroyIcon(handle);
                icon.Dispose();
            }
            UpdateTimerState();
        }

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DestroyIcon(IntPtr handle);
    }

    internal static class DrawingExtensions
    {
        public static void DrawRoundedRectangle(this Graphics graphics, Pen pen, float x, float y, float width, float height, float radius)
        {
            using var path = new GraphicsPath();
            var diameter = radius * 2;
            path.AddArc(x, y, diameter, diameter, 180, 90);
            path.AddArc(x + width - diameter, y, diameter, diameter, 270, 90);
            path.AddArc(x + width - diameter, y + height - diameter, diameter, diameter, 0, 90);
            path.AddArc(x, y + height - diameter, diameter, diameter, 90, 90);
            path.CloseFigure();
            graphics.DrawPath(pen, path);
        }
    }
}

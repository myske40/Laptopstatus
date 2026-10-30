using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;

namespace LaptopStatus
{
    public sealed record JokeWindowSettings(
        string Message,
        string BackgroundHex,
        string IconGlyph,
        string? IconImagePath,
        string Animation)
    {
        public static JokeWindowSettings Default { get; } = new(
            "Kamu telah mengakses laptop tanpa seizin pemilik.", "#AE1218", "😵", null, "pulse");
    }

    public sealed class JokeWindow : Window
    {
        private const string SimulationNotice = "SIMULASI JOKE • Layar tertutup otomatis dalam 30 detik";
        private readonly UIElement _icon;
        private readonly System.Windows.Threading.DispatcherTimer _autoClose;

        public JokeWindow(JokeWindowSettings? settings = null, bool animationsEnabled = true)
        {
            settings ??= JokeWindowSettings.Default;
            Title = "Mode Joke";
            WindowStyle = WindowStyle.None;
            WindowState = WindowState.Maximized;
            Topmost = true;
            ResizeMode = ResizeMode.NoResize;
            Background = new SolidColorBrush(ParseBackground(settings.BackgroundHex));

            _icon = CreateIcon(settings);
            var iconArea = new Grid
            {
                Width = 160,
                Height = 150,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 0, 0, 14),
                Children = { _icon }
            };

            var message = string.IsNullOrWhiteSpace(settings.Message)
                ? JokeWindowSettings.Default.Message
                : settings.Message.Trim();

            Content = new Grid
            {
                Children =
                {
                    new StackPanel
                    {
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        MaxWidth = 1080,
                        Margin = new Thickness(40),
                        Children =
                        {
                            iconArea,
                            new TextBlock
                            {
                                Text = "MODE JOKE AKTIF",
                                Foreground = Brushes.White,
                                FontSize = 50,
                                FontWeight = FontWeights.Bold,
                                TextAlignment = TextAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(0, 0, 0, 18)
                            },
                            new TextBlock
                            {
                                Text = message,
                                Foreground = Brushes.White,
                                FontSize = 27,
                                TextAlignment = TextAlignment.Center,
                                TextWrapping = TextWrapping.Wrap
                            },
                            new TextBlock
                            {
                                Text = SimulationNotice,
                                Foreground = new SolidColorBrush(Color.FromArgb(220, 255, 255, 255)),
                                FontSize = 13,
                                TextAlignment = TextAlignment.Center,
                                TextWrapping = TextWrapping.Wrap,
                                Margin = new Thickness(0, 20, 0, 0)
                            }
                        }
                    }
                }
            };

            if (animationsEnabled) ApplyAnimation(_icon, settings.Animation);

            _autoClose = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _autoClose.Tick += (_, _) => Close();
            Closed += (_, _) => _autoClose.Stop();
            _autoClose.Start();
        }

        private static Color ParseBackground(string? value)
        {
            try
            {
                if (value is not null && ColorConverter.ConvertFromString(value) is Color color) return color;
            }
            catch { }
            return Color.FromRgb(174, 18, 24);
        }

        private static UIElement CreateIcon(JokeWindowSettings settings)
        {
            if (!string.IsNullOrWhiteSpace(settings.IconImagePath) && File.Exists(settings.IconImagePath))
            {
                try
                {
                    var bitmap = new BitmapImage();
                    bitmap.BeginInit();
                    bitmap.CacheOption = BitmapCacheOption.OnLoad;
                    bitmap.DecodePixelWidth = 192;
                    bitmap.UriSource = new Uri(settings.IconImagePath, UriKind.Absolute);
                    bitmap.EndInit();
                    bitmap.Freeze();
                    return new Image
                    {
                        Source = bitmap,
                        Width = 138,
                        Height = 138,
                        Stretch = Stretch.Uniform,
                        HorizontalAlignment = HorizontalAlignment.Center,
                        VerticalAlignment = VerticalAlignment.Center,
                        RenderTransformOrigin = new Point(0.5, 0.5)
                    };
                }
                catch { }
            }

            return new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(settings.IconGlyph) ? "😵" : settings.IconGlyph.Trim(),
                FontFamily = new FontFamily("Segoe UI Emoji"),
                Foreground = Brushes.White,
                FontSize = 112,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextAlignment = TextAlignment.Center,
                RenderTransformOrigin = new Point(0.5, 0.5)
            };
        }

        private static void ApplyAnimation(UIElement icon, string? animation)
        {
            switch ((animation ?? "pulse").ToLowerInvariant())
            {
                case "spin":
                {
                    var rotate = new RotateTransform();
                    icon.RenderTransform = rotate;
                    Loop(rotate, RotateTransform.AngleProperty, 0, 360, 1500, reverse: false);
                    break;
                }
                case "bounce":
                {
                    var move = new TranslateTransform();
                    icon.RenderTransform = move;
                    Loop(move, TranslateTransform.YProperty, 0, -26, 420);
                    break;
                }
                case "shake":
                {
                    var move = new TranslateTransform();
                    icon.RenderTransform = move;
                    Loop(move, TranslateTransform.XProperty, -12, 12, 65);
                    break;
                }
                case "swing":
                {
                    var rotate = new RotateTransform();
                    icon.RenderTransform = rotate;
                    Loop(rotate, RotateTransform.AngleProperty, -14, 14, 360);
                    break;
                }
                case "zoom":
                {
                    var scale = new ScaleTransform(1, 1);
                    icon.RenderTransform = scale;
                    Loop(scale, ScaleTransform.ScaleXProperty, .72, 1.16, 620);
                    Loop(scale, ScaleTransform.ScaleYProperty, .72, 1.16, 620);
                    break;
                }
                case "fade":
                    Loop(icon, UIElement.OpacityProperty, 1, .28, 520);
                    break;
                case "slide":
                {
                    var move = new TranslateTransform();
                    icon.RenderTransform = move;
                    Loop(move, TranslateTransform.XProperty, -42, 42, 820);
                    break;
                }
                case "wobble":
                {
                    var transform = new TransformGroup();
                    var scale = new ScaleTransform(1, 1);
                    var rotate = new RotateTransform();
                    transform.Children.Add(scale);
                    transform.Children.Add(rotate);
                    icon.RenderTransform = transform;
                    Loop(rotate, RotateTransform.AngleProperty, -9, 9, 150);
                    Loop(scale, ScaleTransform.ScaleXProperty, 1, 1.12, 230);
                    Loop(scale, ScaleTransform.ScaleYProperty, 1, .92, 230);
                    break;
                }
                case "pop":
                {
                    var scale = new ScaleTransform(1, 1);
                    icon.RenderTransform = scale;
                    Loop(scale, ScaleTransform.ScaleXProperty, .62, 1.2, 300);
                    Loop(scale, ScaleTransform.ScaleYProperty, .62, 1.2, 300);
                    break;
                }
                default:
                {
                    var scale = new ScaleTransform(1, 1);
                    icon.RenderTransform = scale;
                    Loop(scale, ScaleTransform.ScaleXProperty, 1, 1.18, 720);
                    Loop(scale, ScaleTransform.ScaleYProperty, 1, 1.18, 720);
                    break;
                }
            }
        }

        private static void Loop(IAnimatable target, DependencyProperty property, double from, double to, int milliseconds, bool reverse = true)
        {
            target.BeginAnimation(property, new DoubleAnimation(from, to, TimeSpan.FromMilliseconds(milliseconds))
            {
                AutoReverse = reverse,
                RepeatBehavior = RepeatBehavior.Forever,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
        }
    }
}

using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using QRCoder;

namespace LaptopStatus
{
    internal sealed class QrPairingWindow : Window
    {
        public QrPairingWindow(string payload, string deviceName, string addresses, bool enableAnimations = true)
        {
            Title = "Pasangkan Android dengan QR";
            var workArea = SystemParameters.WorkArea;
            var maxWidth = Math.Max(300, workArea.Width - 32);
            var maxHeight = Math.Max(260, workArea.Height - 32);
            Width = Math.Min(480, maxWidth);
            MinWidth = Math.Min(360, Width);
            MaxWidth = maxWidth;
            Height = Math.Min(640, maxHeight);
            MinHeight = Math.Min(480, Height);
            MaxHeight = maxHeight;
            ResizeMode = ResizeMode.CanResize;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            Background = new SolidColorBrush(Color.FromRgb(18, 24, 33));

            using var generator = new QRCodeGenerator();
            using var data = generator.CreateQrCode(payload, QRCodeGenerator.ECCLevel.Q);
            using var renderer = new PngByteQRCode(data);
            var bytes = renderer.GetGraphic(8);
            var bitmap = new BitmapImage();
            using (var stream = new MemoryStream(bytes))
            {
                bitmap.BeginInit();
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.StreamSource = stream;
                bitmap.EndInit();
                bitmap.Freeze();
            }

            var content = new StackPanel { Margin = new Thickness(24), VerticalAlignment = VerticalAlignment.Top };
            content.Children.Add(new TextBlock
            {
                Text = "Pindai untuk memasangkan",
                Foreground = Brushes.White,
                FontSize = 21,
                FontWeight = FontWeights.SemiBold,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 6)
            });
            content.Children.Add(new TextBlock
            {
                Text = deviceName,
                Foreground = new SolidColorBrush(Color.FromRgb(80, 220, 177)),
                FontSize = 15,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 4)
            });
            content.Children.Add(new TextBlock
            {
                Text = addresses,
                Foreground = new SolidColorBrush(Color.FromRgb(177, 192, 210)),
                FontSize = 11,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 0, 0, 18)
            });
            var qrImage = new Image { Source = bitmap, Width = 250, Height = 250, Stretch = Stretch.Uniform };
            content.Children.Add(new Border
            {
                Background = Brushes.White,
                CornerRadius = new CornerRadius(14),
                Padding = new Thickness(12),
                HorizontalAlignment = HorizontalAlignment.Center,
                Child = qrImage
            });
            content.Children.Add(new TextBlock
            {
                Text = "QR berisi alamat Wi-Fi dan Tailscale saat aktif, serta hostname Cloudflare bila dipakai. Untuk koneksi jarak jauh, login ke tailnet yang sama di Android dan Windows. Setelah dipindai, cocokkan kode dan setujui pemasangan di laptop.",
                Foreground = new SolidColorBrush(Color.FromRgb(177, 192, 210)),
                FontSize = 12,
                TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center,
                Margin = new Thickness(0, 16, 0, 14)
            });
            var close = new Button
            {
                Content = "Tutup",
                Padding = new Thickness(22, 8, 22, 8),
                HorizontalAlignment = HorizontalAlignment.Center
            };
            close.Click += (_, _) => Close();
            content.Children.Add(close);
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = content
            };
            Loaded += (_, _) =>
            {
                if (!enableAnimations || !SystemParameters.ClientAreaAnimation) return;
                content.Opacity = 0;
                content.RenderTransformOrigin = new Point(0.5, 0.5);
                var transform = new TransformGroup();
                var scale = new ScaleTransform(0.94, 0.94);
                var translate = new TranslateTransform(0, 10);
                transform.Children.Add(scale);
                transform.Children.Add(translate);
                content.RenderTransform = transform;
                var ease = new CubicEase { EasingMode = EasingMode.EaseOut };
                var entrance = TimeSpan.FromMilliseconds(550);
                content.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, entrance) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.94, 1, entrance) { EasingFunction = ease });
                scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.94, 1, entrance) { EasingFunction = ease });
                translate.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(10, 0, entrance) { EasingFunction = ease });

                var reveal = new EllipseGeometry(new Point(125, 125), 0, 0);
                qrImage.Clip = reveal;
                var qrEase = new CircleEase { EasingMode = EasingMode.EaseOut };
                reveal.BeginAnimation(EllipseGeometry.RadiusXProperty, new DoubleAnimation(0, 180, TimeSpan.FromMilliseconds(720)) { EasingFunction = qrEase });
                reveal.BeginAnimation(EllipseGeometry.RadiusYProperty, new DoubleAnimation(0, 180, TimeSpan.FromMilliseconds(720)) { EasingFunction = qrEase });
            };
        }
    }
}

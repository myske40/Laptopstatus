using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace LaptopStatus
{
    internal sealed class PowerScheduleNotificationWindow : Window
    {
        private readonly DispatcherTimer _dismissTimer = new() { Interval = TimeSpan.FromSeconds(8) };

        public PowerScheduleNotificationWindow(string title, string message)
        {
            Title = "Laptop Status";
            Width = 390;
            Height = 142;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ShowInTaskbar = false;
            ShowActivated = false;
            Topmost = true;
            Left = SystemParameters.WorkArea.Right - Width - 20;
            Top = SystemParameters.WorkArea.Bottom - Height - 20;

            var closeButton = new Button
            {
                Content = "×",
                Width = 28,
                Height = 28,
                Padding = new Thickness(0),
                FontSize = 20,
                Foreground = new SolidColorBrush(Color.FromRgb(172, 187, 202)),
                Background = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                VerticalAlignment = VerticalAlignment.Top
            };
            closeButton.Click += (_, _) => Close();

            var heading = new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            };
            var header = new DockPanel { LastChildFill = true };
            DockPanel.SetDock(closeButton, Dock.Right);
            header.Children.Add(closeButton);
            header.Children.Add(heading);

            var detail = new TextBlock
            {
                Text = message,
                FontSize = 13,
                TextWrapping = TextWrapping.Wrap,
                Foreground = new SolidColorBrush(Color.FromRgb(194, 207, 220)),
                Margin = new Thickness(0, 9, 0, 0)
            };

            var content = new StackPanel();
            content.Children.Add(header);
            content.Children.Add(detail);

            Content = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(17, 26, 37)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(57, 91, 91)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(15),
                Padding = new Thickness(18, 14, 14, 14),
                Child = content,
                Effect = new System.Windows.Media.Effects.DropShadowEffect
                {
                    Color = Colors.Black,
                    Opacity = 0.32,
                    BlurRadius = 18,
                    ShadowDepth = 4
                }
            };

            _dismissTimer.Tick += (_, _) => Close();
            Closed += (_, _) => _dismissTimer.Stop();
        }

        public void StartDismissTimer() => _dismissTimer.Start();
    }
}

using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Threading;
using System.Windows.Media.Animation;

namespace LaptopStatus
{
    internal sealed class UserLockWindow : Window
    {
        private readonly PasswordBox _password;
        private readonly TextBlock _error;
        private readonly Func<string, bool> _verify;
        private readonly Action _unlocked;
        private bool _authorized;

        public UserLockWindow(Func<string, bool> verify, Action unlocked, string title = "LaptopStatus dikunci", string prompt = "Masukkan password akses layar yang tersimpan di Pengaturan LaptopStatus.")
        {
            _verify = verify;
            _unlocked = unlocked;
            WindowStyle = WindowStyle.None; ResizeMode = ResizeMode.NoResize; ShowInTaskbar = false;
            WindowState = WindowState.Normal;
            Left = SystemParameters.VirtualScreenLeft; Top = SystemParameters.VirtualScreenTop;
            Width = SystemParameters.VirtualScreenWidth; Height = SystemParameters.VirtualScreenHeight;
            Topmost = true; Background = new SolidColorBrush(Color.FromRgb(12, 18, 27));
            _password = new PasswordBox { FontSize = 18, Padding = new Thickness(12), Width = 300, Margin = new Thickness(0, 18, 0, 8), HorizontalAlignment = HorizontalAlignment.Center };
            _error = new TextBlock { Foreground = Brushes.IndianRed, FontSize = 13, TextAlignment = TextAlignment.Center, Margin = new Thickness(0, 0, 0, 12) };
            var button = new Button { Content = "Buka kunci", FontSize = 15, Padding = new Thickness(18, 9, 18, 9), HorizontalAlignment = HorizontalAlignment.Center, IsDefault = true };
            button.Click += (_, _) => TryUnlock();
            _password.KeyDown += (_, e) => { if (e.Key == Key.Enter) { TryUnlock(); e.Handled = true; } };
            var panel = new StackPanel { Width = 420, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center };
            panel.Children.Add(new TextBlock { Text = title, Foreground = Brushes.White, FontSize = 28, FontWeight = FontWeights.SemiBold, TextAlignment = TextAlignment.Center });
            panel.Children.Add(new TextBlock { Text = prompt, Foreground = Brushes.LightGray, FontSize = 14, TextWrapping = TextWrapping.Wrap, TextAlignment = TextAlignment.Center, Margin = new Thickness(8, 12, 8, 0) });
            panel.Children.Add(_password); panel.Children.Add(_error); panel.Children.Add(button);
            Content = panel;
            Opacity = 0;
            panel.RenderTransformOrigin = new Point(.5, .5);
            panel.RenderTransform = new ScaleTransform(.96, .96);
            Loaded += (_, _) =>
            {
                var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(240)); BeginAnimation(OpacityProperty, fade);
                ((ScaleTransform)panel.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.96, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
                ((ScaleTransform)panel.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.96, 1, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            };
            ContentRendered += (_, _) => { Activate(); _password.Focus(); };
            Deactivated += (_, _) => Dispatcher.BeginInvoke(new Action(() => { if (!_authorized && IsVisible) { Topmost = true; Activate(); } }), DispatcherPriority.ApplicationIdle);
        }

        protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
        {
            if (!_authorized) { e.Cancel = true; return; }
            base.OnClosing(e);
        }

        private void TryUnlock()
        {
            if (!_verify(_password.Password)) { _error.Text = "Password salah. Coba lagi."; _password.Clear(); _password.Focus(); return; }
            _authorized = true;
            _unlocked();
            var fade = new DoubleAnimation(0, TimeSpan.FromMilliseconds(190));
            fade.Completed += (_, _) => Close();
            BeginAnimation(OpacityProperty, fade);
        }

        public void AuthorizeAndClose()
        {
            _authorized = true;
            Close();
        }
    }
}

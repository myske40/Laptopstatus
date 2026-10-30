using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace LaptopStatus
{
    internal sealed class PowerCancellationPasswordDialog : Window
    {
        private static readonly Color Accent = Color.FromRgb(111, 227, 193);
        private static readonly Color Blue = Color.FromRgb(67, 184, 255);
        private static readonly Color Green = Color.FromRgb(74, 222, 128);
        private static readonly Color Red = Color.FromRgb(255, 92, 112);
        private static readonly string[] WaitingMessages = { "Menghubungi laptop…", "Memeriksa password…", "Mengonfirmasi akses…" };
        private readonly Func<string, bool> _verifyPassword;
        private readonly Action _resumeCountdown;
        private readonly Action _pauseCountdown;
        private readonly bool _passwordConfigured;
        private readonly bool _animationsEnabled;
        private readonly Border _chrome;
        private readonly StackPanel _formPane;
        private readonly StackPanel _statusPane;
        private readonly PasswordBox _passwordBox;
        private readonly TextBox _visiblePassword;
        private readonly Border _passwordField;
        private readonly TextBlock _hint;
        private readonly TextBlock _statusTitle;
        private readonly TextBlock _statusSubtitle;
        private readonly StackPanel _statusActions;
        private readonly Grid _progressTrack;
        private readonly Border _progressFill;
        private readonly TranslateTransform _shimmerTransform = new(-110, 0);
        private readonly StageParts _stage;
        private readonly DispatcherTimer _messageTimer;
        private readonly DispatcherTimer _autoCloseTimer;
        private readonly double _idleHeight;
        private readonly double _verifyingHeight;
        private int _messageIndex;
        private int _runId;
        private int _paneTransitionGeneration;
        private bool _isVerifying;
        private string _state = "idle";

        private sealed record StageParts(
            Canvas Root, Canvas Arc, Canvas Dots, Canvas Orbit, Ellipse Glow, Ellipse FinalRing,
            Ellipse RippleOne, Ellipse RippleTwo, Canvas Logo, ScaleTransform LogoScale,
            TranslateTransform LogoFloat, Path Screen, Path Base, Path Beat, Rectangle Scan,
            Path Check, Path CrossOne, Path CrossTwo);

        public bool PasswordVerified { get; private set; }
        public bool PasswordRejected { get; private set; }

        public PowerCancellationPasswordDialog(Window owner, bool passwordConfigured, Func<string, bool> verifyPassword,
            bool animationsEnabled, Action resumeCountdown, Action pauseCountdown)
        {
            Owner = owner;
            _passwordConfigured = passwordConfigured;
            _verifyPassword = verifyPassword;
            _resumeCountdown = resumeCountdown;
            _pauseCountdown = pauseCountdown;
            _animationsEnabled = animationsEnabled;
            Title = "Konfirmasi pembatalan";
            var workArea = SystemParameters.WorkArea;
            var availableWidth = Math.Max(320, workArea.Width - 32);
            var availableHeight = Math.Max(300, workArea.Height - 32);
            Width = Math.Min(440, availableWidth);
            MinWidth = Math.Min(380, Width);
            MaxWidth = Width;
            _idleHeight = Math.Min(382, availableHeight);
            _verifyingHeight = Math.Min(490, Math.Max(_idleHeight, availableHeight));
            Height = _idleHeight;
            MinHeight = Math.Min(300, _idleHeight);
            MaxHeight = availableHeight;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            Topmost = true;
            ShowInTaskbar = false;
            Focusable = true;

            _passwordBox = new PasswordBox
            {
                MinHeight = 54, Padding = new Thickness(18, 7, 4, 7), FontSize = 17,
                FontWeight = FontWeights.SemiBold, Foreground = Brush("#EEF4FA"), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center, IsEnabled = passwordConfigured
            };
            _visiblePassword = new TextBox
            {
                MinHeight = 54, Padding = new Thickness(18, 7, 4, 7), FontSize = 17,
                FontWeight = FontWeights.SemiBold, Foreground = Brush("#EEF4FA"), Background = Brushes.Transparent,
                BorderThickness = new Thickness(0), VerticalContentAlignment = VerticalAlignment.Center,
                Visibility = Visibility.Collapsed, IsEnabled = passwordConfigured
            };
            var passwordGrid = new Grid();
            passwordGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            passwordGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(44) });
            passwordGrid.Children.Add(_passwordBox);
            passwordGrid.Children.Add(_visiblePassword);
            var eye = new Button
            {
                Content = EyeIcon(), Width = 38, Height = 38, Margin = new Thickness(0, 0, 7, 0),
                HorizontalAlignment = HorizontalAlignment.Right, VerticalAlignment = VerticalAlignment.Center,
                Background = Brushes.Transparent, BorderBrush = Brushes.Transparent, Foreground = Brush("#A7B5C5"),
                IsEnabled = passwordConfigured, ToolTip = "Tampilkan password", Focusable = false
            };
            Grid.SetColumn(eye, 1);
            passwordGrid.Children.Add(eye);
            _passwordField = new Border
            {
                MinHeight = 58, Margin = new Thickness(0, 22, 0, 0), CornerRadius = new CornerRadius(18),
                Background = Brush("#070E14"), BorderBrush = new SolidColorBrush(Color.FromArgb(150, Accent.R, Accent.G, Accent.B)),
                BorderThickness = new Thickness(2), Child = passwordGrid, RenderTransform = new TranslateTransform()
            };
            eye.Click += (_, _) => TogglePassword(eye);
            eye.Template = ButtonTemplate();
            _passwordBox.GotFocus += (_, _) => SetFieldFocus(true);
            _passwordBox.LostFocus += (_, _) => SetFieldFocus(false);
            _visiblePassword.GotFocus += (_, _) => SetFieldFocus(true);
            _visiblePassword.LostFocus += (_, _) => SetFieldFocus(false);
            _passwordBox.PasswordChanged += (_, _) => ClearHint();
            _visiblePassword.TextChanged += (_, _) => ClearHint();

            _hint = new TextBlock { MinHeight = 20, Margin = new Thickness(1, 8, 1, 0), Foreground = Brush("#A7B5C5"), FontSize = 13, TextWrapping = TextWrapping.Wrap };
            var continueButton = MakeButton("Lanjutkan countdown", false, ContinueCountdown, 184);
            var verifyButton = MakeButton("Verifikasi", true, () => _ = VerifyAsync(), 112);
            verifyButton.IsDefault = true;
            verifyButton.IsEnabled = passwordConfigured;

            _formPane = new StackPanel { VerticalAlignment = VerticalAlignment.Center, RenderTransform = new TranslateTransform() };
            _formPane.Children.Add(new TextBlock
            {
                Text = "Batalkan perintah daya?", Foreground = Brushes.White, FontSize = 24,
                FontWeight = FontWeights.Bold, Margin = new Thickness(0, 0, 0, 8)
            });
            _formPane.Children.Add(new TextBlock
            {
                Text = passwordConfigured
                    ? "Masukkan password akses layar LaptopStatus. Countdown dijeda selama verifikasi."
                    : "Password akses layar belum diaktifkan. Atur password minimal 3 karakter di Pengaturan Windows terlebih dahulu.",
                Foreground = Brush("#A7B5C5"), FontSize = 14, LineHeight = 21, TextWrapping = TextWrapping.Wrap
            });
            _formPane.Children.Add(_passwordField);
            _formPane.Children.Add(_hint);
            var formActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Margin = new Thickness(0, 18, 0, 0) };
            formActions.Children.Add(continueButton);
            verifyButton.Margin = new Thickness(12, 0, 0, 0);
            formActions.Children.Add(verifyButton);
            _formPane.Children.Add(formActions);

            _stage = BuildStage();
            _statusTitle = new TextBlock { Foreground = Brushes.White, FontSize = 23, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, TextAlignment = TextAlignment.Center };
            _statusSubtitle = new TextBlock
            {
                Foreground = Brush("#A7B5C5"), FontSize = 14, LineHeight = 21, TextWrapping = TextWrapping.Wrap,
                TextAlignment = TextAlignment.Center, HorizontalAlignment = HorizontalAlignment.Center, MaxWidth = 340,
                Margin = new Thickness(0, 8, 0, 0), RenderTransform = new TranslateTransform()
            };
            _progressTrack = new Grid { Width = 270, Height = 4, ClipToBounds = true, Background = Brush("#1AFFFFFF"), Margin = new Thickness(0, 18, 0, 0), Opacity = 0 };
            _progressFill = new Border { HorizontalAlignment = HorizontalAlignment.Left, Background = new SolidColorBrush(Green), RenderTransformOrigin = new Point(0, .5), RenderTransform = new ScaleTransform(1, 1), Opacity = 0 };
            var shimmer = new Border
            {
                Width = 110, HorizontalAlignment = HorizontalAlignment.Left,
                Background = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, Accent.R, Accent.G, Accent.B), 0),
                    new GradientStop(Accent, .5),
                    new GradientStop(Color.FromArgb(0, Blue.R, Blue.G, Blue.B), 1)
                }), RenderTransform = _shimmerTransform
            };
            _progressTrack.Children.Add(_progressFill);
            _progressTrack.Children.Add(shimmer);
            _statusActions = new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 20, 0, 0) };
            _statusPane = new StackPanel
            {
                Visibility = Visibility.Collapsed, Opacity = 0, HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center, RenderTransform = new TranslateTransform(0, 10)
            };
            _statusPane.Children.Add(new Viewbox { Width = 176, Height = 176, Stretch = Stretch.Uniform, Margin = new Thickness(0, 0, 0, 12), Child = _stage.Root });
            _statusPane.Children.Add(_statusTitle);
            _statusPane.Children.Add(_statusSubtitle);
            _statusPane.Children.Add(_progressTrack);
            _statusPane.Children.Add(_statusActions);

            var panes = new Grid();
            panes.Children.Add(_formPane);
            panes.Children.Add(_statusPane);
            _chrome = new Border
            {
                Background = new LinearGradientBrush(Color.FromRgb(20, 30, 41), Color.FromRgb(16, 25, 34), 90),
                BorderBrush = Brush("#25313E"), BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(28),
                Padding = new Thickness(28), Child = panes,
                Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 36, ShadowDepth = 12, Opacity = .5, Color = Colors.Black }
            };
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = _chrome
            };

            _messageTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1050) };
            _messageTimer.Tick += (_, _) =>
            {
                _messageIndex = Math.Min(_messageIndex + 1, WaitingMessages.Length - 1);
                SwapSubtitle(WaitingMessages[_messageIndex]);
            };
            _autoCloseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(3100) };
            _autoCloseTimer.Tick += (_, _) => { _autoCloseTimer.Stop(); CloseSuccess(); };

            PreviewKeyDown += OnPreviewKeyDown;
            Loaded += (_, _) =>
            {
                Activate();
                if (passwordConfigured) _passwordBox.Focus();
                else continueButton.Focus();
            };
            Closed += (_, _) =>
            {
                _messageTimer.Stop();
                _autoCloseTimer.Stop();
                StopStageAnimations();
            };
        }

        public void CloseForCountdownCompleted()
        {
            _messageTimer.Stop();
            _autoCloseTimer.Stop();
            Close();
        }

        private async Task VerifyAsync()
        {
            if (_isVerifying || !_passwordConfigured) return;
            var password = _visiblePassword.Visibility == Visibility.Visible ? _visiblePassword.Text : _passwordBox.Password;
            if (string.IsNullOrEmpty(password)) { ShowInvalid("Masukkan password dulu."); return; }

            ClearHint();
            _pauseCountdown();
            ResetStageVisuals();
            PasswordRejected = false;
            _isVerifying = true;
            var run = ++_runId;
            _messageIndex = 0;
            _statusTitle.Text = "Memverifikasi…";
            _statusTitle.Foreground = Brushes.White;
            _statusSubtitle.Text = WaitingMessages[0];
            SetActions(MakeButton("Batal", false, CancelVerification, 100));
            ShowStatusPane();
            StartVerificationAnimations();
            _messageTimer.Start();

            var started = Environment.TickCount64;
            bool valid;
            try { valid = await Task.Run(() => _verifyPassword(password)); }
            catch { valid = false; }
            var rest = 2000 - (Environment.TickCount64 - started);
            if (rest > 0) await Task.Delay((int)rest);
            if (run != _runId) return;

            _messageTimer.Stop();
            _isVerifying = false;
            _passwordBox.Clear();
            _visiblePassword.Clear();
            if (valid)
            {
                PasswordVerified = true;
                ShowSuccess();
            }
            else
            {
                PasswordRejected = true;
                _resumeCountdown();
                ShowFailure();
            }
        }

        private void CancelVerification()
        {
            if (!_isVerifying) return;
            _isVerifying = false;
            ++_runId;
            _messageTimer.Stop();
            StopStageAnimations();
            _resumeCountdown();
            _hint.Text = "";
            ShowFormPane();
        }

        private void ShowSuccess()
        {
            _state = "success";
            _statusTitle.Text = "Verifikasi berhasil";
            _statusTitle.Foreground = Brush("#BBF7D0");
            _statusSubtitle.Text = "Perintah daya dibatalkan. Countdown dihentikan.";
            SetActions(MakeButton("Tutup", true, CloseSuccess, 104));
            SetProgressMode("success");
            ShowFinalState(true);
            _autoCloseTimer.Start();
        }

        private void ShowFailure()
        {
            _state = "error";
            _statusTitle.Text = "Password salah";
            _statusTitle.Foreground = Brush("#FFB3BD");
            _statusSubtitle.Text = "Periksa lagi password akses layar. Hitung mundur dilanjutkan.";
            var retry = MakeButton("Coba lagi", true, Retry, 126);
            var resume = MakeButton("Lanjutkan countdown", false, ContinueCountdown, 176);
            retry.Margin = new Thickness(0, 0, 10, 0);
            SetActions(retry, resume);
            SetProgressMode("error");
            ShowFinalState(false);
            ShakeDialog();
            FlashDialog(Red);
        }

        private void Retry()
        {
            if (_state != "error") return;
            _pauseCountdown();
            PasswordRejected = false;
            StopStageAnimations();
            _hint.Text = "";
            ShowFormPane();
            _passwordBox.Clear();
            _visiblePassword.Clear();
            _passwordBox.Focus();
        }

        private void ContinueCountdown()
        {
            if (_state == "success") return;
            if (_isVerifying) CancelVerification();
            _resumeCountdown();
            DialogResult = false;
        }

        private void CloseSuccess()
        {
            if (PasswordVerified) DialogResult = true;
        }

        private void ShowStatusPane()
        {
            var transition = ++_paneTransitionGeneration;
            _state = "verifying";
            _chrome.BorderBrush = new SolidColorBrush(Color.FromArgb(180, Accent.R, Accent.G, Accent.B));
            _statusPane.Visibility = Visibility.Visible;
            if (!_animationsEnabled)
            {
                _formPane.Visibility = Visibility.Collapsed;
                _statusPane.Opacity = 1;
                Height = _verifyingHeight;
                return;
            }
            _statusPane.Opacity = 0;
            ((TranslateTransform)_statusPane.RenderTransform).Y = 10;
            var formOffset = (TranslateTransform)_formPane.RenderTransform;
            _formPane.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(280)));
            formOffset.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -10, TimeSpan.FromMilliseconds(360)));
            _statusPane.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(350)) { BeginTime = TimeSpan.FromMilliseconds(50) });
            ((TranslateTransform)_statusPane.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(450)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            AnimateHeight(_verifyingHeight);
            HidePaneAfter(_formPane, 380, transition);
        }

        private void ShowFormPane()
        {
            var transition = ++_paneTransitionGeneration;
            _state = "idle";
            _chrome.BorderBrush = Brush("#25313E");
            SetProgressMode("off");
            if (!_animationsEnabled)
            {
                _statusPane.Visibility = Visibility.Collapsed;
                _formPane.Visibility = Visibility.Visible;
                _formPane.Opacity = 1;
                Height = _idleHeight;
                return;
            }
            _formPane.Visibility = Visibility.Visible;
            _formPane.Opacity = 0;
            ((TranslateTransform)_formPane.RenderTransform).Y = -10;
            _statusPane.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180)));
            ((TranslateTransform)_statusPane.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, 10, TimeSpan.FromMilliseconds(220)));
            _formPane.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(280)) { BeginTime = TimeSpan.FromMilliseconds(35) });
            ((TranslateTransform)_formPane.RenderTransform).BeginAnimation(TranslateTransform.YProperty,
                new DoubleAnimation(-10, 0, TimeSpan.FromMilliseconds(380)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
            AnimateHeight(_idleHeight);
            HidePaneAfter(_statusPane, 320, transition);
        }

        private void HidePaneAfter(UIElement pane, int milliseconds, int transition)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(milliseconds) };
            timer.Tick += (_, _) =>
            {
                timer.Stop();
                if (transition != _paneTransitionGeneration) return;
                pane.Visibility = Visibility.Collapsed;
                pane.Opacity = 1;
            };
            timer.Start();
        }

        private void ResetStageVisuals()
        {
            StopStageAnimations();
            _stage.Glow.BeginAnimation(OpacityProperty, null);
            _stage.Glow.Opacity = 0;
            _stage.FinalRing.BeginAnimation(OpacityProperty, null);
            _stage.FinalRing.Opacity = 0;
            _stage.FinalRing.RenderTransform = new ScaleTransform(1, 1);
            foreach (var ripple in new[] { _stage.RippleOne, _stage.RippleTwo })
            {
                ripple.BeginAnimation(OpacityProperty, null);
                ripple.Opacity = 0;
                ripple.RenderTransform = Transform.Identity;
            }
            _stage.Logo.BeginAnimation(OpacityProperty, null);
            _stage.Logo.Opacity = 0;
            _stage.LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, null);
            _stage.LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, null);
            _stage.LogoScale.ScaleX = _stage.LogoScale.ScaleY = .78;
            _stage.LogoFloat.Y = 0;
            _stage.Screen.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _stage.Base.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _stage.Screen.StrokeDashOffset = _stage.Base.StrokeDashOffset = 0;
            _stage.Scan.Opacity = 0;
            _stage.Check.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _stage.CrossOne.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _stage.CrossTwo.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _stage.Check.Opacity = _stage.CrossOne.Opacity = _stage.CrossTwo.Opacity = 0;
            _stage.Check.StrokeDashOffset = _stage.CrossOne.StrokeDashOffset = _stage.CrossTwo.StrokeDashOffset = 0;
        }

        private void AnimateHeight(double target)
        {
            target = Math.Min(target, MaxHeight);
            if (!_animationsEnabled) { Height = target; return; }
            BeginAnimation(HeightProperty, new DoubleAnimation(Height, target, TimeSpan.FromMilliseconds(430))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void StartVerificationAnimations()
        {
            if (!_animationsEnabled) { SetProgressMode("verifying"); return; }
            _stage.Glow.Opacity = .55;
            Rotate(_stage.Arc, 360, 1.5);
            Rotate(_stage.Dots, -360, 14);
            Rotate(_stage.Orbit, 360, 3.4);
            _stage.Logo.Opacity = 1;
            _stage.LogoScale.ScaleX = _stage.LogoScale.ScaleY = .78;
            _stage.LogoScale.BeginAnimation(ScaleTransform.ScaleXProperty, Ease(.78, 1, 700));
            _stage.LogoScale.BeginAnimation(ScaleTransform.ScaleYProperty, Ease(.78, 1, 700));
            _stage.LogoFloat.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, -3.5, TimeSpan.FromMilliseconds(1300))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, BeginTime = TimeSpan.FromMilliseconds(700),
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
            Draw(_stage.Screen, 1000, 0);
            Draw(_stage.Base, 800, 250);
            _stage.Beat.StrokeDashArray = new DoubleCollection { 34, 66 };
            _stage.Beat.StrokeDashOffset = 34;
            _stage.Beat.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(34, -66, TimeSpan.FromSeconds(1.5)) { RepeatBehavior = RepeatBehavior.Forever });
            _stage.Scan.Opacity = 1;
            _stage.Scan.RenderTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-4, 112, TimeSpan.FromSeconds(1.9))
            {
                AutoReverse = true, RepeatBehavior = RepeatBehavior.Forever, EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
            });
            SetProgressMode("verifying");
        }

        private void ShowFinalState(bool success)
        {
            StopStageAnimations();
            var tone = success ? Green : Red;
            _chrome.BorderBrush = new SolidColorBrush(Color.FromArgb(190, tone.R, tone.G, tone.B));
            _stage.Glow.Fill = new RadialGradientBrush(new GradientStopCollection
            {
                new GradientStop(Color.FromArgb(110, tone.R, tone.G, tone.B), 0),
                new GradientStop(Color.FromArgb(0, tone.R, tone.G, tone.B), 1)
            });
            _stage.Glow.Opacity = 0;
            _stage.Glow.BeginAnimation(OpacityProperty, new DoubleAnimation(0, .55, TimeSpan.FromMilliseconds(700)));
            _stage.Logo.BeginAnimation(OpacityProperty, new DoubleAnimation(_stage.Logo.Opacity, 0, TimeSpan.FromMilliseconds(350)));
            _stage.FinalRing.Stroke = new SolidColorBrush(tone);
            _stage.FinalRing.Opacity = 0;
            _stage.FinalRing.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(550)));
            _stage.FinalRing.RenderTransform = new ScaleTransform(.72, .72);
            ((ScaleTransform)_stage.FinalRing.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, Ease(.72, 1, 800));
            ((ScaleTransform)_stage.FinalRing.RenderTransform).BeginAnimation(ScaleTransform.ScaleYProperty, Ease(.72, 1, 800));
            if (success)
            {
                _stage.RippleOne.Stroke = new SolidColorBrush(Green);
                _stage.RippleTwo.Stroke = new SolidColorBrush(Accent);
                Ripple(_stage.RippleOne, 550);
                Ripple(_stage.RippleTwo, 800);
                _stage.Check.Stroke = new SolidColorBrush(Green);
                Draw(_stage.Check, 550, 450);
                EmitParticles();
                FlashDialog(Green);
            }
            else
            {
                _stage.CrossOne.Stroke = _stage.CrossTwo.Stroke = new SolidColorBrush(Red);
                Draw(_stage.CrossOne, 400, 450);
                Draw(_stage.CrossTwo, 400, 620);
                FlashDialog(Red);
            }
        }

        private void SetProgressMode(string mode)
        {
            var shimmer = (Border)_progressTrack.Children[1];
            _shimmerTransform.BeginAnimation(TranslateTransform.XProperty, null);
            shimmer.Opacity = 0;
            _progressFill.BeginAnimation(OpacityProperty, null);
            _progressFill.Width = 0;
            _progressFill.Opacity = 0;
            _progressFill.Background = new SolidColorBrush(mode == "error" ? Red : Green);
            if (mode == "off") { _progressTrack.Opacity = 0; return; }
            _progressTrack.Opacity = 1;
            if (mode == "verifying")
            {
                shimmer.Opacity = 1;
                if (_animationsEnabled)
                    _shimmerTransform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(-110, 270, TimeSpan.FromSeconds(1.3)) { RepeatBehavior = RepeatBehavior.Forever });
            }
            else if (mode == "success")
            {
                _progressFill.Opacity = 1;
                _progressFill.Width = 270;
                _progressFill.RenderTransform = new ScaleTransform(1, 1);
                if (_animationsEnabled)
                    ((ScaleTransform)_progressFill.RenderTransform).BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(2400)));
            }
            else { _progressFill.Opacity = 1; _progressFill.Width = 270; }
        }

        private void SwapSubtitle(string text)
        {
            _statusSubtitle.Text = text;
            if (!_animationsEnabled) return;
            _statusSubtitle.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(320)));
            ((TranslateTransform)_statusSubtitle.RenderTransform).BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(5, 0, TimeSpan.FromMilliseconds(320)));
        }

        private void ShowInvalid(string message)
        {
            _hint.Text = message;
            _hint.Foreground = Brush("#FF8C9A");
            _passwordField.BorderBrush = new SolidColorBrush(Red);
            if (_animationsEnabled)
            {
                var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(460) };
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(-9, KeyTime.FromPercent(.16)));
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(8, KeyTime.FromPercent(.34)));
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromPercent(.52)));
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(4, KeyTime.FromPercent(.72)));
                animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
                ((TranslateTransform)_passwordField.RenderTransform).BeginAnimation(TranslateTransform.XProperty, animation);
            }
            if (_visiblePassword.Visibility == Visibility.Visible) _visiblePassword.Focus(); else _passwordBox.Focus();
        }

        private void ClearHint()
        {
            if (_hint is null) return;
            _hint.Text = "";
            _hint.Foreground = Brush("#A7B5C5");
            _passwordField.BorderBrush = new SolidColorBrush(Color.FromArgb(150, Accent.R, Accent.G, Accent.B));
        }

        private void SetFieldFocus(bool focused)
        {
            _passwordField.BorderBrush = focused ? new SolidColorBrush(Accent) : new SolidColorBrush(Color.FromArgb(150, Accent.R, Accent.G, Accent.B));
            _passwordField.Effect = focused
                ? new System.Windows.Media.Effects.DropShadowEffect { Color = Accent, BlurRadius = 18, ShadowDepth = 0, Opacity = .28 }
                : null;
        }

        private void TogglePassword(Button eye)
        {
            if (_visiblePassword.Visibility == Visibility.Visible)
            {
                _passwordBox.Password = _visiblePassword.Text;
                _visiblePassword.Visibility = Visibility.Collapsed;
                _passwordBox.Visibility = Visibility.Visible;
                eye.ToolTip = "Tampilkan password";
                _passwordBox.Focus();
            }
            else
            {
                _visiblePassword.Text = _passwordBox.Password;
                _passwordBox.Visibility = Visibility.Collapsed;
                _visiblePassword.Visibility = Visibility.Visible;
                eye.ToolTip = "Sembunyikan password";
                _visiblePassword.Focus();
                _visiblePassword.CaretIndex = _visiblePassword.Text.Length;
            }
        }

        private void OnPreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            e.Handled = true;
            if (_isVerifying) CancelVerification();
            else if (_state == "success") CloseSuccess();
            else ContinueCountdown();
        }

        private void SetActions(params Button[] buttons)
        {
            _statusActions.Children.Clear();
            foreach (var button in buttons) _statusActions.Children.Add(button);
        }

        private void StopStageAnimations()
        {
            _stage.Arc.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            _stage.Dots.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            _stage.Orbit.RenderTransform.BeginAnimation(RotateTransform.AngleProperty, null);
            _stage.Beat.BeginAnimation(Shape.StrokeDashOffsetProperty, null);
            _stage.Scan.RenderTransform.BeginAnimation(TranslateTransform.XProperty, null);
            _stage.LogoFloat.BeginAnimation(TranslateTransform.YProperty, null);
            _shimmerTransform.BeginAnimation(TranslateTransform.XProperty, null);
        }

        private void Rotate(Canvas canvas, double degrees, double seconds) =>
            canvas.RenderTransform.BeginAnimation(RotateTransform.AngleProperty,
                new DoubleAnimation(0, degrees, TimeSpan.FromSeconds(seconds)) { RepeatBehavior = RepeatBehavior.Forever });

        private void Draw(Shape shape, int duration, int delay)
        {
            shape.Opacity = 1;
            if (!_animationsEnabled) return;
            shape.StrokeDashArray = new DoubleCollection { 100, 100 };
            shape.StrokeDashOffset = 100;
            shape.BeginAnimation(Shape.StrokeDashOffsetProperty, new DoubleAnimation(100, 0, TimeSpan.FromMilliseconds(duration))
            {
                BeginTime = TimeSpan.FromMilliseconds(delay), EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void Ripple(Ellipse shape, int delay)
        {
            if (!_animationsEnabled) { shape.Opacity = 0; return; }
            var scale = new ScaleTransform(.5, .5);
            shape.RenderTransform = scale;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(.5, 1.3, TimeSpan.FromMilliseconds(1100)) { BeginTime = TimeSpan.FromMilliseconds(delay) });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(.5, 1.3, TimeSpan.FromMilliseconds(1100)) { BeginTime = TimeSpan.FromMilliseconds(delay) });
            shape.BeginAnimation(OpacityProperty, new DoubleAnimation(.6, 0, TimeSpan.FromMilliseconds(1100)) { BeginTime = TimeSpan.FromMilliseconds(delay) });
        }

        private static DoubleAnimation Ease(double from, double to, int milliseconds) => new(from, to, TimeSpan.FromMilliseconds(milliseconds))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        private void ShakeDialog()
        {
            if (!_animationsEnabled) return;
            var transform = new TranslateTransform();
            _chrome.RenderTransform = transform;
            var animation = new DoubleAnimationUsingKeyFrames { Duration = TimeSpan.FromMilliseconds(460) };
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(-9, KeyTime.FromPercent(.16)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(8, KeyTime.FromPercent(.34)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(-6, KeyTime.FromPercent(.52)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(4, KeyTime.FromPercent(.72)));
            animation.KeyFrames.Add(new EasingDoubleKeyFrame(0, KeyTime.FromPercent(1)));
            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void FlashDialog(Color color)
        {
            if (!_animationsEnabled) return;
            var brush = new SolidColorBrush(color);
            _chrome.BorderBrush = brush;
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(Color.FromArgb(150, color.R, color.G, color.B), Color.FromRgb(37, 49, 62), TimeSpan.FromMilliseconds(1100))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            });
        }

        private void EmitParticles()
        {
            if (!_animationsEnabled) return;
            var random = new Random();
            var colors = new[] { Green, Accent, Blue, Colors.White, Color.FromRgb(167, 243, 208) };
            for (var i = 0; i < 28; i++)
            {
                var size = 4 + random.NextDouble() * 5;
                Shape particle = i % 3 == 0 ? new Rectangle { Width = size, Height = size } : new Ellipse { Width = size, Height = size };
                particle.Fill = new SolidColorBrush(colors[i % colors.Length]);
                var transform = new TranslateTransform();
                particle.RenderTransform = transform;
                Canvas.SetLeft(particle, 100 - size / 2);
                Canvas.SetTop(particle, 100 - size / 2);
                _stage.Root.Children.Add(particle);
                var angle = random.NextDouble() * Math.PI * 2;
                var distance = 70 + random.NextDouble() * 90;
                var duration = TimeSpan.FromMilliseconds(900 + random.NextDouble() * 500);
                transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(0, Math.Cos(angle) * distance, duration));
                transform.BeginAnimation(TranslateTransform.YProperty, new DoubleAnimation(0, Math.Sin(angle) * distance + 46, duration));
                particle.BeginAnimation(OpacityProperty, new DoubleAnimation(1, 0, duration));
                var cleanup = new DispatcherTimer { Interval = duration };
                cleanup.Tick += (_, _) => { cleanup.Stop(); _stage.Root.Children.Remove(particle); };
                cleanup.Start();
            }
        }

        private StageParts BuildStage()
        {
            var root = new Canvas { Width = 200, Height = 200, IsHitTestVisible = false };
            var glow = new Ellipse
            {
                Width = 184, Height = 184, Opacity = 0,
                Fill = new RadialGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(90, Accent.R, Accent.G, Accent.B), 0),
                    new GradientStop(Color.FromArgb(0, Accent.R, Accent.G, Accent.B), 1)
                })
            };
            Canvas.SetLeft(glow, 8); Canvas.SetTop(glow, 8); root.Children.Add(glow);
            root.Children.Add(Ring(172, 14, Brush("#12FFFFFF"), 3));
            var arc = RotatingLayer();
            var arcRing = Ring(172, 14, new LinearGradientBrush(new GradientStopCollection { new GradientStop(Accent, 0), new GradientStop(Blue, 1) }, 35), 4.5);
            arcRing.StrokeDashArray = new DoubleCollection { 24, 76 };
            arc.Children.Add(arcRing); root.Children.Add(arc);
            var dots = RotatingLayer();
            var dottedRing = Ring(148, 26, new SolidColorBrush(Color.FromArgb(140, Accent.R, Accent.G, Accent.B)), 2);
            dottedRing.StrokeDashArray = new DoubleCollection { .8, 2.2 };
            dots.Children.Add(dottedRing); root.Children.Add(dots);
            var orbit = RotatingLayer();
            AddDot(orbit, 8, new SolidColorBrush(Accent), 96, 10);
            AddDot(orbit, 5, new SolidColorBrush(Color.FromArgb(180, Accent.R, Accent.G, Accent.B)), 97.5, 184);
            AddDot(orbit, 4, new SolidColorBrush(Color.FromArgb(125, Accent.R, Accent.G, Accent.B)), 10, 98);
            root.Children.Add(orbit);

            var finalRing = Ring(172, 14, new SolidColorBrush(Green), 4.5);
            finalRing.Opacity = 0; finalRing.RenderTransformOrigin = new Point(.5, .5); root.Children.Add(finalRing);
            var rippleOne = Ring(140, 30, new SolidColorBrush(Green), 2);
            var rippleTwo = Ring(140, 30, new SolidColorBrush(Accent), 2);
            rippleOne.Opacity = rippleTwo.Opacity = 0;
            rippleOne.RenderTransformOrigin = rippleTwo.RenderTransformOrigin = new Point(.5, .5);
            root.Children.Add(rippleOne); root.Children.Add(rippleTwo);

            var logo = new Canvas { Width = 200, Height = 200, Opacity = 0, RenderTransformOrigin = new Point(.5, .5) };
            var logoScale = new ScaleTransform(.78, .78);
            var logoFloat = new TranslateTransform();
            logo.RenderTransform = new TransformGroup { Children = new TransformCollection { logoScale, logoFloat } };
            var screen = PathShape(Geometry.Parse("M 70,66 H 130 A 8,8 0 0 1 138,74 V 108 A 8,8 0 0 1 130,116 H 70 A 8,8 0 0 1 62,108 V 74 A 8,8 0 0 1 70,66 Z"), new SolidColorBrush(Accent), 3.5);
            screen.Fill = new SolidColorBrush(Color.FromArgb(15, Accent.R, Accent.G, Accent.B));
            screen.StrokeDashArray = new DoubleCollection { 100, 100 };
            var basePath = PathShape(Geometry.Parse("M 52,125 H 148 L 142,132 H 58 Z"), new SolidColorBrush(Accent), 3.5);
            basePath.StrokeDashArray = new DoubleCollection { 100, 100 };
            var scan = new Rectangle
            {
                Width = 30, Height = 50, Opacity = 0, Clip = Geometry.Parse("M 62,66 H 138 V 116 H 62 Z"),
                Fill = new LinearGradientBrush(new GradientStopCollection
                {
                    new GradientStop(Color.FromArgb(0, Accent.R, Accent.G, Accent.B), 0),
                    new GradientStop(Color.FromArgb(155, Accent.R, Accent.G, Accent.B), .55),
                    new GradientStop(Color.FromArgb(0, Accent.R, Accent.G, Accent.B), 1)
                }, 0), RenderTransform = new TranslateTransform()
            };
            Canvas.SetLeft(scan, 32); Canvas.SetTop(scan, 66);
            var beatGeometry = Geometry.Parse("M 70,94 H 82 L 88,80 L 97,108 L 105,86 L 110,94 H 130");
            var beat = PathShape(beatGeometry, new SolidColorBrush(Accent), 3.5);
            beat.StrokeDashArray = new DoubleCollection { 34, 66 };
            beat.Effect = new System.Windows.Media.Effects.DropShadowEffect { Color = Accent, BlurRadius = 8, ShadowDepth = 0, Opacity = .75 };
            logo.Children.Add(screen); logo.Children.Add(scan); logo.Children.Add(basePath);
            logo.Children.Add(PathShape(beatGeometry, new SolidColorBrush(Color.FromArgb(65, Accent.R, Accent.G, Accent.B)), 3));
            logo.Children.Add(beat); root.Children.Add(logo);

            var check = PathShape(Geometry.Parse("M 68,102 L 92,126 L 134,78"), new SolidColorBrush(Green), 9);
            var crossOne = PathShape(Geometry.Parse("M 76,76 L 124,124"), new SolidColorBrush(Red), 9);
            var crossTwo = PathShape(Geometry.Parse("M 124,76 L 76,124"), new SolidColorBrush(Red), 9);
            check.Opacity = crossOne.Opacity = crossTwo.Opacity = 0;
            check.StrokeDashArray = crossOne.StrokeDashArray = crossTwo.StrokeDashArray = new DoubleCollection { 100, 100 };
            root.Children.Add(check); root.Children.Add(crossOne); root.Children.Add(crossTwo);
            return new StageParts(root, arc, dots, orbit, glow, finalRing, rippleOne, rippleTwo, logo, logoScale, logoFloat, screen, basePath, beat, scan, check, crossOne, crossTwo);
        }

        private static Canvas RotatingLayer() => new() { Width = 200, Height = 200, RenderTransformOrigin = new Point(.5, .5), RenderTransform = new RotateTransform() };

        private static Ellipse Ring(double size, double position, Brush stroke, double thickness)
        {
            var shape = new Ellipse { Width = size, Height = size, Stroke = stroke, StrokeThickness = thickness, Fill = Brushes.Transparent, StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round };
            Canvas.SetLeft(shape, position); Canvas.SetTop(shape, position);
            return shape;
        }

        private static Path PathShape(Geometry data, Brush stroke, double width) => new()
        {
            Data = data, Fill = Brushes.Transparent, Stroke = stroke, StrokeThickness = width,
            StrokeStartLineCap = PenLineCap.Round, StrokeEndLineCap = PenLineCap.Round, StrokeLineJoin = PenLineJoin.Round
        };

        private static void AddDot(Canvas canvas, double size, Brush fill, double x, double y)
        {
            var dot = new Ellipse { Width = size, Height = size, Fill = fill };
            Canvas.SetLeft(dot, x); Canvas.SetTop(dot, y); canvas.Children.Add(dot);
        }

        private static Path EyeIcon() => new()
        {
            Width = 20, Height = 20, Stretch = Stretch.Uniform,
            Data = Geometry.Parse("M 2,10 C 4,6 7,4 10,4 C 13,4 16,6 18,10 C 16,14 13,16 10,16 C 7,16 4,14 2,10 Z M 10,7 C 8,7 7,8 7,10 C 7,12 8,13 10,13 C 12,13 13,12 13,10 C 13,8 12,7 10,7 Z"),
            Stroke = Brush("#A7B5C5"), StrokeThickness = 1.5, Fill = Brushes.Transparent, StrokeLineJoin = PenLineJoin.Round
        };

        private Button MakeButton(string label, bool primary, Action action, double minWidth)
        {
            var button = new Button
            {
                Content = label, MinWidth = minWidth, MinHeight = 44, Padding = new Thickness(16, 10, 16, 10),
                Background = primary ? new SolidColorBrush(Color.FromArgb(38, Accent.R, Accent.G, Accent.B)) : Brush("#1B2632"),
                BorderBrush = primary ? new SolidColorBrush(Color.FromArgb(135, Accent.R, Accent.G, Accent.B)) : Brush("#334353"),
                BorderThickness = new Thickness(1), Foreground = primary ? Brush("#DCFFF4") : Brush("#EEF4FA"),
                FontSize = 13, FontWeight = FontWeights.SemiBold, Cursor = Cursors.Hand, FocusVisualStyle = null,
                Template = ButtonTemplate()
            };
            button.Click += (_, _) => action();
            return button;
        }

        private static ControlTemplate ButtonTemplate()
        {
            var border = new FrameworkElementFactory(typeof(Border)) { Name = "Chrome" };
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(15));
            border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderBrushProperty, new System.Windows.Data.Binding("BorderBrush") { RelativeSource = RelativeSource.TemplatedParent });
            border.SetBinding(Border.BorderThicknessProperty, new System.Windows.Data.Binding("BorderThickness") { RelativeSource = RelativeSource.TemplatedParent });
            var content = new FrameworkElementFactory(typeof(ContentPresenter));
            content.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            content.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            content.SetBinding(ContentPresenter.ContentProperty, new System.Windows.Data.Binding("Content") { RelativeSource = RelativeSource.TemplatedParent });
            border.AppendChild(content);
            var template = new ControlTemplate(typeof(Button)) { VisualTree = border };
            var hover = new Trigger { Property = Button.IsMouseOverProperty, Value = true };
            hover.Setters.Add(new Setter(Border.BackgroundProperty, Brush("#202C3A"), "Chrome"));
            hover.Setters.Add(new Setter(Border.BorderBrushProperty, Brush("#3A4A5B"), "Chrome"));
            template.Triggers.Add(hover);
            return template;
        }

        private static Brush Brush(string value) => new SolidColorBrush((Color)ColorConverter.ConvertFromString(value)!);
    }
}

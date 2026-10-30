using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Shapes;
using Microsoft.Win32;

namespace LaptopStatus;

internal sealed class OnboardingGuideWindow : Window
{
    private readonly TextBlock _stepLabel = new();
    private readonly TextBlock _icon = new();
    private readonly TextBlock _title = new();
    private readonly TextBlock _description = new();
    private readonly Border _progressFill = new() { Width = 0 };
    private Border _progressTrack = new();
    private readonly Grid _pageContent = new();
    private readonly Button _backButton = new();
    private readonly Button _nextButton = new();
    private int _page;

    private static readonly (string Icon, string Title, string Description)[] Pages =
    {
        ("⌁", "Pasangkan laptop", "Buka aplikasi Windows, tampilkan QR, lalu pindai dari Android. Pada pemasangan pertama, cocokkan kode dan setujui permintaan di Windows."),
        ("◉", "Pantau status", "Halaman Perangkat menampilkan status online, CPU, RAM, dan baterai. Laptop dan ponsel dapat ditemukan otomatis jika berada di jaringan Wi-Fi yang sama."),
        ("⌨", "Kendalikan dengan aman", "Perintah daya dan kendali layar memerlukan konfirmasi. Periksa nama laptop yang dipilih sebelum mengirim perintah; sesi kendali layar memiliki indikator yang dapat dihentikan dari Windows.")
    };

    public OnboardingGuideWindow()
    {
        Title = "Panduan Laptop Status";
        var workArea = SystemParameters.WorkArea;
        var maxWidth = Math.Max(340, workArea.Width - 40);
        var windowMaxHeight = Math.Max(280, workArea.Height - 64);
        Width = Math.Min(580, maxWidth);
        Height = Math.Min(540, windowMaxHeight);
        MinWidth = Math.Min(420, Width);
        MinHeight = Math.Min(460, Height);
        MaxWidth = Width;
        MaxHeight = Height;
        ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Background = new SolidColorBrush(Color.FromRgb(14, 21, 30));
        Foreground = new SolidColorBrush(Color.FromRgb(241, 246, 252));
        FontFamily = new FontFamily("Segoe UI");
        ShowInTaskbar = false;
        UseLayoutRounding = true;
        SnapsToDevicePixels = true;
        Content = BuildContent();
        Loaded += (_, _) => UpdatePage(false);
        UpdatePage(false);
    }

    private UIElement BuildContent()
    {
        var root = new Grid { Margin = new Thickness(26) };
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        root.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        root.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var heading = new Grid { Margin = new Thickness(0, 0, 0, 13) };
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        heading.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        heading.Children.Add(new TextBlock
        {
            Text = "PANDUAN SINGKAT",
            Foreground = new SolidColorBrush(Color.FromRgb(114, 228, 191)),
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            VerticalAlignment = VerticalAlignment.Center
        });
        _stepLabel.Foreground = new SolidColorBrush(Color.FromRgb(167, 181, 197));
        _stepLabel.FontSize = 11;
        _stepLabel.VerticalAlignment = VerticalAlignment.Center;
        Grid.SetColumn(_stepLabel, 1);
        heading.Children.Add(_stepLabel);
        root.Children.Add(heading);

        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(25, 36, 49)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(59, 75, 91)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(18),
            Padding = new Thickness(24)
        };
        Grid.SetRow(card, 1);
        var cardGrid = new Grid();
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
        cardGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

        var iconTile = new Border
        {
            Width = 64,
            Height = 64,
            CornerRadius = new CornerRadius(19),
            Background = new SolidColorBrush(Color.FromArgb(45, 114, 228, 191)),
            BorderBrush = new SolidColorBrush(Color.FromArgb(95, 114, 228, 191)),
            BorderThickness = new Thickness(1),
            Child = _icon
        };
        _icon.FontSize = 29;
        _icon.Foreground = new SolidColorBrush(Color.FromRgb(114, 228, 191));
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _icon.VerticalAlignment = VerticalAlignment.Center;
        _icon.RenderTransform = new ScaleTransform(1, 1);
        cardGrid.Children.Add(iconTile);

        _title.FontSize = 22;
        _title.FontWeight = FontWeights.SemiBold;
        _title.Margin = new Thickness(0, 17, 0, 8);
        Grid.SetRow(_title, 1);
        cardGrid.Children.Add(_title);

        _pageContent.Opacity = 1;
        _pageContent.RenderTransform = new TranslateTransform();
        _description.FontSize = 13;
        _description.Foreground = new SolidColorBrush(Color.FromRgb(184, 197, 211));
        _description.TextWrapping = TextWrapping.Wrap;
        _description.LineHeight = 21;
        _description.VerticalAlignment = VerticalAlignment.Top;
        _pageContent.Children.Add(_description);
        var descriptionScroller = new ScrollViewer
        {
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Content = _pageContent
        };
        Grid.SetRow(_pageContent, 2);
        Grid.SetRow(descriptionScroller, 2);
        cardGrid.Children.Add(descriptionScroller);

        _progressTrack = new Border
        {
            Height = 5,
            Background = new SolidColorBrush(Color.FromRgb(50, 63, 77)),
            CornerRadius = new CornerRadius(4),
            ClipToBounds = true,
            Child = new Grid { Children = { _progressFill } }
        };
        _progressFill.HorizontalAlignment = HorizontalAlignment.Left;
        _progressFill.Height = 5;
        _progressFill.Background = new SolidColorBrush(Color.FromRgb(114, 228, 191));
        _progressFill.CornerRadius = new CornerRadius(4);
        Grid.SetRow(_progressTrack, 3);
        cardGrid.Children.Add(_progressTrack);
        card.Child = cardGrid;
        root.Children.Add(card);

        var footer = new Grid { Margin = new Thickness(0, 18, 0, 0) };
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var skip = new Button { Content = "Lewati panduan", Padding = new Thickness(11, 7, 11, 7), Margin = new Thickness(0, 0, 10, 0) };
        skip.Click += (_, _) => CompleteAndClose();
        footer.Children.Add(skip);

        _backButton.Content = "Kembali";
        _backButton.Padding = new Thickness(13, 7, 13, 7);
        _backButton.Margin = new Thickness(0, 0, 9, 0);
        _backButton.Click += (_, _) => { if (_page > 0) { _page--; UpdatePage(true); } };
        Grid.SetColumn(_backButton, 1);
        footer.Children.Add(_backButton);

        _nextButton.Content = "Berikutnya";
        _nextButton.Style = (Style)Application.Current.FindResource("PrimaryButton");
        _nextButton.Padding = new Thickness(15, 7, 15, 7);
        _nextButton.Click += (_, _) =>
        {
            if (_page < Pages.Length - 1) { _page++; UpdatePage(true); }
            else CompleteAndClose();
        };
        Grid.SetColumn(_nextButton, 2);
        footer.Children.Add(_nextButton);
        Grid.SetRow(footer, 2);
        root.Children.Add(footer);
        return root;
    }

    private void UpdatePage(bool animate)
    {
        _stepLabel.Text = $"{_page + 1:00} / {Pages.Length:00}";
        _icon.Text = Pages[_page].Icon;
        _title.Text = Pages[_page].Title;
        _description.Text = Pages[_page].Description;
        _backButton.IsEnabled = _page > 0;
        _nextButton.Content = _page == Pages.Length - 1 ? "Selesai" : "Berikutnya";

        var target = _progressTrack.ActualWidth * (_page + 1) / Pages.Length;
        _progressFill.BeginAnimation(FrameworkElement.WidthProperty,
            new DoubleAnimation(_progressFill.Width, target, TimeSpan.FromMilliseconds(280)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });

        if (!animate) return;
        _pageContent.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(260)));
        if (_pageContent.RenderTransform is TranslateTransform transform)
            transform.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(22, 0, TimeSpan.FromMilliseconds(300)) { EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut } });
        if (_icon.RenderTransform is ScaleTransform scale)
        {
            scale.ScaleX = 0.82;
            scale.ScaleY = 0.82;
            scale.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } });
            scale.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(0.82, 1, TimeSpan.FromMilliseconds(320)) { EasingFunction = new BackEase { EasingMode = EasingMode.EaseOut, Amplitude = 0.3 } });
        }
    }

    private void CompleteAndClose()
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\LaptopStatus");
        key?.SetValue("OnboardingCompleted", 1, RegistryValueKind.DWord);
        Close();
    }
}

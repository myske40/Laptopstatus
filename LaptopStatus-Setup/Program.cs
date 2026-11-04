using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Xml.Linq;
using Timer = System.Windows.Forms.Timer;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace LaptopStatusSetup;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        ApplicationConfiguration.Initialize();
        Application.Run(new SetupForm());
    }
}

internal sealed class SetupForm : Form
{
    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "ReleaseCapture", SetLastError = true)]
    [return: System.Runtime.InteropServices.MarshalAs(System.Runtime.InteropServices.UnmanagedType.Bool)]
    private static extern bool NativeReleaseCapture();

    [System.Runtime.InteropServices.DllImport("user32.dll", EntryPoint = "SendMessageW", CharSet = System.Runtime.InteropServices.CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern IntPtr SendWindowMessage(IntPtr handle, int message, IntPtr wParam, IntPtr lParam);

    private const int WmNcLeftButtonDown = 0x00A1;
    private static readonly IntPtr HtCaption = new IntPtr(2);
    private readonly Panel _shell = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 246, 243) };
    private readonly Panel _mainColumn = new() { Dock = DockStyle.Fill, BackColor = Color.FromArgb(244, 246, 243) };
    private readonly Panel _stepRail = new() { Dock = DockStyle.Left, Width = 224, BackColor = Color.FromArgb(14, 26, 39) };
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = Padding.Empty, BackColor = Color.FromArgb(251, 252, 250) };
    private readonly Panel _header = new() { Dock = DockStyle.Top, Height = 48, BackColor = Color.FromArgb(10, 19, 29) };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.FromArgb(10, 19, 29), Padding = new Padding(0, 12, 0, 12) };
    private readonly VScrollBar _pageScrollBar = new() { Dock = DockStyle.Right, Width = SystemInformation.VerticalScrollBarWidth, Visible = false, SmallChange = 32 };
    private readonly FlowLayoutPanel _footerButtons = new() { Dock = DockStyle.Fill, AutoSize = false, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 0, 24, 0), BackColor = Color.Transparent };
    private readonly FlowLayoutPanel _pageBody = new() { Location = Point.Empty, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(34, 28, 34, 30), BackColor = Color.FromArgb(251, 252, 250) };
    private readonly WebView2 _webWizard = new() { Dock = DockStyle.Fill, Visible = false };
    private readonly string _defaultInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LaptopStatus");
    private readonly string _wizardFilesDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "InstallerWizard", "2.13.26");
    private readonly string _webViewUserDataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LaptopStatus", "InstallerWebView2");
    private readonly Timer _installStatusTimer = new() { Interval = 1050 };
    private readonly Timer _iconMotionTimer = new() { Interval = 40 };
    private readonly Dictionary<Button, Timer> _buttonColorAnimations = new();
    private readonly HashSet<Control> _wheelHooked = new();
    private TextBox? _installPath;
    private CheckBox? _autoStart;
    private CheckBox? _launchNow;
    private CheckBox? _installTailscale;
    private CheckBox? _installSpacedeskOption;
    private CheckBox? _createDesktopShortcut;
    private CheckBox? _pinTaskbar;
    private CheckBox? _pinStartMenu;
    private Label? _installStatusLabel;
    private bool _launchAfterInstall;
    private bool _autoStartSelected;
    private bool _installTailscaleSelected;
    private bool _installSpacedeskOptionSelected;
    private bool _desktopShortcutCreated;
    private bool _taskbarPinRequested;
    private bool _startPinRequested;
    private bool _taskbarPinSucceeded;
    private bool _startPinSucceeded;
    private bool _pageLayoutBusy;
    private bool _webWizardActive;
    private bool _webWizardReady;
    private Task<CoreWebView2Environment>? _webViewEnvironmentTask;
    private bool _darkTheme;
    private Button? _themeButton;
    private readonly List<(Panel Marker, Label Title, Label Note)> _wizardSteps = new();
    private BrandMark? _brandMark;
    private double _motionPhase;
    private int _installStatusIndex;
    private static readonly string[] BasicInstallStatusMessages = { "Menyiapkan berkas aplikasi…", "Menyalin aplikasi ke folder pilihan…", "Menyelesaikan pengaturan Windows…" };
    private static readonly string[] TailscaleInstallStatusMessages = { "Menyiapkan berkas aplikasi…", "Menyalin aplikasi ke folder pilihan…", "Mengunduh dan memeriksa Tailscale resmi…", "Menyelesaikan pengaturan Windows…" };
    private string[] ActiveInstallStatusMessages => _installTailscaleSelected ? TailscaleInstallStatusMessages : BasicInstallStatusMessages;

    public SetupForm()
    {
        Text = "Pengaturan Laptop Status";
        AutoScaleMode = AutoScaleMode.Dpi;
        AutoScaleDimensions = new SizeF(96f, 96f);
        var workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
        var formWidth = Math.Max(Math.Min(680, workArea.Width), Math.Min(940, workArea.Width - 48));
        var formHeight = Math.Max(Math.Min(440, workArea.Height), Math.Min(700, workArea.Height - 48));
        ClientSize = new Size(formWidth, formHeight);
        FormBorderStyle = FormBorderStyle.None;
        MinimumSize = new Size(Math.Min(760, formWidth), Math.Min(520, formHeight));
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(244, 246, 243);
        ForeColor = Color.FromArgb(14, 26, 39);
        Font = new Font("Segoe UI", 10);
        try
        {
            using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LaptopStatus.Brand.ico");
            if (iconStream is not null) Icon = new Icon(iconStream);
        }
        catch { }
        _header.Controls.Add(new Label
        {
            Text = "PENGATURAN LAPTOP STATUS",
            Location = new Point(24, 0),
            Size = new Size(420, 48),
            TextAlign = ContentAlignment.MiddleLeft,
            ForeColor = Color.FromArgb(196, 209, 219),
            Font = new Font("Segoe UI", 9, FontStyle.Bold)
        });
        _themeButton = new Button
        {
            Text = "☼",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(formWidth - 322, 5),
            Size = new Size(38, 38),
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(22, 39, 58),
            ForeColor = Color.FromArgb(91, 224, 184),
            Font = new Font("Segoe UI Symbol", 16),
            Cursor = Cursors.Hand,
            AccessibleName = "Ganti tema terang atau gelap"
        };
        _themeButton.FlatAppearance.BorderSize = 0;
        _themeButton.Click += (_, _) => { _darkTheme = !_darkTheme; ApplyWizardTheme(); };
        _header.Controls.Add(_themeButton);

        _iconMotionTimer.Tick += (_, _) =>
        {
            _motionPhase = (_motionPhase + Math.Tau * _iconMotionTimer.Interval / 1800d) % Math.Tau;
            _brandMark?.SetMotionPhase(_motionPhase);
            foreach (var (marker, _, _) in _wizardSteps) marker.Invalidate();
        };
        FormClosed += (_, _) => { _iconMotionTimer.Stop(); _iconMotionTimer.Dispose(); _installStatusTimer.Stop(); };

        _installStatusTimer.Tick += (_, _) =>
        {
            if (_webWizardActive) return;
            _installStatusIndex = (_installStatusIndex + 1) % ActiveInstallStatusMessages.Length;
            if (_installStatusLabel is not null && !_installStatusLabel.IsDisposed)
                _installStatusLabel.Text = ActiveInstallStatusMessages[_installStatusIndex];
        };
        _content.Controls.Add(_pageBody);
        _pageScrollBar.Scroll += (_, _) => _pageBody.Top = -_pageScrollBar.Value;
        _content.ClientSizeChanged += (_, _) => LayoutPageBody();
        _footer.Controls.Add(_footerButtons);
        _footer.SizeChanged += (_, _) => LayoutFooterButtons();
        _shell.Visible = false;
        BackColor = Color.FromArgb(10, 19, 29);
        _webWizard.Visible = true;
        Controls.Add(_webWizard);
        _webWizard.BringToFront();
        Load += (_, _) => _ = InitializeWebWizardAsync();
        Resize += (_, _) => PushWebWindowState();
        // Warm the WebView environment while WinForms creates and paints the first window.
        _webViewEnvironmentTask = CoreWebView2Environment.CreateAsync(null, _webViewUserDataDirectory);
    }

    private async Task InitializeWebWizardAsync()
    {
        try
        {
            // Prepare the local page before WebView2 starts so navigation can begin as soon as its core is ready.
            ExtractWizardResources();
            var environment = await (_webViewEnvironmentTask ??= CoreWebView2Environment.CreateAsync());
            await _webWizard.EnsureCoreWebView2Async(environment);
            _webWizard.DefaultBackgroundColor = Color.FromArgb(10, 19, 29);
            _webWizard.CoreWebView2.Settings.AreDevToolsEnabled = false;
            _webWizard.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _webWizard.CoreWebView2.Settings.IsStatusBarEnabled = false;
            _webWizard.CoreWebView2.WebMessageReceived += WebWizardMessageReceived;
            _webWizard.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (!e.Uri.StartsWith("https://laptopstatus-installer.local/", StringComparison.OrdinalIgnoreCase))
                {
                    e.Cancel = true;
                    try { Process.Start(new ProcessStartInfo(e.Uri) { UseShellExecute = true }); } catch { }
                }
            };
            _webWizard.CoreWebView2.SetVirtualHostNameToFolderMapping("laptopstatus-installer.local", _wizardFilesDirectory, CoreWebView2HostResourceAccessKind.Allow);
            _webWizard.CoreWebView2.Navigate("https://laptopstatus-installer.local/Main.dc.html");
        }
        catch (Exception ex)
        {
            _webWizardReady = false;
            _webWizardActive = false;
            _webWizard.Visible = false;
            var retry = MessageBox.Show(this,
                $"Tampilan installer WebView gagal dimuat.\n\n{ex.Message}\n\nCoba muat lagi?",
                "Laptop Status Setup", MessageBoxButtons.RetryCancel, MessageBoxIcon.Error);
            if (retry == DialogResult.Retry && !IsDisposed)
            {
                _webViewEnvironmentTask = CoreWebView2Environment.CreateAsync(null, _webViewUserDataDirectory);
                _webWizard.Visible = true;
                _ = InitializeWebWizardAsync();
            }
            else if (!IsDisposed) Close();
        }
    }

    private void ExtractWizardResources()
    {
        Directory.CreateDirectory(Path.Combine(_wizardFilesDirectory, "vendor"));
        var resources = new (string Name, string Path)[]
        {
            ("LaptopStatus.Wizard.Main.dc.html", "Main.dc.html"),
            ("LaptopStatus.Wizard.support.js", "support.js"),
            ("LaptopStatus.Wizard.vendor.react.js", Path.Combine("vendor", "react.js")),
            ("LaptopStatus.Wizard.vendor.react-dom.js", Path.Combine("vendor", "react-dom.js"))
        };
        foreach (var (name, relativePath) in resources)
        {
            using var source = Assembly.GetExecutingAssembly().GetManifestResourceStream(name)
                ?? throw new FileNotFoundException($"Aset wizard tidak tersedia: {name}");
            var target = Path.Combine(_wizardFilesDirectory, relativePath);
            using var destination = File.Create(target);
            source.CopyTo(destination);
        }
    }

    private async void WebWizardMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            var action = root.TryGetProperty("action", out var actionValue) ? actionValue.GetString() : "";
            switch (action)
            {
                case "ready":
                    _webWizardReady = true;
                    _webWizardActive = true;
                    var path = JsonSerializer.Serialize(GetPreviousInstallPath() ?? _defaultInstallPath);
                    await _webWizard.CoreWebView2.ExecuteScriptAsync($"window.LaptopStatusInstaller&&window.LaptopStatusInstaller.setInstallPath({path})");
                    PushWebWindowState();
                    break;
                case "cancel": Close(); break;
                case "browse":
                    if (_installPath is null) _installPath = new TextBox { Text = GetPreviousInstallPath() ?? _defaultInstallPath };
                    BrowseFolder();
                    await _webWizard.CoreWebView2.ExecuteScriptAsync($"window.LaptopStatusInstaller&&window.LaptopStatusInstaller.setInstallPath({JsonSerializer.Serialize(_installPath.Text)})");
                    break;
                case "install":
                    var requestedPath = root.TryGetProperty("path", out var installPathValue) ? installPathValue.GetString() : null;
                    if (root.TryGetProperty("options", out var options)) SetWebInstallOptions(options, requestedPath);
                    Install();
                    break;
                case "finish":
                    var installDir = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\LaptopStatus")?.GetValue("InstallLocation") as string;
                    if (_launchAfterInstall && !string.IsNullOrWhiteSpace(installDir)) await LaunchInstalledAppAsync(installDir);
                    Close();
                    break;
                case "minimize": WindowState = FormWindowState.Minimized; break;
                case "maximize": WindowState = WindowState == FormWindowState.Maximized ? FormWindowState.Normal : FormWindowState.Maximized; break;
                case "close": Close(); break;
                case "drag":
                    NativeReleaseCapture();
                    _ = SendWindowMessage(Handle, WmNcLeftButtonDown, HtCaption, IntPtr.Zero);
                    break;
                case "spacedesk": OpenSpacedeskDownloadPage(); break;
            }
        }
        catch (Exception ex)
        {
            if (_webWizardReady)
                await _webWizard.CoreWebView2.ExecuteScriptAsync($"window.LaptopStatusInstaller&&window.LaptopStatusInstaller.showError({JsonSerializer.Serialize(ex.Message)})");
        }
    }

    private void SetWebInstallOptions(JsonElement options, string? requestedPath)
    {
        bool GetBool(string key, bool fallback) => options.TryGetProperty(key, out var value) && value.ValueKind is JsonValueKind.True or JsonValueKind.False ? value.GetBoolean() : fallback;
        _installPath = new TextBox { Text = string.IsNullOrWhiteSpace(requestedPath) ? _defaultInstallPath : requestedPath };
        _autoStart = new CheckBox { Checked = GetBool("autoStart", true) };
        _launchNow = new CheckBox { Checked = GetBool("launchNow", true) };
        _createDesktopShortcut = new CheckBox { Checked = GetBool("desktop", true) };
        _installTailscale = new CheckBox { Checked = GetBool("tailscale", true) };
        _installSpacedeskOption = new CheckBox { Checked = GetBool("spacedesk", false) };
        _pinTaskbar = new CheckBox { Checked = GetBool("pinTaskbar", false) };
        _pinStartMenu = new CheckBox { Checked = GetBool("pinStart", false) };
    }

    private void PushWebWindowState()
    {
        if (!_webWizardReady || IsDisposed || _webWizard.IsDisposed) return;
        _ = _webWizard.CoreWebView2.ExecuteScriptAsync($"window.LaptopStatusInstaller&&window.LaptopStatusInstaller.setMaximized({(WindowState == FormWindowState.Maximized ? "true" : "false")})");
    }

    private void BuildStepRail()
    {
        _brandMark = new BrandMark { Location = new Point(24, 28), Size = new Size(44, 44) };
        _brandMark.SetMotionPhase(_motionPhase);
        _stepRail.Controls.Add(_brandMark);
        _stepRail.Controls.Add(new Label
        {
            Text = "Laptop Status",
            Location = new Point(78, 27),
            Size = new Size(132, 26),
            ForeColor = Color.FromArgb(241, 246, 252),
            Font = new Font("Segoe UI Semibold", 14, FontStyle.Bold)
        });
        _stepRail.Controls.Add(new Label
        {
            Text = "Pemantauan dan kendali laptop",
            Location = new Point(79, 52),
            Size = new Size(132, 34),
            ForeColor = Color.FromArgb(126, 146, 163),
            Font = new Font("Segoe UI", 8.5f)
        });

        var steps = new[]
        {
            ("Selamat datang", "MULAI"),
            ("Pilihan instalasi", "PILIHAN"),
            ("Pemasangan", "PROSES"),
            ("Selesai", "RAMPUNG")
        };
        for (var index = 0; index < steps.Length; index++)
        {
            var stepIndex = index;
            var y = 150 + index * 72;
            var marker = new Panel { Location = new Point(28, y), Size = new Size(36, 36), BackColor = Color.Transparent, Tag = stepIndex };
            marker.Paint += (_, e) => PaintStepMarker(marker, e.Graphics, stepIndex);
            var title = new Label
            {
                Text = steps[index].Item1,
                Location = new Point(78, y + 1),
                Size = new Size(132, 22),
                ForeColor = Color.FromArgb(126, 146, 163),
                Font = new Font("Segoe UI", 9.5f)
            };
            var note = new Label
            {
                Text = steps[index].Item2,
                Location = new Point(79, y + 23),
                Size = new Size(130, 17),
                ForeColor = Color.FromArgb(126, 146, 163),
                Font = new Font("Consolas", 7.5f)
            };
            _stepRail.Controls.Add(marker);
            _stepRail.Controls.Add(title);
            _stepRail.Controls.Add(note);
            _wizardSteps.Add((marker, title, note));
        }

        _stepRail.Controls.Add(new Label
        {
            Text = $"PT HAHA NASIONAL\nINSTALLER · v{Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}",
            Anchor = AnchorStyles.Bottom | AnchorStyles.Left,
            Location = new Point(28, 0),
            Size = new Size(180, 44),
            ForeColor = Color.FromArgb(126, 146, 163),
            Font = new Font("Consolas", 7.5f)
        });
        UpdateWizardProgress(0);
    }

    private void PaintStepMarker(Panel marker, Graphics graphics, int stepIndex)
    {
        graphics.SmoothingMode = SmoothingMode.AntiAlias;
        var active = stepIndex == _activeWizardStep;
        var done = stepIndex < _activeWizardStep;
        var pulse = (Math.Sin(_motionPhase) + 1d) / 2d;
        if (active)
        {
            using var glow = new Pen(Color.FromArgb((int)(12 + 40 * pulse), 91, 224, 184), 3f);
            graphics.DrawEllipse(glow, 0.5f, 0.5f, marker.Width - 1f, marker.Height - 1f);
        }
        var color = active || done ? Color.FromArgb(91, 224, 184) : Color.FromArgb(42, 65, 83);
        var bounds = new Rectangle(2, 2, marker.Width - 5, marker.Height - 5);
        using (var fill = new SolidBrush(done ? Color.FromArgb(91, 224, 184) : Color.FromArgb(14, 26, 39)))
            graphics.FillEllipse(fill, bounds);
        using (var ringBackground = new Pen(Color.FromArgb(42, 65, 83), 2f))
            graphics.DrawEllipse(ringBackground, bounds);
        var progress = done ? 1f : active ? stepIndex == 2
            ? Math.Clamp((_installStatusIndex + 1f) / ActiveInstallStatusMessages.Length, 0.12f, 0.9f)
            : 0.48f : 0f;
        if (progress > 0)
        {
            using var ringProgress = new Pen(Color.FromArgb(91, 224, 184), 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
            graphics.DrawArc(ringProgress, bounds, -90, 360f * progress);
        }
        var iconColor = done ? Color.FromArgb(7, 19, 28) : color;
        using var icon = new Pen(iconColor, 1.55f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
        var cx = marker.Width / 2f;
        if (stepIndex == 0)
        {
            graphics.DrawRectangle(icon, cx - 6f, 11f, 12f, 8f);
            graphics.DrawLine(icon, cx, 19.5f, cx, 23f);
            graphics.DrawLine(icon, cx - 4.5f, 24.5f, cx + 4.5f, 24.5f);
        }
        else if (stepIndex == 1)
        {
            foreach (var y in new[] { 11f, 18f, 25f }) graphics.DrawLine(icon, cx - 6f, y, cx + 6f, y);
            using var knob = new SolidBrush(iconColor);
            graphics.FillEllipse(knob, cx - 3.5f, 9f, 4f, 4f);
            graphics.FillEllipse(knob, cx + 1f, 16f, 4f, 4f);
            graphics.FillEllipse(knob, cx - 2.5f, 23f, 4f, 4f);
        }
        else if (stepIndex == 2)
        {
            var bobPhase = _motionPhase * 1800d / 1100d;
            var offset = active ? (float)(Math.Sin(bobPhase) * 1.5f) : 0f;
            graphics.DrawLine(icon, cx, 9.5f + offset, cx, 20f + offset);
            graphics.DrawLine(icon, cx, 20f + offset, cx - 4f, 16f + offset);
            graphics.DrawLine(icon, cx, 20f + offset, cx + 4f, 16f + offset);
            graphics.DrawLine(icon, cx - 5f, 25f, cx + 5f, 25f);
        }
        else
        {
            graphics.DrawLine(icon, cx - 5f, 18f, cx - 1f, 22f);
            graphics.DrawLine(icon, cx - 1f, 22f, cx + 6f, 14f);
        }
    }

    private int _activeWizardStep;

    private void UpdateWizardProgress(int step)
    {
        _activeWizardStep = Math.Clamp(step, 0, 3);
        foreach (var (marker, title, note) in _wizardSteps)
        {
            var index = (int)marker.Tag!;
            var active = index == _activeWizardStep;
            var done = index < _activeWizardStep;
            title.ForeColor = active ? Color.FromArgb(231, 238, 243) : Color.FromArgb(126, 146, 163);
            title.Font = new Font("Segoe UI", 9.5f, active ? FontStyle.Bold : FontStyle.Regular);
            note.ForeColor = active ? Color.FromArgb(91, 224, 184) : Color.FromArgb(126, 146, 163);
            if (done) note.Text = "SELESAI";
            else note.Text = index switch { 0 => "MULAI", 1 => "PILIHAN", 2 => "PROSES", _ => "RAMPUNG" };
            marker.Invalidate();
        }
    }

    private Color PageBackground => _darkTheme ? Color.FromArgb(15, 26, 38) : Color.FromArgb(251, 252, 250);
    private Color PageText => _darkTheme ? Color.FromArgb(230, 238, 243) : Color.FromArgb(14, 26, 39);
    private Color PageMuted => _darkTheme ? Color.FromArgb(156, 176, 191) : Color.FromArgb(85, 99, 111);
    private Color PageLine => _darkTheme ? Color.FromArgb(33, 50, 68) : Color.FromArgb(213, 220, 215);
    private Color PageAccent => _darkTheme ? Color.FromArgb(91, 224, 184) : Color.FromArgb(10, 107, 85);

    private void ApplyWizardTheme()
    {
        _content.BackColor = PageBackground;
        _mainColumn.BackColor = PageBackground;
        _pageBody.BackColor = PageBackground;
        BackColor = PageBackground;
        ForeColor = PageText;
        if (_themeButton is not null)
        {
            _themeButton.Text = _darkTheme ? "☾" : "☼";
            _themeButton.ForeColor = PageAccent;
            _themeButton.BackColor = Color.FromArgb(22, 39, 58);
        }

        ApplyWizardThemeToPage(_pageBody);
        foreach (Control control in _footerButtons.Controls)
            if (control is Button button && button.Tag is bool primary) StyleWizardButton(button, primary);
        UpdateWizardProgress(_activeWizardStep);
        LayoutPageBody();
    }

    private void ApplyWizardThemeToPage(Control parent)
    {
        foreach (Control control in parent.Controls)
        {
            if (control is Panel stage && Equals(stage.Tag, "stage"))
            {
                stage.BackColor = _darkTheme ? Color.FromArgb(11, 21, 32) : Color.FromArgb(244, 246, 243);
                stage.Invalidate();
            }
            else if (control is Panel card && Equals(card.Tag, "card"))
            {
                card.BackColor = _darkTheme ? Color.FromArgb(22, 39, 58) : Color.FromArgb(255, 255, 255);
                card.ForeColor = PageText;
                card.BorderStyle = BorderStyle.FixedSingle;
            }
            else if (control is Label label)
            {
                label.ForeColor = Equals(label.Tag, "muted") ? PageMuted : Equals(label.Tag, "accent") ? PageAccent : PageText;
            }
            else if (control is TextBox textBox)
            {
                textBox.BackColor = _darkTheme ? Color.FromArgb(11, 21, 32) : Color.White;
                textBox.ForeColor = PageText;
            }
            else if (control is CheckBox checkBox)
            {
                checkBox.ForeColor = PageText;
                checkBox.BackColor = Color.Transparent;
            }
            else if (control is Button button && button.Tag is bool primary)
            {
                StyleWizardButton(button, primary);
            }
            else if (control is Button otherButton)
            {
                otherButton.BackColor = _darkTheme ? Color.FromArgb(22, 39, 58) : Color.FromArgb(230, 236, 232);
                otherButton.ForeColor = PageText;
                otherButton.FlatAppearance.BorderColor = PageLine;
            }
            else if (control is ProgressBar progress)
            {
                progress.ForeColor = PageAccent;
                progress.BackColor = _darkTheme ? Color.FromArgb(22, 39, 58) : Color.FromArgb(225, 232, 227);
            }
            else if (control is TableLayoutPanel table)
                table.BackColor = Color.Transparent;
            else if (control is FlowLayoutPanel flow)
                flow.BackColor = Color.Transparent;
            else if (control is Panel panel && !Equals(panel.Tag, "card"))
                panel.BackColor = Color.Transparent;

            ApplyWizardThemeToPage(control);
        }
    }

    private void StyleWizardButton(Button button, bool primary)
    {
        var normal = primary
            ? (_darkTheme ? Color.FromArgb(91, 224, 184) : Color.FromArgb(14, 26, 39))
            : (_darkTheme ? Color.FromArgb(22, 39, 58) : Color.FromArgb(230, 236, 232));
        var hover = primary
            ? (_darkTheme ? Color.FromArgb(122, 235, 200) : Color.FromArgb(29, 51, 73))
            : (_darkTheme ? Color.FromArgb(33, 50, 68) : Color.FromArgb(213, 220, 215));
        button.BackColor = normal;
        button.ForeColor = primary && !_darkTheme ? Color.White : PageText;
        button.FlatAppearance.MouseDownBackColor = hover;
        button.FlatAppearance.BorderColor = primary ? normal : PageLine;
    }

    private Label CreateLabel(string text, float size = 10, bool bold = false, Color? color = null)
    {
        var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        var role = color == Color.FromArgb(167, 181, 197) ? "muted" : color == Color.FromArgb(114, 228, 191) ? "accent" : "primary";
        return new Label
        {
            Text = text,
            AutoSize = true,
            AutoEllipsis = false,
            UseMnemonic = false,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = role == "muted" ? PageMuted : role == "accent" ? PageAccent : PageText,
            Tag = role,
            Font = font,
            Margin = new Padding(0, 0, 0, 8),
            MaximumSize = new Size(Math.Max(180, _pageBody.ClientSize.Width - _pageBody.Padding.Horizontal), 0)
        };
    }

    private Label AddLabel(string text, float size = 10, bool bold = false, Color? color = null)
    {
        var label = CreateLabel(text, size, bold, color);
        _pageBody.Controls.Add(label);
        return label;
    }

    private void AddCardLabel(FlowLayoutPanel card, string text, float size = 10, bool bold = false, Color? color = null)
        => AddToCard(card, CreateLabel(text, size, bold, color));

    private CheckBox CreateOption(string text, bool isChecked)
    {
        return new CheckBox
        {
            Text = text,
            Checked = isChecked,
            AutoSize = true,
            MaximumSize = new Size(Math.Max(180, _pageBody.ClientSize.Width - _pageBody.Padding.Horizontal - 36), 0),
            ForeColor = PageText,
            BackColor = Color.Transparent,
            Font = Font,
            Padding = new Padding(0, 4, 0, 4),
            Margin = new Padding(0, 0, 0, 8),
            UseVisualStyleBackColor = false
        };
    }

    private Button AddButton(string text, EventHandler click, bool primary = false)
    {
        var button = new Button
        {
            Text = text,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            MinimumSize = new Size(96, 42),
            Padding = new Padding(16, 4, 16, 4),
            Margin = new Padding(8, 0, 0, 0),
            FlatStyle = FlatStyle.Flat,
            BackColor = primary ? Color.FromArgb(14, 26, 39) : Color.FromArgb(230, 236, 232),
            ForeColor = primary ? Color.White : PageText,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(39, 148, 119);
        button.Tag = primary;
        button.Cursor = Cursors.Hand;
        button.MouseEnter += (_, _) => { };
        button.MouseLeave += (_, _) => StyleWizardButton(button, primary);
        button.Click += click;
        _footerButtons.Controls.Add(button);
        LayoutFooterButtons();
        return button;
    }

    private void LayoutFooterButtons()
    {
        if (_footer.IsDisposed || _footerButtons.IsDisposed) return;
        var requiredWidth = _footerButtons.Controls.Cast<Control>().Sum(control => Math.Max(control.Width, control.GetPreferredSize(Size.Empty).Width) + control.Margin.Horizontal)
            + _footerButtons.Padding.Horizontal;
        var wraps = requiredWidth > Math.Max(1, _footer.ClientSize.Width);
        _footer.Height = wraps ? 112 : 72;
        _footerButtons.WrapContents = wraps;
        _footerButtons.Padding = wraps ? new Padding(8, 3, 16, 3) : new Padding(0, 0, 24, 0);
        _footerButtons.PerformLayout();
    }

    private FlowLayoutPanel CreateCard()
    {
        var card = new Panel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Padding = new Padding(18),
            Margin = new Padding(0, 0, 0, 16),
            BackColor = PageBackground,
            Tag = "card",
            Width = Math.Max(240, _pageBody.ClientSize.Width - _pageBody.Padding.Horizontal),
            MinimumSize = new Size(Math.Max(240, _pageBody.ClientSize.Width - _pageBody.Padding.Horizontal), 0)
        };
        var stack = new FlowLayoutPanel
        {
            Name = "CardStack",
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            Margin = Padding.Empty,
            Location = new Point(card.Padding.Left, card.Padding.Top),
            BackColor = Color.Transparent,
            Width = Math.Max(180, card.Width - card.Padding.Horizontal)
        };
        card.Controls.Add(stack);
        _pageBody.Controls.Add(card);
        return stack;
    }

    private Panel CreateWelcomeStage()
    {
        var stage = new Panel
        {
            Height = 184,
            Margin = new Padding(0, 8, 0, 18),
            BackColor = PageBackground,
            Tag = "stage"
        };
        stage.Paint += (_, e) =>
        {
            var graphics = e.Graphics;
            graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var width = stage.ClientSize.Width;
            var accent = _darkTheme ? Color.FromArgb(91, 224, 184) : Color.FromArgb(10, 107, 85);
            var line = _darkTheme ? Color.FromArgb(58, 82, 102) : Color.FromArgb(159, 174, 166);
            var panel = _darkTheme ? Color.FromArgb(5, 11, 17) : Color.FromArgb(14, 26, 39);
            var screen = _darkTheme ? Color.FromArgb(11, 21, 32) : Color.FromArgb(251, 252, 250);
            var muted = _darkTheme ? Color.FromArgb(143, 163, 178) : Color.FromArgb(85, 99, 111);
            using var bg = new SolidBrush(_darkTheme ? Color.FromArgb(11, 21, 32) : Color.FromArgb(244, 246, 243));
            graphics.Clear(bg.Color);
            var laptopX = Math.Max(24, (int)(width * .12));
            var laptopY = 18;
            var laptopWidth = Math.Min(260, (int)(width * .46));
            const int laptopHeight = 118;
            using var laptop = new SolidBrush(panel);
            using var panelBrush = new SolidBrush(screen);
            using var outline = new Pen(line, 1.5f);
            using var accentPen = new Pen(accent, 2.5f) { StartCap = LineCap.Round, EndCap = LineCap.Round, LineJoin = LineJoin.Round };
            graphics.FillRectangle(laptop, laptopX, laptopY, laptopWidth, laptopHeight);
            graphics.DrawRectangle(outline, laptopX, laptopY, laptopWidth, laptopHeight);
            graphics.FillRectangle(panelBrush, laptopX + 10, laptopY + 10, laptopWidth - 20, laptopHeight - 24);
            for (var row = 0; row < 3; row++)
            {
                using var dimPen = new Pen(Color.FromArgb(44, 67, 87), 5);
                graphics.DrawLine(dimPen, laptopX + 24, laptopY + 29 + row * 12, laptopX + 88, laptopY + 29 + row * 12);
                using var activePen = new Pen(accent, 5);
                graphics.DrawLine(activePen, laptopX + 24, laptopY + 29 + row * 12, laptopX + 48 + row * 10, laptopY + 29 + row * 12);
            }
            var points = new[]
            {
                new Point(laptopX + 24, laptopY + 98), new Point(laptopX + 75, laptopY + 98),
                new Point(laptopX + 92, laptopY + 82), new Point(laptopX + 105, laptopY + 109),
                new Point(laptopX + 122, laptopY + 91), new Point(laptopX + 142, laptopY + 98),
                new Point(laptopX + laptopWidth - 18, laptopY + 68)
            };
            graphics.DrawLines(accentPen, points);
            graphics.FillPolygon(laptop, new[]
            {
                new Point(laptopX - 20, laptopY + laptopHeight + 2),
                new Point(laptopX + laptopWidth + 20, laptopY + laptopHeight + 2),
                new Point(laptopX + laptopWidth + 5, laptopY + laptopHeight + 15),
                new Point(laptopX - 5, laptopY + laptopHeight + 15)
            });
            graphics.DrawLine(outline, laptopX - 20, laptopY + laptopHeight + 2, laptopX + laptopWidth + 20, laptopY + laptopHeight + 2);

            var phoneWidth = 76;
            var phoneHeight = 132;
            var phoneX = width - phoneWidth - Math.Max(22, (int)(width * .12));
            var phoneY = 12;
            using var phone = new SolidBrush(_darkTheme ? Color.FromArgb(5, 11, 17) : Color.FromArgb(251, 252, 250));
            using var phoneShape = CreateRoundedRectanglePath(new Rectangle(phoneX, phoneY, phoneWidth, phoneHeight), 14);
            graphics.FillPath(phone, phoneShape);
            graphics.DrawPath(outline, phoneShape);
            using var rowBrush = new SolidBrush(_darkTheme ? Color.FromArgb(22, 39, 58) : Color.FromArgb(225, 232, 227));
            for (var row = 0; row < 3; row++)
            {
                var y = phoneY + 30 + row * 29;
                graphics.FillRectangle(rowBrush, phoneX + 10, y, phoneWidth - 20, 19);
                using var dot = new SolidBrush(row == 0 ? accent : muted);
                graphics.FillEllipse(dot, phoneX + 16, y + 6, 7, 7);
                graphics.DrawLine(outline, phoneX + 29, y + 9, phoneX + phoneWidth - 16, y + 9);
            }

            using var linkPen = new Pen(accent, 1.6f) { DashPattern = new[] { 4f, 5f } };
            var linkStart = new Point(laptopX + laptopWidth + 18, laptopY + 63);
            var linkEnd = new Point(phoneX - 12, phoneY + 63);
            if (linkEnd.X > linkStart.X) graphics.DrawLine(linkPen, linkStart, linkEnd);
            using var caption = new Font("Consolas", 8f, FontStyle.Bold);
            using var captionBrush = new SolidBrush(muted);
            graphics.DrawString("WINDOWS", caption, captionBrush, laptopX + Math.Max(0, (laptopWidth - 54) / 2), 156);
            graphics.DrawString("ANDROID", caption, captionBrush, phoneX + 8, 156);
            if (linkEnd.X > linkStart.X)
            {
                using var accentBrush = new SolidBrush(accent);
                graphics.DrawString("QR + TLS", caption, accentBrush, linkStart.X + 20, 50);
            }
        };
        _pageBody.Controls.Add(stage);
        return stage;
    }

    private static GraphicsPath CreateRoundedRectanglePath(Rectangle bounds, int radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.X, bounds.Y, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Y, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.X, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }

    private void AddToCard(FlowLayoutPanel card, Control control)
    {
        control.Margin = new Padding(0, 0, 0, 10);
        card.Controls.Add(control);
    }

    private void LayoutPageBody()
    {
        if (_pageLayoutBusy || _content.ClientSize.Width <= 0 || _pageBody.IsDisposed) return;
        _pageLayoutBusy = true;
        try
        {
            LayoutFooterButtons();
            for (var pass = 0; pass < 2; pass++)
            {
                var bodyWidth = _content.ClientSize.Width;
                _pageBody.MinimumSize = new Size(bodyWidth, 0);
                _pageBody.Width = bodyWidth;
                var innerWidth = Math.Max(180, bodyWidth - _pageBody.Padding.Horizontal);
                foreach (Control control in _pageBody.Controls)
                {
                    if (control is Panel card)
                    {
                        card.MinimumSize = new Size(innerWidth, 0);
                        card.Width = innerWidth;
                        foreach (Control child in card.Controls)
                        {
                            child.Width = Math.Max(140, card.ClientSize.Width - card.Padding.Horizontal);
                            foreach (Control nested in child.Controls)
                                SetResponsiveWidth(nested, Math.Max(120, child.ClientSize.Width));
                        }
                    }
                    else SetResponsiveWidth(control, innerWidth);
                }
                HookWheelEvents(_pageBody);
                _pageBody.PerformLayout();
                var needsScroll = _pageBody.Height > _content.ClientSize.Height;
                if (_pageScrollBar.Visible == needsScroll) break;
                _pageScrollBar.Visible = needsScroll;
            }

            _pageScrollBar.LargeChange = Math.Max(1, _content.ClientSize.Height);
            _pageScrollBar.Maximum = Math.Max(0, _pageBody.Height - 1);
            var maxValue = Math.Max(0, _pageScrollBar.Maximum - _pageScrollBar.LargeChange + 1);
            if (_pageScrollBar.Value > maxValue) _pageScrollBar.Value = maxValue;
            _pageBody.Top = -_pageScrollBar.Value;
        }
        finally { _pageLayoutBusy = false; }
    }

    private void HookWheelEvents(Control parent)
    {
        if (_wheelHooked.Add(parent)) parent.MouseWheel += ScrollPageWithWheel;
        foreach (Control child in parent.Controls) HookWheelEvents(child);
    }

    private void ScrollPageWithWheel(object? sender, MouseEventArgs e)
    {
        if (!_pageScrollBar.Visible || e.Delta == 0) return;
        var lines = SystemInformation.MouseWheelScrollLines;
        var offset = lines == -1 ? Math.Max(1, _pageScrollBar.LargeChange - 1) : Math.Max(1, lines) * 32;
        var maxValue = Math.Max(0, _pageScrollBar.Maximum - _pageScrollBar.LargeChange + 1);
        _pageScrollBar.Value = Math.Clamp(_pageScrollBar.Value + (e.Delta > 0 ? -offset : offset), 0, maxValue);
        _pageBody.Top = -_pageScrollBar.Value;
    }

    private static void SetResponsiveWidth(Control control, int width)
    {
        if (control is Label or CheckBox)
        {
            control.MaximumSize = new Size(Math.Max(140, width), 0);
            return;
        }
        if (control is TableLayoutPanel table)
        {
            table.AutoSize = false;
            table.MinimumSize = new Size(width, Math.Max(38, table.MinimumSize.Height));
            table.Width = width;
            table.Height = Math.Max(38, table.Height);
            foreach (Control child in table.Controls)
                if (child.Dock != DockStyle.Fill) SetResponsiveWidth(child, Math.Max(120, width - 120));
            return;
        }
        if (control is ProgressBar)
        {
            control.Width = width;
            return;
        }
        if (control is FlowLayoutPanel flow)
        {
            flow.Width = width;
            foreach (Control child in flow.Controls) SetResponsiveWidth(child, width);
            return;
        }
        control.Width = width;
    }

    private void ResetPage()
    {
        _installStatusTimer.Stop();
        _installStatusLabel = null;
        foreach (var timer in _buttonColorAnimations.Values) { timer.Stop(); timer.Dispose(); }
        _buttonColorAnimations.Clear();
        _wheelHooked.Clear();
        foreach (Control button in _footerButtons.Controls.Cast<Control>().ToArray())
        {
            _footerButtons.Controls.Remove(button);
            button.Dispose();
        }
        foreach (Control control in _pageBody.Controls.Cast<Control>().ToArray())
        {
            _pageBody.Controls.Remove(control);
            control.Dispose();
        }
        _pageBody.Padding = new Padding(34, 28, 34, 30);
        _pageScrollBar.Value = 0;
        _pageScrollBar.Visible = false;
        _pageBody.Top = 0;
        LayoutPageBody();
    }

    private void AnimateButtonColor(Button button, Color target)
    {
        if (_buttonColorAnimations.Remove(button, out var previous)) { previous.Stop(); previous.Dispose(); }
        button.BackColor = target;
    }

    private void ShowWelcome()
    {
        ResetPage();
        AddLabel("LANGKAH 1 / 4", 8.5f, true, Color.FromArgb(114, 228, 191));
        AddLabel("Selamat datang", 23, true);
        AddLabel("Pasang Laptop Status di perangkat Windows ini.", 11, false, Color.FromArgb(167, 181, 197));
        CreateWelcomeStage();

        var overview = CreateCard();
        AddCardLabel(overview, "Sebelum memulai", 13, true);
        AddCardLabel(overview, "Wizard akan menyalin aplikasi ke folder yang Anda pilih, lalu menyiapkan pintasan dan opsi koneksi sesuai pilihan Anda.");

        var privacy = CreateCard();
        AddCardLabel(privacy, "Izin dan keamanan", 13, true);
        AddCardLabel(privacy, "Aplikasi berjalan dengan izin pengguna biasa agar fitur seperti drag-and-drop dapat digunakan. Windows hanya akan meminta izin tambahan ketika suatu tindakan memang membutuhkannya.");
        AddCardLabel(privacy, "Kontrol hanya tersedia untuk perangkat Android yang sudah dipasangkan.", 9, false, Color.FromArgb(167, 181, 197));

        AddButton("Batal", (_, _) => Close());
        AddButton("Berikutnya", (_, _) => ShowOptions(), primary: true);
        LayoutPageBody();
        UpdateWizardProgress(0);
        ApplyWizardTheme();
    }

    private void ShowOptions()
    {
        if (_webWizardActive && _webWizardReady)
        {
            _ = _webWizard.CoreWebView2.ExecuteScriptAsync("window.LaptopStatusInstaller&&window.LaptopStatusInstaller.showError('Periksa kembali pilihan instalasi lalu coba lagi.')");
            return;
        }
        ResetPage();
        AddLabel("LANGKAH 2 / 4", 8.5f, true, Color.FromArgb(114, 228, 191));
        AddLabel("Pilihan instalasi", 21, true);
        AddLabel("Atur lokasi aplikasi dan perilaku awalnya.", 10, false, Color.FromArgb(167, 181, 197));

        var folderCard = CreateCard();
        AddCardLabel(folderCard, "Folder aplikasi", 12, true);
        AddCardLabel(folderCard, "Pilih lokasi untuk menyimpan aplikasi Windows.", 9, false, Color.FromArgb(167, 181, 197));
        var pathRow = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            AutoSize = false,
            Size = new Size(Math.Max(240, _content.ClientSize.Width - 100), 38),
            MinimumSize = new Size(240, 38),
            Margin = new Padding(0, 2, 0, 4),
            BackColor = Color.Transparent
        };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        pathRow.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        _installPath = new TextBox
        {
            Text = GetPreviousInstallPath() ?? _defaultInstallPath,
            Dock = DockStyle.Fill,
            Margin = new Padding(0, 0, 10, 0),
            Font = new Font("Segoe UI", 9.5f),
            BackColor = Color.FromArgb(30, 39, 52),
            ForeColor = Color.White,
            BorderStyle = BorderStyle.FixedSingle
        };
        pathRow.Controls.Add(_installPath, 0, 0);
        var browseButton = new Button
        {
            Text = "Pilih…",
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            FlatStyle = FlatStyle.Flat,
            BackColor = Color.FromArgb(38, 49, 65),
            ForeColor = Color.White,
            UseVisualStyleBackColor = false,
            Cursor = Cursors.Hand
        };
        browseButton.FlatAppearance.BorderSize = 0;
        browseButton.Click += (_, _) => BrowseFolder();
        pathRow.Controls.Add(browseButton, 1, 0);
        AddToCard(folderCard, pathRow);

        var behavior = CreateCard();
        AddCardLabel(behavior, "Saat instalasi selesai", 12, true);
        _autoStart = CreateOption("Jalankan otomatis saat saya masuk ke Windows", true);
        _launchNow = CreateOption("Buka Laptop Status setelah instalasi selesai", true);
        _createDesktopShortcut = CreateOption("Buat shortcut di desktop", true);
        AddToCard(behavior, _autoStart);
        AddToCard(behavior, _launchNow);
        AddToCard(behavior, _createDesktopShortcut);

        var connection = CreateCard();
        AddCardLabel(connection, "Koneksi jarak jauh", 12, true);
        _installTailscale = CreateOption("Pasang Tailscale untuk koneksi jarak jauh", true);
        AddToCard(connection, _installTailscale);
        AddCardLabel(connection, "Tailscale dilewati bila sudah terpasang. Untuk menghubungkan laptop dan Android, login dengan akun Tailscale yang sama di kedua perangkat.", 9, false, Color.FromArgb(167, 181, 197));

        var display = CreateCard();
        AddCardLabel(display, "Layar virtual spacedesk", 12, true);
        _installSpacedeskOption = CreateOption("Tampilkan opsi pemasangan spacedesk setelah instalasi", false);
        AddToCard(display, _installSpacedeskOption);
        AddCardLabel(display, "Driver diunduh langsung dari situs spacedesk. Setelah driver terpasang dan layarnya aktif, Laptop Status akan memilih layar spacedesk otomatis; jika belum aktif, aplikasi memakai desktop virtual Windows.", 9, false, Color.FromArgb(167, 181, 197));

        var pinning = CreateCard();
        AddCardLabel(pinning, "Pintasan Windows", 12, true);
        _pinTaskbar = CreateOption("Sematkan ke taskbar (jika diizinkan Windows)", false);
        _pinStartMenu = CreateOption("Sematkan ke menu Start (jika diizinkan Windows)", false);
        AddToCard(pinning, _pinTaskbar);
        AddToCard(pinning, _pinStartMenu);
        AddCardLabel(pinning, "Windows mungkin meminta Anda menyematkan aplikasi secara manual. Tautan aplikasi tetap tersedia dari menu Start.", 9, false, Color.FromArgb(167, 181, 197));

        AddButton("Kembali", (_, _) => ShowWelcome());
        AddButton("Instal", (_, _) => Install(), primary: true);
        AddButton("Batal", (_, _) => Close());
        LayoutPageBody();
        UpdateWizardProgress(1);
        ApplyWizardTheme();
    }

    private string? GetPreviousInstallPath()
    {
        using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\LaptopStatus");
        var path = key?.GetValue("InstallLocation") as string;
        return !string.IsNullOrWhiteSpace(path) ? path : null;
    }

    private void BrowseFolder()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Pilih folder induk untuk memasang Laptop Status",
            SelectedPath = Path.GetDirectoryName(_installPath!.Text) ?? _defaultInstallPath
        };
        if (dialog.ShowDialog(this) == DialogResult.OK)
            _installPath!.Text = Path.Combine(dialog.SelectedPath, "LaptopStatus");
    }

    private async void Install()
    {
        try
        {
            var destination = Path.GetFullPath(_installPath!.Text.Trim());
            if (destination.Length < 8 || Path.GetPathRoot(destination)?.TrimEnd('\\') == destination.TrimEnd('\\'))
                throw new InvalidOperationException("Pilih folder instalasi yang bukan root drive.");
            var exePath = Path.Combine(destination, "LaptopStatus.exe");
            var uninstallerPath = Path.Combine(destination, "Uninstall-LaptopStatus.ps1");
            var existingFiles = new[] { exePath, uninstallerPath }.Where(File.Exists).ToArray();
            if (existingFiles.Length > 0)
            {
                var names = string.Join(Environment.NewLine, existingFiles.Select(Path.GetFileName));
                var choice = MessageBox.Show(this,
                    $"Folder ini sudah berisi berkas Laptop Status:\n\n{names}\n\nTimpa berkas aplikasi lama dengan versi baru?",
                    "Konfirmasi timpa aplikasi", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2);
                if (choice != DialogResult.Yes)
                {
                    if (_webWizardActive && _webWizardReady)
                        _ = _webWizard.CoreWebView2.ExecuteScriptAsync("window.LaptopStatusInstaller&&window.LaptopStatusInstaller.showError('Pemasangan dibatalkan. Anda bisa mengubah folder atau lanjutkan lagi.')");
                    return;
                }
                if (IsApplicationRunningFrom(exePath))
                {
                    const string message = "Laptop Status sedang berjalan dari folder ini. Keluar melalui ikon Laptop Status di area notifikasi, lalu jalankan installer lagi.";
                    if (_webWizardActive && _webWizardReady)
                        _ = _webWizard.CoreWebView2.ExecuteScriptAsync($"window.LaptopStatusInstaller&&window.LaptopStatusInstaller.showError({JsonSerializer.Serialize(message)})");
                    else
                        MessageBox.Show(this, message, "Tutup aplikasi sebelum memperbarui", MessageBoxButtons.OK, MessageBoxIcon.Information);
                    return;
                }
            }

            var autoStart = _autoStart!.Checked;
            _autoStartSelected = autoStart;
            _launchAfterInstall = _launchNow!.Checked;
            var installTailscale = _installTailscale!.Checked;
            _installTailscaleSelected = installTailscale;
            _installSpacedeskOptionSelected = _installSpacedeskOption!.Checked;
            var createDesktopShortcut = _createDesktopShortcut!.Checked;
            var pinTaskbar = _pinTaskbar!.Checked;
            var pinStartMenu = _pinStartMenu!.Checked;
            _taskbarPinRequested = pinTaskbar;
            _startPinRequested = pinStartMenu;
            ShowInstalling();
            await Task.Run(() => ExtractAndInstallFiles(destination));
            UpdateWebProgress(52);
            await Task.Run(() =>
            {
                if (installTailscale) InstallTailscaleIfNeeded();
                ConfigureConnectionFirewallRules(exePath);
            });
            UpdateWebProgress(83);

            ConfigureAutoStart(exePath, autoStart);
            var shortcutResult = CreateShortcuts(destination, autoStart, createDesktopShortcut, pinTaskbar, pinStartMenu);
            _desktopShortcutCreated = shortcutResult.DesktopShortcutCreated;
            _taskbarPinSucceeded = shortcutResult.TaskbarPinSucceeded;
            _startPinSucceeded = shortcutResult.StartPinSucceeded;
            RegisterUninstall(destination);
            UpdateWebProgress(95);
            ShowFinished();
        }
        catch (Exception ex)
        {
            _installStatusTimer.Stop();
            UseWaitCursor = false;
            if (_webWizardActive && _webWizardReady)
                _ = _webWizard.CoreWebView2.ExecuteScriptAsync($"window.LaptopStatusInstaller&&window.LaptopStatusInstaller.showError({JsonSerializer.Serialize(ex.Message)})");
            else
            {
                MessageBox.Show(this, ex.Message, "Instalasi gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
                ShowOptions();
            }
        }
    }

    private static bool IsApplicationRunningFrom(string exePath)
    {
        var fullPath = Path.GetFullPath(exePath);
        foreach (var process in Process.GetProcessesByName("LaptopStatus"))
        {
            using (process)
            {
                try
                {
                    if (string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), fullPath, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch { }
            }
        }
        return false;
    }

    private static void ExtractAndInstallFiles(string destination)
    {
        var payloadPath = Path.Combine(Path.GetTempPath(), "LaptopStatus-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(payloadPath);
        long expectedAppLength;
        try
        {
            using (var resource = Assembly.GetExecutingAssembly().GetManifestResourceStream("LaptopStatus.Payload.zip")
                ?? throw new FileNotFoundException("Berkas aplikasi tidak ditemukan di dalam installer."))
            using (var archive = new ZipArchive(resource, ZipArchiveMode.Read))
            {
                var appEntry = archive.GetEntry("LaptopStatus.exe")
                    ?? throw new InvalidDataException("Berkas LaptopStatus.exe tidak ada di dalam paket instalasi.");
                expectedAppLength = appEntry.Length;
                if (expectedAppLength < 10_000_000)
                    throw new InvalidDataException($"Paket aplikasi tidak lengkap: ukuran LaptopStatus.exe hanya {expectedAppLength:N0} byte.");
                archive.ExtractToDirectory(payloadPath);
            }

            Directory.CreateDirectory(destination);
            var sourceExe = Path.Combine(payloadPath, "LaptopStatus.exe");
            var destinationExe = Path.Combine(destination, "LaptopStatus.exe");
            if (!File.Exists(sourceExe) || new FileInfo(sourceExe).Length != expectedAppLength)
                throw new InvalidDataException("Berkas aplikasi hasil ekstraksi tidak lengkap. Jalankan ulang installer yang diunduh dengan sempurna.");
            File.Copy(sourceExe, destinationExe, overwrite: true);
            var installedLength = new FileInfo(destinationExe).Length;
            if (installedLength != expectedAppLength)
                throw new IOException($"Pemeriksaan instalasi gagal: EXE terpasang berukuran {installedLength:N0} byte, seharusnya {expectedAppLength:N0} byte. Berkas tidak dijalankan.");
            foreach (var sourceFile in Directory.EnumerateFiles(payloadPath, "*", SearchOption.AllDirectories))
            {
                var relativePath = Path.GetRelativePath(payloadPath, sourceFile);
                if (string.Equals(relativePath, "LaptopStatus.exe", StringComparison.OrdinalIgnoreCase)) continue;
                var targetFile = Path.Combine(destination, relativePath);
                Directory.CreateDirectory(Path.GetDirectoryName(targetFile)!);
                File.Copy(sourceFile, targetFile, overwrite: true);
            }
        }
        finally
        {
            if (Directory.Exists(payloadPath)) Directory.Delete(payloadPath, recursive: true);
        }
    }

    private static void InstallTailscaleIfNeeded()
    {
        if (IsTailscaleInstalled()) return;

        var stagingDirectory = Path.Combine(Path.GetTempPath(), "LaptopStatus-Tailscale-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingDirectory);
        var msiPath = Path.Combine(stagingDirectory, "tailscale-setup-amd64.msi");
        var verifierPath = Path.Combine(stagingDirectory, "verify-signature.ps1");
        try
        {
            using var client = new HttpClient { Timeout = TimeSpan.FromMinutes(3) };
            var packageUri = new Uri("https://pkgs.tailscale.com/stable/tailscale-setup-latest-amd64.msi");
            using var response = client.GetAsync(packageUri, HttpCompletionOption.ResponseHeadersRead).GetAwaiter().GetResult();
            response.EnsureSuccessStatusCode();
            var finalUri = response.RequestMessage?.RequestUri
                ?? throw new InvalidDataException("Alamat unduhan Tailscale tidak dapat diverifikasi.");
            if (finalUri.Scheme != Uri.UriSchemeHttps || !finalUri.Host.Equals("pkgs.tailscale.com", StringComparison.OrdinalIgnoreCase) ||
                !Regex.IsMatch(finalUri.AbsolutePath, @"/tailscale-setup-[0-9.]+-amd64\.msi$", RegexOptions.IgnoreCase))
                throw new InvalidDataException("Tailscale mengarahkan unduhan ke alamat yang tidak dikenal.");
            if (response.Content.Headers.ContentLength is long size && (size < 1_000_000 || size > 100_000_000))
                throw new InvalidDataException("Ukuran installer Tailscale di luar batas yang diharapkan.");

            using (var input = response.Content.ReadAsStream())
            using (var output = new FileStream(msiPath, FileMode.CreateNew, FileAccess.Write, FileShare.None))
            {
                input.CopyTo(output);
                output.Flush(flushToDisk: true);
            }

            var checksumText = client.GetStringAsync(new Uri(finalUri.AbsoluteUri + ".sha256")).GetAwaiter().GetResult();
            var checksumMatch = Regex.Match(checksumText, @"^\s*([0-9a-fA-F]{64})\b", RegexOptions.CultureInvariant);
            if (!checksumMatch.Success) throw new InvalidDataException("Checksum resmi Tailscale tidak terbaca.");
            string actualChecksum;
            using (var stream = File.OpenRead(msiPath))
                actualChecksum = Convert.ToHexString(SHA256.HashData(stream));
            if (!actualChecksum.Equals(checksumMatch.Groups[1].Value, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Checksum installer Tailscale tidak cocok; berkas tidak dipasang.");

            VerifyTailscaleSignature(msiPath, verifierPath);
            var installer = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "msiexec.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true
            };
            installer.ArgumentList.Add("/i");
            installer.ArgumentList.Add(msiPath);
            installer.ArgumentList.Add("/passive");
            installer.ArgumentList.Add("/norestart");
            using var process = Process.Start(installer)
                ?? throw new InvalidOperationException("Installer Tailscale tidak dapat dijalankan.");
            process.WaitForExit();
            if (process.ExitCode is not (0 or 3010) && !IsTailscaleInstalled())
                throw new InvalidOperationException($"Pemasangan Tailscale gagal (kode {process.ExitCode}).");
        }
        finally
        {
            TryDeleteFile(msiPath);
            TryDeleteFile(verifierPath);
            try { if (Directory.Exists(stagingDirectory) && !Directory.EnumerateFileSystemEntries(stagingDirectory).Any()) Directory.Delete(stagingDirectory); }
            catch { }
        }
    }

    private static bool IsTailscaleInstalled()
    {
        foreach (var root in new[]
        {
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
        })
        {
            if (!string.IsNullOrWhiteSpace(root) && File.Exists(Path.Combine(root, "Tailscale", "tailscale.exe")))
                return true;
        }
        return false;
    }

    private static void ConfigureConnectionFirewallRules(string appPath)
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), "LaptopStatus-Firewall-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(temporaryDirectory);
        var scriptPath = Path.Combine(temporaryDirectory, "configure-firewall.ps1");
        try
        {
            File.WriteAllText(scriptPath,
                "$ErrorActionPreference = 'Stop'\n" +
                "$lanRule = 'LaptopStatus local network'\n" +
                "$tailscaleRule = 'LaptopStatus Tailscale remote'\n" +
                "Get-NetFirewallRule -DisplayName $lanRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue\n" +
                "Get-NetFirewallRule -DisplayName $tailscaleRule -ErrorAction SilentlyContinue | Remove-NetFirewallRule -ErrorAction SilentlyContinue\n" +
                "New-NetFirewallRule -DisplayName $lanRule -Direction Inbound -Action Allow -Program $args[0] -Protocol TCP -LocalPort 8765,8767 -RemoteAddress LocalSubnet -Profile Private,Domain | Out-Null\n" +
                "New-NetFirewallRule -DisplayName $tailscaleRule -Direction Inbound -Action Allow -Program $args[0] -Protocol TCP -LocalPort 8765,8767 -RemoteAddress '100.64.0.0/10' -Profile Any | Out-Null\n",
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe"))
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardError = true,
                RedirectStandardOutput = true
            };
            startInfo.ArgumentList.Add("-NoProfile");
            startInfo.ArgumentList.Add("-NonInteractive");
            startInfo.ArgumentList.Add("-ExecutionPolicy");
            startInfo.ArgumentList.Add("Bypass");
            startInfo.ArgumentList.Add("-File");
            startInfo.ArgumentList.Add(scriptPath);
            startInfo.ArgumentList.Add(appPath);
            using var process = Process.Start(startInfo)
                ?? throw new InvalidOperationException("Pengaturan Windows Firewall untuk LAN/Tailscale tidak dapat dijalankan.");
            var stdout = process.StandardOutput.ReadToEnd();
            var stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0)
                throw new InvalidOperationException("Aturan Windows Firewall LAN/Tailscale gagal dibuat. " + (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim());
        }
        finally
        {
            TryDeleteFile(scriptPath);
            try { if (Directory.Exists(temporaryDirectory) && !Directory.EnumerateFileSystemEntries(temporaryDirectory).Any()) Directory.Delete(temporaryDirectory); }
            catch { }
        }
    }

    private static void VerifyTailscaleSignature(string msiPath, string verifierPath)
    {
        File.WriteAllText(verifierPath,
            "$signature = Get-AuthenticodeSignature -LiteralPath $args[0]\n" +
            "if ($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate -or $signature.SignerCertificate.Subject -notmatch '(?i)Tailscale Inc') { exit 12 }\n" +
            "exit 0\n",
            new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        var powershellPath = Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", "powershell.exe");
        var verify = new ProcessStartInfo(powershellPath)
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true
        };
        verify.ArgumentList.Add("-NoProfile");
        verify.ArgumentList.Add("-NonInteractive");
        verify.ArgumentList.Add("-ExecutionPolicy");
        verify.ArgumentList.Add("Bypass");
        verify.ArgumentList.Add("-File");
        verify.ArgumentList.Add(verifierPath);
        verify.ArgumentList.Add(msiPath);
        using var process = Process.Start(verify)
            ?? throw new InvalidOperationException("Pemeriksaan tanda tangan Tailscale tidak dapat dijalankan.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
            throw new InvalidDataException("Tanda tangan installer bukan milik Tailscale Inc. " + (string.IsNullOrWhiteSpace(stderr) ? stdout : stderr).Trim());
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); }
        catch { }
    }

    private void ShowInstalling()
    {
        if (_webWizardActive && _webWizardReady)
        {
            UseWaitCursor = true;
            _installStatusIndex = 0;
            _ = _webWizard.CoreWebView2.ExecuteScriptAsync("window.LaptopStatusInstaller&&window.LaptopStatusInstaller.setInstallProgress(8)");
            UpdateWizardProgress(2);
            return;
        }
        ResetPage();
        AddLabel("LANGKAH 3 / 4", 8.5f, true, Color.FromArgb(114, 228, 191));
        AddLabel("Sedang memasang", 22, true);
        AddLabel("Tunggu sebentar sementara berkas dan pengaturan disiapkan.", 10, false, Color.FromArgb(167, 181, 197));
        var progressCard = CreateCard();
        AddCardLabel(progressCard, "Proses instalasi", 13, true);
        _installStatusIndex = 0;
        _installStatusLabel = CreateLabel(ActiveInstallStatusMessages[0], 11);
        AddToCard(progressCard, _installStatusLabel);
        var progress = new ProgressBar
        {
            Style = ProgressBarStyle.Marquee,
            MarqueeAnimationSpeed = 25,
            Height = 14,
            Margin = new Padding(0, 4, 0, 12),
            ForeColor = Color.FromArgb(114, 228, 191),
            BackColor = Color.FromArgb(30, 39, 52)
        };
        AddToCard(progressCard, progress);
        AddCardLabel(progressCard, "Jangan tutup wizard sampai pengaturan selesai.", 9, false, Color.FromArgb(167, 181, 197));
        UseWaitCursor = true;
        LayoutPageBody();
        UpdateWizardProgress(2);
        ApplyWizardTheme();
        _installStatusTimer.Start();
    }

    private void UpdateWebProgress(double progress)
    {
        if (!_webWizardActive || !_webWizardReady) return;
        _ = _webWizard.CoreWebView2.ExecuteScriptAsync($"window.LaptopStatusInstaller&&window.LaptopStatusInstaller.setInstallProgress({Math.Clamp(progress, 0, 100).ToString(System.Globalization.CultureInfo.InvariantCulture)})");
    }

    private static void ConfigureAutoStart(string exePath, bool enabled)
    {
        if (!enabled)
        {
            RunTaskScheduler(new[] { "/Delete", "/TN", @"\LaptopStatus", "/F" }, allowFailure: true);
            return;
        }

        var user = WindowsIdentity.GetCurrent();
        var userSid = user.User?.Value ?? throw new InvalidOperationException("Windows tidak dapat menentukan akun untuk auto-start.");
        var ns = (XNamespace)"http://schemas.microsoft.com/windows/2004/02/mit/task";
        var task = new XDocument(
            new XElement(ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(ns + "RegistrationInfo",
                    new XElement(ns + "Description", "Menjalankan layanan Laptop Status saat akun Windows masuk.")),
                new XElement(ns + "Triggers",
                    new XElement(ns + "LogonTrigger", new XElement(ns + "Enabled", "true"))),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "DisallowStartIfOnBatteries", "false"),
                    new XElement(ns + "StopIfGoingOnBatteries", "false"),
                    new XElement(ns + "AllowHardTerminate", "true"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "RunOnlyIfNetworkAvailable", "false"),
                    new XElement(ns + "IdleSettings",
                        new XElement(ns + "StopOnIdleEnd", "false"),
                        new XElement(ns + "RestartOnIdle", "false")),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "Hidden", "false"),
                    new XElement(ns + "RunOnlyIfIdle", "false"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S"),
                    new XElement(ns + "Priority", "7")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", Path.GetFullPath(exePath)),
                        new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? "")))));

        var taskPath = Path.Combine(Path.GetTempPath(), "LaptopStatus-Task-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            // schtasks.exe expects its XML input in UTF-16 little-endian on Windows.
            // XDocument.Save(path) emits UTF-8, which schtasks reports as malformed at (1,2).
            var xmlSettings = new XmlWriterSettings
            {
                Encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
                Indent = true,
                OmitXmlDeclaration = false
            };
            using (var writer = XmlWriter.Create(taskPath, xmlSettings))
                task.Save(writer);

            var validatedTask = XDocument.Load(taskPath);
            if (validatedTask.Root?.Name != ns + "Task")
                throw new InvalidDataException("Definisi auto-start tidak membentuk elemen Task yang valid.");

            // /F replaces an existing task only after Windows accepts the new definition.
            // Avoid deleting a working auto-start task before the replacement is validated.
            RunTaskScheduler(new[] { "/Create", "/TN", @"\LaptopStatus", "/XML", taskPath, "/F" });
        }
        finally
        {
            if (File.Exists(taskPath)) File.Delete(taskPath);
        }
    }

    private static void RunTaskScheduler(IEnumerable<string> arguments, bool allowFailure = false)
    {
        var startInfo = new ProcessStartInfo(Path.Combine(Environment.SystemDirectory, "schtasks.exe"))
        {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };
        foreach (var argument in arguments) startInfo.ArgumentList.Add(argument);
        using var process = Process.Start(startInfo) ?? throw new InvalidOperationException("Task Scheduler Windows tidak dapat dijalankan.");
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 && !allowFailure)
            throw new InvalidOperationException(string.IsNullOrWhiteSpace(stderr) ? stdout : stderr);
    }

    private sealed record ShortcutInstallResult(bool DesktopShortcutCreated, bool TaskbarPinSucceeded, bool StartPinSucceeded);

    private static ShortcutInstallResult CreateShortcuts(string installDir, bool useScheduledTask, bool createDesktopShortcut, bool pinTaskbar, bool pinStartMenu)
    {
        var group = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs), "Laptop Status");
        Directory.CreateDirectory(group);
        var shellType = Type.GetTypeFromProgID("WScript.Shell") ?? throw new InvalidOperationException("Windows tidak dapat membuat shortcut.");
        dynamic shell = Activator.CreateInstance(shellType)!;
        var appLinkPath = Path.Combine(group, "Laptop Status.lnk");
        CreateApplicationShortcut(shell, appLinkPath, installDir, useScheduledTask);
        dynamic uninstallLink = shell.CreateShortcut(Path.Combine(group, "Hapus Laptop Status.lnk"));
        uninstallLink.TargetPath = "powershell.exe";
        uninstallLink.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(installDir, "Uninstall-LaptopStatus.ps1") + "\"";
        uninstallLink.Save();

        var desktopCreated = false;
        if (createDesktopShortcut)
        {
            var desktopLink = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Laptop Status.lnk");
            CreateApplicationShortcut(shell, desktopLink, installDir, useScheduledTask);
            desktopCreated = true;
        }

        var taskbarPinned = pinTaskbar && TryPinShortcut(appLinkPath, "taskbar");
        var startPinned = pinStartMenu && TryPinShortcut(appLinkPath, "start");
        return new ShortcutInstallResult(desktopCreated, taskbarPinned, startPinned);
    }

    private static void CreateApplicationShortcut(dynamic shell, string path, string installDir, bool useScheduledTask)
    {
        dynamic appLink = shell.CreateShortcut(path);
        appLink.TargetPath = useScheduledTask ? Path.Combine(Environment.SystemDirectory, "schtasks.exe") : Path.Combine(installDir, "LaptopStatus.exe");
        appLink.Arguments = useScheduledTask ? @"/Run /TN \LaptopStatus" : "";
        appLink.WorkingDirectory = installDir;
        appLink.IconLocation = Path.Combine(installDir, "LaptopStatus.exe");
        appLink.Description = "Pemantauan dan kendali laptop";
        appLink.Save();
    }

    private static bool TryPinShortcut(string shortcutPath, string target)
    {
        try
        {
            var shellType = Type.GetTypeFromProgID("Shell.Application");
            if (shellType is null) return false;
            dynamic shell = Activator.CreateInstance(shellType)!;
            dynamic? folder = shell.Namespace(Path.GetDirectoryName(shortcutPath));
            dynamic? item = folder?.ParseName(Path.GetFileName(shortcutPath));
            if (item is null) return false;
            dynamic verbs = item.Verbs();
            for (var index = 0; index < (int)verbs.Count; index++)
            {
                dynamic verb = verbs.Item(index);
                var name = ((string)verb.Name).Replace("&", "", StringComparison.Ordinal).Trim();
                var normalized = name.ToLowerInvariant();
                var destinationMatches = target == "taskbar"
                    ? normalized.Contains("taskbar") || normalized.Contains("bilah tugas")
                    : normalized.Contains("start") || normalized.Contains("mulai");
                if (!destinationMatches) continue;
                if (normalized.Contains("unpin") || normalized.Contains("lepas sematan")) return true;
                if (!normalized.Contains("pin") && !normalized.Contains("sematkan")) continue;
                verb.DoIt();
                return true;
            }
        }
        catch { }
        return false;
    }

    private static void RegisterUninstall(string installDir)
    {
        using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\LaptopStatus", writable: true);
        key.SetValue("DisplayName", "Laptop Status");
        key.SetValue("DisplayVersion", "2.11.38");
        key.SetValue("Publisher", "PT HAHA NASIONAL");
        key.SetValue("InstallLocation", installDir);
        key.SetValue("UninstallString", "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(installDir, "Uninstall-LaptopStatus.ps1") + "\"");
    }

    private void ShowFinished()
    {
        if (_webWizardActive && _webWizardReady)
        {
            _installStatusTimer.Stop();
            UseWaitCursor = false;
            var taskbar = _taskbarPinRequested ? _taskbarPinSucceeded ? "Taskbar berhasil disematkan." : "Sematkan aplikasi lewat menu Start bila Windows menolak." : null;
            var start = _startPinRequested ? _startPinSucceeded ? "Pintasan menu Start berhasil disematkan." : "Sematkan aplikasi manual dari menu Start bila Windows menolak." : null;
            var payload = JsonSerializer.Serialize(new { desktop = _desktopShortcutCreated, taskbar, start, spacedesk = _installSpacedeskOptionSelected });
            _ = _webWizard.CoreWebView2.ExecuteScriptAsync($"window.LaptopStatusInstaller&&window.LaptopStatusInstaller.finishInstall({payload})");
            UpdateWizardProgress(3);
            return;
        }
        ResetPage();
        AddLabel("LANGKAH 4 / 4", 8.5f, true, Color.FromArgb(114, 228, 191));
        AddLabel("Instalasi selesai", 22, true);
        AddLabel("Laptop Status sudah dipasang di perangkat ini.", 11, false, Color.FromArgb(167, 181, 197));

        var nextSteps = CreateCard();
        AddCardLabel(nextSteps, "Langkah berikutnya", 13, true);
        AddCardLabel(nextSteps, "Untuk koneksi jarak jauh, buka Tailscale di laptop dan Android lalu login dengan akun yang sama. Setelah tersambung, pindai QR dari Ringkasan Windows di Android dan setujui pairing di laptop.");

        var shortcutSummary = _desktopShortcutCreated ? "Shortcut desktop dibuat." : "Shortcut desktop tidak dibuat.";
        var pinSummary = new List<string>();
        if (_taskbarPinRequested) pinSummary.Add(_taskbarPinSucceeded ? "Taskbar: disematkan." : "Taskbar: sematkan lewat menu Start bila Windows menolak.");
        if (_startPinRequested) pinSummary.Add(_startPinSucceeded ? "Menu Start: disematkan." : "Menu Start: sematkan manual dari daftar aplikasi bila Windows menolak.");
        var shortcutCard = CreateCard();
        AddCardLabel(shortcutCard, "Pintasan", 13, true);
        AddCardLabel(shortcutCard, shortcutSummary + (pinSummary.Count > 0 ? "\n" + string.Join("\n", pinSummary) : ""), 9);

        if (_installSpacedeskOptionSelected)
        {
            var spacedesk = CreateCard();
            AddCardLabel(spacedesk, "Layar virtual spacedesk", 13, true);
            AddCardLabel(spacedesk, "Unduh driver dari situs resmi spacedesk. Installer spacedesk akan menampilkan persetujuan dan meminta izin administratornya sendiri. Windows mungkin meminta restart setelah pemasangan.");
            AddButton("Buka unduhan spacedesk", (_, _) => OpenSpacedeskDownloadPage());
        }

        AddButton("Selesai", async (_, _) =>
        {
            try
            {
                var installDir = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\LaptopStatus")?.GetValue("InstallLocation") as string;
                if (_launchAfterInstall && !string.IsNullOrWhiteSpace(installDir))
                    await LaunchInstalledAppAsync(installDir);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Instalasi sudah selesai, tetapi Laptop Status belum berhasil dibuka. Periksa izin Windows lalu tekan Selesai untuk mencoba lagi.\n\n{ex.Message}",
                    "Aplikasi belum terbuka", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }, primary: true);
        UseWaitCursor = false;
        LayoutPageBody();
        UpdateWizardProgress(3);
        ApplyWizardTheme();
    }

    private static void OpenSpacedeskDownloadPage()
    {
        Process.Start(new ProcessStartInfo("https://www.spacedesk.net/download/") { UseShellExecute = true });
    }

    private async Task LaunchInstalledAppAsync(string installDir)
    {
        var exePath = Path.Combine(installDir, "LaptopStatus.exe");
        var exeInfo = new FileInfo(exePath);
        if (!exeInfo.Exists || exeInfo.Length < 10_000_000)
            throw new InvalidDataException($"EXE aplikasi tidak valid ({(exeInfo.Exists ? exeInfo.Length : 0):N0} byte) di:\n{exePath}");

        if (_autoStartSelected)
        {
            try { await Task.Run(() => RunTaskScheduler(new[] { "/Run", "/TN", @"\LaptopStatus" })); }
            catch { }
            if (await WaitForApplicationAsync(exePath, TimeSpan.FromSeconds(5))) return;
        }

        await LaunchThroughLimitedTaskAsync(exePath);
        if (await WaitForApplicationAsync(exePath, TimeSpan.FromSeconds(12))) return;
        throw new InvalidOperationException("Windows belum menjalankan aplikasi dengan izin pengguna biasa.");
    }

    private async Task LaunchThroughLimitedTaskAsync(string exePath)
    {
        var taskName = @"\LaptopStatus-FirstLaunch-" + Guid.NewGuid().ToString("N");
        var userSid = WindowsIdentity.GetCurrent().User?.Value
            ?? throw new InvalidOperationException("Windows tidak dapat menentukan akun untuk membuka aplikasi.");
        var ns = (XNamespace)"http://schemas.microsoft.com/windows/2004/02/mit/task";
        var task = new XDocument(
            new XElement(ns + "Task",
                new XAttribute("version", "1.2"),
                new XElement(ns + "Principals",
                    new XElement(ns + "Principal",
                        new XAttribute("id", "Author"),
                        new XElement(ns + "UserId", userSid),
                        new XElement(ns + "LogonType", "InteractiveToken"),
                        new XElement(ns + "RunLevel", "LeastPrivilege"))),
                new XElement(ns + "Settings",
                    new XElement(ns + "MultipleInstancesPolicy", "IgnoreNew"),
                    new XElement(ns + "StartWhenAvailable", "true"),
                    new XElement(ns + "AllowStartOnDemand", "true"),
                    new XElement(ns + "Enabled", "true"),
                    new XElement(ns + "ExecutionTimeLimit", "PT0S")),
                new XElement(ns + "Actions",
                    new XAttribute("Context", "Author"),
                    new XElement(ns + "Exec",
                        new XElement(ns + "Command", Path.GetFullPath(exePath)),
                        new XElement(ns + "WorkingDirectory", Path.GetDirectoryName(Path.GetFullPath(exePath)) ?? "")))));
        var taskPath = Path.Combine(Path.GetTempPath(), "LaptopStatus-Launch-" + Guid.NewGuid().ToString("N") + ".xml");
        try
        {
            using (var writer = XmlWriter.Create(taskPath, new XmlWriterSettings
            {
                Encoding = new UnicodeEncoding(bigEndian: false, byteOrderMark: true),
                Indent = true,
                OmitXmlDeclaration = false
            })) task.Save(writer);

            await Task.Run(() => RunTaskScheduler(new[] { "/Create", "/TN", taskName, "/XML", taskPath, "/F" }));
            await Task.Run(() => RunTaskScheduler(new[] { "/Run", "/TN", taskName }));
            if (!await WaitForApplicationAsync(exePath, TimeSpan.FromSeconds(12)))
                throw new InvalidOperationException("Windows tidak memulai Laptop Status melalui Task Scheduler.");
        }
        finally
        {
            try { await Task.Run(() => RunTaskScheduler(new[] { "/Delete", "/TN", taskName, "/F" }, allowFailure: true)); }
            catch { }
            TryDeleteFile(taskPath);
        }
    }

    private static async Task<bool> WaitForApplicationAsync(string exePath, TimeSpan timeout)
    {
        var fullPath = Path.GetFullPath(exePath);
        var timer = Stopwatch.StartNew();
        while (timer.Elapsed < timeout)
        {
            foreach (var process in Process.GetProcessesByName("LaptopStatus"))
            {
                using (process)
                {
                    try
                    {
                        if (string.Equals(Path.GetFullPath(process.MainModule?.FileName ?? ""), fullPath, StringComparison.OrdinalIgnoreCase))
                            return true;
                    }
                    catch { }
                }
            }
            await Task.Delay(200);
        }
        return false;
    }

}

internal sealed class BrandMark : Control
{
    private double _motionPhase;

    public BrandMark()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
    }

    public void SetMotionPhase(double phase)
    {
        _motionPhase = phase;
        Invalidate();
    }

    protected override void OnPaint(PaintEventArgs e)
    {
        base.OnPaint(e);
        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
        e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
        var scale = Math.Min(Width, Height) / 48f;
        e.Graphics.TranslateTransform((Width - 48 * scale) / 2f, (Height - 48 * scale) / 2f);
        e.Graphics.ScaleTransform(scale, scale);

        using var tilePath = RoundedRectangle(new RectangleF(1, 1, 46, 46), 12);
        using var tile = new SolidBrush(Color.FromArgb(255, 17, 28, 40));
        using var tileEdge = new Pen(Color.FromArgb(255, 54, 82, 99), 1.4f);
        e.Graphics.FillPath(tile, tilePath);
        e.Graphics.DrawPath(tileEdge, tilePath);

        using var mint = new Pen(Color.FromArgb(255, 114, 228, 191), 2.7f)
        {
            StartCap = LineCap.Round,
            EndCap = LineCap.Round,
            LineJoin = LineJoin.Round
        };
        using var screen = RoundedRectangle(new RectangleF(10, 13, 28, 20), 4);
        e.Graphics.DrawPath(mint, screen);
        e.Graphics.DrawLine(mint, 21, 35, 21, 38);
        e.Graphics.DrawLine(mint, 27, 35, 27, 38);
        e.Graphics.DrawLine(mint, 16, 40, 32, 40);
        var pulse = (Math.Sin(_motionPhase) + 1d) / 2d;
        using var dotGlow = new SolidBrush(Color.FromArgb((int)(22 + pulse * 48), 114, 228, 191));
        e.Graphics.FillEllipse(dotGlow, 32.5f, 5.5f, 10, 10);
        using var dot = new SolidBrush(Color.FromArgb((int)(175 + pulse * 80), 114, 228, 191));
        e.Graphics.FillEllipse(dot, 34, 7, 7, 7);
        e.Graphics.ResetTransform();
    }

    private static GraphicsPath RoundedRectangle(RectangleF bounds, float radius)
    {
        var diameter = radius * 2;
        var path = new GraphicsPath();
        path.AddArc(bounds.Left, bounds.Top, diameter, diameter, 180, 90);
        path.AddArc(bounds.Right - diameter, bounds.Top, diameter, diameter, 270, 90);
        path.AddArc(bounds.Right - diameter, bounds.Bottom - diameter, diameter, diameter, 0, 90);
        path.AddArc(bounds.Left, bounds.Bottom - diameter, diameter, diameter, 90, 90);
        path.CloseFigure();
        return path;
    }
}

using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Security.Principal;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Xml.Linq;
using Timer = System.Windows.Forms.Timer;

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
    private readonly Panel _content = new() { Dock = DockStyle.Fill, Padding = Padding.Empty, BackColor = Color.FromArgb(18, 24, 33) };
    private readonly Panel _header = new() { Dock = DockStyle.Top, Height = 76, BackColor = Color.FromArgb(12, 18, 26) };
    private readonly Panel _footer = new() { Dock = DockStyle.Bottom, Height = 72, BackColor = Color.FromArgb(12, 18, 26), Padding = new Padding(0, 12, 0, 12) };
    private readonly VScrollBar _pageScrollBar = new() { Dock = DockStyle.Right, Width = SystemInformation.VerticalScrollBarWidth, Visible = false, SmallChange = 32 };
    private readonly FlowLayoutPanel _footerButtons = new() { Dock = DockStyle.Fill, AutoSize = false, FlowDirection = FlowDirection.LeftToRight, WrapContents = false, Padding = new Padding(0, 0, 24, 0), BackColor = Color.Transparent };
    private readonly FlowLayoutPanel _pageBody = new() { Location = Point.Empty, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink, Padding = new Padding(28, 24, 28, 28), BackColor = Color.FromArgb(18, 24, 33) };
    private readonly string _defaultInstallPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "LaptopStatus");
    private readonly Timer _installStatusTimer = new() { Interval = 1050 };
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
        var formWidth = Math.Max(Math.Min(360, workArea.Width), Math.Min(820, workArea.Width - 48));
        var formHeight = Math.Max(Math.Min(360, workArea.Height), Math.Min(700, workArea.Height - 48));
        ClientSize = new Size(formWidth, formHeight);
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MinimumSize = new Size(Math.Min(620, formWidth), Math.Min(440, formHeight));
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        BackColor = Color.FromArgb(18, 24, 33);
        ForeColor = Color.FromArgb(232, 237, 244);
        Font = new Font("Segoe UI", 10);
        try
        {
            using var iconStream = Assembly.GetExecutingAssembly().GetManifestResourceStream("LaptopStatus.Brand.ico");
            if (iconStream is not null) Icon = new Icon(iconStream);
        }
        catch { }
        var brandMark = new BrandMark { Location = new Point(28, 14), Size = new Size(48, 48) };
        _header.Controls.Add(brandMark);
        _header.Controls.Add(new Label
        {
            Text = "Laptop Status",
            Location = new Point(89, 13),
            Size = new Size(360, 28),
            ForeColor = Color.FromArgb(241, 246, 252),
            Font = new Font("Segoe UI Semibold", 16, FontStyle.Bold)
        });
        _header.Controls.Add(new Label
        {
            Text = "Pemantauan dan kendali laptop",
            Location = new Point(91, 42),
            Size = new Size(380, 21),
            ForeColor = Color.FromArgb(167, 181, 197),
            Font = new Font("Segoe UI", 9)
        });
        _header.Controls.Add(new Label
        {
            Text = "PT HAHA NASIONAL",
            Anchor = AnchorStyles.Top | AnchorStyles.Right,
            Location = new Point(Math.Max(470, formWidth - 205), 28),
            Size = new Size(170, 24),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.FromArgb(114, 228, 191),
            Font = new Font("Segoe UI Semibold", 9, FontStyle.Bold)
        });
        _installStatusTimer.Tick += (_, _) =>
        {
            if (_installStatusLabel is null || _installStatusLabel.IsDisposed) return;
            _installStatusIndex = (_installStatusIndex + 1) % ActiveInstallStatusMessages.Length;
            _installStatusLabel.Text = ActiveInstallStatusMessages[_installStatusIndex];
        };
        _content.Controls.Add(_pageBody);
        _pageScrollBar.Scroll += (_, _) => _pageBody.Top = -_pageScrollBar.Value;
        _content.ClientSizeChanged += (_, _) => LayoutPageBody();
        _footer.Controls.Add(_footerButtons);
        _footer.SizeChanged += (_, _) => LayoutFooterButtons();
        Controls.Add(_content);
        Controls.Add(_pageScrollBar);
        Controls.Add(_footer);
        Controls.Add(_header);
        Shown += (_, _) => LayoutPageBody();
        LayoutPageBody();
        ShowWelcome();
    }

    private Label CreateLabel(string text, float size = 10, bool bold = false, Color? color = null)
    {
        var font = new Font("Segoe UI", size, bold ? FontStyle.Bold : FontStyle.Regular);
        return new Label
        {
            Text = text,
            AutoSize = true,
            AutoEllipsis = false,
            UseMnemonic = false,
            TextAlign = ContentAlignment.TopLeft,
            ForeColor = color ?? ForeColor,
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
            ForeColor = ForeColor,
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
            BackColor = primary ? Color.FromArgb(53, 170, 135) : Color.FromArgb(38, 49, 65),
            ForeColor = Color.White,
            UseVisualStyleBackColor = false
        };
        button.FlatAppearance.BorderSize = 0;
        button.FlatAppearance.MouseDownBackColor = Color.FromArgb(39, 148, 119);
        button.Tag = button.BackColor;
        button.Cursor = Cursors.Hand;
        button.MouseEnter += (_, _) => AnimateButtonColor(button, primary ? Color.FromArgb(91, 204, 167) : Color.FromArgb(52, 69, 90));
        button.MouseLeave += (_, _) => AnimateButtonColor(button, (Color)button.Tag);
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
            BackColor = Color.FromArgb(25, 35, 49),
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
        _pageBody.Padding = new Padding(28, 24, 28, 28);
        _pageScrollBar.Value = 0;
        _pageScrollBar.Visible = false;
        _pageBody.Top = 0;
        LayoutPageBody();
    }

    private void AnimateButtonColor(Button button, Color target)
    {
        if (_buttonColorAnimations.Remove(button, out var previous)) { previous.Stop(); previous.Dispose(); }
        var initial = button.BackColor;
        var started = Stopwatch.StartNew();
        var timer = new Timer { Interval = 15 };
        _buttonColorAnimations[button] = timer;
        timer.Tick += (_, _) =>
        {
            var progress = Math.Min(1d, started.Elapsed.TotalMilliseconds / 150d);
            var eased = 1d - Math.Pow(1d - progress, 3d);
            button.BackColor = Color.FromArgb(
                (int)(initial.R + (target.R - initial.R) * eased),
                (int)(initial.G + (target.G - initial.G) * eased),
                (int)(initial.B + (target.B - initial.B) * eased));
            if (progress < 1d) return;
            timer.Stop();
            timer.Dispose();
            if (_buttonColorAnimations.TryGetValue(button, out var active) && ReferenceEquals(active, timer))
                _buttonColorAnimations.Remove(button);
        };
        timer.Start();
    }

    private void ShowWelcome()
    {
        ResetPage();
        AddLabel("Selamat datang", 23, true);
        AddLabel("Pasang Laptop Status di perangkat Windows ini.", 11, false, Color.FromArgb(167, 181, 197));

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
    }

    private void ShowOptions()
    {
        ResetPage();
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
                if (choice != DialogResult.Yes) return;
                if (IsApplicationRunningFrom(exePath))
                {
                    MessageBox.Show(this, "Laptop Status sedang berjalan dari folder ini. Keluar melalui ikon Laptop Status di area notifikasi, lalu jalankan installer lagi.",
                        "Tutup aplikasi sebelum memperbarui", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
            await Task.Run(() =>
            {
                if (installTailscale) InstallTailscaleIfNeeded();
                ConfigureConnectionFirewallRules(exePath);
            });

            ConfigureAutoStart(exePath, autoStart);
            var shortcutResult = CreateShortcuts(destination, autoStart, createDesktopShortcut, pinTaskbar, pinStartMenu);
            _desktopShortcutCreated = shortcutResult.DesktopShortcutCreated;
            _taskbarPinSucceeded = shortcutResult.TaskbarPinSucceeded;
            _startPinSucceeded = shortcutResult.StartPinSucceeded;
            RegisterUninstall(destination);
            ShowFinished();
        }
        catch (Exception ex)
        {
            _installStatusTimer.Stop();
            UseWaitCursor = false;
            MessageBox.Show(this, ex.Message, "Instalasi gagal", MessageBoxButtons.OK, MessageBoxIcon.Error);
            ShowOptions();
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
        ResetPage();
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
        _installStatusTimer.Start();
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
        key.SetValue("DisplayVersion", "2.11.30");
        key.SetValue("Publisher", "PT HAHA NASIONAL");
        key.SetValue("InstallLocation", installDir);
        key.SetValue("UninstallString", "powershell.exe -NoProfile -ExecutionPolicy Bypass -File \"" + Path.Combine(installDir, "Uninstall-LaptopStatus.ps1") + "\"");
    }

    private void ShowFinished()
    {
        ResetPage();
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
    public BrandMark()
    {
        SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.SupportsTransparentBackColor, true);
        BackColor = Color.Transparent;
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
        using var dot = new SolidBrush(Color.FromArgb(255, 114, 228, 191));
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

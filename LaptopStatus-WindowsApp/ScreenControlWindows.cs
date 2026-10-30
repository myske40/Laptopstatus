using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using MediaColor = System.Windows.Media.Color;
using MediaBrushes = System.Windows.Media.Brushes;
using DrawingPixelFormat = System.Drawing.Imaging.PixelFormat;
using DrawingPoint = System.Drawing.Point;

namespace LaptopStatus
{
    internal sealed class ScreenApprovalWindow : Window
    {
        public ScreenApprovalWindow()
        {
            Title = "Persetujuan Kendali Layar";
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(520, Math.Max(340, workArea.Width - 40));
            MinWidth = Math.Min(420, Width);
            MinHeight = Math.Min(230, Math.Max(180, workArea.Height - 48));
            SizeToContent = SizeToContent.Height;
            MaxHeight = Math.Max(220, workArea.Height - 48);
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            Background = new SolidColorBrush(MediaColor.FromRgb(19, 26, 36));
            Foreground = MediaBrushes.White;
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new StackPanel
                {
                    Margin = new Thickness(24),
                    Children =
                    {
                        new TextBlock { Text = "Permintaan kendali layar", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) },
                        new TextBlock { Text = "Aplikasi Status Perangkat Android meminta melihat dan mengendalikan mouse serta keyboard Windows selama maksimal 10 menit.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(MediaColor.FromRgb(190, 202, 218)) },
                        new TextBlock { Text = "Setujui hanya jika Anda yang memulai permintaan ini.", TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 10, 0, 18), Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 190, 110)) },
                        new StackPanel
                        {
                            Orientation = Orientation.Horizontal,
                            HorizontalAlignment = HorizontalAlignment.Right,
                            Children =
                            {
                                MakeButton("Tolak", false),
                                MakeButton("Izinkan 10 menit", true)
                            }
                        }
                    }
                }
            };
        }

        private Button MakeButton(string text, bool allow)
        {
            var button = new Button
            {
                Content = text,
                MinWidth = 110,
                Margin = new Thickness(8, 0, 0, 0),
                Padding = new Thickness(10, 7, 10, 7),
                IsDefault = allow,
                IsCancel = !allow
            };
            button.Click += (_, _) =>
            {
                DialogResult = allow;
                Close();
            };
            return button;
        }
    }

    internal sealed class PairingApprovalWindow : Window
    {
        public PairingApprovalWindow(string deviceName, string code)
        {
            Title = "Pasangkan perangkat";
            var workArea = SystemParameters.WorkArea;
            Width = Math.Min(500, Math.Max(340, workArea.Width - 40));
            MinWidth = Math.Min(420, Width);
            MinHeight = Math.Min(250, Math.Max(190, workArea.Height - 48));
            SizeToContent = SizeToContent.Height;
            MaxHeight = Math.Max(230, workArea.Height - 48);
            ResizeMode = ResizeMode.NoResize;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            Topmost = true;
            Background = new SolidColorBrush(MediaColor.FromRgb(19, 26, 36));
            Foreground = MediaBrushes.White;
            var allow = new Button { Content = "Pasangkan", MinWidth = 110, Padding = new Thickness(12, 8, 12, 8), IsDefault = true };
            var deny = new Button { Content = "Tolak", MinWidth = 80, Margin = new Thickness(8, 0, 0, 0), Padding = new Thickness(12, 8, 12, 8), IsCancel = true };
            allow.Click += (_, _) => { DialogResult = true; Close(); };
            deny.Click += (_, _) => { DialogResult = false; Close(); };
            Content = new ScrollViewer
            {
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                Content = new StackPanel
                {
                    Margin = new Thickness(24),
                    Children =
                    {
                        new TextBlock { Text = "Permintaan pemasangan Android", FontSize = 20, FontWeight = FontWeights.SemiBold, Margin = new Thickness(0, 0, 0, 12) },
                        new TextBlock { Text = $"Perangkat “{deviceName}” ingin dipasangkan untuk memantau dan mengendalikan laptop ini.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(MediaColor.FromRgb(190, 202, 218)) },
                        new TextBlock { Text = $"Kode verifikasi: {code}", FontSize = 24, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 18, 0, 6) },
                        new TextBlock { Text = "Pastikan kode ini sama dengan yang terlihat di HP sebelum menyetujui. Kunci dan sertifikat akan disimpan otomatis di kedua perangkat.", TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(MediaColor.FromRgb(255, 190, 110)), Margin = new Thickness(0, 0, 0, 18) },
                        new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right, Children = { deny, allow } }
                    }
                }
            };
        }
    }

    internal sealed class ScreenControlIndicatorWindow : Window
    {
        public ScreenControlIndicatorWindow(Action stop, bool passwordAuthenticated = false)
        {
            Title = "Kendali layar aktif";
            Width = 390;
            Height = 54;
            WindowStyle = WindowStyle.None;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            Topmost = true;
            Background = new SolidColorBrush(MediaColor.FromRgb(145, 26, 34));
            Content = new DockPanel
            {
                LastChildFill = true,
                Margin = new Thickness(14, 5, 8, 5),
                Children =
                {
                    new Button
                    {
                        Content = "Hentikan",
                        Padding = new Thickness(10, 4, 10, 4),
                        Margin = new Thickness(10, 0, 0, 0),
                        VerticalAlignment = VerticalAlignment.Center,
                        MinWidth = 84
                    },
                    new TextBlock
                    {
                        Text = passwordAuthenticated
                            ? "KENDALI LAYAR AKTIF • Login password LaptopStatus"
                            : "KENDALI LAYAR AKTIF • Android terhubung",
                        Foreground = MediaBrushes.White,
                        FontWeight = FontWeights.SemiBold,
                        VerticalAlignment = VerticalAlignment.Center
                    }
                }
            };
            var panel = (DockPanel)Content;
            var button = (Button)panel.Children[0];
            DockPanel.SetDock(button, Dock.Right);
            button.Click += (_, _) => stop();
            Loaded += (_, _) =>
            {
                var area = System.Windows.Forms.Screen.PrimaryScreen?.WorkingArea;
                Left = area is null ? 20 : area.Value.Left + Math.Max(20, (area.Value.Width - Width) / 2);
                Top = area is null ? 20 : area.Value.Top + 12;
            };
        }
    }

    internal static class DesktopControl
    {
        private const uint MouseLeftDown = 0x0002;
        private const uint MouseLeftUp = 0x0004;
        private const uint InputKeyboard = 1;
        private const uint KeyEventUnicode = 0x0004;
        private const uint KeyEventKeyUp = 0x0002;

        public static (byte[] Image, int Width, int Height, string Source) CaptureControlScreen()
        {
            var (bounds, source) = GetControlScreenTarget();
            using var bitmap = new Bitmap(bounds.Width, bounds.Height, DrawingPixelFormat.Format24bppRgb);
            using (var graphics = Graphics.FromImage(bitmap))
                graphics.CopyFromScreen(new DrawingPoint(bounds.Left, bounds.Top), DrawingPoint.Empty, bounds.Size, CopyPixelOperation.SourceCopy);
            using var output = new MemoryStream();
            var jpeg = ImageCodecInfo.GetImageEncoders().First(codec => codec.MimeType == "image/jpeg");
            using var settings = new EncoderParameters(1);
            settings.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 70L);
            bitmap.Save(output, jpeg, settings);
            return (output.ToArray(), bounds.Width, bounds.Height, source);
        }

        public static void MoveAndClick(int x, int y, string action)
        {
            var (bounds, _) = GetControlScreenTarget();
            x = Math.Clamp(x, 0, bounds.Width - 1) + bounds.Left;
            y = Math.Clamp(y, 0, bounds.Height - 1) + bounds.Top;
            SetCursorPos(x, y);
            if (action == "down") mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
            else if (action == "up") mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
            else if (action == "click")
            {
                mouse_event(MouseLeftDown, 0, 0, 0, UIntPtr.Zero);
                mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);
            }
        }

        private static (Rectangle Bounds, string Source) GetControlScreenTarget()
        {
            var screens = System.Windows.Forms.Screen.AllScreens;
            var spacedeskScreen = screens.FirstOrDefault(screen => IsSpacedeskDisplay(screen.DeviceName));
            if (spacedeskScreen is not null && spacedeskScreen.Bounds.Width > 0 && spacedeskScreen.Bounds.Height > 0)
                return (spacedeskScreen.Bounds, "spacedesk virtual display");

            // Without an active spacedesk monitor, use Windows' virtual desktop
            // bounds. On a single-screen laptop this is the built-in display;
            // with several displays it includes the whole desktop canvas.
            var virtualBounds = System.Windows.Forms.SystemInformation.VirtualScreen;
            if (virtualBounds.Width > 0 && virtualBounds.Height > 0)
                return (virtualBounds, "Windows virtual desktop");

            var primary = System.Windows.Forms.Screen.PrimaryScreen?.Bounds;
            if (primary is { Width: > 0, Height: > 0 }) return (primary.Value, "Windows display");
            return (new Rectangle(0, 0, 1280, 720), "fallback virtual canvas");
        }

        private static bool IsSpacedeskDisplay(string screenDeviceName)
        {
            for (uint index = 0; ; index++)
            {
                var adapter = new DisplayDevice { Size = (uint)Marshal.SizeOf<DisplayDevice>() };
                if (!EnumDisplayDevices(null, index, ref adapter, 0)) return false;
                if (!string.Equals(adapter.DeviceName, screenDeviceName, StringComparison.OrdinalIgnoreCase)) continue;
                return string.Join(" ", adapter.DeviceString, adapter.DeviceId, adapter.DeviceKey)
                    .Contains("spacedesk", StringComparison.OrdinalIgnoreCase);
            }
        }

        public static void ReleaseMouse() => mouse_event(MouseLeftUp, 0, 0, 0, UIntPtr.Zero);

        public static void TypeText(string text)
        {
            var inputs = text.Take(500).SelectMany(character => new[]
            {
                MakeUnicodeInput(character, KeyEventUnicode),
                MakeUnicodeInput(character, KeyEventUnicode | KeyEventKeyUp)
            }).ToArray();
            if (inputs.Length > 0) SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<InputRecord>());
        }

        private static InputRecord MakeUnicodeInput(char character, uint flags) => new()
        {
            Type = InputKeyboard,
            Data = new InputUnion { Keyboard = new KeyboardInput { ScanCode = character, Flags = flags } }
        };

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetCursorPos(int x, int y);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct DisplayDevice
        {
            public uint Size;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)] public string? DeviceName;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceString;
            public uint StateFlags;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceId;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)] public string? DeviceKey;
        }

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool EnumDisplayDevices(string? device, uint deviceNumber, ref DisplayDevice displayDevice, uint flags);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern void mouse_event(uint flags, uint dx, uint dy, uint data, UIntPtr extraInfo);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint count, InputRecord[] inputs, int size);

        [StructLayout(LayoutKind.Sequential)]
        private struct InputRecord
        {
            public uint Type;
            public InputUnion Data;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct InputUnion
        {
            [FieldOffset(0)] public KeyboardInput Keyboard;
            [FieldOffset(0)] public MouseInput Mouse;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KeyboardInput
        {
            public ushort VirtualKey;
            public ushort ScanCode;
            public uint Flags;
            public uint Time;
            public UIntPtr ExtraInfo;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct MouseInput
        {
            public int Dx, Dy;
            public uint MouseData, Flags, Time;
            public UIntPtr ExtraInfo;
        }
    }
}

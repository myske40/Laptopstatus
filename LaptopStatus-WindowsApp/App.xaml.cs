using System;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using Microsoft.Win32;

namespace LaptopStatus
{
    public partial class App : Application
    {
        private const string InstanceMutexName = @"Local\LaptopStatus.Main";
        private const int SwRestore = 9;
        private static Mutex? _instanceMutex;

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr FindWindow(string? className, string? windowName);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool ShowWindowAsync(IntPtr windowHandle, int command);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr windowHandle);

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            if (PrivilegedActionRunner.TryHandleHelperArguments(e.Args, out var helperExitCode))
            {
                Shutdown(helperExitCode);
                return;
            }

            var resumeScheduledPower = Array.Exists(e.Args, argument =>
                string.Equals(argument, "--resume-scheduled-power", StringComparison.OrdinalIgnoreCase));
            var instanceMutex = new Mutex(initiallyOwned: false, name: InstanceMutexName);
            var ownsInstanceMutex = false;
            try { ownsInstanceMutex = instanceMutex.WaitOne(0); }
            catch (AbandonedMutexException) { ownsInstanceMutex = true; }
            if (!ownsInstanceMutex)
            {
                instanceMutex.Dispose();
                if (!resumeScheduledPower)
                {
                    var existingWindow = FindWindow(null, "Status Perangkat");
                    if (existingWindow != IntPtr.Zero)
                    {
                        ShowWindowAsync(existingWindow, SwRestore);
                        SetForegroundWindow(existingWindow);
                    }
                }
                Shutdown(0);
                return;
            }
            _instanceMutex = instanceMutex;

            var mainWindow = new MainWindow(resumeScheduledPower);
            MainWindow = mainWindow;
            if (resumeScheduledPower)
                mainWindow.Loaded += (_, _) => mainWindow.Hide();
            mainWindow.Show();

            using var onboarding = Registry.CurrentUser.OpenSubKey(@"Software\LaptopStatus");
            var onboardingCompleted = onboarding?.GetValue("OnboardingCompleted") is int value && value == 1;
            if (!resumeScheduledPower && !onboardingCompleted)
            {
                var guide = new OnboardingGuideWindow { Owner = mainWindow };
                guide.ShowDialog();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            if (_instanceMutex is not null)
            {
                try { _instanceMutex.ReleaseMutex(); } catch (ApplicationException) { }
                _instanceMutex.Dispose();
                _instanceMutex = null;
            }
            base.OnExit(e);
        }
    }
}

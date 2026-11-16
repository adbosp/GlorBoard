using System;
using System.ComponentModel;
using System.Drawing;
using System.IO;
using System.Media;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Forms = System.Windows.Forms;

namespace GlorBoard;

public partial class App : Application
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private Mutex? instance;
    private EventWaitHandle? showSignal;
    private RegisteredWaitHandle? showWait;
    private DesktopInput? desktop;
    private DispatcherTimer? contextTimer;
    private Forms.NotifyIcon? tray;
    private Icon? vietnameseIcon, englishIcon;
    private MainWindow? window;
    private string lastApplication = "";
    private bool? displayedMode;
    private readonly GlassTheme appearance = new();
    private NativeHost? nativeHost;

    internal Preferences Settings { get; private set; } = new();
    internal bool Exiting { get; private set; }
    internal bool InputReady => desktop != null;
    internal bool Mode => DesktopInput.IsEnabled(Settings, lastApplication);
    internal string CurrentAppName => Path.GetFileNameWithoutExtension(lastApplication);
    internal string Translate(string vi, string en) => Settings.Vietnamese ? vi : en;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        nativeHost = NativeHost.Connect(e.Args);
        string instanceName = @"Local\GlorBoard." + Environment.UserName;
        showSignal = new(false, EventResetMode.AutoReset, instanceName + ".Show");
        instance = new(true, instanceName, out bool first);
        if (!first)
        {
            showSignal.Set();
            Shutdown();
            return;
        }

        Settings = Preferences.Load();
        appearance.Apply(Resources, Settings.Theme);
        SystemEvents.UserPreferenceChanged += WindowsAppearanceChanged;
        if (nativeHost == null) StartDesktopInput();

        window = new(this);
        MainWindow = window;
        if (nativeHost is { } host)
        {
            if (!host.Register(window, this))
            {
                Exiting = true;
                Shutdown();
                return;
            }
            Settings = Preferences.Load();
            appearance.Apply(Resources, Settings.Theme);
            if (!StartDesktopInput())
            {
                Exiting = true;
                Shutdown();
                return;
            }
            window.RefreshSettings();
        }
        else StartTray();

        UpdateMode();
        contextTimer = new() { Interval = TimeSpan.FromMilliseconds(180) };
        contextTimer.Tick += (_, _) => UpdateMode();
        contextTimer.Start();
        showWait = ThreadPool.RegisterWaitForSingleObject(showSignal, (_, _) => Dispatcher.BeginInvoke(ShowWindow), null, -1, false);
        if (nativeHost != null || Settings.ShowOnStart) ShowWindow();
    }

    private bool StartDesktopInput()
    {
        try { desktop = new(() => Settings, ToggleForApplication); }
        catch (Win32Exception error)
        {
            MessageBox.Show(Translate("Không thể bật bộ gõ Windows.\n", "Could not start Windows input.\n") + error.Message,
                "GlorBoard", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        return desktop != null;
    }

    private void StartTray()
    {
        vietnameseIcon = LoadTrayIcon("V");
        englishIcon = LoadTrayIcon("E");

        tray = new() { Icon = vietnameseIcon, Text = "GlorBoard", Visible = true };
        tray.MouseClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left && args.Clicks == 1) ToggleMode();
        };
        tray.MouseDoubleClick += (_, args) =>
        {
            if (args.Button == Forms.MouseButtons.Left) ShowWindow();
        };
        tray.ContextMenuStrip = new();
        tray.ContextMenuStrip.Opening += (_, _) => UpdateTrayMenu();

    }

    internal void OptionsChanged(bool translateUi = false)
    {
        desktop?.Reset();
        SaveSettings();
        if (translateUi) window?.RefreshSettings();
        displayedMode = null;
        UpdateMode();
    }

    internal void ChangeTheme(UiTheme theme)
    {
        if (Settings.Theme == theme) return;
        Settings.Theme = theme;
        appearance.Apply(Resources, theme);
        SaveSettings();
    }

    private void WindowsAppearanceChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category is UserPreferenceCategory.General or UserPreferenceCategory.Color or
            UserPreferenceCategory.VisualStyle or UserPreferenceCategory.Accessibility)
            Dispatcher.BeginInvoke(() => appearance.Apply(Resources, Settings.Theme));
    }

    internal bool SetWindowsStartup(bool enabled)
    {
        try
        {
            using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (key == null) throw new IOException("HKCU Run");
            if (enabled)
            {
                string program = nativeHost != null ? Path.Combine(AppContext.BaseDirectory, "GlorBoard.exe") :
                    Environment.ProcessPath ?? throw new IOException("GlorBoard.exe");
                key.SetValue("GlorBoard", $"\"{program}\" --startup");
            }
            else key.DeleteValue("GlorBoard", false);
            Settings.StartWithWindows = enabled;
            return true;
        }
        catch (Exception e) when (e is UnauthorizedAccessException or IOException or System.Security.SecurityException)
        {
            MessageBox.Show(Translate("Không cập nhật được tùy chọn khởi động.\n", "Could not update Windows startup.\n") + e.Message, "GlorBoard");
            return false;
        }
    }

    internal bool ResetSettings()
    {
        if (!SetWindowsStartup(false)) return false;
        Settings = new();
        appearance.Apply(Resources, Settings.Theme);
        OptionsChanged(translateUi: true);
        return true;
    }

    private void SaveSettings()
    {
        try
        {
            Settings.Save();
            nativeHost?.SettingsChanged();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            Dispatcher.BeginInvoke(() => MessageBox.Show(
                Translate("Không lưu được cài đặt.\n", "Could not save settings.\n") + e.Message, "GlorBoard"));
        }
    }

    private void CaptureActiveApplication()
    {
        string active = desktop?.ActiveApplication ?? "";
        if (active.Length > 0) lastApplication = active;
    }

    internal void ToggleMode()
    {
        if (!InputReady) return;
        ToggleForApplication(desktop?.ActiveApplication ?? "");
    }

    private void ToggleForApplication(string appKey)
    {
        if (appKey.Length > 0) lastApplication = appKey;
        bool enabled = !Mode;
        if (Settings.PerApp && lastApplication.Length > 0) Settings.AppModes[lastApplication] = enabled;
        else Settings.Enabled = enabled;
        desktop?.Reset();
        Dispatcher.BeginInvoke(() =>
        {
            SaveSettings();
            UpdateMode();
            if (Settings.Sound) SystemSounds.Asterisk.Play();
            if (Settings.Notifications)
            {
                nativeHost?.Notify(enabled);
                tray?.ShowBalloonTip(1200, "GlorBoard", ModeText(enabled), Forms.ToolTipIcon.Info);
            }
        });
    }

    private string ModeText(bool enabled) => enabled
        ? Translate("Đang gõ tiếng Việt", "Vietnamese input active")
        : Translate("Đang gõ tiếng Anh", "English input active");

    private void UpdateMode()
    {
        CaptureActiveApplication();
        bool mode = Mode;
        window?.ShowMode(mode);
        if (tray == null || displayedMode == mode) return;
        displayedMode = mode;
        tray.Icon = mode && InputReady ? vietnameseIcon : englishIcon;
        tray.Text = InputReady ? "GlorBoard · " + ModeText(mode) : "GlorBoard";
    }

    private void UpdateTrayMenu()
    {
        var menu = tray!.ContextMenuStrip!;
        while (menu.Items.Count > 0) menu.Items[0].Dispose();
        menu.Items.Add(Translate("Mở GlorBoard", "Open GlorBoard"), null, (_, _) => ShowWindow());
        var active = menu.Items.Add(Mode ? Translate("Chuyển sang tiếng Anh", "Switch to English") :
            Translate("Chuyển sang tiếng Việt", "Switch to Vietnamese"), null, (_, _) => ToggleMode());
        active.Enabled = InputReady;
        var methods = new Forms.ToolStripMenuItem(Translate("Kiểu gõ", "Input method"));
        foreach (InputMethod method in Enum.GetValues<InputMethod>())
        {
            var item = new Forms.ToolStripMenuItem(method == InputMethod.Telex ? "Telex" : "VNI") { Checked = Settings.Method == method };
            item.Click += (_, _) => { Settings.Method = method; OptionsChanged(translateUi: true); };
            methods.DropDownItems.Add(item);
        }
        menu.Items.Add(methods);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add(Translate("Kết thúc", "Exit"), null, (_, _) => ExitApp());
    }

    internal void ShowWindow()
    {
        if (window == null) return;
        CaptureActiveApplication();
        window.Show();
        window.WindowState = WindowState.Normal;
        window.Activate();
        UpdateMode();
    }

    internal void ExitApp()
    {
        nativeHost?.Shutdown();
        Exiting = true;
        Shutdown();
    }

    internal void CloseToTray()
    {
        if (nativeHost == null)
        {
            window?.Hide();
            return;
        }
        Dispatcher.BeginInvoke(() =>
        {
            if (Exiting) return;
            Exiting = true;
            Shutdown();
        });
    }

    protected override void OnExit(ExitEventArgs e)
    {
        SystemEvents.UserPreferenceChanged -= WindowsAppearanceChanged;
        contextTimer?.Stop();
        desktop?.Dispose();
        tray?.ContextMenuStrip?.Dispose();
        tray?.Dispose();
        vietnameseIcon?.Dispose();
        englishIcon?.Dispose();
        showWait?.Unregister(null);
        showSignal?.Dispose();
        instance?.Dispose();
        base.OnExit(e);
    }

    private static Icon LoadTrayIcon(string letter)
    {
        using Stream resource = GetResourceStream(new Uri($"Assets/Keycap{letter}.ico", UriKind.Relative)).Stream;
        return new Icon(resource, Forms.SystemInformation.SmallIconSize);
    }
}

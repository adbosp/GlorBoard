using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace GlorBoard;

public partial class MainWindow : Window
{
    private const double CompactHeight = 396;
    private const double ExpandedHeight = 804;
    private static readonly BitmapSource VietKeycap = LoadKeycap("V");
    private static readonly BitmapSource EnglishKeycap = LoadKeycap("E");
    private readonly App app;
    private bool refreshing = true;
    private bool advanced;
    private (bool Enabled, bool Ready, bool Vietnamese, string? Target)? displayed;

    internal MainWindow(App app)
    {
        this.app = app;
        InitializeComponent();
        Width = Math.Min(Width, SystemParameters.WorkArea.Width - 14);
        WindowGlass.Attach(this);
        Resize(CompactHeight);
        RefreshSettings();
    }

    private string T(string vi, string en) => app.Translate(vi, en);

    internal void RefreshSettings()
    {
        refreshing = true;
        try
        {
            Preferences settings = app.Settings;
            EncodingLabel.Text = T("Bảng mã", "Encoding");
            MethodLabel.Text = T("Kiểu gõ", "Input method");
            ShortcutLabel.Text = T("Phím chuyển EN / VI", "EN / VI shortcut");
            MoreLabel.Text = T("Mở rộng", "Settings");
            TypingHeading.Text = T("Tùy chọn gõ", "Typing options");
            SystemHeading.Text = T("Hệ thống", "System");
            MacrosHeading.Text = T("Gõ tắt", "Text shortcuts");
            SetButton(MinimizeButton, T("Thu nhỏ", "Minimize"), icon: true);
            SetButton(CloseButton, T("Đóng về khay hệ thống", "Close to system tray"), icon: true);
            SetButton(AppearanceButton, T("Giao diện", "Appearance"));
            SetButton(MethodOptionsButton, T("Tùy chọn kiểu gõ", "Input method options"), icon: true);
            SetButton(OkButton, T("OK · Đóng về khay hệ thống", "OK · Close to system tray"), icon: true);
            SetButton(MacroButton, T("Bảng gõ tắt…", "Shortcut table…"));
            SetButton(ExitButton, T("Kết thúc GlorBoard", "Exit GlorBoard"));
            SetButton(GuideButton, T("Hướng dẫn", "Help"));
            SetButton(InfoButton, T("Thông tin", "About"));
            SetButton(DefaultsButton, T("Mặc định", "Defaults"));
            UpdateAdvancedLabel();

            EncodingChoices.ItemsSource = new[] { "Unicode", T("Unicode tổ hợp", "Unicode decomposed"), "VIQR" };
            MethodChoices.ItemsSource = new[] { "Telex", "VNI" };
            ShortcutChoices.ItemsSource = new[] { "Ctrl + Shift", "Alt + Z" };
            EncodingChoices.SelectedIndex = (int)settings.Encoding;
            MethodChoices.SelectedIndex = (int)settings.Method;
            ShortcutChoices.SelectedIndex = (int)settings.Hotkey;

            TypingOptions.Children.Clear();
            SystemOptions.Children.Clear();
            MacroOptions.Children.Clear();
            Option(TypingOptions, T("Cho phép gõ tự do", "Allow free typing"), settings.FreeTyping, value => settings.FreeTyping = value);
            Option(TypingOptions, T("Đặt dấu kiểu mới (oà, uý)", "Use modern tone placement (oà, uý)"),
                settings.ModernTone, value => settings.ModernTone = value);
            Option(TypingOptions, T("Luôn dùng clipboard cho Unicode", "Always use clipboard for Unicode"), settings.Clipboard, value => settings.Clipboard = value);
            Option(TypingOptions, T("Kiểm tra chính tả", "Check spelling"), settings.SpellCheck, value => settings.SpellCheck = value, rebuild: true);
            Option(TypingOptions, T("Khôi phục phím khi từ sai", "Restore keys for invalid words"), settings.Restore, value => settings.Restore = value).IsEnabled = settings.SpellCheck;
            Option(TypingOptions, T("Hiện thông báo phản hồi", "Show switch notifications"), settings.Notifications, value => settings.Notifications = value);
            Option(TypingOptions, T("Phát âm khi chuyển EN / VI", "Play sound on EN / VI switch"), settings.Sound, value => settings.Sound = value);
            Option(SystemOptions, T("Hiện cửa sổ khi khởi động", "Open window at startup"), settings.ShowOnStart, value => settings.ShowOnStart = value);
            var startup = Option(SystemOptions, T("Khởi động cùng Windows", "Start with Windows"), settings.StartWithWindows, value =>
            {
                if (!app.SetWindowsStartup(value)) RefreshSettings();
            });
            AutomationProperties.SetAutomationId(startup, "StartWithWindows");
            Option(SystemOptions, T("Bật/tắt theo từng ứng dụng", "Remember mode per application"), settings.PerApp, value => settings.PerApp = value);
            Option(SystemOptions, "Giao diện tiếng Việt", settings.Vietnamese, value => settings.Vietnamese = value, rebuild: true);
            Option(MacroOptions, T("Cho phép gõ tắt", "Enable text shortcuts"), settings.MacrosEnabled, value => settings.MacrosEnabled = value, rebuild: true);
            Option(MacroOptions, T("Gõ tắt cả khi ở chế độ EN", "Expand shortcuts in English mode"), settings.MacrosInEnglish,
                value => settings.MacrosInEnglish = value).IsEnabled = settings.MacrosEnabled;
            ShowMode(app.Mode);
        }
        finally { refreshing = false; }
    }

    internal void ShowMode(bool enabled)
    {
        string? target = app.Settings.PerApp && app.CurrentAppName.Length > 0 ? app.CurrentAppName : null;
        var state = (enabled, app.InputReady, app.Settings.Vietnamese, target);
        if (displayed == state) return;
        displayed = state;
        ModeIcon.Source = enabled ? VietKeycap : EnglishKeycap;
        AutomationProperties.SetName(ModeIcon, enabled ? "V" : "E");
        ModeTitle.Text = enabled ? T("Tiếng Việt", "Vietnamese") : T("Tiếng Anh", "English");
        ModeSwitch.IsChecked = enabled;
        ModeSwitch.IsEnabled = app.InputReady;
        ModeStatus.Text = enabled ? T("Đang bật", "Enabled") : T("Đang tắt", "Disabled");
        AutomationProperties.SetName(ModeSwitch, T("Bật/tắt tiếng Việt", "Toggle Vietnamese input"));
        ModeSwitch.ToolTip = target;
        SystemStatus.Text = app.InputReady ? T("Đang chạy trên Windows", "Running on Windows") : T("Bộ gõ chưa chạy", "Input unavailable");
        StatusDot.SetResourceReference(System.Windows.Shapes.Shape.FillProperty, app.InputReady ? "Success" : "Attention");
    }

    private static BitmapSource LoadKeycap(string letter)
    {
        BitmapFrame frame = BitmapFrame.Create(new Uri($"pack://application:,,,/Assets/Keycap{letter}.png"));
        frame.Freeze();
        return frame;
    }

    private CheckBox Option(Panel target, string title, bool initial, Action<bool> change, bool rebuild = false)
    {
        CheckBox checkbox = new()
        {
            IsChecked = initial,
            Content = new TextBlock { Text = title, TextWrapping = TextWrapping.Wrap },
        };
        AutomationProperties.SetName(checkbox, title);
        checkbox.Click += (_, _) =>
        {
            change(checkbox.IsChecked == true);
            app.OptionsChanged(translateUi: rebuild);
        };
        target.Children.Add(checkbox);
        return checkbox;
    }

    private static void SetButton(Button button, string text, bool icon = false)
    {
        AutomationProperties.SetName(button, text);
        if (icon) button.ToolTip = text;
        else button.Content = text;
    }

    private void SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (refreshing || EncodingChoices.SelectedIndex < 0 || MethodChoices.SelectedIndex < 0 ||
            ShortcutChoices.SelectedIndex < 0) return;
        app.Settings.Encoding = (CharacterSet)EncodingChoices.SelectedIndex;
        app.Settings.Method = (InputMethod)MethodChoices.SelectedIndex;
        app.Settings.Hotkey = (Shortcut)ShortcutChoices.SelectedIndex;
        app.OptionsChanged();
    }

    private void Resize(double requested)
    {
        Height = Math.Min(requested, SystemParameters.WorkArea.Height - 10);
        if (!IsVisible) return;
        if (Top + Height > SystemParameters.WorkArea.Bottom)
            Top = Math.Max(SystemParameters.WorkArea.Top, SystemParameters.WorkArea.Bottom - Height);
    }

    private void UpdateAdvancedLabel() => SetButton(AdvancedButton,
        advanced ? T("Thu gọn", "Collapse") : T("Nâng cao", "Advanced"));

    private void AdvancedClick(object sender, RoutedEventArgs e)
    {
        advanced = !advanced;
        AdvancedPanel.Visibility = advanced ? Visibility.Visible : Visibility.Collapsed;
        if (advanced && SystemParameters.ClientAreaAnimation && !SystemParameters.HighContrast)
            AdvancedPanel.BeginAnimation(OpacityProperty, new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(150))
            {
                FillBehavior = FillBehavior.Stop,
            });
        UpdateAdvancedLabel();
        Resize(advanced ? ExpandedHeight : CompactHeight);
    }

    private void DragWindow(object sender, MouseButtonEventArgs e)
    {
        if (!e.Handled && e.LeftButton == MouseButtonState.Pressed) DragMove();
    }

    private void ModeClick(object sender, RoutedEventArgs e) => app.ToggleMode();
    private void CloseClick(object sender, RoutedEventArgs e) => app.CloseToTray();
    private void MinimizeClick(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
    private void ExitClick(object sender, RoutedEventArgs e) => app.ExitApp();

    protected override void OnClosing(CancelEventArgs e)
    {
        base.OnClosing(e);
        if (app.Exiting) return;
        e.Cancel = true;
        app.CloseToTray();
    }
}

using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Interop;

namespace GlorBoard;

internal sealed class NativeHost
{
    private const uint ShutdownMessage = 0x800A;
    private const uint SettingsChangedMessage = 0x800B;
    private const uint SettingsReadyMessage = 0x800C;
    private const uint NotificationMessage = 0x800D;
    private const int ToggleModeMessage = 0x8014;
    private const int MethodMessage = 0x8015;
    private const int ExitSettingsMessage = 0x8016;
    private readonly nint window;

    private NativeHost(nint window) => this.window = window;

    internal static NativeHost? Connect(string[] arguments)
    {
        if (arguments.Length != 2 || arguments[0] != "--native-settings" ||
            !nint.TryParse(arguments[1], out nint handle)) return null;
        StringBuilder name = new(64);
        return GetClassNameW(handle, name, name.Capacity) > 0 && name.ToString() == "GlorBoard.Native.Tray"
            ? new(handle) : null;
    }

    internal bool Register(MainWindow settings, App app)
    {
        nint handle = new WindowInteropHelper(settings).EnsureHandle();
        HwndSource.FromHwnd(handle)?.AddHook(OnMessage);
        return SendMessageTimeoutW(window, SettingsReadyMessage, handle, 0, 3, 1500, out nint result) != 0 && result == 1;

        nint OnMessage(nint _, int message, nint value, nint unused, ref bool handled)
        {
            switch (message)
            {
                case ToggleModeMessage:
                    app.Dispatcher.BeginInvoke(app.ToggleMode);
                    break;
                case MethodMessage:
                    app.Dispatcher.BeginInvoke(() =>
                    {
                        app.Settings.Method = value == 1 ? InputMethod.Vni : InputMethod.Telex;
                        app.OptionsChanged(translateUi: true);
                    });
                    break;
                case ExitSettingsMessage:
                    app.Dispatcher.BeginInvoke(app.CloseToTray);
                    break;
                default:
                    return 0;
            }
            handled = true;
            return 0;
        }
    }

    internal void SettingsChanged() => _ = PostMessageW(window, SettingsChangedMessage, 0, 0);
    internal void Notify(bool enabled) => _ = PostMessageW(window, NotificationMessage, enabled ? 1 : 0, 0);
    internal void Shutdown() => _ = PostMessageW(window, ShutdownMessage, 0, 0);

    [DllImport("user32", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern int GetClassNameW(nint handle, StringBuilder name, int max);

    [DllImport("user32", ExactSpelling = true)]
    private static extern nint SendMessageTimeoutW(nint handle, uint message, nint parameter, nint data,
        uint flags, uint timeout, out nint result);

    [DllImport("user32", ExactSpelling = true)]
    private static extern bool PostMessageW(nint handle, uint message, nint parameter, nint data);
}

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace GlorBoard;

internal sealed class DesktopInput : IDisposable
{
    private const uint Marker = 0x474C4F52;
    private const int KeyboardHook = 13;
    private const int MouseHook = 14;
    private const int KeyUp = 2;
    private const int UnicodeKey = 4;
    private const int Back = 0x08;
    private const int ShiftKey = 0x10;
    private const int ControlKey = 0x11;
    private const int AltKey = 0x12;
    private const int LeftShift = 0xA0;
    private const int RightShift = 0xA1;
    private const int LeftCtrl = 0xA2;
    private const int RightCtrl = 0xA3;
    private const int LeftAlt = 0xA4;
    private const int RightAlt = 0xA5;
    private const int Z = 0x5A;

    private readonly Func<Preferences> preferences;
    private readonly Action<string> toggle;
    private readonly VietnameseInput composer = new();
    private readonly HookProc keyboardCallback;
    private readonly HookProc mouseCallback;
    private readonly HashSet<int> down = [];
    private readonly HashSet<int> suppressed = [];
    private readonly DispatcherTimer clipboardTimer;
    private readonly Dictionary<uint, string> processNames = [];
    private nint keyboard, mouse;
    private nint foreground, focus;
    private uint processId, threadId;
    private string applicationKey = "";
    private bool protectedEdit;
    private bool switchCandidate;
    private string shown = "";
    private System.Windows.IDataObject? previousClipboard;
    private uint clipboardVersion;
    private bool clipboardPending;

    internal DesktopInput(Func<Preferences> preferences, Action<string> toggle)
    {
        this.preferences = preferences;
        this.toggle = toggle;
        keyboardCallback = OnKeyboard;
        mouseCallback = OnMouse;
        clipboardTimer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(550) };
        clipboardTimer.Tick += (_, _) => RestoreClipboard();
        nint module = GetModuleHandle(null);
        keyboard = SetWindowsHookEx(KeyboardHook, keyboardCallback, module, 0);
        if (keyboard == 0) throw new Win32Exception();
        mouse = SetWindowsHookEx(MouseHook, mouseCallback, module, 0);
        if (mouse != 0) return;
        int error = Marshal.GetLastWin32Error();
        Dispose();
        throw new Win32Exception(error);
    }

    internal string ActiveApplication
    {
        get
        {
            TrackFocus();
            return applicationKey;
        }
    }

    internal void Reset()
    {
        composer.Reset();
        shown = "";
    }

    internal static bool IsEnabled(Preferences settings, string appKey) =>
        settings.PerApp && appKey.Length > 0 && settings.AppModes.TryGetValue(appKey, out bool value)
            ? value : settings.Enabled;

    private void Toggle()
    {
        string appKey = ActiveApplication;
        Reset();
        toggle(appKey);
    }

    private nint OnMouse(int code, nint message, nint data)
    {
        if (code >= 0 && (int)message is 0x201 or 0x204 or 0x207 or 0x20B or 0x20A or 0x20E)
            Reset();
        return CallNextHookEx(0, code, message, data);
    }

    private nint OnKeyboard(int code, nint message, nint pointer)
    {
        if (code < 0) return CallNextHookEx(0, code, message, pointer);
        KbdData data = Marshal.PtrToStructure<KbdData>(pointer);
        if (data.Extra == Marker) return CallNextHookEx(0, code, message, pointer);
        bool released = (int)message is 0x101 or 0x105;
        try
        {
            if (HandleKey(data, released)) return 1;
        }
        catch (Exception e)
        {
            Debug.WriteLine(e);
            Reset();
        }
        return CallNextHookEx(0, code, message, pointer);
    }

    private bool HandleKey(KbdData data, bool released)
    {
        int key = (int)data.Key;
        bool firstPress = !down.Contains(key);
        bool ctrl = IsHeld(ControlKey, LeftCtrl, RightCtrl);
        bool shift = IsHeld(ShiftKey, LeftShift, RightShift);
        bool alt = IsHeld(AltKey, LeftAlt, RightAlt);
        bool win = down.Contains(0x5B) || down.Contains(0x5C);
        bool ctrlKey = key is ControlKey or LeftCtrl or RightCtrl;
        bool shiftKey = key is ShiftKey or LeftShift or RightShift;
        if (released) down.Remove(key);
        else down.Add(key);

        Preferences options = preferences();
        if (options.Hotkey == Shortcut.CtrlShift)
        {
            if (!released && firstPress && !alt && !win && (ctrl && shiftKey || shift && ctrlKey))
                switchCandidate = down.Count == 2;
            if (!released && !ctrlKey && !shiftKey) switchCandidate = false;
            if (released && switchCandidate && (ctrlKey || shiftKey))
            {
                switchCandidate = false;
                Toggle();
            }
        }
        if (released) return suppressed.Remove(key);
        if (options.Hotkey == Shortcut.AltZ && key == Z && alt && !ctrl && !shift && !win)
        {
            if (firstPress) Toggle();
            suppressed.Add(key);
            return true;
        }

        if (key is ShiftKey or LeftShift or RightShift) return false;
        if (ctrl || alt || win || ctrlKey || key is AltKey or LeftAlt or RightAlt or 0x5B or 0x5C)
        {
            Reset();
            return false;
        }

        TrackFocus();
        bool enabled = IsEnabled(options, applicationKey);
        if (protectedEdit || !enabled && !(options.MacrosEnabled && options.MacrosInEnglish)) return false;

        if (key == Back)
        {
            if (!composer.Backspace(options)) Reset();
            else shown = composer.Rendered(options);
            return false;
        }

        char typed = Translate(data, shift);
        if (char.IsAsciiLetterOrDigit(typed) ||
            (enabled && options.Method == InputMethod.Telex && options.BracketKeys &&
             shown.Length == 0 && typed is '[' or ']'))
        {
            composer.Add(typed, options, enabled);
            string next = composer.Rendered(options);
            if (next.Length == 0)
            {
                shown = "";
                return false;
            }
            if (next == shown + typed)
            {
                shown = next;
                return false;
            }

            if (!Replace(shown, next, options, enabled, shift, 0, 0))
            {
                Reset();
                return false;
            }
            shown = next;
            suppressed.Add(key);
            return true;
        }

        string before = shown;
        string completed = typed != '\0' || key is 0x09 or 0x0D
            ? composer.Finish(options, enabled) : before;
        Reset();
        if (before.Length == 0 || before == completed) return false;
        if (!Replace(before, completed, options, enabled, shift, key, data.Flags)) return false;
        suppressed.Add(key);
        return true;
    }

    private bool IsHeld(int generic, int left, int right) =>
        down.Contains(generic) || down.Contains(left) || down.Contains(right);

    private char Translate(KbdData data, bool shift)
    {
        byte[] state = new byte[256];
        state[ShiftKey] = shift ? (byte)0x80 : (byte)0;
        state[0x14] = (byte)(GetKeyState(0x14) & 1);
        state[0x90] = (byte)(GetKeyState(0x90) & 1);
        char[] buffer = new char[8];
        int count = ToUnicodeEx(data.Key, data.Scan, state, buffer, buffer.Length, 4, GetKeyboardLayout(threadId));
        return count == 1 ? buffer[0] : '\0';
    }

    private void TrackFocus()
    {
        nint active = GetForegroundWindow();
        uint activeThread = GetWindowThreadProcessId(active, out uint pid);
        GuiThreadInfo ui = new() { Size = Marshal.SizeOf<GuiThreadInfo>() };
        nint currentFocus = GetGUIThreadInfo(activeThread, ref ui) ? ui.Focus : active;
        if (active == foreground && currentFocus == focus && pid == processId) return;
        Reset();
        foreground = active;
        focus = currentFocus;
        threadId = activeThread;
        applicationKey = GetApplicationKey(pid);
        processId = pid;

        StringBuilder className = new(64);
        _ = GetClassName(focus, className, className.Capacity);
        protectedEdit = (className.ToString().Equals("Edit", StringComparison.OrdinalIgnoreCase) ||
                         className.ToString().StartsWith("RichEdit", StringComparison.OrdinalIgnoreCase)) &&
                        (GetWindowLong(focus, -16) & 0x20) != 0;
    }

    private string GetApplicationKey(uint pid)
    {
        if (pid == 0 || pid == Environment.ProcessId) return "";
        if (pid == processId) return applicationKey;
        if (processNames.TryGetValue(pid, out string? name)) return name;

        nint process = OpenProcess(0x1000, false, pid);
        if (process == 0) return "";
        try
        {
            StringBuilder path = new(1024);
            int length = path.Capacity;
            if (!QueryFullProcessImageName(process, 0, path, ref length)) return "";
            if (processNames.Count >= 128) processNames.Clear();
            return processNames[pid] = path.ToString();
        }
        finally { _ = CloseHandle(process); }
    }

    private bool Replace(string before, string after, Preferences options, bool enabled, bool shift, int endKey, uint endFlags)
    {
        int prefix = 0;
        while (prefix < before.Length && prefix < after.Length && before[prefix] == after[prefix]) prefix++;
        if (options.Encoding == CharacterSet.Decomposed)
        {
            while (prefix > 0 && (IsCombiningMark(before, prefix) || IsCombiningMark(after, prefix)))
                prefix--;
        }

        string suffix = after[prefix..];
        List<NativeInput> events = [];
        int deletions = new StringInfo(before[prefix..]).LengthInTextElements;
        for (int i = 0; i < deletions; i++) AppendPress(events, Back);

        if (enabled && options.Clipboard && options.Encoding != CharacterSet.Viqr &&
            suffix.Length != 0 && PrepareClipboard(suffix))
        {
            bool left = down.Contains(LeftShift) || down.Contains(ShiftKey);
            bool right = down.Contains(RightShift);
            if (shift && left) events.Add(CreateEvent(LeftShift, 0, KeyUp));
            if (shift && right) events.Add(CreateEvent(RightShift, 0, KeyUp));
            events.Add(CreateEvent(LeftCtrl, 0, 0));
            AppendPress(events, 0x56);
            events.Add(CreateEvent(LeftCtrl, 0, KeyUp));
            if (shift && left) events.Add(CreateEvent(LeftShift, 0, 0));
            if (shift && right) events.Add(CreateEvent(RightShift, 0, 0));
        }
        else
        {
            foreach (char letter in suffix) AppendPress(events, 0, letter, UnicodeKey);
        }

        if (endKey != 0) AppendPress(events, endKey, flags: (int)(endFlags & 1));
        return events.Count == 0 ||
            SendInput((uint)events.Count, events.ToArray(), Marshal.SizeOf<NativeInput>()) == events.Count;
    }

    private static bool IsCombiningMark(string text, int index) =>
        index < text.Length && CharUnicodeInfo.GetUnicodeCategory(text, index) == UnicodeCategory.NonSpacingMark;

    private bool PrepareClipboard(string text)
    {
        try
        {
            if (!clipboardPending || GetClipboardSequenceNumber() != clipboardVersion)
                previousClipboard = Clipboard.GetDataObject();

            DataObject payload = new();
            payload.SetData(DataFormats.UnicodeText, text);
            payload.SetData("ExcludeClipboardContentFromMonitorProcessing", new System.IO.MemoryStream([1, 0, 0, 0]));
            Clipboard.SetDataObject(payload);
            clipboardVersion = GetClipboardSequenceNumber();
            clipboardPending = true;
            clipboardTimer.Stop();
            clipboardTimer.Start();
            return true;
        }
        catch (ExternalException) { return false; }
    }

    private void RestoreClipboard()
    {
        clipboardTimer.Stop();
        if (!clipboardPending) return;
        try
        {
            if (GetClipboardSequenceNumber() != clipboardVersion) return;
            if (previousClipboard == null) Clipboard.Clear();
            else Clipboard.SetDataObject(previousClipboard, true);
        }
        catch (ExternalException) { }
        finally
        {
            clipboardPending = false;
            previousClipboard = null;
        }
    }

    private static void AppendPress(List<NativeInput> list, int key, int scan = 0, int flags = 0)
    {
        list.Add(CreateEvent(key, scan, flags));
        list.Add(CreateEvent(key, scan, flags | KeyUp));
    }

    private static NativeInput CreateEvent(int key, int scan, int flags) => new()
    {
        Type = 1,
        Data = new() { Keyboard = new() { Key = (ushort)key, Scan = (ushort)scan, Flags = (uint)flags, Extra = Marker } },
    };

    public void Dispose()
    {
        if (keyboard != 0) _ = UnhookWindowsHookEx(keyboard);
        if (mouse != 0) _ = UnhookWindowsHookEx(mouse);
        keyboard = mouse = 0;
        RestoreClipboard();
    }

    private delegate nint HookProc(int code, nint message, nint data);
    [StructLayout(LayoutKind.Sequential)]
    private struct KbdData { internal uint Key, Scan, Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct KeyboardInput { internal ushort Key, Scan; internal uint Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Sequential)]
    private struct MouseInput { internal int X, Y; internal uint Data, Flags, Time; internal nuint Extra; }
    [StructLayout(LayoutKind.Explicit)]
    private struct InputData
    {
        [FieldOffset(0)] internal KeyboardInput Keyboard;
        [FieldOffset(0)] internal MouseInput Mouse;
    }
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeInput { internal uint Type; internal InputData Data; }
    [StructLayout(LayoutKind.Sequential)]
    private struct GuiThreadInfo
    {
        internal int Size, Flags;
        internal nint Active, Focus, Capture, MenuOwner, MoveSize, Caret;
        internal int Left, Top, Right, Bottom;
    }

    [DllImport("user32", SetLastError = true)] private static extern nint SetWindowsHookEx(int id, HookProc callback, nint module, uint thread);
    [DllImport("user32")] private static extern nint CallNextHookEx(nint hook, int code, nint message, nint data);
    [DllImport("user32")] private static extern bool UnhookWindowsHookEx(nint hook);
    [DllImport("user32", SetLastError = true)] private static extern uint SendInput(uint count, NativeInput[] input, int size);
    [DllImport("user32")] private static extern nint GetForegroundWindow();
    [DllImport("user32")] private static extern uint GetWindowThreadProcessId(nint window, out uint pid);
    [DllImport("user32")] private static extern bool GetGUIThreadInfo(uint thread, ref GuiThreadInfo info);
    [DllImport("user32")] private static extern short GetKeyState(int key);
    [DllImport("user32")] private static extern nint GetKeyboardLayout(uint thread);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int ToUnicodeEx(uint key, uint scan, byte[] state, [Out] char[] buffer, int bufferSize, uint flags, nint layout);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetClassName(nint window, StringBuilder name, int size);
    [DllImport("user32", CharSet = CharSet.Unicode)] private static extern int GetWindowLong(nint window, int index);
    [DllImport("user32")] private static extern uint GetClipboardSequenceNumber();
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern nint GetModuleHandle(string? name);
    [DllImport("kernel32")] private static extern nint OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageName(nint process, uint flags, StringBuilder image, ref int size);
    [DllImport("kernel32")] private static extern bool CloseHandle(nint handle);
}

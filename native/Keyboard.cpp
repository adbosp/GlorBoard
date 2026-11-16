#include "Keyboard.hpp"

#include <algorithm>
#include <cstring>
#include <vector>

namespace glorboard {
namespace {

constexpr ULONG_PTR marker = 0x474C4F52;

bool asciiLetterOrDigit(wchar_t key) {
    return (key >= L'a' && key <= L'z') || (key >= L'A' && key <= L'Z') || (key >= L'0' && key <= L'9');
}

void event(std::vector<INPUT>& events, WORD key, WORD scan, DWORD flags) {
    INPUT input{};
    input.type = INPUT_KEYBOARD;
    input.ki = {key, scan, flags, 0, marker};
    events.push_back(input);
}

void press(std::vector<INPUT>& events, WORD key, WORD scan = 0, DWORD flags = 0) {
    event(events, key, scan, flags);
    event(events, key, scan, flags | KEYEVENTF_KEYUP);
}

template<typename Method>
Method oleCall(HMODULE ole, const char* method) {
    return reinterpret_cast<Method>(GetProcAddress(ole, method));
}

}

Keyboard* Keyboard::active = nullptr;

Keyboard::Keyboard(Preferences& settings, HWND window, Toggle toggleMode) :
    options(settings), owner(window), toggle(toggleMode) {}

Keyboard::~Keyboard() {
    stop();
    if (oleInitialized) oleCall<void (WINAPI*)()>(ole, "OleUninitialize")();
    if (ole) FreeLibrary(ole);
}

bool Keyboard::start() {
    if (keyHook && mouseHook) return true;
    active = this;
    HINSTANCE module = GetModuleHandleW(nullptr);
    keyHook = SetWindowsHookExW(WH_KEYBOARD_LL, keyCallback, module, 0);
    if (keyHook) mouseHook = SetWindowsHookExW(WH_MOUSE_LL, mouseCallback, module, 0);
    if (keyHook && mouseHook) return true;
    stop();
    return false;
}

void Keyboard::stop() {
    if (keyHook) UnhookWindowsHookEx(keyHook);
    if (mouseHook) UnhookWindowsHookEx(mouseHook);
    keyHook = mouseHook = nullptr;
    if (active == this) active = nullptr;
    keys.fill(false);
    suppressed.fill(false);
    switchCandidate = false;
    reset();
    restoreClipboard();
}

void Keyboard::reset() {
    composer.reset();
    shown.clear();
}

bool Keyboard::isEnabled(const Preferences& options, const std::wstring& application) {
    if (options.perApp && !application.empty()) {
        const auto found = options.appModes.find(application);
        if (found != options.appModes.end()) return found->second;
    }
    return options.enabled;
}

const std::wstring& Keyboard::activeApplication() {
    trackFocus();
    return application;
}

LRESULT CALLBACK Keyboard::keyCallback(int code, WPARAM message, LPARAM data) {
    if (code == HC_ACTION && active) {
        const auto& key = *reinterpret_cast<const KBDLLHOOKSTRUCT*>(data);
        if (key.dwExtraInfo != marker && key.vkCode < 256) {
            try {
                if (active->handle(key, message == WM_KEYUP || message == WM_SYSKEYUP)) return 1;
            } catch (...) { active->reset(); }
        }
    }
    return CallNextHookEx(nullptr, code, message, data);
}

LRESULT CALLBACK Keyboard::mouseCallback(int code, WPARAM message, LPARAM data) {
    if (code == HC_ACTION && active && (message == WM_LBUTTONDOWN || message == WM_RBUTTONDOWN ||
        message == WM_MBUTTONDOWN || message == WM_XBUTTONDOWN || message == WM_MOUSEWHEEL ||
        message == WM_MOUSEHWHEEL)) active->reset();
    return CallNextHookEx(nullptr, code, message, data);
}

bool Keyboard::handle(const KBDLLHOOKSTRUCT& input, bool released) {
    const DWORD key = input.vkCode;
    const bool first = !keys[key];
    const bool ctrl = keys[VK_CONTROL] || keys[VK_LCONTROL] || keys[VK_RCONTROL];
    const bool shift = keys[VK_SHIFT] || keys[VK_LSHIFT] || keys[VK_RSHIFT];
    const bool alt = keys[VK_MENU] || keys[VK_LMENU] || keys[VK_RMENU];
    const bool win = keys[VK_LWIN] || keys[VK_RWIN];
    const bool ctrlKey = key == VK_CONTROL || key == VK_LCONTROL || key == VK_RCONTROL;
    const bool shiftKey = key == VK_SHIFT || key == VK_LSHIFT || key == VK_RSHIFT;
    const bool altKey = key == VK_MENU || key == VK_LMENU || key == VK_RMENU;
    keys[key] = !released;

    if (options.hotkey == 0) {
        if (!released && first && !alt && !win && ((ctrl && shiftKey) || (shift && ctrlKey)))
            switchCandidate = std::count(keys.begin(), keys.end(), true) == 2;
        if (!released && !ctrlKey && !shiftKey) switchCandidate = false;
        if (released && switchCandidate && (ctrlKey || shiftKey)) {
            switchCandidate = false;
            toggle(activeApplication());
        }
    }
    if (released) {
        const bool consume = suppressed[key];
        suppressed[key] = false;
        return consume;
    }
    if (options.hotkey == 1 && key == 'Z' && alt && !ctrl && !shift && !win) {
        if (first) toggle(activeApplication());
        suppressed[key] = true;
        return true;
    }
    if (shiftKey) return false;
    if (ctrl || alt || win || ctrlKey || altKey || key == VK_LWIN || key == VK_RWIN || key == VK_PACKET) {
        reset();
        return false;
    }
    if (!options.perApp && !options.enabled && !(options.macrosEnabled && options.macrosInEnglish)) return false;
    trackFocus();
    const bool enabled = isEnabled(options, application);
    if (protectedEdit || (!enabled && !(options.macrosEnabled && options.macrosInEnglish))) return false;

    if (key == VK_BACK) {
        if (composer.backspace(options)) shown = composer.rendered(options);
        else reset();
        return false;
    }
    const wchar_t typed = translate(input, shift);
    if (asciiLetterOrDigit(typed) ||
        (enabled && options.method == 0 && options.bracketKeys && shown.empty() && (typed == L'[' || typed == L']'))) {
        composer.add(typed, options, enabled);
        auto next = composer.rendered(options);
        if (next.empty() || next == shown + typed) {
            shown = std::move(next);
            return false;
        }
        if (!replace(next, enabled, shift, 0, 0)) {
            reset();
            return false;
        }
        shown = std::move(next);
        suppressed[key] = true;
        return true;
    }
    const auto complete = typed || key == VK_TAB || key == VK_RETURN ? composer.finish(options, enabled) : shown;
    const bool consume = !shown.empty() && complete != shown && replace(complete, enabled, shift, static_cast<WORD>(key), input.flags);
    reset();
    if (consume) suppressed[key] = true;
    return consume;
}

wchar_t Keyboard::translate(const KBDLLHOOKSTRUCT& key, bool shift) const {
    BYTE state[256]{};
    state[VK_SHIFT] = shift ? 0x80 : 0;
    state[VK_CAPITAL] = static_cast<BYTE>(GetKeyState(VK_CAPITAL) & 1);
    state[VK_NUMLOCK] = static_cast<BYTE>(GetKeyState(VK_NUMLOCK) & 1);
    wchar_t buffer[8]{};
    const int count = ToUnicodeEx(key.vkCode, key.scanCode, state, buffer, ARRAYSIZE(buffer), 4, GetKeyboardLayout(threadId));
    return count == 1 ? buffer[0] : 0;
}

void Keyboard::trackFocus() {
    HWND nextForeground = GetForegroundWindow();
    DWORD process = 0;
    DWORD thread = GetWindowThreadProcessId(nextForeground, &process);
    GUITHREADINFO info{sizeof(info)};
    HWND nextFocus = GetGUIThreadInfo(thread, &info) ? info.hwndFocus : nextForeground;
    if (nextForeground == foreground && nextFocus == focus && process == processId) return;
    reset();
    foreground = nextForeground;
    focus = nextFocus;
    threadId = thread;
    if (process != processId) {
        processId = process;
        application.clear();
        if (process && process != GetCurrentProcessId()) {
            HANDLE target = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, FALSE, process);
            if (target) {
                wchar_t path[1024];
                DWORD size = ARRAYSIZE(path);
                if (QueryFullProcessImageNameW(target, 0, path, &size)) application.assign(path, size);
                CloseHandle(target);
            }
        }
    }
    wchar_t name[64]{};
    GetClassNameW(focus, name, ARRAYSIZE(name));
    protectedEdit = (_wcsicmp(name, L"Edit") == 0 || _wcsnicmp(name, L"RichEdit", 8) == 0) &&
        (GetWindowLongPtrW(focus, GWL_STYLE) & ES_PASSWORD);
}

bool Keyboard::replace(const std::wstring& output, bool enabled, bool shift, WORD endKey, DWORD endFlags) {
    const auto [prefix, backspaces] = textEdit(shown, output, options.encoding);
    std::vector<INPUT> events;
    events.reserve(2 * (backspaces + output.size() - prefix) + 8);
    for (size_t i = 0; i < backspaces; ++i) press(events, VK_BACK);

    const auto suffix = output.substr(prefix);
    if (enabled && options.clipboard && options.encoding != 2 && !suffix.empty() && prepareClipboard(suffix)) {
        const bool left = keys[VK_LSHIFT] || keys[VK_SHIFT];
        const bool right = keys[VK_RSHIFT];
        if (shift && left) event(events, VK_LSHIFT, 0, KEYEVENTF_KEYUP);
        if (shift && right) event(events, VK_RSHIFT, 0, KEYEVENTF_KEYUP);
        event(events, VK_LCONTROL, 0, 0);
        press(events, 'V');
        event(events, VK_LCONTROL, 0, KEYEVENTF_KEYUP);
        if (shift && left) event(events, VK_LSHIFT, 0, 0);
        if (shift && right) event(events, VK_RSHIFT, 0, 0);
    } else {
        for (wchar_t c : suffix) press(events, 0, c, KEYEVENTF_UNICODE);
    }
    if (endKey) press(events, endKey, 0, endFlags & LLKHF_EXTENDED);
    return events.empty() || SendInput(static_cast<UINT>(events.size()), events.data(), sizeof(INPUT)) == events.size();
}

bool Keyboard::prepareClipboard(const std::wstring& text) {
    if (!ole) ole = LoadLibraryW(L"ole32.dll");
    if (!ole) return false;
    if (!oleInitialized) oleInitialized = SUCCEEDED(oleCall<HRESULT (WINAPI*)(LPVOID)>(ole, "OleInitialize")(nullptr));
    if (!oleInitialized) return false;

    if (clipboardPending && GetClipboardSequenceNumber() != clipboardVersion) restoreClipboard();
    IDataObject* previous = nullptr;
    if (!clipboardPending &&
        FAILED(oleCall<HRESULT (WINAPI*)(IDataObject**)>(ole, "OleGetClipboard")(&previous))) return false;

    const size_t bytes = (text.size() + 1) * sizeof(wchar_t);
    HGLOBAL buffer = GlobalAlloc(GMEM_MOVEABLE, bytes);
    void* data = buffer ? GlobalLock(buffer) : nullptr;
    if (data) {
        std::memcpy(data, text.c_str(), bytes);
        GlobalUnlock(buffer);
    }
    bool ready = false;
    bool emptied = false;
    if (data && OpenClipboard(owner)) {
        emptied = EmptyClipboard() != FALSE;
        const UINT excludedFormat = RegisterClipboardFormatW(L"ExcludeClipboardContentFromMonitorProcessing");
        if (emptied && excludedFormat) {
            HGLOBAL exclusion = GlobalAlloc(GMEM_MOVEABLE, sizeof(DWORD));
            DWORD* flag = exclusion ? static_cast<DWORD*>(GlobalLock(exclusion)) : nullptr;
            if (flag) {
                *flag = 1;
                GlobalUnlock(exclusion);
            }
            if (flag && SetClipboardData(excludedFormat, exclusion))
                ready = SetClipboardData(CF_UNICODETEXT, buffer) != nullptr;
            else if (exclusion) GlobalFree(exclusion);
        }
        CloseClipboard();
    }
    if (!ready) {
        if (buffer) GlobalFree(buffer);
        if (emptied) {
            IDataObject* original = clipboardPending ? previousClipboard : previous;
            if (SUCCEEDED(oleCall<HRESULT (WINAPI*)(IDataObject*)>(ole, "OleSetClipboard")(original)) && original)
                oleCall<HRESULT (WINAPI*)()>(ole, "OleFlushClipboard")();
        }
        if (previous) previous->Release();
        return false;
    }
    if (!clipboardPending) previousClipboard = previous;
    clipboardPending = true;
    clipboardVersion = GetClipboardSequenceNumber();
    SetTimer(owner, clipboardTimer, 550, nullptr);
    return true;
}

void Keyboard::restoreClipboard() {
    KillTimer(owner, clipboardTimer);
    if (!clipboardPending) return;
    if (GetClipboardSequenceNumber() == clipboardVersion &&
        SUCCEEDED(oleCall<HRESULT (WINAPI*)(IDataObject*)>(ole, "OleSetClipboard")(previousClipboard)) && previousClipboard)
        oleCall<HRESULT (WINAPI*)()>(ole, "OleFlushClipboard")();
    if (previousClipboard) previousClipboard->Release();
    previousClipboard = nullptr;
    clipboardPending = false;
}

}

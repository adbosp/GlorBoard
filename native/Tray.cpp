#include "Keyboard.hpp"

#include <shellapi.h>
#include <memory>
#include <utility>

namespace {

using namespace glorboard;
constexpr wchar_t hostClass[] = L"GlorBoard.Native.Tray";
constexpr wchar_t hostTitle[] = L"GlorBoard";
constexpr UINT trayEvent = WM_APP + 1;
constexpr UINT openSettingsMessage = WM_APP + 2;
constexpr UINT saveModeMessage = WM_APP + 3;
constexpr UINT shutdownMessage = WM_APP + 10;
constexpr UINT settingsChangedMessage = WM_APP + 11;
constexpr UINT settingsReadyMessage = WM_APP + 12;
constexpr UINT settingsNotificationMessage = WM_APP + 13;
constexpr UINT uiModeMessage = WM_APP + 20;
constexpr UINT uiMethodMessage = WM_APP + 21;
constexpr UINT uiExitMessage = WM_APP + 22;
constexpr UINT openItem = 201, toggleItem = 202, telexItem = 203, vniItem = 204, exitItem = 205;

Preferences options;
std::unique_ptr<Keyboard> keyboard;
HWND host = nullptr;
HWND settingsWindow = nullptr;
HANDLE settingsProcess = nullptr;
HWINEVENTHOOK foregroundHook = nullptr;
NOTIFYICONDATAW icon{};
HICON vietnameseIcon = nullptr;
HICON englishIcon = nullptr;
HICON applicationIcon = nullptr;
UINT taskbarCreated = 0;
std::wstring lastApplication;
bool ready = false;
int lastTrayState = -1;
bool lastLanguage = false;

const wchar_t* tr(const wchar_t* vietnamese, const wchar_t* english) {
    return options.vietnamese ? vietnamese : english;
}

void captureApplication() {
    if (!options.perApp) return;
    auto foreground = GetForegroundWindow();
    if (foreground == settingsWindow || foreground == host || !keyboard) return;
    const auto& application = keyboard->activeApplication();
    if (!application.empty()) lastApplication = application;
}

const wchar_t* modeLabel(bool enabled) {
    return enabled ? tr(L"Đang gõ tiếng Việt", L"Vietnamese input active")
                   : tr(L"Đang gõ tiếng Anh", L"English input active");
}

void updateTray(bool add = false) {
    captureApplication();
    const bool enabled = Keyboard::isEnabled(options, lastApplication);
    const int state = ready || settingsWindow ? static_cast<int>(enabled) : 2;
    if (!add && lastTrayState == state && lastLanguage == options.vietnamese) return;
    lastTrayState = state;
    lastLanguage = options.vietnamese;
    icon.hIcon = state == 2 ? applicationIcon : (state == 1 ? vietnameseIcon : englishIcon);
    const auto label = std::wstring(L"GlorBoard · ") +
        (state == 2 ? tr(L"Bộ gõ chưa chạy", L"Input unavailable") : modeLabel(enabled));
    lstrcpynW(icon.szTip, label.c_str(), ARRAYSIZE(icon.szTip));
    icon.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP;
    Shell_NotifyIconW(add ? NIM_ADD : NIM_MODIFY, &icon);
}

void balloon(bool enabled) {
    icon.uFlags = NIF_INFO;
    icon.dwInfoFlags = NIIF_INFO;
    lstrcpynW(icon.szInfoTitle, L"GlorBoard", ARRAYSIZE(icon.szInfoTitle));
    lstrcpynW(icon.szInfo, modeLabel(enabled), ARRAYSIZE(icon.szInfo));
    Shell_NotifyIconW(NIM_MODIFY, &icon);
}

void toggleMode(const std::wstring& application) {
    if (!application.empty()) lastApplication = application;
    const bool enabled = !Keyboard::isEnabled(options, lastApplication);
    if (options.perApp && !lastApplication.empty()) options.appModes[lastApplication] = enabled;
    else options.enabled = enabled;
    if (keyboard) keyboard->reset();
    PostMessageW(host, saveModeMessage, enabled, 0);
}

void CALLBACK foregroundChanged(HWINEVENTHOOK, DWORD, HWND, LONG, LONG, DWORD, DWORD) {
    updateTray();
}

void configureForegroundHook() {
    if (options.perApp && !foregroundHook)
        foregroundHook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, nullptr,
                                         foregroundChanged, 0, 0, WINEVENT_OUTOFCONTEXT);
    else if (!options.perApp && foregroundHook) {
        UnhookWinEvent(foregroundHook);
        foregroundHook = nullptr;
    }
}

bool startKeyboard() {
    ready = keyboard->start();
    updateTray();
    return ready;
}

void settingsClosed() {
    CloseHandle(settingsProcess);
    settingsProcess = nullptr;
    settingsWindow = nullptr;
    options = loadPreferences();
    configureForegroundHook();
    if (!startKeyboard())
        MessageBoxW(host, tr(L"Không thể khởi động bộ gõ.", L"Could not start the keyboard hook."), L"GlorBoard", MB_ICONWARNING);
}

std::wstring settingsExecutable() {
    wchar_t file[MAX_PATH];
    DWORD length = GetModuleFileNameW(nullptr, file, ARRAYSIZE(file));
    if (!length || length == ARRAYSIZE(file)) return {};
    const std::wstring name(file, length);
    return name.substr(0, name.find_last_of(L'\\') + 1) + L"GlorBoard.Panel.exe";
}

void openSettings() {
    if (settingsProcess) {
        if (settingsWindow) {
            ShowWindow(settingsWindow, SW_RESTORE);
            SetForegroundWindow(settingsWindow);
        }
        return;
    }
    const auto file = settingsExecutable();
    std::wstring command = L"\"" + file + L"\" --native-settings " +
        std::to_wstring(reinterpret_cast<UINT_PTR>(host));
    STARTUPINFOW startup{sizeof(startup)};
    PROCESS_INFORMATION process{};
    captureApplication();
    if (file.empty() || !CreateProcessW(file.c_str(), command.data(), nullptr, nullptr, FALSE, 0, nullptr, nullptr, &startup, &process)) {
        MessageBoxW(host, tr(L"Không mở được GlorBoard.Panel.exe bên cạnh bộ gõ.",
                            L"Could not open the companion GlorBoard.Panel.exe."),
                    L"GlorBoard", MB_ICONWARNING);
        return;
    }
    CloseHandle(process.hThread);
    settingsProcess = process.hProcess;
    // The child registers its settings window and installs its own hook during handoff.
}

void quit() {
    if (settingsWindow) PostMessageW(settingsWindow, uiExitMessage, 0, 0);
    DestroyWindow(host);
}

void handleTrayToggle() {
    if (settingsWindow) PostMessageW(settingsWindow, uiModeMessage, 0, 0);
    else if (!settingsProcess) {
        captureApplication();
        toggleMode(lastApplication);
    }
}

void showTrayMenu() {
    captureApplication();
    POINT cursor{};
    GetCursorPos(&cursor);
    HMENU menu = CreatePopupMenu(), method = CreatePopupMenu();
    if (!menu || !method) {
        if (menu) DestroyMenu(menu);
        if (method) DestroyMenu(method);
        return;
    }
    AppendMenuW(menu, MF_STRING, openItem, tr(L"Mở GlorBoard", L"Open GlorBoard"));
    AppendMenuW(menu, MF_STRING, toggleItem,
                Keyboard::isEnabled(options, lastApplication) ? tr(L"Chuyển sang tiếng Anh", L"Switch to English")
                                                              : tr(L"Chuyển sang tiếng Việt", L"Switch to Vietnamese"));
    AppendMenuW(method, MF_STRING | (options.method == 0 ? MF_CHECKED : MF_UNCHECKED), telexItem, L"Telex");
    AppendMenuW(method, MF_STRING | (options.method == 1 ? MF_CHECKED : MF_UNCHECKED), vniItem, L"VNI");
    AppendMenuW(menu, MF_POPUP, reinterpret_cast<UINT_PTR>(method), tr(L"Kiểu gõ", L"Input method"));
    AppendMenuW(menu, MF_SEPARATOR, 0, nullptr);
    AppendMenuW(menu, MF_STRING, exitItem, tr(L"Kết thúc", L"Exit"));
    SetForegroundWindow(host);
    const UINT action = TrackPopupMenu(menu, TPM_RETURNCMD | TPM_RIGHTBUTTON | TPM_NONOTIFY, cursor.x, cursor.y, 0, host, nullptr);
    DestroyMenu(menu);
    PostMessageW(host, WM_NULL, 0, 0);
    switch (action) {
        case openItem: openSettings(); break;
        case toggleItem: handleTrayToggle(); break;
        case telexItem: case vniItem:
            if (settingsWindow) PostMessageW(settingsWindow, uiMethodMessage, action == vniItem, 0);
            else if (!settingsProcess) {
                options.method = action == vniItem;
                keyboard->reset();
                if (!saveMethod(options.method))
                    MessageBoxW(host, tr(L"Không lưu được kiểu gõ.", L"Could not save the input method."), L"GlorBoard", MB_ICONWARNING);
            }
            break;
        case exitItem: quit(); break;
    }
}

LRESULT CALLBACK windowProc(HWND window, UINT message, WPARAM wParam, LPARAM lParam) {
    if (message == taskbarCreated && taskbarCreated) {
        updateTray(true);
        return 0;
    }
    switch (message) {
        case trayEvent:
            switch (lParam) {
                case WM_LBUTTONUP: handleTrayToggle(); break;
                case WM_RBUTTONUP: showTrayMenu(); break;
            }
            return 0;
        case WM_TIMER:
            if (wParam == Keyboard::clipboardTimer) keyboard->restoreClipboard();
            return 0;
        case openSettingsMessage: openSettings(); return 0;
        case saveModeMessage:
            if (!saveMode(options, lastApplication, wParam != 0))
                MessageBoxW(host, tr(L"Không lưu được chế độ gõ.", L"Could not save the input mode."), L"GlorBoard", MB_ICONWARNING);
            if (options.sound) MessageBeep(MB_ICONASTERISK);
            if (options.notifications) balloon(wParam != 0);
            updateTray();
            return 0;
        case settingsReadyMessage: {
            HWND child = reinterpret_cast<HWND>(wParam);
            DWORD pid = 0;
            GetWindowThreadProcessId(child, &pid);
            if (!settingsProcess || GetProcessId(settingsProcess) != pid) return 0;
            settingsWindow = child;
            keyboard->stop();
            ready = false;
            MSG pending{};
            while (PeekMessageW(&pending, window, saveModeMessage, saveModeMessage, PM_REMOVE))
                DispatchMessageW(&pending);
            updateTray();
            return 1;
        }
        case settingsChangedMessage:
            if (settingsProcess) {
                options = loadPreferences();
                configureForegroundHook();
                updateTray();
            }
            return 0;
        case settingsNotificationMessage: balloon(wParam != 0); return 0;
        case shutdownMessage: case WM_CLOSE: quit(); return 0;
        case WM_DESTROY:
            Shell_NotifyIconW(NIM_DELETE, &icon);
            if (foregroundHook) UnhookWinEvent(foregroundHook);
            foregroundHook = nullptr;
            if (keyboard) keyboard->stop();
            PostQuitMessage(0);
            return 0;
    }
    return DefWindowProcW(window, message, wParam, lParam);
}

}

int WINAPI wWinMain(HINSTANCE instance, HINSTANCE, PWSTR command, int) {
    HANDLE lock = CreateMutexW(nullptr, TRUE, L"Local\\GlorBoard.Native.1");
    if (!lock) return 1;
    const bool trayOnly = std::wstring_view(command).find(L"--tray") != std::wstring_view::npos;
    if (GetLastError() == ERROR_ALREADY_EXISTS) {
        if (!trayOnly) {
            if (HWND previous = FindWindowW(hostClass, hostTitle)) PostMessageW(previous, openSettingsMessage, 0, 0);
        }
        CloseHandle(lock);
        return 0;
    }
    options = loadPreferences();
    taskbarCreated = RegisterWindowMessageW(L"TaskbarCreated");
    WNDCLASSW type{};
    type.lpfnWndProc = windowProc;
    type.hInstance = instance;
    type.lpszClassName = hostClass;
    if (!RegisterClassW(&type)) { CloseHandle(lock); return 1; }
    host = CreateWindowExW(WS_EX_TOOLWINDOW, hostClass, hostTitle, WS_POPUP,
                           0, 0, 0, 0, nullptr, nullptr, instance, nullptr);
    if (!host) { CloseHandle(lock); return 1; }
    const int smallWidth = GetSystemMetrics(SM_CXSMICON);
    const int smallHeight = GetSystemMetrics(SM_CYSMICON);
    applicationIcon = static_cast<HICON>(LoadImageW(instance, MAKEINTRESOURCEW(101), IMAGE_ICON, smallWidth, smallHeight, LR_SHARED));
    englishIcon = static_cast<HICON>(LoadImageW(instance, MAKEINTRESOURCEW(102), IMAGE_ICON, smallWidth, smallHeight, LR_SHARED));
    vietnameseIcon = static_cast<HICON>(LoadImageW(instance, MAKEINTRESOURCEW(103), IMAGE_ICON, smallWidth, smallHeight, LR_SHARED));
    if (!applicationIcon) applicationIcon = LoadIconW(nullptr, IDI_APPLICATION);
    if (!englishIcon) englishIcon = applicationIcon;
    if (!vietnameseIcon) vietnameseIcon = applicationIcon;
    keyboard = std::make_unique<Keyboard>(options, host, toggleMode);
    icon.cbSize = sizeof(icon);
    icon.hWnd = host;
    icon.uID = 1;
    icon.uCallbackMessage = trayEvent;
    ready = keyboard->start();
    configureForegroundHook();
    updateTray(true);
    if (!ready)
        MessageBoxW(host, tr(L"Không cài được hook bàn phím Windows.", L"Could not install the Windows keyboard hook."),
                    L"GlorBoard", MB_ICONWARNING);
    if (!trayOnly && options.showOnStart) openSettings();
    bool running = true;
    while (running) {
        HANDLE process = settingsProcess;
        DWORD result = MsgWaitForMultipleObjects(process ? 1 : 0, process ? &process : nullptr, FALSE, INFINITE, QS_ALLINPUT);
        if (process && result == WAIT_OBJECT_0) settingsClosed();
        if (result == WAIT_FAILED) break;
        MSG message{};
        while (PeekMessageW(&message, nullptr, 0, 0, PM_REMOVE)) {
            if (message.message == WM_QUIT) { running = false; break; }
            TranslateMessage(&message);
            DispatchMessageW(&message);
        }
    }
    if (settingsProcess) CloseHandle(settingsProcess);
    keyboard.reset();
    CloseHandle(lock);
    return 0;
}

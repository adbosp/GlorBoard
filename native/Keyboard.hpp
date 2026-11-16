#pragma once

#include "Composer.hpp"

#include <objidl.h>
#include <array>

namespace glorboard {

class Keyboard {
public:
    static constexpr UINT clipboardTimer = 101;
    using Toggle = void (*)(const std::wstring&);

    Keyboard(Preferences& options, HWND owner, Toggle toggle);
    ~Keyboard();

    bool start();
    void stop();
    void reset();
    void restoreClipboard();
    const std::wstring& activeApplication();
    static bool isEnabled(const Preferences& options, const std::wstring& application);

private:
    static Keyboard* active;
    Preferences& options;
    HWND owner;
    Toggle toggle;
    HHOOK keyHook = nullptr;
    HHOOK mouseHook = nullptr;
    Composer composer;
    std::array<bool, 256> keys{};
    std::array<bool, 256> suppressed{};
    std::wstring shown;
    HWND foreground = nullptr;
    HWND focus = nullptr;
    DWORD processId = 0;
    DWORD threadId = 0;
    std::wstring application;
    bool protectedEdit = false;
    bool switchCandidate = false;

    HMODULE ole = nullptr;
    bool oleInitialized = false;
    IDataObject* previousClipboard = nullptr;
    DWORD clipboardVersion = 0;
    bool clipboardPending = false;

    void trackFocus();
    bool handle(const KBDLLHOOKSTRUCT& keyEvent, bool released);
    wchar_t translate(const KBDLLHOOKSTRUCT& keyEvent, bool shift) const;
    bool replace(const std::wstring& output, bool enabled, bool shift, WORD endKey, DWORD endFlags);
    bool prepareClipboard(const std::wstring& text);
    static LRESULT CALLBACK keyCallback(int code, WPARAM message, LPARAM data);
    static LRESULT CALLBACK mouseCallback(int code, WPARAM message, LPARAM data);
};

}

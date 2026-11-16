#pragma once

#include <windows.h>
#include <map>
#include <optional>
#include <string>
#include <string_view>

namespace glorboard {

struct OrdinalIgnoreCase {
    bool operator()(const std::wstring& first, const std::wstring& second) const {
        return CompareStringOrdinal(first.data(), static_cast<int>(first.size()), second.data(),
                                    static_cast<int>(second.size()), TRUE) == CSTR_LESS_THAN;
    }
};

struct Preferences {
    int method = 0;
    int encoding = 0;
    int hotkey = 0;
    bool enabled = true;
    bool freeTyping = true;
    bool modernTone = false;
    bool clipboard = false;
    bool spellCheck = true;
    bool restore = true;
    bool notifications = false;
    bool sound = true;
    bool macrosEnabled = false;
    bool macrosInEnglish = false;
    bool showOnStart = true;
    bool perApp = false;
    bool vietnamese = true;
    bool standaloneW = true;
    bool bracketKeys = true;
    std::map<std::wstring, std::wstring, OrdinalIgnoreCase> macros;
    std::map<std::wstring, bool, OrdinalIgnoreCase> appModes;
};

Preferences loadPreferences();
bool saveMode(const Preferences& options, const std::wstring& application, bool enabled);
bool saveMethod(int method);

}

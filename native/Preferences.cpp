#include "Preferences.hpp"

#include <cwchar>
#include <utility>

namespace glorboard {
namespace {

// Keep values as raw JSON so that host-only updates preserve settings unknown to the tray process.
using Object = std::map<std::wstring, std::wstring>;

class JsonReader {
public:
    explicit JsonReader(std::wstring_view bytes) : input(bytes) {}

    std::optional<Object> readObject() {
        Object object;
        if (!consume(L'{')) return {};
        if (!consume(L'}')) {
            do {
                auto key = readString();
                if (!key || !consume(L':')) return {};
                skipSpace();
                const size_t start = position;
                if (!skipValue(0)) return {};
                object[*key] = input.substr(start, position - start);
            } while (consume(L','));
            if (!consume(L'}')) return {};
        }
        skipSpace();
        if (position != input.size()) return {};
        return object;
    }

    std::optional<std::wstring> readString() {
        if (!consume(L'"')) return {};
        std::wstring value;
        while (position < input.size()) {
            wchar_t c = input[position++];
            if (c == L'"') return value;
            if (c < 0x20) return {};
            if (c == L'\\') {
                if (position == input.size()) return {};
                c = input[position++];
                switch (c) {
                    case L'"': case L'\\': case L'/': break;
                    case L'b': c = L'\b'; break;
                    case L'f': c = L'\f'; break;
                    case L'n': c = L'\n'; break;
                    case L'r': c = L'\r'; break;
                    case L't': c = L'\t'; break;
                    case L'u': {
                        if (input.size() - position < 4) return {};
                        unsigned code = 0;
                        for (int i = 0; i < 4; ++i) {
                            wchar_t digit = input[position++];
                            if (digit >= L'0' && digit <= L'9') code = code * 16 + digit - L'0';
                            else if (digit >= L'a' && digit <= L'f') code = code * 16 + digit - L'a' + 10;
                            else if (digit >= L'A' && digit <= L'F') code = code * 16 + digit - L'A' + 10;
                            else return {};
                        }
                        c = static_cast<wchar_t>(code);
                        break;
                    }
                    default: return {};
                }
            }
            value.push_back(c);
        }
        return {};
    }

private:
    std::wstring_view input;
    size_t position = 0;

    void skipSpace() {
        while (position < input.size() &&
               std::wstring_view(L" \r\n\t\xfeff").find(input[position]) != std::wstring_view::npos) ++position;
    }

    bool consume(wchar_t expected) {
        skipSpace();
        if (position == input.size() || input[position] != expected) return false;
        ++position;
        return true;
    }

    bool skipValue(unsigned depth) {
        skipSpace();
        if (position == input.size() || depth > 64) return false;
        if (input[position] == L'"') return readString().has_value();
        if (consume(L'{')) {
            if (consume(L'}')) return true;
            do {
                if (!readString() || !consume(L':') || !skipValue(depth + 1)) return false;
            } while (consume(L','));
            return consume(L'}');
        }
        if (consume(L'[')) {
            if (consume(L']')) return true;
            do {
                if (!skipValue(depth + 1)) return false;
            } while (consume(L','));
            return consume(L']');
        }
        size_t start = position;
        while (position < input.size() &&
               std::wstring_view(L" ,\r\n\t]}").find(input[position]) == std::wstring_view::npos) ++position;
        const auto token = input.substr(start, position - start);
        return token == L"true" || token == L"false" || token == L"null" ||
            (!token.empty() && token.find_first_not_of(L"-+0123456789.eE") == std::wstring_view::npos);
    }
};

std::wstring settingsPath() {
    const DWORD size = GetEnvironmentVariableW(L"LOCALAPPDATA", nullptr, 0);
    if (!size) return {};
    std::wstring directory(size, L'\0');
    const DWORD length = GetEnvironmentVariableW(L"LOCALAPPDATA", directory.data(), size);
    if (!length || length >= size) return {};
    directory.resize(length);
    return directory + L"\\GlorBoard\\settings.json";
}

std::optional<Object> readSettings() {
    const auto path = settingsPath();
    if (path.empty()) return {};
    HANDLE file = CreateFileW(path.c_str(), GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_DELETE, nullptr,
                              OPEN_EXISTING, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE)
        return GetLastError() == ERROR_FILE_NOT_FOUND || GetLastError() == ERROR_PATH_NOT_FOUND
            ? std::optional<Object>(Object{}) : std::nullopt;
    LARGE_INTEGER size{};
    std::string bytes;
    DWORD read = 0;
    if (GetFileSizeEx(file, &size) && size.QuadPart > 0 && size.QuadPart <= 4 * 1024 * 1024) {
        bytes.resize(static_cast<size_t>(size.QuadPart));
        if (!ReadFile(file, bytes.data(), static_cast<DWORD>(bytes.size()), &read, nullptr)) read = 0;
    }
    CloseHandle(file);
    if (read == 0 || read != bytes.size()) return {};
    int length = MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(), read, nullptr, 0);
    if (length == 0) return {};
    std::wstring json(length, L'\0');
    MultiByteToWideChar(CP_UTF8, MB_ERR_INVALID_CHARS, bytes.data(), read, json.data(), length);
    return JsonReader(json).readObject();
}

std::wstring quote(const std::wstring& value) {
    std::wstring json = L"\"";
    constexpr wchar_t digits[] = L"0123456789abcdef";
    for (wchar_t c : value) {
        if (c == L'"' || c == L'\\') json += L'\\';
        if (c < 0x20) {
            json += L"\\u00";
            json += digits[c >> 4];
            json += digits[c & 15];
        } else json += c;
    }
    json += L'"';
    return json;
}

std::wstring serialize(const Object& object) {
    std::wstring json = L"{";
    for (const auto& [key, value] : object) {
        if (json.size() > 1) json += L',';
        json += L"\n  " + quote(key) + L": " + value;
    }
    return json + L"\n}";
}

bool writeSettings(const Object& object) {
    const auto path = settingsPath();
    if (path.empty()) return false;
    const auto parent = path.substr(0, path.find_last_of(L'\\'));
    if (!CreateDirectoryW(parent.c_str(), nullptr) && GetLastError() != ERROR_ALREADY_EXISTS) return false;
    const auto text = serialize(object);
    const int length = WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(),
                                          static_cast<int>(text.size()), nullptr, 0, nullptr, nullptr);
    if (!length) return false;
    std::string bytes(length, '\0');
    WideCharToMultiByte(CP_UTF8, WC_ERR_INVALID_CHARS, text.data(), static_cast<int>(text.size()),
                        bytes.data(), length, nullptr, nullptr);
    const auto temp = path + L".native.tmp";
    HANDLE file = CreateFileW(temp.c_str(), GENERIC_WRITE, 0, nullptr, CREATE_ALWAYS, FILE_ATTRIBUTE_NORMAL, nullptr);
    if (file == INVALID_HANDLE_VALUE) return false;
    DWORD written = 0;
    const bool ready = WriteFile(file, bytes.data(), length, &written, nullptr) && written == bytes.size();
    CloseHandle(file);
    if (ready && MoveFileExW(temp.c_str(), path.c_str(), MOVEFILE_REPLACE_EXISTING | MOVEFILE_WRITE_THROUGH)) return true;
    DeleteFileW(temp.c_str());
    return false;
}

}

Preferences loadPreferences() {
    Preferences options;
    auto settings = readSettings();
    if (!settings) return options;
    const auto value = [&](const wchar_t* key) {
        auto it = settings->find(key);
        return it == settings->end() ? std::wstring{} : it->second;
    };
    const auto boolean = [&](const wchar_t* key, bool& setting) {
        const auto json = value(key);
        if (json == L"true" || json == L"false") setting = json == L"true";
    };
    const auto option = [&](const wchar_t* key, int& setting, int max) {
        const auto json = value(key);
        if (json.size() == 1 && json.front() >= L'0' && json.front() <= L'0' + max) setting = json.front() - L'0';
    };
    option(L"Method", options.method, 1);
    option(L"Encoding", options.encoding, 2);
    option(L"Hotkey", options.hotkey, 1);
    boolean(L"Enabled", options.enabled);
    boolean(L"FreeTyping", options.freeTyping);
    boolean(L"ModernTone", options.modernTone);
    boolean(L"Clipboard", options.clipboard);
    boolean(L"SpellCheck", options.spellCheck);
    boolean(L"Restore", options.restore);
    boolean(L"Notifications", options.notifications);
    boolean(L"Sound", options.sound);
    boolean(L"MacrosEnabled", options.macrosEnabled);
    boolean(L"MacrosInEnglish", options.macrosInEnglish);
    boolean(L"ShowOnStart", options.showOnStart);
    boolean(L"PerApp", options.perApp);
    boolean(L"Vietnamese", options.vietnamese);
    boolean(L"StandaloneW", options.standaloneW);
    boolean(L"BracketKeys", options.bracketKeys);
    const auto macros = value(L"Macros");
    if (auto entries = JsonReader(macros).readObject()) {
        for (const auto& [key, raw] : *entries)
            if (auto text = JsonReader(raw).readString()) options.macros[key] = std::move(*text);
    }
    const auto modes = value(L"AppModes");
    if (auto entries = JsonReader(modes).readObject()) {
        for (const auto& [key, raw] : *entries)
            if (raw == L"true" || raw == L"false") options.appModes[key] = raw == L"true";
    }
    return options;
}

bool saveMode(const Preferences& options, const std::wstring& application, bool enabled) {
    auto settings = readSettings();
    if (!settings) return false;
    if (options.perApp && !application.empty()) {
        Object modes;
        for (const auto& [app, mode] : options.appModes) modes[app] = mode ? L"true" : L"false";
        (*settings)[L"AppModes"] = serialize(modes);
    } else (*settings)[L"Enabled"] = enabled ? L"true" : L"false";
    return writeSettings(*settings);
}

bool saveMethod(int method) {
    auto settings = readSettings();
    if (!settings) return false;
    (*settings)[L"Method"] = method == 1 ? L"1" : L"0";
    return writeSettings(*settings);
}

}

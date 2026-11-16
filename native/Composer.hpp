#pragma once

#include "Preferences.hpp"

namespace glorboard {

class Composer {
public:
    void reset();
    bool backspace(const Preferences& options);
    void add(wchar_t key, const Preferences& options, bool enabled);
    std::wstring rendered(const Preferences& options) const;
    std::wstring finish(const Preferences& options, bool enabled) const;

private:
    std::wstring raw;
    std::wstring word;
    int tone = 0;
    bool canRestore = true;
    bool literalRemainder = false;
    bool full = false;

    bool shape(wchar_t key, const Preferences& options);
    void escape(wchar_t key, std::wstring reverted);
    std::wstring render(const Preferences& options) const;
};

bool combiningMark(wchar_t c);

struct TextEdit {
    size_t prefix;
    size_t backspaces;
};

TextEdit textEdit(std::wstring_view before, std::wstring_view after, int encoding);

}

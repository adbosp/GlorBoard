#include "Composer.hpp"

#include <utility>

namespace glorboard {
namespace {

constexpr std::wstring_view tones = L"\u0301\u0300\u0309\u0303\u0323";
constexpr std::wstring_view vowels = L"aăâeêioôơuưy";
constexpr std::wstring_view initials = L"||b|c|ch|d|đ|g|gh|gi|h|k|kh|l|m|n|ng|ngh|nh|p|ph|q|qu|r|s|t|th|tr|v|x|";
constexpr std::wstring_view finals = L"||c|ch|m|n|ng|nh|p|t|";
constexpr std::wstring_view nuclei = L"|a|ă|â|e|ê|i|o|ô|ơ|u|ư|y|ai|ao|au|ay|âu|ây|eo|êu|ia|iê|iu|oa|oă|oe|oi|ôi|ơi|oo|ua|uâ|uê|ui|uo|uô|uơ|uy|ưa|ưi|ươ|ưu|yê|iêu|oai|oao|oay|oeo|uây|uôi|uya|uyê|uyu|ươi|ươu|yêu|";
constexpr int maxWordLength = 96;

int indexOf(std::wstring_view text, wchar_t c) {
    const auto pos = text.find(c);
    return pos == text.npos ? -1 : static_cast<int>(pos);
}

std::wstring lowerCase(const std::wstring& text) {
    if (text.empty()) return {};
    std::wstring value(text.size(), L'\0');
    if (LCMapStringEx(LOCALE_NAME_INVARIANT, LCMAP_LOWERCASE, text.data(), static_cast<int>(text.size()),
                      value.data(), static_cast<int>(value.size()), nullptr, nullptr, 0) == 0) return text;
    return value;
}

wchar_t lowerCase(wchar_t c) {
    if (c >= L'A' && c <= L'Z') return c - L'A' + L'a';
    return c;
}

wchar_t matchCase(wchar_t value, wchar_t model) {
    WORD type = 0;
    if (GetStringTypeW(CT_CTYPE1, &model, 1, &type) && (type & C1_UPPER)) {
        wchar_t upper;
        if (LCMapStringEx(LOCALE_NAME_INVARIANT, LCMAP_UPPERCASE, &value, 1, &upper, 1, nullptr, nullptr, 0))
            return upper;
    }
    return value;
}

std::wstring normalized(const std::wstring& input, NORM_FORM form) {
    if (input.empty()) return {};
    const int size = NormalizeString(form, input.data(), static_cast<int>(input.size()), nullptr, 0);
    if (size <= 0) return input;
    std::wstring result(size, L'\0');
    const int written = NormalizeString(form, input.data(), static_cast<int>(input.size()), result.data(), size);
    if (written <= 0) return input;
    result.resize(written);
    return result;
}

std::pair<int, int> vowelRange(std::wstring_view word) {
    const int size = static_cast<int>(word.size());
    for (int i = 0; i < size; ++i) {
        if (indexOf(vowels, word[i]) < 0) continue;
        if (i == 1 && size > 2 && indexOf(vowels, word[2]) >= 0 &&
            (word.starts_with(L"qu") || word.starts_with(L"gi"))) continue;
        int end = i + 1;
        while (end < size && indexOf(vowels, word[end]) >= 0) ++end;
        return {i, end};
    }
    return {size, size};
}

int toneIndex(const std::wstring& value, bool modern) {
    const std::wstring lower = lowerCase(value);
    const auto [start, end] = vowelRange(lower);
    if (start == end) return -1;
    for (int i = end - 1; i >= start; --i)
        if (indexOf(L"êơ", lower[i]) >= 0) return i;
    for (int i = end - 1; i >= start; --i)
        if (indexOf(L"ăâô", lower[i]) >= 0) return i;
    const auto nucleus = std::wstring_view(lower).substr(start, end - start);
    if (end - start == 3 || (end - start == 2 &&
        (end < static_cast<int>(lower.size()) || (modern && (nucleus == L"oa" || nucleus == L"oe" || nucleus == L"uy")))))
        return start + 1;
    return start;
}

bool inTable(std::wstring_view table, std::wstring_view value) {
    std::wstring key = L"|";
    key += value;
    key += L'|';
    return table.find(key) != table.npos;
}

bool isSyllable(const std::wstring& value, int usedTone, bool partial = false) {
    const std::wstring lower = lowerCase(value);
    std::wstring_view view(lower);
    const auto [start, end] = vowelRange(view);
    const auto initial = view.substr(0, start);
    if (start == end || !inTable(initials, initial) || !inTable(nuclei, view.substr(start, end - start))) return false;
    const auto ending = view.substr(end);
    if (partial) return inTable(finals, ending) || ending == L"h";
    if (!inTable(finals, ending) || initial == L"q") return false;
    return (ending != L"c" && ending != L"ch" && ending != L"p" && ending != L"t") ||
        usedTone == 0 || usedTone == 1 || usedTone == 5;
}

std::wstring encode(const std::wstring& input, int encoding) {
    if (encoding == 0) return input;
    const auto decomposed = normalized(input, NormalizationD);
    if (encoding == 1) return decomposed;
    std::wstring output;
    wchar_t tone = 0;
    for (wchar_t c : decomposed) {
        if (tone && !combiningMark(c)) {
            output += tone;
            tone = 0;
        }
        const int accent = indexOf(tones, c);
        if (accent >= 0) tone = L"'`?~."[accent];
        else switch (c) {
            case 0x0302: output += L'^'; break;
            case 0x0306: output += L'('; break;
            case 0x031b: output += L'+'; break;
            case L'đ': output += L"dd"; break;
            case L'Đ': output += L"DD"; break;
            default: output += c;
        }
    }
    if (tone) output += tone;
    return output;
}

}

bool combiningMark(wchar_t c) {
    WORD type = 0;
    // Windows also sets C3_NONSPACING on some precomposed alphabetic letters.
    return GetStringTypeW(CT_CTYPE3, &c, 1, &type) && (type & C3_NONSPACING) && !(type & C3_ALPHA);
}

TextEdit textEdit(std::wstring_view before, std::wstring_view after, int encoding) {
    size_t prefix = 0;
    while (prefix < before.size() && prefix < after.size() && before[prefix] == after[prefix]) ++prefix;
    if (encoding == 1)
        while (prefix && ((prefix < before.size() && combiningMark(before[prefix])) ||
                          (prefix < after.size() && combiningMark(after[prefix])))) --prefix;
    size_t backspaces = 0;
    for (wchar_t c : before.substr(prefix))
        if (!combiningMark(c)) ++backspaces;
    return {prefix, backspaces};
}

void Composer::reset() {
    raw.clear();
    word.clear();
    tone = 0;
    canRestore = true;
    literalRemainder = full = false;
}

bool Composer::backspace(const Preferences& options) {
    if (full || word.empty() || options.encoding == 1 || (options.encoding == 2 && word != raw)) {
        reset();
        return false;
    }
    if (toneIndex(word, options.modernTone) == static_cast<int>(word.size()) - 1) tone = 0;
    word.pop_back();
    if (!raw.empty()) raw.pop_back();
    canRestore = false;
    if (word.empty()) reset();
    return true;
}

void Composer::add(wchar_t key, const Preferences& options, bool enabled) {
    if (full) return;
    if (raw.size() >= maxWordLength) {
        reset();
        full = true;
        return;
    }
    raw += key;
    if (!enabled || literalRemainder) {
        word += key;
        return;
    }

    const bool telex = options.method == 0;
    const wchar_t lower = lowerCase(key);
    const int requested = indexOf(telex ? L"sfrxj" : L"12345", lower) + 1;
    if (requested > 0 && toneIndex(word, options.modernTone) >= 0 &&
        (!options.spellCheck || isSyllable(word, 0) || (options.freeTyping && isSyllable(word, 0, true)))) {
        if (requested == tone) escape(key, word);
        else tone = requested;
        return;
    }
    if (lower == (telex ? L'z' : L'0') && tone) {
        tone = 0;
        return;
    }
    if (shape(key, options)) return;
    if (telex && word.empty()) {
        if (options.bracketKeys && (key == L'[' || key == L']')) {
            word = key == L'[' ? L"ư" : L"ơ";
            return;
        }
        if (options.standaloneW && lower == L'w') {
            word += matchCase(L'ư', key);
            return;
        }
    }
    word += key;
}

std::wstring Composer::render(const Preferences& options) const {
    if (!tone) return word;
    const int index = toneIndex(word, options.modernTone);
    if (index < 0) return word;
    std::wstring result = word;
    result.insert(index + 1, 1, tones[tone - 1]);
    return normalized(result, NormalizationC);
}

std::wstring Composer::rendered(const Preferences& options) const {
    return encode(render(options), options.encoding);
}

std::wstring Composer::finish(const Preferences& options, bool enabled) const {
    if (full) return {};
    if (options.macrosEnabled && (enabled || options.macrosInEnglish)) {
        auto match = options.macros.find(raw);
        if (match == options.macros.end()) match = options.macros.find(render(options));
        if (match != options.macros.end()) return enabled ? encode(match->second, options.encoding) : match->second;
    }
    if (enabled && canRestore && options.restore && options.spellCheck && !literalRemainder && !isSyllable(word, tone))
        return raw;
    return enabled ? rendered(options) : word;
}

bool Composer::shape(wchar_t key, const Preferences& options) {
    const bool telex = options.method == 0;
    std::wstring_view shapes;
    if (telex) {
        switch (lowerCase(key)) {
            case L'a': shapes = L"aâ"; break;
            case L'e': shapes = L"eê"; break;
            case L'o': shapes = L"oô"; break;
            case L'd': shapes = L"dđ"; break;
            case L'w': shapes = L"aăuưoơ"; break;
        }
    } else switch (key) {
        case L'6': shapes = L"aâeêoô"; break;
        case L'7': shapes = L"uưoơ"; break;
        case L'8': shapes = L"aă"; break;
        case L'9': shapes = L"dđ"; break;
    }
    if (shapes.empty() || word.empty()) return false;
    const auto lower = lowerCase(word);
    const int last = static_cast<int>(word.size()) - 1;
    for (int i = last; i >= (options.freeTyping ? 0 : last); --i) {
        const wchar_t current = lower[i];
        const int pair = indexOf(shapes, current);
        if (pair < 0 || (current == L'u' && i == 1 && lower[0] == L'q' && lower.size() > 2)) continue;
        const wchar_t replacement = matchCase(shapes[pair ^ 1], word[i]);
        std::wstring shaped = word;
        shaped[i] = replacement;
        const bool pairWithU = i > 0 && lower[i - 1] == L'u' && !(i == 2 && lower[0] == L'q');
        if (pair & 1) {
            if (current == L'ơ' && pairWithU) {
                shaped = word;
                shaped[i - 1] = matchCase(L'ư', word[i - 1]);
            } else {
                if (current == L'ơ' && i > 0 && lower[i - 1] == L'ư') shaped[i - 1] = matchCase(L'u', word[i - 1]);
                escape(key, std::move(shaped));
                return true;
            }
        } else if (telex && (replacement == L'ơ' || replacement == L'Ơ') && pairWithU) {
            shaped[i - 1] = matchCase(L'ư', word[i - 1]);
        }
        word = std::move(shaped);
        return true;
    }
    return false;
}

void Composer::escape(wchar_t key, std::wstring reverted) {
    word = std::move(reverted);
    word += key;
    literalRemainder = true;
    if (indexOf(L"sfrxj12345", lowerCase(key)) >= 0) tone = 0;
}

}

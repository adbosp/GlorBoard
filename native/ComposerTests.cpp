#include "Composer.hpp"

#include <cstdio>
#include <utility>

using namespace glorboard;

int main() {
    Composer composer;
    Preferences options;
    int passed = 0, failed = 0;
    const auto check = [&](const wchar_t* keys, const wchar_t* expected, bool enabled = true) {
        composer.reset();
        for (const wchar_t* key = keys; *key; ++key) composer.add(*key, options, enabled);
        if (composer.finish(options, enabled) == expected) ++passed;
        else { ++failed; std::fwprintf(stderr, L"FAIL: %ls\n", keys); }
    };
    const std::pair<const wchar_t*, const wchar_t*> telex[] = {
        {L"tieengs", L"tiếng"}, {L"Vieetj", L"Việt"}, {L"dduwowngf", L"đường"}, {L"nuowcs", L"nước"},
        {L"quoocs", L"quốc"}, {L"giaif", L"giài"}, {L"uwowu", L"ươu"}, {L"Toanfs", L"Toán"},
        {L"toansz", L"toan"}, {L"toanss", L"toans"}, {L"awrq", L"awrq"}, {L"hoas", L"hóa"},
        {L"hoafng", L"hoàng"}, {L"thuyr", L"thủy"}, {L"NGUYEENX", L"NGUYỄN"}, {L"[", L"ư"},
        {L"]", L"ơ"}, {L"w", L"ư"}, {L"www", L"w"}, {L"WWW", L"W"}, {L"Www", L"W"},
        {L"wwww", L"ww"}, {L"bwww", L"bwww"}, {L"khuaats", L"khuất"}, {L"tree", L"trê"}, {L"goox", L"gỗ"},
        {L"hai", L"hai"}, {L"gif", L"gì"}, {L"khuyur", L"khuỷu"},
    };
    for (const auto& [keys, expected] : telex) check(keys, expected);
    check(L"WWW", L"WWW", false);
    options.standaloneW = false;
    check(L"www", L"w");
    options.standaloneW = true;
    options.modernTone = true;
    check(L"hoas", L"hoá");
    check(L"quys", L"quý");
    options.modernTone = false;
    options.method = 1;
    check(L"WWW", L"WWW");
    const std::pair<const wchar_t*, const wchar_t*> vni[] = {
        {L"tie6ng1", L"tiếng"}, {L"Vie6t5", L"Việt"}, {L"d9u7o7ng2", L"đường"}, {L"duong9772", L"đường"},
        {L"cha8m", L"chăm"}, {L"toan21", L"toán"}, {L"toan10", L"toan"}, {L"HELLO123", L"HELLO123"},
        {L"123456", L"123456"}, {L"thuy3", L"thủy"},
    };
    for (const auto& [keys, expected] : vni) check(keys, expected);
    options.encoding = 1;
    check(L"tie6ng1", L"tie\u0302\u0301ng");
    options.encoding = 2;
    check(L"Vie6t5", L"Vie^.t");
    options.encoding = 0;
    options.macrosEnabled = true;
    options.macros[L"dc"] = L"địa chỉ";
    check(L"dc", L"địa chỉ");
    check(L"dc", L"dc", false);
    options.macrosInEnglish = true;
    check(L"DC", L"địa chỉ", false);
    if (combiningMark(L'ê') || !combiningMark(L'\u0302')) {
        ++failed;
        std::fputs("FAIL: precomposed and combining marks must be counted separately\n", stderr);
    } else ++passed;
    for (int method = 0; method < 2; ++method) {
        options.method = method;
        const std::wstring_view keys = method == 0 ? L"tieengs Vieetj WWW " : L"tie6ng1 Vie6t5 WWW ";
        std::wstring document, displayed;
        composer.reset();
        for (wchar_t key : keys) {
            const bool boundary = key == L' ';
            if (!boundary) composer.add(key, options, true);
            const auto next = boundary ? composer.finish(options, true) : composer.rendered(options);
            if (next == displayed + key) document += key;
            else {
                const auto [prefix, backspaces] = textEdit(displayed, next, options.encoding);
                document.resize(document.size() - backspaces);
                document += next.substr(prefix);
                if (boundary) document += key;
            }
            displayed = boundary ? std::wstring{} : next;
            if (boundary) composer.reset();
        }
        if (document == (method == 0 ? L"tiếng Việt W " : L"tiếng Việt WWW ")) ++passed;
        else { ++failed; std::fwprintf(stderr, L"FAIL keyboard replacement: %ls\n", document.c_str()); }
    }
    std::printf("Composer: %d passed, %d failed\n", passed, failed);
    return failed != 0;
}

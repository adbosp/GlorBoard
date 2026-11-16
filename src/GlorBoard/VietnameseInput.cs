using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GlorBoard;

internal sealed class VietnameseInput
{
    private const string Tones = "\u0301\u0300\u0309\u0303\u0323";
    private const string PlainVowels = "aăâeêioôơuưy";
    private const string Initials = "||b|c|ch|d|đ|g|gh|gi|h|k|kh|l|m|n|ng|ngh|nh|p|ph|q|qu|r|s|t|th|tr|v|x|";
    private const string Finals = "||c|ch|m|n|ng|nh|p|t|";
    private const string Nuclei = "|a|ă|â|e|ê|i|o|ô|ơ|u|ư|y|ai|ao|au|ay|âu|ây|eo|êu|ia|iê|iu|oa|oă|oe|oi|ôi|ơi|oo|ua|uâ|uê|ui|uo|uô|uơ|uy|ưa|ưi|ươ|ưu|yê|iêu|oai|oao|oay|oeo|uây|uôi|uya|uyê|uyu|ươi|ươu|yêu|";
    private const int MaxWordLength = 96;

    private string raw = "";
    private string word = "";
    private int tone;
    private bool canRestore = true;
    private bool literalRemainder;
    private bool full;

    internal void Reset()
    {
        raw = word = "";
        tone = 0;
        canRestore = true;
        literalRemainder = full = false;
    }

    internal string Rendered(Preferences options) => Encode(Render(options), options.Encoding);

    internal bool Backspace(Preferences options)
    {
        if (full || word.Length == 0 || options.Encoding == CharacterSet.Viqr && word != raw)
        {
            Reset();
            return false;
        }

        // The active control handles deletion. Unicode controls vary in how they delete combining marks.
        if (options.Encoding == CharacterSet.Decomposed)
        {
            Reset();
            return false;
        }

        int accent = ToneIndex(word, options.ModernTone);
        if (accent == word.Length - 1) tone = 0;
        word = word[..^1];
        if (raw.Length > 0) raw = raw[..^1];
        canRestore = false;
        if (word.Length == 0) Reset();
        return true;
    }

    internal void Add(char key, Preferences options, bool enabled)
    {
        if (full) return;
        if (raw.Length >= MaxWordLength)
        {
            Reset();
            full = true;
            return;
        }

        raw += key;
        bool telex = options.Method == InputMethod.Telex;
        char lower = char.ToLowerInvariant(key);
        // A third W completes the literal sequence even after the second W escaped ư.
        if (enabled && telex && raw.Length == 3 && lower == 'w' &&
            char.ToLowerInvariant(raw[0]) == 'w' && char.ToLowerInvariant(raw[1]) == 'w')
        {
            word = raw[..1];
            canRestore = false;
            literalRemainder = true;
            return;
        }
        if (!enabled || literalRemainder)
        {
            word += key;
            return;
        }

        int requestedTone = telex ? "sfrxj".IndexOf(lower) + 1 : "12345".IndexOf(lower) + 1;

        if (requestedTone > 0 && ToneIndex(word, options.ModernTone) >= 0 &&
            (!options.SpellCheck || IsSyllable(word, 0) || options.FreeTyping && PotentialSyllable(word)))
        {
            if (requestedTone == tone) Escape(key, word);
            else tone = requestedTone;
            return;
        }

        if ((telex ? lower == 'z' : key == '0') && tone != 0)
        {
            tone = 0;
            return;
        }

        if (TryShape(key, options)) return;
        if (telex && options.BracketKeys && key is '[' or ']' && word.Length == 0)
        {
            word = key == '[' ? "ư" : "ơ";
            return;
        }
        if (telex && options.StandaloneW && lower == 'w' && word.Length == 0)
        {
            word = MatchCase('ư', key).ToString();
            return;
        }

        word += key;
    }

    internal string Finish(Preferences options, bool enabled)
    {
        if (full) return "";
        if (options.MacrosEnabled && (enabled || options.MacrosInEnglish))
        {
            string typed = Render(options);
            if (options.Macros.TryGetValue(raw, out string? expansion) ||
                options.Macros.TryGetValue(typed, out expansion))
                return enabled ? Encode(expansion, options.Encoding) : expansion;
        }

        if (enabled && canRestore && options.Restore && options.SpellCheck && !literalRemainder &&
            !IsSyllable(word, tone))
            return raw;

        return enabled ? Rendered(options) : word;
    }

    private bool TryShape(char key, Preferences options)
    {
        if (word.Length == 0) return false;
        char letter = char.ToLowerInvariant(key);
        bool telex = options.Method == InputMethod.Telex;
        string shapes = telex ? letter switch
        {
            'a' => "aâ", 'e' => "eê", 'o' => "oô", 'd' => "dđ", 'w' => "aăuưoơ", _ => "",
        } : key switch
        {
            '6' => "aâeêoô", '7' => "uưoơ", '8' => "aă", '9' => "dđ", _ => "",
        };
        if (shapes.Length == 0) return false;

        string lowerWord = word.ToLowerInvariant();
        int start = options.FreeTyping ? 0 : word.Length - 1;
        for (int i = word.Length - 1; i >= start; i--)
        {
            char current = lowerWord[i];
            int shape = shapes.IndexOf(current);
            if (shape < 0) continue;
            if (current == 'u' && i == 1 && lowerWord[0] == 'q' && lowerWord.Length > 2) continue;

            char replacement = MatchCase(shapes[shape ^ 1], word[i]);
            string shaped = word[..i] + replacement + word[(i + 1)..];
            if ((shape & 1) == 1)
            {
                // A second horn completes the pair when only one vowel was shaped.
                if (current == 'ơ' && i > 0 && lowerWord[i - 1] == 'u' && !(i == 2 && lowerWord[0] == 'q'))
                    shaped = ReplaceAt(word, i - 1, 'ư');
                else
                {
                    if (current == 'ơ' && i > 0 && lowerWord[i - 1] == 'ư')
                        shaped = ReplaceAt(shaped, i - 1, 'u');
                    Escape(key, shaped);
                    return true;
                }
            }
            else if (replacement is 'ơ' or 'Ơ' && i > 0 && lowerWord[i - 1] == 'u' &&
                     !(i == 2 && lowerWord[0] == 'q') && telex)
                shaped = ReplaceAt(shaped, i - 1, 'ư');

            word = shaped;
            return true;
        }
        return false;
    }

    private void Escape(char key, string reverted)
    {
        word = reverted + key;
        literalRemainder = true;
        if ("sfrxj12345".Contains(char.ToLowerInvariant(key))) tone = 0;
    }

    private static string ReplaceAt(string value, int index, char replacement) =>
        value[..index] + MatchCase(replacement, value[index]) + value[(index + 1)..];

    private static char MatchCase(char value, char model) =>
        char.IsUpper(model) ? char.ToUpperInvariant(value) : value;

    private string Render(Preferences options)
    {
        if (tone == 0) return word;
        int index = ToneIndex(word, options.ModernTone);
        return index < 0 ? word : word.Insert(index + 1, Tones[tone - 1].ToString()).Normalize();
    }

    private static int FindVowels(string lower, out int end)
    {
        int start = lower.Length;
        end = lower.Length;
        for (int i = 0; i < lower.Length; i++)
        {
            if (!PlainVowels.Contains(lower[i])) continue;
            if (i == 1 && lower.Length > 2 && PlainVowels.Contains(lower[2]) &&
                (lower.StartsWith("qu", StringComparison.Ordinal) && lower[i] == 'u' ||
                 lower.StartsWith("gi", StringComparison.Ordinal) && lower[i] == 'i'))
                continue;
            start = i;
            end = i + 1;
            while (end < lower.Length && PlainVowels.Contains(lower[end])) end++;
            break;
        }
        return start;
    }

    private static int ToneIndex(string value, bool modern)
    {
        string lower = value.ToLowerInvariant();
        int start = FindVowels(lower, out int end);
        if (start == end) return -1;
        for (int i = end - 1; i >= start; i--)
            if ("êơ".Contains(lower[i])) return i;
        for (int i = end - 1; i >= start; i--)
            if ("ăâô".Contains(lower[i])) return i;
        if (end - start == 3) return start + 1;
        if (end - start == 2)
        {
            if (end < lower.Length || (modern && lower[start..end] is "oa" or "oe" or "uy")) return start + 1;
        }
        return start;
    }

    private static bool PotentialSyllable(string value)
    {
        string lower = value.ToLowerInvariant();
        int start = FindVowels(lower, out int end);
        if (start == end || !InTable(Initials, lower[..start]) || !InTable(Nuclei, lower[start..end]))
            return false;
        string ending = lower[end..];
        return InTable(Finals, ending) || ending == "h";
    }

    private static bool IsSyllable(string value, int usedTone)
    {
        if (!PotentialSyllable(value)) return false;
        string lower = value.ToLowerInvariant();
        int start = FindVowels(lower, out int end);
        string ending = lower[end..];
        if (!InTable(Finals, ending) || lower[..start] == "q") return false;
        return ending is not ("c" or "ch" or "p" or "t") || usedTone is 0 or 1 or 5;
    }

    private static bool InTable(string table, string value) =>
        table.Contains("|" + value + "|", StringComparison.Ordinal);

    internal static string Encode(string input, CharacterSet encoding)
    {
        if (encoding == CharacterSet.Unicode) return input;
        if (encoding == CharacterSet.Decomposed) return input.Normalize(NormalizationForm.FormD);
        StringBuilder result = new();
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(input);
        while (elements.MoveNext())
        {
            string marks = elements.GetTextElement().Normalize(NormalizationForm.FormD);
            char toneMark = '\0';
            foreach (char c in marks)
            {
                int accent = Tones.IndexOf(c);
                if (accent >= 0) toneMark = "'`?~."[accent];
                else result.Append(c switch
                {
                    '\u0302' => "^", '\u0306' => "(", '\u031B' => "+",
                    'đ' => "dd", 'Đ' => "DD", _ => c.ToString(),
                });
            }
            if (toneMark != '\0') result.Append(toneMark);
        }
        return result.ToString();
    }
}

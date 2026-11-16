using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GlorBoard;

internal enum InputMethod { Telex, Vni }
internal enum CharacterSet { Unicode, Decomposed, Viqr }
internal enum Shortcut { CtrlShift, AltZ }
internal enum UiTheme { Dark, Light, System }

internal sealed class Preferences
{
    internal static readonly string Folder = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GlorBoard");

    public InputMethod Method { get; set; }
    public CharacterSet Encoding { get; set; }
    public Shortcut Hotkey { get; set; }
    public bool Enabled { get; set; } = true;
    public bool FreeTyping { get; set; } = true;
    public bool ModernTone { get; set; }
    public bool Clipboard { get; set; }
    public bool SpellCheck { get; set; } = true;
    public bool Restore { get; set; } = true;
    public bool Notifications { get; set; }
    public bool Sound { get; set; } = true;
    public bool MacrosEnabled { get; set; }
    public bool MacrosInEnglish { get; set; }
    public bool ShowOnStart { get; set; } = true;
    public bool StartWithWindows { get; set; }
    public bool PerApp { get; set; }
    public bool Vietnamese { get; set; } = true;
    public bool StandaloneW { get; set; } = true;
    public bool BracketKeys { get; set; } = true;
    public UiTheme Theme { get; set; }
    public Dictionary<string, string> Macros { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    public Dictionary<string, bool> AppModes { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    private static string FilePath => Path.Combine(Folder, "settings.json");
    internal static Preferences Load()
    {
        try
        {
            var settings = JsonSerializer.Deserialize<Preferences>(File.ReadAllText(FilePath)) ?? new();
            settings.Macros = new(settings.Macros ?? new(), StringComparer.OrdinalIgnoreCase);
            settings.AppModes = new(settings.AppModes ?? new(), StringComparer.OrdinalIgnoreCase);
            if (!Enum.IsDefined(settings.Method)) settings.Method = InputMethod.Telex;
            if (!Enum.IsDefined(settings.Encoding)) settings.Encoding = CharacterSet.Unicode;
            if (!Enum.IsDefined(settings.Hotkey)) settings.Hotkey = Shortcut.CtrlShift;
            if (!Enum.IsDefined(settings.Theme)) settings.Theme = UiTheme.Dark;
            return settings;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or ArgumentException)
        {
            return new();
        }
    }

    internal void Save()
    {
        Directory.CreateDirectory(Folder);
        string temporary = FilePath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, FilePath, true);
    }
}

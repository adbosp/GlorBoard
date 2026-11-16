using System;
using System.Security;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace GlorBoard;

internal sealed class GlassTheme
{
    private const string Personalization = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";
    private (bool Light, bool HighContrast, bool Transparent)? current;

    internal static bool TransparencyAllowed => !SystemParameters.HighContrast && ReadPersonalization("EnableTransparency", 1) != 0;

    internal void Apply(ResourceDictionary resources, UiTheme selection)
    {
        bool light = selection == UiTheme.Light ||
            selection == UiTheme.System && ReadPersonalization("AppsUseLightTheme", 1) != 0;
        bool highContrast = SystemParameters.HighContrast;
        bool transparent = TransparencyAllowed;
        if (!highContrast && current == (light, highContrast, transparent)) return;
        current = (light, highContrast, transparent);

        if (highContrast)
        {
            resources["Text"] = resources["SecondaryText"] = resources["MenuText"] = SystemColors.WindowTextBrush;
            resources["GlassStandbyFill"] = resources["GlassReflection"] = resources["GlassDockFill"] =
                resources["ControlFill"] = resources["MenuFill"] = resources["FieldFill"] = resources["AppearanceFill"] = resources["InactiveTrack"] =
                resources["AlternateRow"] = SystemColors.WindowBrush;
            resources["GlassRim"] = resources["GlassLine"] = resources["Separator"] =
                resources["AppearanceBorder"] = SystemColors.WindowTextBrush;
            resources["ActionFill"] = resources["ActiveTrack"] = resources["SelectionFill"] = SystemColors.HighlightBrush;
            resources["ActionText"] = resources["ThumbFill"] = resources["SelectionText"] = SystemColors.HighlightTextBrush;
            resources["FocusLine"] = resources["Success"] = resources["Attention"] = SystemColors.WindowTextBrush;
            resources["HoverWash"] = Brushes.Transparent;
            return;
        }

        if (light)
        {
            SetColors(resources,
                ("Text", 0xFF172B35), ("SecondaryText", 0xFF354D58),
                ("GlassStandbyFill", transparent ? 0xEAD6E8F8u : 0xFFF0F5FAu),
                ("GlassDockFill", 0xD8EEF6FF), ("ControlFill", 0x4985BFFF), ("MenuFill", 0xFBF2F7FF),
                ("AppearanceFill", 0xF2E9F3FC), ("AppearanceBorder", 0xAFBAD1E3),
                ("FieldFill", 0xEAF7FBFC), ("AlternateRow", 0xE5E9F2F6), ("Separator", 0x457A95A2),
                ("ActionFill", 0xD890CEFF), ("ActionText", 0xFF102D4A), ("ActiveTrack", 0xDB347EC8),
                ("InactiveTrack", 0x594E92DE), ("ThumbFill", 0xFFFFFFFF), ("SelectionFill", 0xBFA7D2FF), ("SelectionText", 0xFF12324B),
                ("FocusLine", 0xFF255EA0), ("HoverWash", 0x6579AEED), ("Success", 0xFF266BB1), ("Attention", 0xFFAA2C46));
            resources["GlassReflection"] = Gradient(0xEBFAFEFF, 0xD3EDF4FA, 0xE3EAF3FD);
            resources["ClearCardFill"] = Gradient(0x58FFFFFF, 0x24EFF6FF, 0x48DDECFD);
            resources["ClearDockFill"] = Gradient(0x50FFFFFF, 0x27E7F2FD, 0x40EAF4FF);
            resources["GlassLine"] = Gradient(0xF8FFFFFF, 0x8C9EB2BC, 0xDBFFFFFF);
            resources["GlassRim"] = Gradient(0xFFFFFFFF, 0xB2A4C2D7, 0xF9FFFFFF);
        }
        else
        {
            SetColors(resources,
                ("Text", 0xFFF7FCFD), ("SecondaryText", 0xFFDDEBEF),
                ("GlassStandbyFill", transparent ? 0xEA172D49u : 0xFF192C44u),
                ("GlassDockFill", 0xD5223249), ("ControlFill", 0x447CAFFF), ("MenuFill", 0xF222334C),
                ("AppearanceFill", 0xEE344F66), ("AppearanceBorder", 0x879FBAD0),
                ("FieldFill", 0xF21C2B36), ("AlternateRow", 0xF2233540), ("Separator", 0x64B4D4DE),
                ("ActionFill", 0xDCA1D4FF), ("ActionText", 0xFF122D4D), ("ActiveTrack", 0xDF387FC9),
                ("InactiveTrack", 0x5B528ACF), ("ThumbFill", 0xFFF5FAFF), ("SelectionFill", 0xBD366BA9), ("SelectionText", 0xFFF5FAFF),
                ("FocusLine", 0xFF9FCFFF), ("HoverWash", 0x609CCBFF), ("Success", 0xFF99C9FF), ("Attention", 0xFFFFB4C3));
            resources["GlassReflection"] = Gradient(0xD22E405A, 0xCB19263B, 0xD129405C);
            resources["ClearCardFill"] = Gradient(0x492E415B, 0x2415253A, 0x42294260);
            resources["ClearDockFill"] = Gradient(0x482B425C, 0x241B2D44, 0x41233855);
            resources["GlassLine"] = Gradient(0xBCDBEEFF, 0x51B3CBED, 0xA9CBDFFF);
            resources["GlassRim"] = Gradient(0xFFFFFFFF, 0x7297B4D7, 0xE1CBDDFF);
        }
        resources["MenuText"] = resources["Text"];
    }

    private static void SetColors(ResourceDictionary resources, params (string Key, uint Color)[] colors)
    {
        foreach (var (key, color) in colors)
        {
            var brush = new SolidColorBrush(ColorFromArgb(color));
            brush.Freeze();
            resources[key] = brush;
        }
    }

    private static LinearGradientBrush Gradient(uint start, uint middle, uint end)
    {
        var brush = new LinearGradientBrush
        {
            StartPoint = new Point(0, 0),
            EndPoint = new Point(1, 1),
            GradientStops =
            [
                new(ColorFromArgb(start), 0),
                new(ColorFromArgb(middle), .53),
                new(ColorFromArgb(end), 1),
            ],
        };
        brush.Freeze();
        return brush;
    }

    private static Color ColorFromArgb(uint color) =>
        Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color);

    private static int ReadPersonalization(string key, int fallback)
    {
        try { return Registry.GetValue($@"HKEY_CURRENT_USER\{Personalization}", key, fallback) is int value ? value : fallback; }
        catch (Exception error) when (error is UnauthorizedAccessException or SecurityException or System.IO.IOException) { return fallback; }
    }
}

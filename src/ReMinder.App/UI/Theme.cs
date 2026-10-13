using Microsoft.Win32;

namespace ReMinder.App.UI;

public static class Theme
{
    public static bool Dark { get; private set; }
    public static bool Reduced { get; private set; }
    private static string signature = "";
    public static void Apply(AppSettings settings)
    {
        var light = Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize", "AppsUseLightTheme", 1);
        Dark = settings.ThemeMode == "dark" || settings.ThemeMode == "system" && Convert.ToInt32(light) == 0;
        var reduced = settings.ReduceMotion || !SystemParameters.ClientAreaAnimation || SystemParameters.HighContrast;
        if (Reduced != reduced) { Reduced = reduced; Motion.UpdatePreference(); }
        var textScale = Math.Clamp(Convert.ToDouble(Registry.GetValue(@"HKEY_CURRENT_USER\Software\Microsoft\Accessibility", "TextScaleFactor", 100)) / 100, 1, 2.25);
        var nextSignature = $"{Dark}|{settings.PaletteId}|{SystemParameters.HighContrast}|{textScale}";
        if (signature == nextSignature) return;
        signature = nextSignature;
        var r = Application.Current.Resources;
        var palette = settings.PaletteId;
        string[] primary = palette switch { "blue" => ["#415F91", "#AAC7FF", "#FFFFFF", "#0A305F", "#D6E3FF", "#284777", "#001B3E", "#D6E3FF"], "teal" => ["#006A60", "#80D5C7", "#FFFFFF", "#003730", "#9EF2E3", "#005048", "#00201C", "#9EF2E3"], "rose" => ["#8E4957", "#FFB1C0", "#FFFFFF", "#561D2B", "#FFD9E0", "#723341", "#3B0716", "#FFD9E0"], _ => ["#6750A4", "#D0BCFF", "#FFFFFF", "#381E72", "#EADDFF", "#4F378B", "#21005D", "#EADDFF"] };
        string[] surfaces = palette switch { "blue" => ["#F3F6FF", "#191C22", "#EDF0F8", "#1D2026", "#E7E8F0", "#282A31"], "teal" => ["#F0F7F3", "#171D1B", "#EAF1ED", "#1B211F", "#E4EBE7", "#262C2A"], "rose" => ["#FFF1F3", "#22191C", "#F9EBEE", "#261D20", "#F3E5E8", "#31282B"], _ => ["#F7F2FA", "#1D1B20", "#F3EDF7", "#211F26", "#ECE6F0", "#2B2930"] };
        var shift = Dark ? 1 : 0;
        Set("Primary", primary[shift]); Set("OnPrimary", primary[2 + shift]); Set("PrimaryContainer", primary[4 + shift]); Set("OnPrimaryContainer", primary[6 + shift]);
        Set("Surface", surfaces[shift]); Set("Card", surfaces[2 + shift]); Set("Raised", surfaces[4 + shift]);
        Set("OnSurface", Dark ? "#E6E0E9" : "#1D1B20"); Set("Muted", Dark ? "#CAC4D0" : "#49454F");
        Set("Outline", Dark ? "#938F99" : "#79747E"); Set("Line", Dark ? "#49454F" : "#CAC4D0");
        Set("ErrorContainer", Dark ? "#8C1D18" : "#F9DEDC"); Set("OnErrorContainer", Dark ? "#F9DEDC" : "#410E0B");
        Set("Error", Dark ? "#FFB4AB" : "#BA1A1A"); Set("WarningContainer", Dark ? "#4A3900" : "#FFF0C2"); Set("OnWarningContainer", Dark ? "#FFE3A0" : "#5D4300");
        Set("Inverse", Dark ? "#E6E0E9" : "#322F35"); Set("OnInverse", Dark ? "#322F35" : "#F5EFF7");
        if (SystemParameters.HighContrast)
        {
            foreach (var key in new[] { "Surface", "Card", "Raised", "PrimaryContainer", "ErrorContainer", "WarningContainer" }) r[key] = SystemColors.WindowBrush;
            foreach (var key in new[] { "OnSurface", "Muted", "Outline", "Line", "OnPrimaryContainer", "Error", "OnErrorContainer", "OnWarningContainer" }) r[key] = SystemColors.WindowTextBrush;
            r["Primary"] = SystemColors.HighlightBrush; r["OnPrimary"] = SystemColors.HighlightTextBrush;
            r["Inverse"] = SystemColors.WindowTextBrush; r["OnInverse"] = SystemColors.WindowBrush;
        }
        r["SmallSize"] = 12 * textScale; r["BodySize"] = 14 * textScale; r["TitleSize"] = 16 * textScale; r["HeadingSize"] = 22 * textScale; r["CountdownSize"] = 24 * textScale;
        void Set(string key, string color) { var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color)); brush.Freeze(); r[key] = brush; }
    }
}

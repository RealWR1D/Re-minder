using Microsoft.Win32;

namespace ReMinder.App.Services;

internal static class AutoStartService
{
    private const string Key = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string Name = "ReMinder";
    private static string Command => $"\"{Environment.ProcessPath}\" --silent";
    public static bool Enabled { get { using var key = Registry.CurrentUser.OpenSubKey(Key); return string.Equals(key?.GetValue(Name) as string, Command, StringComparison.OrdinalIgnoreCase); } }
    public static void Set(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(Key, true);
        if (enabled) key.SetValue(Name, Command); else key.DeleteValue(Name, false);
        if (Enabled != enabled) throw new IOException("Windows 未能更新自启动，请检查用户权限后重试。");
    }
}

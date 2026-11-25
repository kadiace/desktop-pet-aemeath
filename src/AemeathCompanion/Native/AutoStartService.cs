using System.IO;
using Microsoft.Win32;

namespace AemeathCompanion.Native;

/// <summary>
/// Manages Windows per-user login auto-start via HKCU\Software\Microsoft\Windows\CurrentVersion\Run.
/// Requires no administrator privileges.
/// </summary>
public static class AutoStartService
{
    public const string RunRegistryPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "AemeathCompanion";

    public static string FormatRunCommand(string exePath)
    {
        string trimmed = (exePath ?? string.Empty).Trim().Trim('"');
        return string.IsNullOrEmpty(trimmed) ? string.Empty : $"\"{trimmed}\"";
    }

    public static string NormalizeRegisteredPath(string? rawValue)
    {
        if (string.IsNullOrWhiteSpace(rawValue)) return string.Empty;
        string trimmed = rawValue.Trim();
        if (trimmed.StartsWith('"'))
        {
            int endQuote = trimmed.IndexOf('"', 1);
            if (endQuote > 1) return trimmed[1..endQuote];
        }
        return trimmed.Trim('"');
    }

    public static bool IsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: false);
            string? value = key?.GetValue(ValueName) as string;
            string registeredPath = NormalizeRegisteredPath(value);
            return !string.IsNullOrWhiteSpace(registeredPath) && File.Exists(registeredPath);
        }
        catch
        {
            return false;
        }
    }

    public static bool SetEnabled(bool enabled)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true)
                            ?? Registry.CurrentUser.CreateSubKey(RunRegistryPath, writable: true);
            if (key is null) return false;

            if (enabled)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
                    return false;
                key.SetValue(ValueName, FormatRunCommand(exePath), RegistryValueKind.String);
                return true;
            }

            key.DeleteValue(ValueName, throwOnMissingValue: false);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// If auto-start is already enabled, updates the registered path to the current executable
    /// so moving/updating the app never leaves a stale startup path behind.
    /// </summary>
    public static void RefreshPathIfEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, writable: true);
            if (key is null) return;
            string? existing = key.GetValue(ValueName) as string;
            if (string.IsNullOrWhiteSpace(existing)) return;

            string? currentExe = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(currentExe) || !File.Exists(currentExe)) return;

            string desired = FormatRunCommand(currentExe);
            if (!string.Equals(existing.Trim(), desired, StringComparison.OrdinalIgnoreCase))
            {
                key.SetValue(ValueName, desired, RegistryValueKind.String);
            }
        }
        catch { }
    }
}

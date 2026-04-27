using System;
using System.IO;
using Microsoft.Win32;

/// <summary>
/// Utility.
/// </summary>
public class Util
{
    private const string RUN_LOCATION = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Sets the autostart value for the application command.
    /// </summary>
    /// <param name="keyName">Registry Key Name</param>
    /// <param name="startupCommand">Startup command (for example, a quoted executable path)</param>
    public static void SetAutoStart(string keyName, string startupCommand)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentException.ThrowIfNullOrEmpty(startupCommand);

        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RUN_LOCATION);
        if (key == null)
        {
            throw new InvalidOperationException("Failed to open the registry Run key for writing.");
        }

        key.SetValue(keyName, startupCommand);
    }

    /// <summary>
    /// Returns whether auto start is enabled.
    /// </summary>
    /// <param name="keyName">Registry Key Name</param>
    /// <param name="startupCommand">Startup command (for example, a quoted executable path)</param>
    public static bool IsAutoStartEnabled(string keyName, string startupCommand)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentException.ThrowIfNullOrEmpty(startupCommand);

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_LOCATION, writable: false);
        if (key == null)
        {
            return false;
        }

        var value = key.GetValue(keyName) as string;
        if (string.Equals(value, startupCommand, StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var actualPath = ExtractCommandPath(value);
        var expectedPath = ExtractCommandPath(startupCommand);
        return !string.IsNullOrEmpty(actualPath)
            && !string.IsNullOrEmpty(expectedPath)
            && string.Equals(actualPath, expectedPath, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Unsets the autostart value for the assembly.
    /// </summary>
    /// <param name="keyName">Registry Key Name</param>
    public static void UnSetAutoStart(string keyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_LOCATION, writable: true);
        if (key == null)
        {
            return;
        }

        key.DeleteValue(keyName, throwOnMissingValue: false);
    }

    public static bool MigrateLegacyDllAutoStart(string keyName, string startupCommand)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentException.ThrowIfNullOrEmpty(startupCommand);

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_LOCATION, writable: true);
        if (key == null)
        {
            return false;
        }

        var currentValue = key.GetValue(keyName) as string;
        var currentPath = ExtractCommandPath(currentValue);
        var expectedPath = ExtractCommandPath(startupCommand);
        if (string.IsNullOrWhiteSpace(currentPath) || string.IsNullOrWhiteSpace(expectedPath))
        {
            return false;
        }

        var currentFile = Path.GetFileName(currentPath);
        var expectedFile = Path.GetFileName(expectedPath);
        if (!string.Equals(currentFile, "screenzap.dll", StringComparison.OrdinalIgnoreCase)
            || !string.Equals(expectedFile, "screenzap.exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        key.SetValue(keyName, startupCommand);
        return true;
    }

    private static string? ExtractCommandPath(string? command)
    {
        if (string.IsNullOrWhiteSpace(command))
        {
            return null;
        }

        var trimmed = command.Trim();
        if (trimmed.StartsWith("\"", StringComparison.Ordinal))
        {
            var endQuote = trimmed.IndexOf('"', 1);
            return endQuote > 1 ? trimmed.Substring(1, endQuote - 1) : null;
        }

        var firstSpace = trimmed.IndexOf(' ');
        return firstSpace > 0 ? trimmed.Substring(0, firstSpace) : trimmed;
    }
}

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
    /// <remarks>
    /// Deliberately keyed on PRESENCE of the Run value, not on whether it points at the running
    /// copy. Comparing against the current process path made the toggle read "off" whenever a
    /// second copy was launched (a stale shortcut into bin\Release, say), and clicking it then
    /// repointed autostart at that copy -- or cleared it outright. Presence is the only thing
    /// that actually answers "does Screenzap start when I log in?". Correcting a stale target is
    /// <see cref="RepairAutoStartTarget"/>'s job.
    /// </remarks>
    /// <param name="keyName">Registry Key Name</param>
    public static bool IsAutoStartEnabled(string keyName)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_LOCATION, writable: false);
        if (key == null)
        {
            return false;
        }

        return !string.IsNullOrWhiteSpace(key.GetValue(keyName) as string);
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

    /// <summary>
    /// Repoints an existing autostart entry at the running executable when the recorded target can
    /// no longer launch -- it is not an .exe (the historical case: Assembly.Location yields
    /// screenzap.dll under .NET, which Windows silently fails to run at login), or it no longer
    /// exists on disk (a build output that was cleaned, or an install that moved).
    /// </summary>
    /// <remarks>
    /// Only ever REPAIRS; never creates. A missing value means the user turned autostart off and
    /// must stay off. A target that is a real .exe on disk is left alone even when it is not this
    /// copy, so running a dev build cannot hijack a working install.
    /// </remarks>
    /// <param name="keyName">Registry Key Name</param>
    /// <param name="startupCommand">Startup command (for example, a quoted executable path)</param>
    /// <returns>True when the entry was rewritten.</returns>
    public static bool RepairAutoStartTarget(string keyName, string startupCommand)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentException.ThrowIfNullOrEmpty(startupCommand);

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_LOCATION, writable: true);
        if (key == null)
        {
            return false;
        }

        var currentValue = key.GetValue(keyName) as string;
        if (string.IsNullOrWhiteSpace(currentValue))
        {
            return false;
        }

        var currentPath = ExtractCommandPath(currentValue);
        var expectedPath = ExtractCommandPath(startupCommand);
        if (string.IsNullOrWhiteSpace(currentPath) || string.IsNullOrWhiteSpace(expectedPath))
        {
            return false;
        }

        if (CanLaunch(currentPath))
        {
            return false;
        }

        key.SetValue(keyName, startupCommand);
        return true;
    }

    private static bool CanLaunch(string path)
    {
        if (!string.Equals(Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        try
        {
            return File.Exists(path);
        }
        catch (Exception)
        {
            // An unreadable or malformed path is not something we can launch either.
            return false;
        }
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

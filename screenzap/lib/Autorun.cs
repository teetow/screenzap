using System;
using Microsoft.Win32;

/// <summary>
/// Utility.
/// </summary>
public class Util
{
    private const string RUN_LOCATION = @"Software\Microsoft\Windows\CurrentVersion\Run";

    /// <summary>
    /// Sets the autostart value for the assembly.
    /// </summary>
    /// <param name="keyName">Registry Key Name</param>
    /// <param name="assemblyLocation">Assembly location (e.g. Assembly.GetExecutingAssembly().Location)</param>
    public static void SetAutoStart(string keyName, string assemblyLocation)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentException.ThrowIfNullOrEmpty(assemblyLocation);

        using RegistryKey? key = Registry.CurrentUser.CreateSubKey(RUN_LOCATION);
        if (key == null)
        {
            throw new InvalidOperationException("Failed to open the registry Run key for writing.");
        }

        key.SetValue(keyName, assemblyLocation);
    }

    /// <summary>
    /// Returns whether auto start is enabled.
    /// </summary>
    /// <param name="keyName">Registry Key Name</param>
    /// <param name="assemblyLocation">Assembly location (e.g. Assembly.GetExecutingAssembly().Location)</param>
    public static bool IsAutoStartEnabled(string keyName, string assemblyLocation)
    {
        ArgumentException.ThrowIfNullOrEmpty(keyName);
        ArgumentException.ThrowIfNullOrEmpty(assemblyLocation);

        using RegistryKey? key = Registry.CurrentUser.OpenSubKey(RUN_LOCATION, writable: false);
        if (key == null)
        {
            return false;
        }

        var value = key.GetValue(keyName) as string;
        return string.Equals(value, assemblyLocation, StringComparison.Ordinal);
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
}

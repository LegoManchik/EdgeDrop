using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Win32;

namespace EdgeDrop;

public static class ShellContextMenuManager
{
    private const string FileShellKeyPath = @"Software\Classes\*\shell\EdgeDrop";
    private const string DirShellKeyPath = @"Software\Classes\Directory\shell\EdgeDrop";
    private const string CommandsKeyPath = @"Software\Classes\EdgeDrop\shell";
    private const string SubCommandsKeyName = "EdgeDrop";

    public static bool IsIntegrationEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(FileShellKeyPath);
            return key != null;
        }
        catch
        {
            return false;
        }
    }

    public static void SetIntegrationEnabled(bool enable, Settings settings, IReadOnlyList<ActiveSessionInfo>? webSessions = null)
    {
        try
        {
            if (enable)
            {
                UpdateMenu(settings, webSessions);
            }
            else
            {
                Registry.CurrentUser.DeleteSubKeyTree(FileShellKeyPath, throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree(DirShellKeyPath, throwOnMissingSubKey: false);
                Registry.CurrentUser.DeleteSubKeyTree(@"Software\Classes\EdgeDrop", throwOnMissingSubKey: false);
            }
        }
        catch { }
    }

    public static void UpdateMenuIfEnabled(Settings settings, IReadOnlyList<ActiveSessionInfo>? webSessions = null)
    {
        try
        {
            if (IsIntegrationEnabled())
            {
                UpdateMenu(settings, webSessions);
            }
        }
        catch { }
    }

    public static void UpdateMenu(Settings settings, IReadOnlyList<ActiveSessionInfo>? webSessions = null)
    {
        try
        {
            string? exePath = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exePath)) return;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string iconPath = Path.Combine(baseDir, "icon.ico");
            if (!File.Exists(iconPath))
            {
                iconPath = exePath;
            }

            string rootTitle = LocalizationManager.EffectiveLanguage == AppLanguage.Russian
                ? LocalizationManager.GetString("Str_Context_Send", "Send with EdgeDrop")
                : "Send with EdgeDrop";

            foreach (var targetKeyPath in new[] { FileShellKeyPath, DirShellKeyPath })
            {
                using var rootKey = Registry.CurrentUser.CreateSubKey(targetKeyPath);
                if (rootKey != null)
                {
                    rootKey.SetValue("", rootTitle);
                    rootKey.SetValue("MUIVerb", rootTitle);
                    rootKey.SetValue("Icon", $"\"{iconPath}\"");
                    rootKey.SetValue("ExtendedSubCommandsKey", SubCommandsKeyName);
                    try { rootKey.DeleteValue("SubCommands", throwOnMissingValue: false); } catch { }
                    try { rootKey.DeleteSubKeyTree("Shell", throwOnMissingSubKey: false); } catch { }
                }
            }

            using var shellKey = Registry.CurrentUser.CreateSubKey(CommandsKeyPath);
            if (shellKey == null) return;

            var existingKeys = new HashSet<string>(shellKey.GetSubKeyNames(), StringComparer.OrdinalIgnoreCase);
            var desiredKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var devicesSnapshot = settings.GetDevicesSnapshot();
            for (int i = 0; i < devicesSnapshot.Count; i++)
            {
                var dev = devicesSnapshot[i];
                string subKeyName = $"Device_{i}";
                desiredKeys.Add(subKeyName);
                using var devKey = shellKey.CreateSubKey(subKeyName);
                if (devKey != null)
                {
                    string devTitle = string.IsNullOrWhiteSpace(dev.Name)
                        ? string.Format(LocalizationManager.GetString("Str_Context_Computer", "Computer {0}"), i + 1)
                        : dev.Name;

                    string display = $"{devTitle} ({dev.Ip})";
                    devKey.SetValue("", display);
                    devKey.SetValue("MUIVerb", display);
                    devKey.SetValue("Icon", $"\"{iconPath}\"");

                    using var cmdKey = devKey.CreateSubKey("command");
                    cmdKey?.SetValue("", $"\"{exePath}\" --send-to \"{dev.Ip}:{dev.Port}\" \"%1\"");
                }
            }

            if (webSessions != null && webSessions.Count > 0)
            {
                for (int s = 0; s < webSessions.Count; s++)
                {
                    var session = webSessions[s];
                    string subKeyName = $"Session_{s}";
                    desiredKeys.Add(subKeyName);
                    using var sessKey = shellKey.CreateSubKey(subKeyName);
                    if (sessKey != null)
                    {
                        string defaultName = session.IsMobile
                            ? LocalizationManager.GetString("Str_Context_Phone", "Phone")
                            : LocalizationManager.GetString("Str_Context_Browser", "Browser");

                        string sessTitle = string.IsNullOrWhiteSpace(session.Name) ? defaultName : session.Name;
                        string display = $"{sessTitle} ({session.Ip})";

                        sessKey.SetValue("", display);
                        sessKey.SetValue("MUIVerb", display);
                        sessKey.SetValue("Icon", $"\"{iconPath}\"");

                        using var cmdKey = sessKey.CreateSubKey("command");
                        cmdKey?.SetValue("", $"\"{exePath}\" --send-to \"web:{session.Id}\" \"%1\"");
                    }
                }
            }
            else if (settings.WebServerEnabled)
            {
                string subKeyName = "WebEmpty";
                desiredKeys.Add(subKeyName);
                using var webKey = shellKey.CreateSubKey(subKeyName);
                if (webKey != null)
                {
                    string webTitle = LocalizationManager.EffectiveLanguage == AppLanguage.Russian
                        ? LocalizationManager.GetString("Str_Context_PhoneNoSession", "Phone (no active sessions)")
                        : "Phone (no active sessions)";

                    webKey.SetValue("", webTitle);
                    webKey.SetValue("MUIVerb", webTitle);
                    webKey.SetValue("Icon", $"\"{iconPath}\"");

                    using var cmdKey = webKey.CreateSubKey("command");
                    cmdKey?.SetValue("", $"\"{exePath}\" --send-to \"web\" \"%1\"");
                }
            }

            string openSubKeyName = "OpenApp";
            desiredKeys.Add(openSubKeyName);
            using var openKey = shellKey.CreateSubKey(openSubKeyName);
            if (openKey != null)
            {
                string openTitle = LocalizationManager.EffectiveLanguage == AppLanguage.Russian
                    ? LocalizationManager.GetString("Str_Context_SelectRecipient", "Select recipient...")
                    : "Choose recipient...";

                openKey.SetValue("", openTitle);
                openKey.SetValue("MUIVerb", openTitle);
                openKey.SetValue("Icon", $"\"{iconPath}\"");

                using var cmdKey = openKey.CreateSubKey("command");
                cmdKey?.SetValue("", $"\"{exePath}\" --send-to \"select\" \"%1\"");
            }

            foreach (var oldKey in existingKeys)
            {
                if (!desiredKeys.Contains(oldKey))
                {
                    try { shellKey.DeleteSubKeyTree(oldKey, throwOnMissingSubKey: false); } catch { }
                }
            }
        }
        catch { }
    }
}

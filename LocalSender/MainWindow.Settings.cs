using System;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using SolidColorBrush = System.Windows.Media.SolidColorBrush;
using Geometry = System.Windows.Media.Geometry;
using PenLineCap = System.Windows.Media.PenLineCap;
using Stretch = System.Windows.Media.Stretch;
using Forms = System.Windows.Forms;

namespace EdgeDrop;

public partial class MainWindow : Window
{
    private void OnBrowseFolderClick(object sender, RoutedEventArgs e)
    {
        using var dialog = new Forms.FolderBrowserDialog();
        dialog.Description = LocalizationManager.GetString("Str_Dialog_SelectFolder");
        dialog.UseDescriptionForTitle = true;
        dialog.SelectedPath = Directory.Exists(SaveFolderBox.Text) ? SaveFolderBox.Text : _settings.SaveFolder;

        if (dialog.ShowDialog() == Forms.DialogResult.OK)
        {
            SaveFolderBox.Text = dialog.SelectedPath;
            _settings.SaveFolder = dialog.SelectedPath;
            _settings.Save();
        }
    }

    private void OnSaveFolderLostFocus(object sender, RoutedEventArgs e)
    {
        string text = SaveFolderBox.Text.Trim();
        if (!string.IsNullOrEmpty(text) && Directory.Exists(text))
        {
            _settings.SaveFolder = text;
            _settings.Save();
        }
    }

    private void OnSaveClick(object sender, RoutedEventArgs e)
    {
        string selectedFolder = SaveFolderBox.Text.Trim();
        if (!string.IsNullOrEmpty(selectedFolder))
        {
            _settings.SaveFolder = selectedFolder;
        }

        if (LanguageBox.SelectedItem is ComboBoxItem selectedLangItem &&
            Enum.TryParse<AppLanguage>(selectedLangItem.Tag?.ToString(), out var parsedLang))
        {
            _settings.Language = parsedLang;
            LocalizationManager.ApplyLanguage(parsedLang);
            UpdateTrayMenuText();
            RefreshDevicesUi();
        }

        _settings.Save();
        AppLogger.Info("Settings saved", "Settings");
    }

    private void SelectLanguageInComboBox(AppLanguage lang)
    {
        foreach (ComboBoxItem item in LanguageBox.Items)
        {
            if (item.Tag?.ToString() == lang.ToString())
            {
                LanguageBox.SelectedItem = item;
                break;
            }
        }
    }

    private void OnLanguageSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings == null || LanguageBox == null) return;
        if (LanguageBox.SelectedItem is ComboBoxItem item &&
            Enum.TryParse<AppLanguage>(item.Tag?.ToString(), out var selectedLang))
        {
            _settings.Language = selectedLang;
            _settings.Save();
            LocalizationManager.ApplyLanguage(selectedLang);
            UpdateTrayMenuText();
            UpdatePowerButtonVisual();
            UpdateQueueDisplay();
        }
    }

    private void OnNotificationsEnabledChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingColorInternally || _settings == null || NotificationsEnabledToggle == null) return;
        _settings.NotificationsEnabled = NotificationsEnabledToggle.IsChecked == true;
        _settings.Save();
    }

    private void OnRequireAcceptChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingColorInternally || _settings == null || RequireAcceptToggle == null) return;
        _settings.RequireAcceptIncoming = RequireAcceptToggle.IsChecked == true;
        _settings.Save();
        if (_web != null)
        {
            _web.RequireAcceptIncoming = _settings.RequireAcceptIncoming;
        }
    }

    private void OnShellIntegrationChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingColorInternally || _settings == null || ShellIntegrationToggle == null) return;
        bool isEnabled = ShellIntegrationToggle.IsChecked == true;
        ShellContextMenuManager.SetIntegrationEnabled(isEnabled, _settings, _web?.ActiveSessions);
    }

    private void OnAutoStartServicesChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingColorInternally || _settings == null || AutoStartServicesToggle == null) return;
        _settings.AutoStartServicesOnLaunch = AutoStartServicesToggle.IsChecked == true;
        _settings.Save();
    }

    private void OnSafeModeChanged(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingColorInternally || _settings == null || SafeModeToggle == null) return;
        _settings.SafeModeEnabled = SafeModeToggle.IsChecked == true;
        _settings.Save();
        if (_web != null)
        {
            _web.SafeModeEnabled = _settings.SafeModeEnabled;
        }
    }

    private void OnSpeedLimitSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingColorInternally || _settings == null || SpeedLimitBox == null) return;
        if (SpeedLimitBox.SelectedItem is ComboBoxItem item && int.TryParse(item.Tag?.ToString(), out int val))
        {
            _settings.SpeedLimitKBps = val;
            _settings.Save();
        }
    }

    private void OnMaxFileSizeSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_isUpdatingColorInternally || _settings == null || MaxFileSizeBox == null) return;
        if (MaxFileSizeBox.SelectedItem is ComboBoxItem item && long.TryParse(item.Tag?.ToString(), out long val))
        {
            _settings.MaxIncomingFileSizeMB = val;
            _settings.Save();
        }
    }

    private void InitSpeedLimitComboBox()
    {
        if (SpeedLimitBox == null) return;
        foreach (ComboBoxItem item in SpeedLimitBox.Items)
        {
            if (int.TryParse(item.Tag?.ToString(), out int val) && val == _settings.SpeedLimitKBps)
            {
                SpeedLimitBox.SelectedItem = item;
                return;
            }
        }
        if (SpeedLimitBox.Items.Count > 0) SpeedLimitBox.SelectedIndex = 0;
    }

    private void InitMaxFileSizeComboBox()
    {
        if (MaxFileSizeBox == null) return;
        foreach (ComboBoxItem item in MaxFileSizeBox.Items)
        {
            if (long.TryParse(item.Tag?.ToString(), out long val) && val == _settings.MaxIncomingFileSizeMB)
            {
                MaxFileSizeBox.SelectedItem = item;
                return;
            }
        }
        if (MaxFileSizeBox.Items.Count > 0) MaxFileSizeBox.SelectedIndex = 0;
    }

    private enum DiagnosticLevel { Success, Warning, Error }

    private record DiagnosticResultItem(string Title, string Details, DiagnosticLevel Level);

    private async void OnRunDiagnosticsClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DiagnosticsItemsContainer != null)
            {
                DiagnosticsItemsContainer.Children.Clear();
            }

            if (DiagnosticsModalOverlay != null)
            {
                AnimateModalOpen(DiagnosticsModalOverlay, DiagnosticsModalCard);
            }

            AddDiagnosticItem("...", LocalizationManager.GetString("Str_Common_Loading") ?? "Diagnostics...", DiagnosticLevel.Warning);

            int port = _settings.Port;
            int webPort = _settings.WebPort;
            bool webEnabled = _settings.WebServerEnabled;
            string saveFolder = _settings.SaveFolder;
            string localIp = GetLocalIp();

            var results = await Task.Run(() =>
            {
                var list = new List<DiagnosticResultItem>();

                try
                {
                    var activeNet = NetworkInterface.GetAllNetworkInterfaces()
                        .FirstOrDefault(ni => ni.OperationalStatus == OperationalStatus.Up &&
                                              ni.NetworkInterfaceType != NetworkInterfaceType.Loopback);
                    string diagNetInterface = LocalizationManager.GetString("Str_Diag_NetworkInterface");
                    if (activeNet != null)
                    {
                        string netType = activeNet.NetworkInterfaceType switch
                        {
                            NetworkInterfaceType.Wireless80211 => "Wi-Fi",
                            NetworkInterfaceType.Ethernet => "Ethernet",
                            _ => activeNet.NetworkInterfaceType.ToString()
                        };
                        list.Add(new DiagnosticResultItem(diagNetInterface, string.Format(LocalizationManager.GetString("Str_Diag_NetConnected"), netType, activeNet.Name), DiagnosticLevel.Success));
                    }
                    else
                    {
                        var loopback = NetworkInterface.GetAllNetworkInterfaces()
                            .FirstOrDefault(ni => ni.NetworkInterfaceType == NetworkInterfaceType.Loopback && ni.OperationalStatus == OperationalStatus.Up);
                        if (loopback != null)
                        {
                            list.Add(new DiagnosticResultItem(diagNetInterface, LocalizationManager.GetString("Str_Diag_LoopbackOnly"), DiagnosticLevel.Warning));
                        }
                        else
                        {
                            list.Add(new DiagnosticResultItem(diagNetInterface, LocalizationManager.GetString("Str_Diag_AllNetDisabled"), DiagnosticLevel.Error));
                        }
                    }
                }
                catch { }

                string diagLocalIp = LocalizationManager.GetString("Str_Diag_LocalIp");
                if (!string.IsNullOrEmpty(localIp) && localIp != "127.0.0.1")
                {
                    list.Add(new DiagnosticResultItem(diagLocalIp, string.Format(LocalizationManager.GetString("Str_Diag_IpAvailable"), localIp), DiagnosticLevel.Success));
                }
                else if (localIp == "127.0.0.1")
                {
                    list.Add(new DiagnosticResultItem(diagLocalIp, LocalizationManager.GetString("Str_Diag_IpLoopback"), DiagnosticLevel.Warning));
                }
                else
                {
                    list.Add(new DiagnosticResultItem(diagLocalIp, LocalizationManager.GetString("Str_Diag_IpMissing"), DiagnosticLevel.Error));
                }

                string diagPortReceiver = string.Format(LocalizationManager.GetString("Str_Diag_PortReceiver"), port);
                try
                {
                    var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                    bool isPortActive = listeners.Any(ep => ep.Port == port);
                    if (isPortActive)
                    {
                        list.Add(new DiagnosticResultItem(diagPortReceiver, LocalizationManager.GetString("Str_Diag_PortActive"), DiagnosticLevel.Success));
                    }
                    else
                    {
                        list.Add(new DiagnosticResultItem(diagPortReceiver, LocalizationManager.GetString("Str_Diag_PortFree"), DiagnosticLevel.Warning));
                    }
                }
                catch (Exception ex)
                {
                    list.Add(new DiagnosticResultItem(diagPortReceiver, string.Format(LocalizationManager.GetString("Str_Diag_PortError"), ex.Message), DiagnosticLevel.Error));
                }

                string diagWebPort = string.Format(LocalizationManager.GetString("Str_Diag_WebPort"), webPort);
                try
                {
                    var listeners = IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpListeners();
                    bool isWebActive = listeners.Any(ep => ep.Port == webPort);
                    if (isWebActive)
                    {
                        list.Add(new DiagnosticResultItem(diagWebPort, LocalizationManager.GetString("Str_Diag_WebActive"), DiagnosticLevel.Success));
                    }
                    else if (!webEnabled)
                    {
                        list.Add(new DiagnosticResultItem(diagWebPort, LocalizationManager.GetString("Str_Diag_WebDisabled"), DiagnosticLevel.Warning));
                    }
                    else
                    {
                        list.Add(new DiagnosticResultItem(diagWebPort, LocalizationManager.GetString("Str_Diag_WebIdle"), DiagnosticLevel.Warning));
                    }
                }
                catch (Exception ex)
                {
                    list.Add(new DiagnosticResultItem(diagWebPort, string.Format(LocalizationManager.GetString("Str_Diag_PortError"), ex.Message), DiagnosticLevel.Error));
                }

                string diagFolder = LocalizationManager.GetString("Str_Diag_SaveFolder");
                try
                {
                    Directory.CreateDirectory(saveFolder);
                    string testFile = Path.Combine(saveFolder, $".diag_{Guid.NewGuid():N}.tmp");
                    File.WriteAllText(testFile, "test");
                    File.Delete(testFile);
                    list.Add(new DiagnosticResultItem(diagFolder, string.Format(LocalizationManager.GetString("Str_Diag_FolderWritable"), saveFolder), DiagnosticLevel.Success));
                }
                catch (Exception ex)
                {
                    list.Add(new DiagnosticResultItem(diagFolder, string.Format(LocalizationManager.GetString("Str_Diag_FolderError"), ex.Message), DiagnosticLevel.Error));
                }

                string diagSpace = LocalizationManager.GetString("Str_Diag_FreeSpace");
                try
                {
                    var root = Path.GetPathRoot(Path.GetFullPath(saveFolder));
                    if (!string.IsNullOrEmpty(root))
                    {
                        var drive = new DriveInfo(root);
                        if (drive.IsReady)
                        {
                            double freeGb = drive.AvailableFreeSpace / (1024.0 * 1024 * 1024);
                            if (freeGb >= 5.0)
                            {
                                list.Add(new DiagnosticResultItem(diagSpace, string.Format(LocalizationManager.GetString("Str_Diag_SpaceGb"), $"{freeGb:F1}"), DiagnosticLevel.Success));
                            }
                            else if (freeGb >= 1.0)
                            {
                                list.Add(new DiagnosticResultItem(diagSpace, string.Format(LocalizationManager.GetString("Str_Diag_SpaceMb"), $"{freeGb:F1}"), DiagnosticLevel.Warning));
                            }
                            else
                            {
                                long freeMb = drive.AvailableFreeSpace / (1024 * 1024);
                                list.Add(new DiagnosticResultItem(diagSpace, string.Format(LocalizationManager.GetString("Str_Diag_SpaceCrit"), freeMb), DiagnosticLevel.Error));
                            }
                        }
                    }
                }
                catch { }

                return list;
            });

            if (DiagnosticsItemsContainer != null)
            {
                DiagnosticsItemsContainer.Children.Clear();
                foreach (var item in results)
                {
                    AddDiagnosticItem(item.Title, item.Details, item.Level);
                }
            }

            AppLogger.Info("Ran diagnostics dialog successfully", "Diagnostics");
        }
        catch (Exception ex)
        {
            AppLogger.Error($"Diagnostics failed: {ex.Message}", "Diagnostics");
        }
    }

    private void OnCloseDiagnosticsModalClick(object sender, RoutedEventArgs e)
    {
        if (DiagnosticsModalOverlay != null)
        {
            AnimateModalClose(DiagnosticsModalOverlay, DiagnosticsModalCard);
        }
    }

    private void AddDiagnosticItem(string title, string details, DiagnosticLevel level)
    {
        if (DiagnosticsItemsContainer == null) return;

        var card = new Border
        {
            Background = (Brush)FindResource("ItemContainerBackgroundBrush"),
            BorderBrush = (Brush)FindResource("CardStrokeBrush"),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(12, 10, 12, 10),
            Margin = new Thickness(0, 0, 0, 8)
        };

        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        Color iconColor;
        Color badgeBg;
        string pathData;
        string statusText;

        switch (level)
        {
            case DiagnosticLevel.Success:
                iconColor = Color.FromRgb(0x10, 0x89, 0x3E);
                badgeBg = Color.FromArgb(0x28, 0x10, 0x89, 0x3E);
                pathData = "M3,8 L7,12 L13,4";
                statusText = LocalizationManager.GetString("Str_Diag_Ok");
                break;
            case DiagnosticLevel.Warning:
                iconColor = Color.FromRgb(0xFA, 0x9D, 0x00);
                badgeBg = Color.FromArgb(0x28, 0xFA, 0x9D, 0x00);
                pathData = "M8,2 L15,14 L1,14 Z M8,6 L8,9 M8,11 L8,12";
                statusText = LocalizationManager.GetString("Str_Diag_Warning");
                break;
            case DiagnosticLevel.Error:
            default:
                iconColor = Color.FromRgb(0xE8, 0x11, 0x23);
                badgeBg = Color.FromArgb(0x28, 0xE8, 0x11, 0x23);
                pathData = "M4,4 L12,12 M12,4 L4,12";
                statusText = LocalizationManager.GetString("Str_Diag_Error");
                break;
        }

        var iconBorder = new Border
        {
            Width = 28,
            Height = 28,
            CornerRadius = new CornerRadius(14),
            Background = new SolidColorBrush(badgeBg),
            Margin = new Thickness(0, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };

        var iconPath = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse(pathData),
            Stroke = new SolidColorBrush(iconColor),
            StrokeThickness = 2,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            Width = 14,
            Height = 14,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center
        };
        iconBorder.Child = iconPath;
        Grid.SetColumn(iconBorder, 0);
        grid.Children.Add(iconBorder);

        var textStack = new StackPanel { VerticalAlignment = System.Windows.VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0) };
        var titleBlock = new TextBlock
        {
            Text = title,
            FontWeight = FontWeights.SemiBold,
            FontSize = 13,
            Foreground = (Brush)FindResource("TextForegroundBrush")
        };
        var detailsBlock = new TextBlock
        {
            Text = details,
            FontSize = 11.5,
            Foreground = (Brush)FindResource("SubtleTextBrush"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 2, 0, 0)
        };
        textStack.Children.Add(titleBlock);
        textStack.Children.Add(detailsBlock);
        Grid.SetColumn(textStack, 1);
        grid.Children.Add(textStack);

        var pillBorder = new Border
        {
            Background = new SolidColorBrush(badgeBg),
            CornerRadius = new CornerRadius(4),
            Padding = new Thickness(8, 3, 8, 3),
            VerticalAlignment = VerticalAlignment.Center
        };
        var pillText = new TextBlock
        {
            Text = statusText,
            FontSize = 11,
            FontWeight = FontWeights.SemiBold,
            Foreground = new SolidColorBrush(iconColor)
        };
        pillBorder.Child = pillText;
        Grid.SetColumn(pillBorder, 2);
        grid.Children.Add(pillBorder);

        card.Child = grid;
        DiagnosticsItemsContainer.Children.Add(card);
    }
}

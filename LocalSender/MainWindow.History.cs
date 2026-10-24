using System.Windows.Controls;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Media;

namespace EdgeDrop;

public partial class MainWindow : Window
{
    private static readonly string HistoryPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "EdgeDrop", "history.json");
    public ObservableCollection<HistoryItem> HistoryItems { get; } = new();

    private void LoadHistory()
    {
        try
        {
            if (File.Exists(HistoryPath))
            {
                var json = File.ReadAllText(HistoryPath);
                var items = JsonSerializer.Deserialize<List<HistoryItem>>(json);
                if (items != null)
                {
                    HistoryItems.Clear();
                    foreach (var item in items.Take(200))
                    {
                        HistoryItems.Add(item);
                    }
                }
            }
        }
        catch { }
        UpdateEmptyHistoryVisibility();
    }

    private System.Windows.Threading.DispatcherTimer? _historySaveTimer;

    private void ScheduleSaveHistory()
    {
        if (_historySaveTimer == null)
        {
            _historySaveTimer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _historySaveTimer.Tick += (_, _) =>
            {
                _historySaveTimer.Stop();
                SaveHistory();
            };
        }
        _historySaveTimer.Stop();
        _historySaveTimer.Start();
    }

    public void FlushHistorySave()
    {
        _historySaveTimer?.Stop();
        SaveHistory();
    }

    private void SaveHistory()
    {
        try
        {
            string dir = Path.GetDirectoryName(HistoryPath)!;
            Directory.CreateDirectory(dir);
            string tempFile = Path.Combine(dir, $"history.{Guid.NewGuid():N}.tmp");

            var snapshot = HistoryItems.Take(200).ToList();
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });

            Task.Run(() =>
            {
                try
                {
                    File.WriteAllText(tempFile, json);
                    if (File.Exists(HistoryPath))
                    {
                        File.Replace(tempFile, HistoryPath, null);
                    }
                    else
                    {
                        File.Move(tempFile, HistoryPath, overwrite: true);
                    }
                }
                catch { }
            });
        }
        catch { }
    }

    public void AddHistoryRecord(string fileName, string statusText, bool isIncoming, bool isError = false, string? filePath = null)
    {
        HistoryItems.Insert(0, new HistoryItem
        {
            FileName = fileName,
            StatusText = statusText,
            Timestamp = DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss"),
            IsIncoming = isIncoming,
            IsError = isError,
            FilePath = filePath
        });

        while (HistoryItems.Count > 200)
        {
            HistoryItems.RemoveAt(HistoryItems.Count - 1);
        }

        UpdateEmptyHistoryVisibility();
        ScheduleSaveHistory();
    }

    private void OnHistoryItemDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (HistoryListBox?.SelectedItem is HistoryItem item)
        {
            OpenFileOrFolder(item);
        }
    }

    private void OpenFileOrFolder(HistoryItem item)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "explorer.exe",
                    Arguments = $"/select,\"{item.FilePath}\"",
                    UseShellExecute = true
                });
                return;
            }

            if (!string.IsNullOrWhiteSpace(item.FileName))
            {
                string targetPath = Path.Combine(_settings.SaveFolder, item.FileName);
                if (File.Exists(targetPath))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "explorer.exe",
                        Arguments = $"/select,\"{targetPath}\"",
                        UseShellExecute = true
                    });
                    return;
                }
            }

            if (Directory.Exists(_settings.SaveFolder))
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = _settings.SaveFolder,
                    UseShellExecute = true
                });
            }
        }
        catch { }
    }

    private void OnClearHistoryClick(object sender, RoutedEventArgs e)
    {
        HistoryItems.Clear();
        UpdateEmptyHistoryVisibility();
        SaveHistory();
    }

    private bool _filterSent = true;
    private bool _filterReceived = true;
    private bool _filterErrors = true;
    private System.ComponentModel.ICollectionView? _historyView;

    public void InitHistoryView()
    {
        _historyView = System.Windows.Data.CollectionViewSource.GetDefaultView(HistoryItems);
        _historyView.Filter = FilterHistoryPredicate;
        if (HistoryListBox != null)
        {
            HistoryListBox.ItemsSource = _historyView;
        }
        UpdateHistoryFilterButtonHighlight();
        UpdateEmptyHistoryVisibility();
    }

    private bool FilterHistoryPredicate(object obj)
    {
        if (obj is not HistoryItem item) return false;

        bool allChecked = _filterSent && _filterReceived && _filterErrors;
        bool allUnchecked = !_filterSent && !_filterReceived && !_filterErrors;
        if (allChecked || allUnchecked) return true;

        if (item.IsError) return _filterErrors;
        if (item.IsIncoming) return _filterReceived;
        return _filterSent;
    }

    private void OnHistoryFilterButtonClick(object sender, RoutedEventArgs e)
    {
        if (HistoryFilterPopup != null)
        {
            HistoryFilterPopup.IsOpen = !HistoryFilterPopup.IsOpen;
        }
    }

    private void OnHistoryFilterCheckChanged(object sender, RoutedEventArgs e)
    {
        _filterSent = FilterSentCheck?.IsChecked ?? true;
        _filterReceived = FilterReceivedCheck?.IsChecked ?? true;
        _filterErrors = FilterErrorsCheck?.IsChecked ?? true;

        if (_historyView == null)
        {
            InitHistoryView();
        }
        else
        {
            _historyView.Refresh();
        }

        UpdateHistoryFilterButtonHighlight();
        UpdateEmptyHistoryVisibility();
        AnimateHistoryFilterChange(HistoryFilterBtn);
    }

    private void OnHistoryFilterResetClick(object sender, RoutedEventArgs e)
    {
        if (FilterSentCheck != null) FilterSentCheck.IsChecked = true;
        if (FilterReceivedCheck != null) FilterReceivedCheck.IsChecked = true;
        if (FilterErrorsCheck != null) FilterErrorsCheck.IsChecked = true;

        _filterSent = true;
        _filterReceived = true;
        _filterErrors = true;

        if (_historyView == null)
        {
            InitHistoryView();
        }
        else
        {
            _historyView.Refresh();
        }

        UpdateHistoryFilterButtonHighlight();
        UpdateEmptyHistoryVisibility();
        AnimateHistoryFilterChange(HistoryFilterBtn);
    }

    public void UpdateHistoryFilterButtonHighlight()
    {
        if (HistoryFilterBtn == null) return;
        bool isCustomFilterActive = !(_filterSent && _filterReceived && _filterErrors);

        if (isCustomFilterActive)
        {
            HistoryFilterBtn.Background = (System.Windows.Media.Brush)FindResource("SystemAccentBrush");
            HistoryFilterBtn.Foreground = System.Windows.Media.Brushes.White;
            HistoryFilterBtn.FontWeight = FontWeights.SemiBold;
        }
        else
        {
            HistoryFilterBtn.ClearValue(System.Windows.Controls.Button.BackgroundProperty);
            HistoryFilterBtn.ClearValue(System.Windows.Controls.Button.ForegroundProperty);
            HistoryFilterBtn.FontWeight = FontWeights.Normal;
        }
    }

    private void UpdateEmptyHistoryVisibility()
    {
        if (EmptyHistoryText != null)
        {
            bool hasItems = _historyView != null ? !_historyView.IsEmpty : HistoryItems.Count > 0;
            EmptyHistoryText.Visibility = hasItems ? Visibility.Collapsed : Visibility.Visible;
        }
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject
    {
        while (child != null)
        {
            if (child is T parent) return parent;
            child = VisualTreeHelper.GetParent(child);
        }
        return null;
    }

    private void OnHistoryItemRightMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (sender is System.Windows.Controls.ListBoxItem lbi)
        {
            lbi.IsSelected = true;
            lbi.Focus();
        }
    }

    private void OnHistoryContextMenuOpening(object sender, ContextMenuEventArgs e)
    {
        var dep = e.OriginalSource as DependencyObject;
        var lbi = FindVisualParent<ListBoxItem>(dep);
        if (lbi == null || lbi.DataContext is not HistoryItem item)
        {
            e.Handled = true;
            return;
        }

        lbi.IsSelected = true;
        lbi.Focus();

        var menu = new ContextMenu
        {
            Style = (Style)FindResource("ThemedContextMenuStyle"),
            PlacementTarget = lbi
        };

        var iconColor = ThemeManager.IconColor;
        var openImg = SvgIconHelper.LoadSvgIcon("open.svg", iconColor);
        var folderImg = SvgIconHelper.LoadSvgIcon("folder.svg", iconColor);
        var copyImg = SvgIconHelper.LoadSvgIcon("copy.svg", iconColor);
        var transferImg = SvgIconHelper.LoadSvgIcon("transfer.svg", iconColor);
        var xImg = SvgIconHelper.LoadSvgIcon("x.svg", System.Windows.Media.Color.FromRgb(0xFF, 0x52, 0x52));

        Func<ImageSource?, System.Windows.Controls.Image?> makeIcon = img =>
        {
            if (img == null) return null;
            return new System.Windows.Controls.Image
            {
                Source = img,
                Width = 14,
                Height = 14,
                Stretch = Stretch.Uniform
            };
        };

        var openItem = new MenuItem
        {
            Header = LocalizationManager.GetString("Str_History_Open"),
            Style = (Style)FindResource("ThemedMenuItemStyle"),
            Icon = makeIcon(openImg)
        };
        openItem.Click += (_, _) => OpenHistoryItem(item);
        menu.Items.Add(openItem);

        var folderItem = new MenuItem
        {
            Header = LocalizationManager.GetString("Str_History_ShowInFolder"),
            Style = (Style)FindResource("ThemedMenuItemStyle"),
            Icon = makeIcon(folderImg)
        };
        folderItem.Click += (_, _) => OpenFileOrFolder(item);
        menu.Items.Add(folderItem);

        menu.Items.Add(new Separator { Style = (Style)FindResource("ThemedSeparatorStyle") });

        var copyPathItem = new MenuItem
        {
            Header = LocalizationManager.GetString("Str_History_CopyPath"),
            Style = (Style)FindResource("ThemedMenuItemStyle"),
            Icon = makeIcon(copyImg)
        };
        copyPathItem.Click += (_, _) => CopyHistoryPath(item);
        menu.Items.Add(copyPathItem);

        var resendItem = new MenuItem
        {
            Header = LocalizationManager.GetString("Str_History_Resend"),
            Style = (Style)FindResource("ThemedMenuItemStyle"),
            Icon = makeIcon(transferImg)
        };
        resendItem.Click += (_, _) => ResendHistoryItem(item);
        menu.Items.Add(resendItem);

        menu.Items.Add(new Separator { Style = (Style)FindResource("ThemedSeparatorStyle") });

        var removeItem = new MenuItem
        {
            Header = LocalizationManager.GetString("Str_History_RemoveRecord"),
            Style = (Style)FindResource("ThemedMenuItemStyle"),
            Icon = makeIcon(xImg)
        };
        removeItem.Click += (_, _) => RemoveHistoryRecord(item);
        menu.Items.Add(removeItem);

        lbi.ContextMenu = menu;
        menu.IsOpen = true;
        e.Handled = true;
    }

    private void OpenHistoryItem(HistoryItem item)
    {
        string? path = ResolveHistoryFilePath(item);
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowToast(false, false, ex.Message);
            }
        }
        else
        {
            ShowToast(false, false, LocalizationManager.GetString("Str_History_NotFound"));
        }
    }

    private void CopyHistoryPath(HistoryItem item)
    {
        string? path = ResolveHistoryFilePath(item) ?? item.FilePath ?? item.FileName;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                System.Windows.Clipboard.SetText(path);
                ShowToast(false, true, LocalizationManager.GetString("Str_History_PathCopied"));
            }
            catch { }
        }
    }

    private void ResendHistoryItem(HistoryItem item)
    {
        string? path = ResolveHistoryFilePath(item);
        if (string.IsNullOrEmpty(path))
        {
            ShowToast(false, false, LocalizationManager.GetString("Str_History_NotFound"));
            return;
        }

        var devices = _settings.GetDevicesSnapshot();
        if (devices.Count > 0)
        {
            var targetDev = devices[0];
            if (!_running) Start();
            _transferService.SendFiles(targetDev, new[] { path }, isWebTarget: false);
            ShowToast(false, true, string.Format(LocalizationManager.GetString("Str_Notify_SendSuccess"), item.FileName));
        }
        else if (_web != null && _web.HasActiveSessions)
        {
            if (!_running) Start();
            _transferService.SendFiles(null, new[] { path }, isWebTarget: true);
            ShowToast(false, true, string.Format(LocalizationManager.GetString("Str_Notify_SendSuccess"), item.FileName));
        }
        else
        {
            ShowToast(false, false, LocalizationManager.GetString("Str_Web_NoActiveSessions"));
        }
    }

    private void RemoveHistoryRecord(HistoryItem item)
    {
        HistoryItems.Remove(item);
        UpdateEmptyHistoryVisibility();
        ScheduleSaveHistory();
    }

    private string? ResolveHistoryFilePath(HistoryItem item)
    {
        if (!string.IsNullOrWhiteSpace(item.FilePath) && File.Exists(item.FilePath))
        {
            return item.FilePath;
        }
        if (!string.IsNullOrWhiteSpace(item.FileName))
        {
            string targetPath = Path.Combine(_settings.SaveFolder, item.FileName);
            if (File.Exists(targetPath)) return targetPath;
        }
        return null;
    }

    private HistoryItem? GetSelectedOrContextHistoryItem(object sender)
    {
        if (sender is FrameworkElement fe && fe.DataContext is HistoryItem hi1) return hi1;
        if (sender is MenuItem mi && mi.DataContext is HistoryItem hi2) return hi2;
        return HistoryListBox?.SelectedItem as HistoryItem;
    }

    private void OnHistoryOpenClick(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedOrContextHistoryItem(sender);
        if (item == null) return;
        string? path = ResolveHistoryFilePath(item);
        if (!string.IsNullOrEmpty(path))
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = path,
                    UseShellExecute = true
                });
            }
            catch (Exception ex)
            {
                ShowToast(false, false, ex.Message);
            }
        }
        else
        {
            ShowToast(false, false, LocalizationManager.GetString("Str_History_NotFound"));
        }
    }

    private void OnHistoryShowInFolderClick(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedOrContextHistoryItem(sender);
        if (item == null) return;
        OpenFileOrFolder(item);
    }

    private void OnHistoryCopyPathClick(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedOrContextHistoryItem(sender);
        if (item == null) return;
        string? path = ResolveHistoryFilePath(item) ?? item.FilePath ?? item.FileName;
        if (!string.IsNullOrWhiteSpace(path))
        {
            try
            {
                System.Windows.Clipboard.SetText(path);
                ShowToast(false, true, LocalizationManager.GetString("Str_History_PathCopied"));
            }
            catch { }
        }
    }

    private void OnHistoryResendClick(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedOrContextHistoryItem(sender);
        if (item == null) return;
        string? path = ResolveHistoryFilePath(item);
        if (string.IsNullOrEmpty(path))
        {
            ShowToast(false, false, LocalizationManager.GetString("Str_History_NotFound"));
            return;
        }

        var devices = _settings.GetDevicesSnapshot();
        if (devices.Count > 0)
        {
            var targetDev = devices[0];
            if (!_running) Start();
            _transferService.SendFiles(targetDev, new[] { path }, isWebTarget: false);
            ShowToast(false, true, string.Format(LocalizationManager.GetString("Str_Notify_SendSuccess"), item.FileName));
        }
        else if (_web != null && _web.HasActiveSessions)
        {
            if (!_running) Start();
            _transferService.SendFiles(null, new[] { path }, isWebTarget: true);
            ShowToast(false, true, string.Format(LocalizationManager.GetString("Str_Notify_SendSuccess"), item.FileName));
        }
        else
        {
            ShowToast(false, false, LocalizationManager.GetString("Str_Web_NoActiveSessions"));
        }
    }

    private void OnHistoryRemoveRecordClick(object sender, RoutedEventArgs e)
    {
        var item = GetSelectedOrContextHistoryItem(sender);
        if (item == null) return;
        HistoryItems.Remove(item);
        UpdateEmptyHistoryVisibility();
        ScheduleSaveHistory();
    }

}

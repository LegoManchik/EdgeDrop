using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using Forms = System.Windows.Forms;

namespace EdgeDrop;

public partial class MainWindow : Window
{
    private Forms.NotifyIcon? _notifyIcon;
    private ContextMenu? _activeTrayMenu;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    private void InitTray()
    {
        Icon trayIcon = SystemIcons.Application;
        try
        {
            string iconPath = Path.Combine(AppContext.BaseDirectory, "icon.ico");
            if (File.Exists(iconPath))
            {
                trayIcon = new Icon(iconPath);
            }
            else
            {
                var streamInfo = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/icon.ico"));
                if (streamInfo != null)
                {
                    trayIcon = new Icon(streamInfo.Stream);
                }
            }
        }
        catch { }

        _notifyIcon = new Forms.NotifyIcon
        {
            Icon = trayIcon,
            Visible = true,
            Text = "EdgeDrop"
        };

        _notifyIcon.MouseClick += (s, e) =>
        {
            if (e.Button == Forms.MouseButtons.Right)
            {
                Dispatcher.Invoke(ShowTrayContextMenu);
            }
        };
        _notifyIcon.DoubleClick += (_, _) => ShowFromTray();
        _notifications.AttachNotifyIcon(_notifyIcon);
    }

    private void UpdateTrayMenuText()
    {
    }

    private void ShowTrayContextMenu()
    {
        if (_activeTrayMenu != null && _activeTrayMenu.IsOpen)
        {
            _activeTrayMenu.IsOpen = false;
        }

        var menu = new ContextMenu
        {
            Style = (Style)FindResource("ThemedContextMenuStyle")
        };

        var iconColor = ThemeManager.IconColor;
        var openImg = SvgIconHelper.LoadSvgIcon("open.svg", iconColor);
        var toggleIconName = _running ? "power-off.svg" : "power.svg";
        var toggleImg = SvgIconHelper.LoadSvgIcon(toggleIconName, iconColor);
        var shareImg = SvgIconHelper.LoadSvgIcon("share.svg", iconColor);
        var exitImg = SvgIconHelper.LoadSvgIcon("exit.svg", iconColor);
        var devImg = SvgIconHelper.LoadSvgIcon("devices.svg", iconColor);
        var smartphoneImg = SvgIconHelper.LoadSvgIcon("smartphone.svg", iconColor);
        var browserImg = SvgIconHelper.LoadSvgIcon("browser.svg", iconColor);

        Func<ImageSource?, System.Windows.Controls.Image?> makeIcon = img =>
        {
            if (img == null) return null;
            return new System.Windows.Controls.Image
            {
                Source = img,
                Width = 15,
                Height = 15,
                Stretch = Stretch.Uniform
            };
        };

        var openItem = new MenuItem
        {
            Header = LocalizationManager.GetString("Str_Tray_Open"),
            Style = (Style)FindResource("ThemedMenuItemStyle"),
            Icon = makeIcon(openImg)
        };
        openItem.Click += (_, _) => ShowFromTray();
        menu.Items.Add(openItem);

        var toggleItem = new MenuItem
        {
            Header = _running ? LocalizationManager.GetString("Str_Tray_TurnOff") : LocalizationManager.GetString("Str_Tray_TurnOn"),
            Style = (Style)FindResource("ThemedMenuItemStyle"),
            Icon = makeIcon(toggleImg)
        };
        toggleItem.Click += (_, _) => ToggleService();
        menu.Items.Add(toggleItem);

        menu.Items.Add(new Separator { Style = (Style)FindResource("ThemedSeparatorStyle") });

        var sendToItem = new MenuItem
        {
            Header = LocalizationManager.GetString("Str_Tray_SendTo"),
            Style = (Style)FindResource("ThemedMenuItemStyle"),
            Icon = makeIcon(shareImg)
        };

        bool hasAnyTarget = false;

        foreach (var dev in _settings.GetDevicesSnapshot())
        {
            hasAnyTarget = true;
            var targetDev = dev;
            var devItem = new MenuItem
            {
                Header = $"{targetDev.Name} ({targetDev.Ip})",
                Style = (Style)FindResource("ThemedMenuItemStyle"),
                Icon = makeIcon(devImg)
            };
            devItem.Click += (_, _) => Dispatcher.Invoke(() => PickAndSendFilesToDevice(targetDev));
            sendToItem.Items.Add(devItem);
        }

        var webSessions = _web?.ActiveSessions;
        if (webSessions != null && webSessions.Count > 0)
        {
            foreach (var sess in webSessions)
            {
                hasAnyTarget = true;
                var targetSess = sess;
                string label = string.IsNullOrWhiteSpace(targetSess.Name)
                    ? (targetSess.IsMobile ? LocalizationManager.GetString("Str_Web_PhoneSession") : LocalizationManager.GetString("Str_Web_BrowserSession"))
                    : targetSess.Name;

                var sessItem = new MenuItem
                {
                    Header = $"{label} ({targetSess.Ip})",
                    Style = (Style)FindResource("ThemedMenuItemStyle"),
                    Icon = makeIcon(targetSess.IsMobile ? smartphoneImg : browserImg)
                };
                sessItem.Click += (_, _) => Dispatcher.Invoke(() => PickAndSendFilesToWebSession(targetSess));
                sendToItem.Items.Add(sessItem);
            }
        }

        if (!hasAnyTarget)
        {
            var noDevItem = new MenuItem
            {
                Header = LocalizationManager.GetString("Str_Tray_NoDevices"),
                IsEnabled = false,
                Style = (Style)FindResource("ThemedMenuItemStyle")
            };
            sendToItem.Items.Add(noDevItem);
        }

        menu.Items.Add(sendToItem);

        menu.Items.Add(new Separator { Style = (Style)FindResource("ThemedSeparatorStyle") });

        var exitItem = new MenuItem
        {
            Header = LocalizationManager.GetString("Str_Tray_Exit"),
            Style = (Style)FindResource("ThemedMenuItemStyle"),
            Icon = makeIcon(exitImg)
        };
        exitItem.Click += (_, _) => ExitApplication();
        menu.Items.Add(exitItem);

        _activeTrayMenu = menu;
        menu.Closed += (_, _) =>
        {
            if (_activeTrayMenu == menu) _activeTrayMenu = null;
        };

        var helper = new WindowInteropHelper(this);
        SetForegroundWindow(helper.Handle);

        menu.Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint;
        menu.IsOpen = true;
    }

    private void ShowFromTray()
    {
        Show();
        WindowState = WindowState.Normal;
        Activate();
    }

    private void ShowNotification(string title, string message)
    {
        _notifyIcon?.ShowBalloonTip(3000, title, message, Forms.ToolTipIcon.Info);
    }

    private void ExitApplication()
    {
        _isExplicitExit = true;
        Close();
    }
}

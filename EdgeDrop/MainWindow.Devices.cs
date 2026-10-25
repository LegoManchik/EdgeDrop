using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;

namespace EdgeDrop;

public partial class MainWindow : Window
{
    private string? _justAddedDeviceId;
    private readonly HashSet<string> _seenSessionIds = new();
    private void RefreshDevicesUi()
    {
        if (DevicesCardsContainer == null) return;

        DevicesCardsContainer.Children.Clear();

        var activeSessions = _web?.ActiveSessions ?? (IReadOnlyList<ActiveSessionInfo>)Array.Empty<ActiveSessionInfo>();

        var devicesSnapshot = _settings.GetDevicesSnapshot();

        int pcCount = devicesSnapshot.Count;

        if (AddDeviceButton != null)
        {
            AddDeviceButton.Visibility = pcCount < 4 ? Visibility.Visible : Visibility.Collapsed;
        }

        var iconColor = ThemeManager.IconColor;

        var pingImg = SvgIconHelper.LoadSvgIcon("ping.svg", iconColor);

        var chevronImg = SvgIconHelper.LoadSvgIcon("chevron-down.svg", iconColor);

        var monitorImg = SvgIconHelper.LoadSvgIcon("devices.svg", iconColor);

        var smartphoneImg = SvgIconHelper.LoadSvgIcon("smartphone.svg", iconColor) ?? SvgIconHelper.LoadSvgIcon("monitor-smartphone.svg", iconColor) ?? monitorImg;

        var browserImg = SvgIconHelper.LoadSvgIcon("monitor-smartphone.svg", iconColor) ?? monitorImg;

        var xImg = SvgIconHelper.LoadSvgIcon("x.svg", iconColor);

        for (int i = 0; i < pcCount; i++)
        {
            var device = devicesSnapshot[i];

            int deviceIndex = i;

            var cardBorder = new Border
            {
                Style = (Style)FindResource("AnimatedCardBorderStyle"),
                BorderThickness = new Thickness(0),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(16),

                Margin = new Thickness(0, 0, 0, 12)
            };

            cardBorder.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");

            if (device.Id == _justAddedDeviceId)
            {
                _justAddedDeviceId = null;

                var scaleTransform = new ScaleTransform(1, 0);
                cardBorder.LayoutTransform = scaleTransform;

                var translateTransform = new TranslateTransform(0, -16);
                cardBorder.RenderTransform = translateTransform;
                cardBorder.Opacity = 0;

                var expandAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var slideAnim = new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(250))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };
                var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, expandAnim);
                translateTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
                cardBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            }

            var mainStack = new StackPanel();

            var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var iconBox = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };

            if (monitorImg != null)
            {
                iconBox.Children.Add(new System.Windows.Controls.Image { Source = monitorImg, Width = 18, Height = 18, Stretch = Stretch.Uniform });
            }

            else
            {
                var rect = new System.Windows.Shapes.Rectangle { Width = 16, Height = 12, StrokeThickness = 1.5 };

                rect.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "IconBrush");

                iconBox.Children.Add(rect);
            }

            Grid.SetColumn(iconBox, 0);

            headerGrid.Children.Add(iconBox);

            var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

            var titleRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var titleBlock = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(device.Name) ? LocalizationManager.GetString("Str_Devices_DefaultName", deviceIndex + 1) : device.Name,
                FontSize = 14,

                FontWeight = FontWeights.SemiBold
            };

            titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextForegroundBrush");

            titleRow.Children.Add(titleBlock);

            var statusDot = new System.Windows.Shapes.Ellipse
            {
                Tag = $"statusDot_{device.Id}",
                Width = 7,
                Height = 7,
                Margin = new Thickness(8, 0, 0, 0),

                Fill = device.IsOnline

                    ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129))

                    : new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 150, 150, 150)),

                ToolTip = device.IsOnline

                    ? LocalizationManager.GetString("Str_Devices_Online")

                    : LocalizationManager.GetString("Str_Devices_Offline")
            };

            titleRow.Children.Add(statusDot);

            titleStack.Children.Add(titleRow);

            var edgeSubtitle = new TextBlock
            {
                Text = $"{device.Ip}:{device.Port} • {GetEdgeDisplayName(device.DropEdge)}",
                FontSize = 11,

                Margin = new Thickness(0, 2, 0, 0)
            };

            edgeSubtitle.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

            titleStack.Children.Add(edgeSubtitle);

            Grid.SetColumn(titleStack, 1);

            headerGrid.Children.Add(titleStack);

            var actionStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

            var testBtn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("CardIconButtonStyle"),
                Padding = new Thickness(7, 6, 7, 6),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,

                ToolTip = LocalizationManager.GetString("Str_Devices_TestTooltip")
            };

            var testGrid = new Grid { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };

            if (pingImg != null)
            {
                testGrid.Children.Add(new System.Windows.Controls.Image { Source = pingImg, Width = 15, Height = 15, Stretch = Stretch.Uniform });
            }

            else
            {
                testGrid.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = (System.Windows.Media.Brush)FindResource("IconBrush") });
            }

            testBtn.Content = testGrid;

            testBtn.Click += async (s, e) =>
            {
                e.Handled = true;

                await PerformPingTestAsync(device.Ip, device.Port, device.Name);
            };

            actionStack.Children.Add(testBtn);

            var deleteBtn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("CardIconButtonStyle"),
                Padding = new Thickness(7, 6, 7, 6),
                Margin = new Thickness(0, 0, 4, 0),
                Cursor = System.Windows.Input.Cursors.Hand,

                ToolTip = LocalizationManager.GetString("Str_Devices_DeleteTooltip")
            };

            if (xImg != null)
            {
                deleteBtn.Content = new System.Windows.Controls.Image
                {
                    Source = xImg,
                    Width = 12,
                    Height = 12,

                    Stretch = Stretch.Uniform
                };
            }

            else
            {
                deleteBtn.Content = new TextBlock
                {
                    Text = "✕",
                    FontSize = 12,
                    FontWeight = FontWeights.Bold,

                    Foreground = (System.Windows.Media.Brush)FindResource("SubtleTextBrush")
                };
            }

            deleteBtn.Click += (s, e) =>
            {
                e.Handled = true;
                deleteBtn.IsEnabled = false;
                cardBorder.IsHitTestVisible = false;

                string devName = device.Name;

                var scaleTransform = new ScaleTransform(1, 1);
                cardBorder.LayoutTransform = scaleTransform;

                var translateTransform = new TranslateTransform(0, 0);
                cardBorder.RenderTransform = translateTransform;

                var collapseAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
                };
                var slideAnim = new DoubleAnimation(0, 35, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };
                var fadeAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
                };

                collapseAnim.Completed += (_, _) =>
                {
                    _settings.RemoveKnownDevice(device.Id);

                    AppLogger.Info($"Device removed: {devName}", "Devices");

                    RefreshDevicesUi();

                    RestartEdges();

                    UpdateIpDisplay();
                };

                scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, collapseAnim);
                translateTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
                cardBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
            };

            actionStack.Children.Add(deleteBtn);

            var chevronBtn = new System.Windows.Controls.Button
            {
                Style = (Style)FindResource("CardIconButtonStyle"),
                Padding = new Thickness(7, 6, 7, 6),
                Cursor = System.Windows.Input.Cursors.Hand,

                ToolTip = LocalizationManager.GetString("Str_Devices_CollapseTooltip")
            };

            var chevronContainer = new Grid
            {
                Width = 14,
                Height = 14,

                RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
            };

            var chevronRotate = new RotateTransform(device.IsCollapsed ? 180 : 0);

            chevronContainer.RenderTransform = chevronRotate;

            if (chevronImg != null)
            {
                chevronContainer.Children.Add(new System.Windows.Controls.Image { Source = chevronImg, Width = 14, Height = 14, Stretch = Stretch.Uniform });
            }

            else
            {
                var chevronPath = new System.Windows.Shapes.Path
                {
                    Data = Geometry.Parse("M3,5 L7,9 L11,5"),
                    Stroke = (System.Windows.Media.Brush)FindResource("IconBrush"),
                    StrokeThickness = 1.5,
                    StrokeStartLineCap = PenLineCap.Round,
                    StrokeEndLineCap = PenLineCap.Round,
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Center,

                    VerticalAlignment = VerticalAlignment.Center
                };

                chevronContainer.Children.Add(chevronPath);
            }

            chevronBtn.Content = chevronContainer;

            actionStack.Children.Add(chevronBtn);

            Grid.SetColumn(actionStack, 2);

            headerGrid.Children.Add(actionStack);

            mainStack.Children.Add(headerGrid);

            var cardBody = new StackPanel
            {
                Margin = new Thickness(0, 14, 0, 0),

                Visibility = device.IsCollapsed ? Visibility.Collapsed : Visibility.Visible
            };

            var bodySeparator = new Separator
            {
                Background = (System.Windows.Media.Brush)FindResource("CardStrokeBrush"),
                Margin = new Thickness(0, 0, 0, 8),

                Visibility = Visibility.Collapsed
            };

            cardBody.Children.Add(bodySeparator);

            var nameLabel = new TextBlock
            {
                Text = LocalizationManager.GetString("Str_Devices_DeviceName"),
                FontSize = 12,

                Margin = new Thickness(0, 0, 0, 4)
            };

            nameLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

            var nameBox = new System.Windows.Controls.TextBox
            {
                Text = device.Name,

                Margin = new Thickness(0, 0, 0, 12)
            };

            nameBox.TextChanged += (s, e) =>
            {
                device.Name = nameBox.Text.Trim();

                titleBlock.Text = string.IsNullOrWhiteSpace(device.Name) ? LocalizationManager.GetString("Str_Devices_DefaultName", deviceIndex + 1) : device.Name;

                _settings.Save();

                UpdateIpDisplay();
            };

            cardBody.Children.Add(nameLabel);

            cardBody.Children.Add(nameBox);

            var ipPortGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };

            ipPortGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

            ipPortGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });

            ipPortGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var ipStack = new StackPanel();

            var ipLabel = new TextBlock
            {
                Text = LocalizationManager.GetString("Str_Devices_Ip"),
                FontSize = 12,

                Margin = new Thickness(0, 0, 0, 4)
            };

            ipLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

            var ipBox = new System.Windows.Controls.TextBox { Text = device.Ip };

            ipBox.TextChanged += (s, e) =>
            {
                device.Ip = ipBox.Text.Trim();

                edgeSubtitle.Text = $"{device.Ip}:{device.Port} • {GetEdgeDisplayName(device.DropEdge)}";

                _settings.Save();

                UpdateIpDisplay();
            };

            ipStack.Children.Add(ipLabel);

            ipStack.Children.Add(ipBox);

            Grid.SetColumn(ipStack, 0);

            ipPortGrid.Children.Add(ipStack);

            var portStack = new StackPanel();

            var portLabel = new TextBlock
            {
                Text = LocalizationManager.GetString("Str_Devices_Port"),
                FontSize = 12,

                Margin = new Thickness(0, 0, 0, 4)
            };

            portLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

            var portBox = new System.Windows.Controls.TextBox { Text = device.Port.ToString() };

            portBox.TextChanged += (s, e) =>
            {
                if (int.TryParse(portBox.Text.Trim(), out var p) && p > 0 && p <= 65535)
                {
                    device.Port = p;

                    edgeSubtitle.Text = $"{device.Ip}:{device.Port} • {GetEdgeDisplayName(device.DropEdge)}";

                    _settings.Save();
                }
            };

            portStack.Children.Add(portLabel);

            portStack.Children.Add(portBox);

            Grid.SetColumn(portStack, 2);

            ipPortGrid.Children.Add(portStack);

            cardBody.Children.Add(ipPortGrid);

            var edgeLabel = new TextBlock
            {
                Text = LocalizationManager.GetString("Str_Devices_Edge"),
                FontSize = 12,

                Margin = new Thickness(0, 0, 0, 4)
            };

            edgeLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

            var edgeBox = new System.Windows.Controls.ComboBox
            {
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,

                Width = 220
            };

            edgeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.GetString("Str_Edge_Right"), Tag = "Right" });

            edgeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.GetString("Str_Edge_Left"), Tag = "Left" });

            edgeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.GetString("Str_Edge_Top"), Tag = "Top" });

            edgeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.GetString("Str_Edge_Bottom"), Tag = "Bottom" });

            foreach (ComboBoxItem itm in edgeBox.Items)
            {
                if (itm.Tag?.ToString() == device.DropEdge.ToString())
                {
                    edgeBox.SelectedItem = itm;

                    break;
                }
            }

            edgeBox.SelectionChanged += (s, e) =>
            {
                if (edgeBox.SelectedItem is ComboBoxItem sel &&

                    Enum.TryParse<ScreenEdge>(sel.Tag?.ToString(), out var parsedEdge))
                {
                    device.DropEdge = parsedEdge;

                    edgeSubtitle.Text = $"{device.Ip}:{device.Port} • {GetEdgeDisplayName(device.DropEdge)}";

                    _settings.Save();

                    RestartEdges();
                }
            };

            cardBody.Children.Add(edgeLabel);

            cardBody.Children.Add(edgeBox);

            mainStack.Children.Add(cardBody);

            AttachCardDragDrop(cardBorder, mainStack, files =>
            {
                if (!_running) Start();
                _transferService.SendFiles(device, files, isWebTarget: false);
                ShowTransferFooter(true);
                AppLogger.Info($"Card drag-and-drop: sending {files.Length} file(s) to device '{device.Name}' ({device.Ip})", "Transfer");
            });

            Action toggleCollapse = () =>
            {
                device.IsCollapsed = !device.IsCollapsed;

                _settings.Save();

                var animRotate = new DoubleAnimation(device.IsCollapsed ? 180 : 0, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                chevronRotate.BeginAnimation(RotateTransform.AngleProperty, animRotate);

                if (device.IsCollapsed)
                {
                    var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));

                    fadeOut.Completed += (_, _) => cardBody.Visibility = Visibility.Collapsed;

                    cardBody.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }

                else
                {
                    cardBody.Visibility = Visibility.Visible;

                    var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));

                    cardBody.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                }
            };

            chevronBtn.Click += (s, e) =>
            {
                e.Handled = true;

                toggleCollapse();
            };

            titleStack.MouseDown += (s, e) =>
            {
                toggleCollapse();
            };

            DevicesCardsContainer.Children.Add(cardBorder);
        }

        for (int j = 0; j < activeSessions.Count; j++)
        {
            var session = activeSessions[j];

            var devIcon = session.IsMobile ? smartphoneImg : browserImg;

            var phoneCard = CreatePhoneSessionCardUi(session, pingImg, chevronImg, devIcon, xImg);

            DevicesCardsContainer.Children.Add(phoneCard);
        }

        ShellContextMenuManager.UpdateMenuIfEnabled(_settings, activeSessions);
    }

    private UIElement CreatePhoneSessionCardUi(ActiveSessionInfo session, ImageSource? pingImg, ImageSource? chevronImg, ImageSource? devIcon, ImageSource? xImg)
    {
        var cardBorder = new Border
        {
            Style = (Style)FindResource("AnimatedCardBorderStyle"),
            BorderThickness = new Thickness(0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(16),

            Margin = new Thickness(0, 0, 0, 12)
        };

        cardBorder.SetResourceReference(Border.BackgroundProperty, "CardBackgroundBrush");

        if (_seenSessionIds.Add(session.Id))
        {
            var scaleTransform = new ScaleTransform(1, 0);
            cardBorder.LayoutTransform = scaleTransform;

            var translateTransform = new TranslateTransform(0, -16);
            cardBorder.RenderTransform = translateTransform;
            cardBorder.Opacity = 0;

            var expandAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var slideAnim = new DoubleAnimation(-16, 0, TimeSpan.FromMilliseconds(250))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };
            var fadeAnim = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, expandAnim);
            translateTransform.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            cardBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        }

        var mainStack = new StackPanel();

        var headerGrid = new Grid { Margin = new Thickness(0, 0, 0, 0), Cursor = System.Windows.Input.Cursors.Hand };

        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        headerGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var iconBox = new Grid { Width = 18, Height = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };

        if (devIcon != null)
        {
            iconBox.Children.Add(new System.Windows.Controls.Image { Source = devIcon, Width = 18, Height = 18, Stretch = Stretch.Uniform });
        }

        else
        {
            var rect = new System.Windows.Shapes.Rectangle { Width = 12, Height = 16, StrokeThickness = 1.5, RadiusX = 2, RadiusY = 2 };

            rect.SetResourceReference(System.Windows.Shapes.Shape.StrokeProperty, "IconBrush");

            iconBox.Children.Add(rect);
        }

        Grid.SetColumn(iconBox, 0);

        headerGrid.Children.Add(iconBox);

        var titleStack = new StackPanel { VerticalAlignment = VerticalAlignment.Center };

        var titleRow = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var titleBlock = new TextBlock
        {
            Text = string.IsNullOrWhiteSpace(session.Name) ? (session.IsMobile ? LocalizationManager.GetString("Str_Web_PhoneSession") : LocalizationManager.GetString("Str_Web_BrowserSession")) : session.Name,
            FontSize = 14,

            FontWeight = FontWeights.SemiBold
        };

        titleBlock.SetResourceReference(TextBlock.ForegroundProperty, "TextForegroundBrush");

        titleRow.Children.Add(titleBlock);

        var statusDot = new System.Windows.Shapes.Ellipse
        {
            Width = 7,
            Height = 7,
            Margin = new Thickness(8, 0, 0, 0),
            Fill = new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129)),

            ToolTip = LocalizationManager.GetString("Str_Devices_Online")
        };

        titleRow.Children.Add(statusDot);

        titleStack.Children.Add(titleRow);

        var edgeSubtitle = new TextBlock
        {
            Text = $"{session.Ip}:{session.Port} • {GetEdgeDisplayName(_settings.WebDropEdge)}",
            FontSize = 11,

            Margin = new Thickness(0, 2, 0, 0)
        };

        edgeSubtitle.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

        titleStack.Children.Add(edgeSubtitle);

        Grid.SetColumn(titleStack, 1);

        headerGrid.Children.Add(titleStack);

        var actionStack = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };

        var testBtn = new System.Windows.Controls.Button
        {
            Style = (Style)FindResource("CardIconButtonStyle"),
            Padding = new Thickness(7, 6, 7, 6),
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = System.Windows.Input.Cursors.Hand,

            ToolTip = LocalizationManager.GetString("Str_Devices_TestTooltip")
        };

        var testGrid = new Grid { Width = 15, Height = 15, VerticalAlignment = VerticalAlignment.Center };

        if (pingImg != null)
        {
            testGrid.Children.Add(new System.Windows.Controls.Image { Source = pingImg, Width = 15, Height = 15, Stretch = Stretch.Uniform });
        }

        else
        {
            testGrid.Children.Add(new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = (System.Windows.Media.Brush)FindResource("IconBrush") });
        }

        testBtn.Content = testGrid;

        testBtn.Click += (s, e) =>
        {
            e.Handled = true;

            ShowPingToast(isTesting: false, text: string.Format(LocalizationManager.GetString("Str_Web_ConnectedToast"), session.Name), isSuccess: true);

            AppLogger.Success($"Phone session ping OK: {session.Name} ({session.Ip})", "Web");
        };

        actionStack.Children.Add(testBtn);

        var deleteBtn = new System.Windows.Controls.Button
        {
            Style = (Style)FindResource("CardIconButtonStyle"),
            Padding = new Thickness(7, 6, 7, 6),
            Margin = new Thickness(0, 0, 4, 0),
            Cursor = System.Windows.Input.Cursors.Hand,

            ToolTip = LocalizationManager.GetString("Str_Devices_DeleteTooltip")
        };

        if (xImg != null)
        {
            deleteBtn.Content = new System.Windows.Controls.Image
            {
                Source = xImg,
                Width = 12,
                Height = 12,

                Stretch = Stretch.Uniform
            };
        }

        else
        {
            deleteBtn.Content = new TextBlock
            {
                Text = "✕",
                FontSize = 12,
                FontWeight = FontWeights.Bold,

                Foreground = (System.Windows.Media.Brush)FindResource("SubtleTextBrush")
            };
        }

        deleteBtn.Click += (s, e) =>
        {
            e.Handled = true;
            deleteBtn.IsEnabled = false;
            cardBorder.IsHitTestVisible = false;

            string devName = session.Name;

            var scaleTransform = new ScaleTransform(1, 1);
            cardBorder.LayoutTransform = scaleTransform;

            var translateTransform = new TranslateTransform(0, 0);
            cardBorder.RenderTransform = translateTransform;

            var collapseAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut }
            };
            var slideAnim = new DoubleAnimation(0, 35, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };
            var fadeAnim = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(180))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            collapseAnim.Completed += (_, _) =>
            {
                _seenSessionIds.Remove(session.Id);
                _web?.DisconnectSession(session.Id);

                AppLogger.Info($"Phone session disconnected: {devName} ({session.Ip})", "Devices");

                RefreshDevicesUi();
            };

            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, collapseAnim);
            translateTransform.BeginAnimation(TranslateTransform.XProperty, slideAnim);
            cardBorder.BeginAnimation(UIElement.OpacityProperty, fadeAnim);
        };

        actionStack.Children.Add(deleteBtn);

        var chevronBtn = new System.Windows.Controls.Button
        {
            Style = (Style)FindResource("CardIconButtonStyle"),
            Padding = new Thickness(7, 6, 7, 6),
            Cursor = System.Windows.Input.Cursors.Hand,

            ToolTip = LocalizationManager.GetString("Str_Devices_CollapseTooltip")
        };

        var chevronContainer = new Grid
        {
            Width = 14,
            Height = 14,

            RenderTransformOrigin = new System.Windows.Point(0.5, 0.5)
        };

        var chevronRotate = new RotateTransform(session.IsCollapsed ? 180 : 0);

        chevronContainer.RenderTransform = chevronRotate;

        if (chevronImg != null)
        {
            chevronContainer.Children.Add(new System.Windows.Controls.Image { Source = chevronImg, Width = 14, Height = 14, Stretch = Stretch.Uniform });
        }

        chevronBtn.Content = chevronContainer;

        actionStack.Children.Add(chevronBtn);

        Grid.SetColumn(actionStack, 2);

        headerGrid.Children.Add(actionStack);

        mainStack.Children.Add(headerGrid);

        var cardBody = new StackPanel
        {
            Margin = new Thickness(0, 14, 0, 0),

            Visibility = session.IsCollapsed ? Visibility.Collapsed : Visibility.Visible
        };

        var nameLabel = new TextBlock
        {
            Text = LocalizationManager.GetString("Str_Devices_DeviceName"),
            FontSize = 12,

            Margin = new Thickness(0, 0, 0, 4)
        };

        nameLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

        var nameBox = new System.Windows.Controls.TextBox
        {
            Text = session.Name,

            Margin = new Thickness(0, 0, 0, 12)
        };

        nameBox.TextChanged += (s, e) =>
        {
            string newName = nameBox.Text.Trim();

            session.Name = newName;

            titleBlock.Text = string.IsNullOrWhiteSpace(session.Name) ? (session.IsMobile ? LocalizationManager.GetString("Str_Web_PhoneSession") : LocalizationManager.GetString("Str_Web_BrowserSession")) : session.Name;

            if (!string.IsNullOrWhiteSpace(newName))
            {
                _settings.SetSavedDeviceName(session.Ip, session.OriginalDefaultName, newName);
            }
        };

        cardBody.Children.Add(nameLabel);

        cardBody.Children.Add(nameBox);

        var ipPortGrid = new Grid { Margin = new Thickness(0, 0, 0, 12) };

        ipPortGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(2, GridUnitType.Star) });

        ipPortGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(16) });

        ipPortGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var ipStack = new StackPanel();

        var ipLabel = new TextBlock
        {
            Text = LocalizationManager.GetString("Str_Devices_Ip"),
            FontSize = 12,

            Margin = new Thickness(0, 0, 0, 4)
        };

        ipLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

        var ipBox = new System.Windows.Controls.TextBox
        {
            Text = session.Ip,

            IsReadOnly = true
        };

        ipStack.Children.Add(ipLabel);

        ipStack.Children.Add(ipBox);

        Grid.SetColumn(ipStack, 0);

        ipPortGrid.Children.Add(ipStack);

        var portStack = new StackPanel();

        var portLabel = new TextBlock
        {
            Text = LocalizationManager.GetString("Str_Devices_Port"),
            FontSize = 12,

            Margin = new Thickness(0, 0, 0, 4)
        };

        portLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

        var portBox = new System.Windows.Controls.TextBox
        {
            Text = session.Port.ToString(),

            IsReadOnly = true
        };

        portStack.Children.Add(portLabel);

        portStack.Children.Add(portBox);

        Grid.SetColumn(portStack, 2);

        ipPortGrid.Children.Add(portStack);

        cardBody.Children.Add(ipPortGrid);

        var edgeLabel = new TextBlock
        {
            Text = LocalizationManager.GetString("Str_Devices_Edge"),
            FontSize = 12,

            Margin = new Thickness(0, 0, 0, 4)
        };

        edgeLabel.SetResourceReference(TextBlock.ForegroundProperty, "SubtleTextBrush");

        var edgeBox = new System.Windows.Controls.ComboBox
        {
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,

            Width = 220
        };

        edgeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.GetString("Str_Edge_Right"), Tag = "Right" });

        edgeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.GetString("Str_Edge_Left"), Tag = "Left" });

        edgeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.GetString("Str_Edge_Top"), Tag = "Top" });

        edgeBox.Items.Add(new ComboBoxItem { Content = LocalizationManager.GetString("Str_Edge_Bottom"), Tag = "Bottom" });

        foreach (ComboBoxItem itm in edgeBox.Items)
        {
            if (itm.Tag?.ToString() == _settings.WebDropEdge.ToString())
            {
                edgeBox.SelectedItem = itm;

                break;
            }
        }

        edgeBox.SelectionChanged += (s, e) =>
        {
            if (edgeBox.SelectedItem is ComboBoxItem sel &&

                Enum.TryParse<ScreenEdge>(sel.Tag?.ToString(), out var parsedEdge))
            {
                if (_settings.WebDropEdge != parsedEdge)
                {
                    _settings.WebDropEdge = parsedEdge;

                    _settings.Save();

                    RestartEdges();

                    if (WebEdgeBox != null)
                    {
                        foreach (ComboBoxItem item in WebEdgeBox.Items)
                        {
                            if (string.Equals(item.Tag?.ToString(), parsedEdge.ToString(), StringComparison.OrdinalIgnoreCase))
                            {
                                WebEdgeBox.SelectedItem = item;

                                break;
                            }
                        }
                    }
                }

                edgeSubtitle.Text = $"{session.Ip}:{session.Port} • {GetEdgeDisplayName(_settings.WebDropEdge)}";
            }
        };

        cardBody.Children.Add(edgeLabel);

        cardBody.Children.Add(edgeBox);

        mainStack.Children.Add(cardBody);

        AttachCardDragDrop(cardBorder, mainStack, files =>
        {
            if (!_running) Start();
            _transferService.SendFiles(null, files, isWebTarget: true);
            ShowTransferFooter(true);
            AppLogger.Info($"Card drag-and-drop: sending {files.Length} file(s) to web session '{session.Name}' ({session.Ip})", "Transfer");
        });

        Action toggleCollapse = () =>
        {
            session.IsCollapsed = !session.IsCollapsed;

            var animRotate = new DoubleAnimation(session.IsCollapsed ? 180 : 0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            chevronRotate.BeginAnimation(RotateTransform.AngleProperty, animRotate);

            if (session.IsCollapsed)
            {
                var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));

                fadeOut.Completed += (_, _) => cardBody.Visibility = Visibility.Collapsed;

                cardBody.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }

            else
            {
                cardBody.Visibility = Visibility.Visible;

                var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));

                cardBody.BeginAnimation(UIElement.OpacityProperty, fadeIn);
            }
        };

        chevronBtn.Click += (s, e) =>
        {
            e.Handled = true;

            toggleCollapse();
        };

        titleStack.MouseDown += (s, e) =>
        {
            toggleCollapse();
        };

        return cardBorder;
    }

    private string GetEdgeDisplayName(ScreenEdge edge) => edge switch
    {
        ScreenEdge.Right => LocalizationManager.GetString("Str_Edge_Right"),
        ScreenEdge.Left => LocalizationManager.GetString("Str_Edge_Left"),
        ScreenEdge.Top => LocalizationManager.GetString("Str_Edge_Top"),
        ScreenEdge.Bottom => LocalizationManager.GetString("Str_Edge_Bottom"),

        _ => edge.ToString()
    };

    private void OnAddDeviceClick(object sender, RoutedEventArgs e)
    {
        var devicesSnapshot = _settings.GetDevicesSnapshot();

        if (devicesSnapshot.Count >= 4) return;

        var usedEdges = devicesSnapshot.Select(d => d.DropEdge).ToHashSet();

        if (_settings.WebServerEnabled)
        {
            usedEdges.Add(_settings.WebDropEdge);
        }

        var allEdges = new[] { ScreenEdge.Right, ScreenEdge.Left, ScreenEdge.Top, ScreenEdge.Bottom };

        var freeEdge = allEdges.FirstOrDefault(e => !usedEdges.Contains(e));

        int nextNum = devicesSnapshot.Count + 1;

        var newDev = new DeviceItem
        {
            Name = LocalizationManager.GetString("Str_Devices_DefaultName", nextNum),
            Ip = "192.168.1." + (100 + nextNum),
            Port = 11000,
            DropEdge = freeEdge,

            IsCollapsed = false
        };

        _justAddedDeviceId = newDev.Id;
        _settings.AddKnownDevice(newDev);

        AppLogger.Info($"Device added: {newDev.Name} ({newDev.Ip}:{newDev.Port})", "Devices");

        RefreshDevicesUi();

        RestartEdges();

        UpdateIpDisplay();
    }

    private void OnCopyThisPcIpClick(object sender, RoutedEventArgs e)
    {
        try
        {
            if (DevicesThisPcIpText != null && !string.IsNullOrEmpty(DevicesThisPcIpText.Text))
            {
                System.Windows.Clipboard.SetText(DevicesThisPcIpText.Text);

                ShowToast(isTesting: false, isSuccess: true, text: LocalizationManager.GetString("Str_Toast_IpCopied"));

                SetStatus(LocalizationManager.GetString("Str_Toast_IpCopied"));
            }
        }

        catch { }
    }

    private void OnDevModeChanged(object sender, RoutedEventArgs e)
    {
        if (_settings == null || DevModeBox == null) return;

        _settings.LoopbackMode = DevModeBox.IsChecked == true;

        _settings.Save();

        UpdateIpDisplay();

        RestartEdges();
    }

    private void UpdateDeviceOnlineVisual(DeviceItem dev)
    {
        if (DevicesCardsContainer == null) return;

        string tag = $"statusDot_{dev.Id}";

        var dot = FindVisualChildByTag<System.Windows.Shapes.Ellipse>(DevicesCardsContainer, tag);

        if (dot != null)
        {
            dot.Fill = dev.IsOnline

                ? new SolidColorBrush(System.Windows.Media.Color.FromRgb(16, 185, 129))

                : new SolidColorBrush(System.Windows.Media.Color.FromArgb(120, 150, 150, 150));

            dot.ToolTip = dev.IsOnline

                ? LocalizationManager.GetString("Str_Devices_Online")

                : LocalizationManager.GetString("Str_Devices_Offline");

            return;
        }

        RefreshDevicesUi();
    }

    private static T? FindVisualChildByTag<T>(DependencyObject parent, string tag) where T : FrameworkElement
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T fe && fe.Tag is string s && s == tag)
            {
                return fe;
            }

            var sub = FindVisualChildByTag<T>(child, tag);

            if (sub != null) return sub;
        }

        return null;
    }

    private void InitDiscoveryService()
    {
        if (_settings.AutoDiscoveryEnabled)
        {
            _discovery = new DeviceDiscoveryService(_settings);

            _discovery.NewDeviceDiscovered += dev => Dispatcher.Invoke(() =>
            {
                RefreshDevicesUi();

                RestartEdges();

                UpdateIpDisplay();

                ShowToast(false, true, string.Format(LocalizationManager.GetString("Str_Devices_AutoDiscovered"), dev.Name, dev.Ip));

                AppLogger.Success($"Auto-discovered device: {dev.Name} ({dev.Ip}:{dev.Port})", "Discovery");
            });

            _discovery.DeviceIpUpdated += dev => Dispatcher.Invoke(() =>
            {
                RefreshDevicesUi();

                RestartEdges();

                UpdateIpDisplay();

                ShowToast(false, true, string.Format(LocalizationManager.GetString("Str_Devices_IpUpdated"), dev.Name, dev.Ip));

                AppLogger.Info($"Device IP updated via discovery: {dev.Name} -> {dev.Ip}", "Discovery");
            });

            _discovery.DeviceOnlineStatusChanged += dev => Dispatcher.Invoke(() => UpdateDeviceOnlineVisual(dev));

            _discovery.Start();
        }
    }

    private void OnScanDevicesClick(object sender, RoutedEventArgs e)
    {
        if (DevicesScanRotate != null)
        {
            var anim = new DoubleAnimation(0, 360, TimeSpan.FromMilliseconds(450))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            DevicesScanRotate.BeginAnimation(RotateTransform.AngleProperty, anim);
        }

        if (_discovery != null)
        {
            _discovery.BroadcastQuery();
        }

        else
        {
            InitDiscoveryService();

            _discovery?.BroadcastQuery();
        }

        Task.Run(async () =>
        {
            var devices = _settings.GetDevicesSnapshot();

            foreach (var d in devices)
            {
                if (string.IsNullOrWhiteSpace(d.Ip)) continue;

                try
                {
                    using var client = new System.Net.Sockets.TcpClient();
                    using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(800));

                    await client.ConnectAsync(d.Ip, d.Port > 0 ? d.Port : 11000, cts.Token);

                    d.IsOnline = true;

                    d.LastSeen = DateTime.UtcNow;

                    Dispatcher.Invoke(() => UpdateDeviceOnlineVisual(d));
                }

                catch { }
            }
        });

        ShowToast(false, true, LocalizationManager.GetString("Str_Devices_Scanning"));
    }

    private void AttachCardDragDrop(Border cardBorder, StackPanel mainStack, Action<string[]> onFilesDropped)
    {
        cardBorder.AllowDrop = true;
        cardBorder.BorderThickness = new Thickness(2);
        cardBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
        cardBorder.Padding = new Thickness(0);

        var cardGrid = new Grid();
        mainStack.Margin = new Thickness(16);
        cardGrid.Children.Add(mainStack);

        var dragOverlay = new Grid
        {
            Visibility = Visibility.Collapsed,
            IsHitTestVisible = false
        };

        var accentOverlay = new Border
        {
            CornerRadius = new CornerRadius(6),
            Opacity = 0.18
        };
        accentOverlay.SetResourceReference(Border.BackgroundProperty, "SystemAccentBrush");
        dragOverlay.Children.Add(accentOverlay);

        var downloadIcon = new System.Windows.Shapes.Path
        {
            Data = Geometry.Parse("M12,3 L12,17 M6,11 L12,17 L18,11 M5,21 L19,21"),
            StrokeThickness = 2.5,
            StrokeStartLineCap = PenLineCap.Round,
            StrokeEndLineCap = PenLineCap.Round,
            StrokeLineJoin = PenLineJoin.Round,
            Width = 32,
            Height = 32,
            Stretch = Stretch.Uniform,
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            VerticalAlignment = System.Windows.VerticalAlignment.Center,
            Effect = new DropShadowEffect
            {
                BlurRadius = 8,
                ShadowDepth = 1,
                Opacity = 0.35,
                Color = Colors.Black
            }
        };
        downloadIcon.SetResourceReference(System.Windows.Shapes.Path.StrokeProperty, "SystemAccentBrush");
        dragOverlay.Children.Add(downloadIcon);

        cardGrid.Children.Add(dragOverlay);
        cardBorder.Child = cardGrid;

        int dragCounter = 0;

        void ShowDragVisuals()
        {
            cardBorder.SetResourceReference(Border.BorderBrushProperty, "SystemAccentBrush");
            dragOverlay.Visibility = Visibility.Visible;
        }

        void HideDragVisuals()
        {
            dragCounter = 0;
            cardBorder.BorderBrush = System.Windows.Media.Brushes.Transparent;
            dragOverlay.Visibility = Visibility.Collapsed;
        }

        cardBorder.DragEnter += (s, e) =>
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                dragCounter++;
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = true;
                ShowDragVisuals();
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
        };

        cardBorder.DragOver += (s, e) =>
        {
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                e.Effects = System.Windows.DragDropEffects.Copy;
                e.Handled = true;
            }
            else
            {
                e.Effects = System.Windows.DragDropEffects.None;
            }
        };

        cardBorder.DragLeave += (s, e) =>
        {
            dragCounter--;
            var pos = e.GetPosition(cardBorder);
            if (dragCounter <= 0 || pos.X <= 0 || pos.Y <= 0 || pos.X >= cardBorder.ActualWidth || pos.Y >= cardBorder.ActualHeight)
            {
                HideDragVisuals();
            }
        };

        cardBorder.Drop += (s, e) =>
        {
            HideDragVisuals();
            if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                if (e.Data.GetData(System.Windows.DataFormats.FileDrop) is string[] files && files.Length > 0)
                {
                    e.Handled = true;
                    onFilesDropped(files);
                }
            }
        };
    }
}

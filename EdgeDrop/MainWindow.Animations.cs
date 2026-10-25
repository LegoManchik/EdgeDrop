using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using ComboBox = System.Windows.Controls.ComboBox;
using Point = System.Windows.Point;

namespace EdgeDrop;

public static class ScrollAnimationBehavior
{
    public static readonly DependencyProperty AnimatedVerticalOffsetProperty =

        DependencyProperty.RegisterAttached(

            "AnimatedVerticalOffset",
            typeof(double),
            typeof(ScrollAnimationBehavior),

            new PropertyMetadata(0.0, OnAnimatedVerticalOffsetChanged));

    private static void OnAnimatedVerticalOffsetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is ScrollViewer sv)
        {
            sv.ScrollToVerticalOffset((double)e.NewValue);
        }
    }

    private static readonly Dictionary<ScrollViewer, double> _targetOffsets = new();

    public static void AnimateScroll(ScrollViewer sv, double mouseWheelDelta)
    {
        double target = _targetOffsets.TryGetValue(sv, out var t) ? t : sv.VerticalOffset;

        if (Math.Abs(target - sv.VerticalOffset) > 250) target = sv.VerticalOffset;

        double step = -mouseWheelDelta * 0.85;

        target = Math.Clamp(target + step, 0, sv.ScrollableHeight);

        _targetOffsets[sv] = target;

        var anim = new DoubleAnimation
        {
            To = target,
            Duration = TimeSpan.FromMilliseconds(220),

            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        sv.BeginAnimation(AnimatedVerticalOffsetProperty, anim);
    }
}

public partial class MainWindow : Window
{
    private static ComboBox? _currentOpenComboBox;

    static MainWindow()
    {
        EventManager.RegisterClassHandler(typeof(ComboBox), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            if (s is ComboBox cbo)
            {
                cbo.DropDownOpened += (sender, _) => _currentOpenComboBox = sender as ComboBox;
                cbo.DropDownClosed += (sender, _) => { if (_currentOpenComboBox == sender) _currentOpenComboBox = null; };
            }
        }));
    }

    private static bool IsMouseOverAnyOpenDropDown(DependencyObject? root = null)
    {
        var openCbo = _currentOpenComboBox;

        if (openCbo == null || !openCbo.IsDropDownOpen) return false;

        if (openCbo.IsMouseOver) return true;

        if (openCbo.Template?.FindName("PART_Popup", openCbo) is System.Windows.Controls.Primitives.Popup popup && popup.IsOpen)
        {
            if (popup.IsMouseOver) return true;

            if (popup.Child != null && popup.Child.IsMouseOver) return true;
        }

        return false;
    }

    private static void CloseOpenComboBoxes(DependencyObject? root = null)
    {
        if (_currentOpenComboBox != null)
        {
            _currentOpenComboBox.IsDropDownOpen = false;

            _currentOpenComboBox = null;
        }
    }

    private void ToggleWebCardCollapse()
    {
        _settings.WebCardCollapsed = !_settings.WebCardCollapsed;

        _settings.Save();

        ApplyWebCardCollapsedState(animate: true);
    }

    private void ApplyWebCardCollapsedState(bool animate)
    {
        bool collapsed = _settings.WebCardCollapsed;

        double targetAngle = collapsed ? 180 : 0;

        if (WebChevronRotate != null)
        {
            if (animate)
            {
                var animRotate = new DoubleAnimation(targetAngle, TimeSpan.FromMilliseconds(220))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                WebChevronRotate.BeginAnimation(RotateTransform.AngleProperty, animRotate);
            }

            else
            {
                WebChevronRotate.Angle = targetAngle;
            }
        }

        if (WebCardBody != null)
        {
            if (animate)
            {
                if (collapsed)
                {
                    var fadeOut = new DoubleAnimation(0, TimeSpan.FromMilliseconds(120));

                    fadeOut.Completed += (_, _) => WebCardBody.Visibility = Visibility.Collapsed;

                    WebCardBody.BeginAnimation(UIElement.OpacityProperty, fadeOut);
                }

                else
                {
                    WebCardBody.Visibility = Visibility.Visible;

                    var fadeIn = new DoubleAnimation(1, TimeSpan.FromMilliseconds(200));

                    WebCardBody.BeginAnimation(UIElement.OpacityProperty, fadeIn);
                }
            }

            else
            {
                WebCardBody.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;

                WebCardBody.Opacity = collapsed ? 0 : 1;
            }
        }
    }

    private void OnSmoothScrollViewerMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer sv) return;

        if (IsMouseOverAnyOpenDropDown(sv))
        {
            return;
        }

        var hit = Mouse.DirectlyOver as DependencyObject ?? e.OriginalSource as DependencyObject;

        if (hit != null && IsInsideComboBoxOrDropDown(hit, sv))
        {
            return;
        }

        CloseOpenComboBoxes(sv);

        ScrollAnimationBehavior.AnimateScroll(sv, e.Delta);

        e.Handled = true;
    }

    private void OnScrollViewerScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.OriginalSource != sender) return;

        if (e.VerticalChange == 0) return;

        var openCbo = _currentOpenComboBox;

        if (openCbo == null || !openCbo.IsDropDownOpen) return;

        if (openCbo.IsMouseOver) return;

        if (openCbo.Template?.FindName("PART_Popup", openCbo) is System.Windows.Controls.Primitives.Popup popup && popup.IsOpen)
        {
            if (popup.IsMouseOver) return;

            if (popup.Child != null && popup.Child.IsMouseOver) return;
        }

        openCbo.IsDropDownOpen = false;

        _currentOpenComboBox = null;
    }

    private static bool IsInsideComboBoxOrDropDown(DependencyObject element, ScrollViewer pageScrollViewer)
    {
        DependencyObject? current = element;

        while (current != null && current != pageScrollViewer)
        {
            if (current is ComboBox or ComboBoxItem) return true;

            var typeName = current.GetType().Name;

            if (typeName.Contains("Popup") || typeName.Contains("DropDown")) return true;

            if (current is FrameworkElement fe && (fe.Name == "DropDownScrollViewer" || fe.Name == "PART_Popup" || fe.Name == "dropDownBorder"))
            {
                return true;
            }

            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
            {
                current = VisualTreeHelper.GetParent(current);
            }
            else if (current is FrameworkContentElement fce)
            {
                current = fce.Parent;
            }
            else
            {
                break;
            }
        }

        return false;
    }

    private void OnListBoxSmoothScroll(object sender, MouseWheelEventArgs e)
    {
        if (sender is DependencyObject depObj)
        {
            var sv = FindVisualChild<ScrollViewer>(depObj);

            if (sv != null)
            {
                ScrollAnimationBehavior.AnimateScroll(sv, e.Delta);

                e.Handled = true;
            }
        }
    }

    public void AnimateModalOpen(UIElement overlay, FrameworkElement? card)
    {
        overlay.Visibility = Visibility.Visible;

        var fadeBackdrop = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(220))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };

        overlay.BeginAnimation(UIElement.OpacityProperty, fadeBackdrop);

        if (card != null)
        {
            var fadeCard = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            card.BeginAnimation(UIElement.OpacityProperty, fadeCard);

            var scaleX = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
            };

            var scaleY = new DoubleAnimation(0.88, 1.0, TimeSpan.FromMilliseconds(260))
            {
                EasingFunction = new BackEase { Amplitude = 0.35, EasingMode = EasingMode.EaseOut }
            };

            var scaleTransform = card.RenderTransform as ScaleTransform ?? new ScaleTransform(1, 1);

            card.RenderTransformOrigin = new Point(0.5, 0.5);

            card.RenderTransform = scaleTransform;

            scaleTransform.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);

            scaleTransform.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
        }
    }

    public void AnimateModalClose(UIElement overlay, FrameworkElement? card, Action? onClosed = null)
    {
        if (overlay.Visibility != Visibility.Visible)
        {
            onClosed?.Invoke();

            return;
        }

        var fadeBackdrop = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(160))
        {
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };

        if (card != null)
        {
            var fadeCard = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(140))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            card.BeginAnimation(UIElement.OpacityProperty, fadeCard);

            var scaleX = new DoubleAnimation(1.0, 0.90, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            var scaleY = new DoubleAnimation(1.0, 0.90, TimeSpan.FromMilliseconds(150))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
            };

            if (card.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, scaleX);

                st.BeginAnimation(ScaleTransform.ScaleYProperty, scaleY);
            }
        }

        fadeBackdrop.Completed += (_, _) =>
        {
            overlay.Visibility = Visibility.Collapsed;

            overlay.BeginAnimation(UIElement.OpacityProperty, null);

            overlay.Opacity = 1.0;

            if (card != null)
            {
                card.BeginAnimation(UIElement.OpacityProperty, null);

                card.Opacity = 1.0;

                if (card.RenderTransform is ScaleTransform st)
                {
                    st.BeginAnimation(ScaleTransform.ScaleXProperty, null);

                    st.BeginAnimation(ScaleTransform.ScaleYProperty, null);

                    st.ScaleX = 1;

                    st.ScaleY = 1;
                }
            }

            onClosed?.Invoke();
        };

        overlay.BeginAnimation(UIElement.OpacityProperty, fadeBackdrop);
    }

    public void AnimateHistoryFilterChange(System.Windows.Controls.Button? clickedBtn)
    {
        if (clickedBtn != null)
        {
            var scale = new ScaleTransform(1, 1);

            clickedBtn.RenderTransformOrigin = new Point(0.5, 0.5);

            clickedBtn.RenderTransform = scale;

            var btnAnim = new DoubleAnimation(0.92, 1.0, TimeSpan.FromMilliseconds(200))
            {
                EasingFunction = new BackEase { Amplitude = 0.4, EasingMode = EasingMode.EaseOut }
            };

            scale.BeginAnimation(ScaleTransform.ScaleXProperty, btnAnim);

            scale.BeginAnimation(ScaleTransform.ScaleYProperty, btnAnim);
        }

        if (HistoryListBox != null)
        {
            var fadeAnim = new DoubleAnimation(0.2, 1.0, TimeSpan.FromMilliseconds(220))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            HistoryListBox.BeginAnimation(OpacityProperty, fadeAnim);

            if (HistoryListTranslate != null)
            {
                var slideAnim = new DoubleAnimation(10, 0, TimeSpan.FromMilliseconds(240))
                {
                    EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
                };

                HistoryListTranslate.BeginAnimation(TranslateTransform.YProperty, slideAnim);
            }
        }
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T typedChild) return typedChild;

            var desc = FindVisualChild<T>(child);

            if (desc != null) return desc;
        }

        return null;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        int count = VisualTreeHelper.GetChildrenCount(parent);

        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);

            if (child is T typedChild) yield return typedChild;

            foreach (var desc in FindVisualChildren<T>(child))
            {
                yield return desc;
            }
        }
    }
}

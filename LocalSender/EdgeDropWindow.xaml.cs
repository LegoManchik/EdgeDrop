using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;

namespace EdgeDrop;

public partial class EdgeDropWindow : Window
{
    private readonly Settings _settings;
    private readonly TransferService _transferService;
    public DeviceItem? Device { get; }
    public bool IsWebTarget { get; }
    private readonly ScreenEdge? _customEdge;
    public ScreenEdge DropEdge => _customEdge ?? (Device?.DropEdge ?? _settings.DropEdge);

    public event Action<string>? StatusChanged
    {
        add => _transferService.StatusChanged += value;
        remove => _transferService.StatusChanged -= value;
    }
    public event Action<string, string>? FileSent
    {
        add => _transferService.FileSent += value;
        remove => _transferService.FileSent -= value;
    }
    public event Action<string, TransferProgressReport>? FileProgressChanged
    {
        add => _transferService.TransferProgress += value;
        remove => _transferService.TransferProgress -= value;
    }
    public event Action<string>? TransferStarted
    {
        add => _transferService.TransferStarted += value;
        remove => _transferService.TransferStarted -= value;
    }
    public event Action? TransferCompleted
    {
        add => _transferService.TransferCompleted += value;
        remove => _transferService.TransferCompleted -= value;
    }
    public event Action<string, string>? FileSendError
    {
        add => _transferService.TransferError += value;
        remove => _transferService.TransferError -= value;
    }

    private bool _isHighlighted;
    private bool _isStripRevealed;
    private static readonly List<EdgeDropWindow> _activeEdgeWindows = new();
    private static DispatcherTimer? _sharedDwellTimer;

    public ObservableCollection<QueueItem> TransferQueue => _transferService.Queue;

    private static readonly System.Windows.Media.Brush IdleBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(1, 255, 255, 255));
    private static readonly System.Windows.Media.Brush HighlightBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 0, 120, 212));
    private static readonly System.Windows.Media.Brush WebHighlightBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 16, 185, 129));
    private System.Windows.Media.Brush _currentHighlightBrush = HighlightBrush;
    private System.Windows.Media.Brush _currentRevealBrush = IdleBrush;

    public EdgeDropWindow(Settings settings, TransferService transferService, DeviceItem? device = null, bool isWebTarget = false, ScreenEdge? customEdge = null)
    {
        InitializeComponent();
        _settings = settings;
        _transferService = transferService;
        Device = device;
        IsWebTarget = isWebTarget;
        _customEdge = customEdge;

        AllowDrop = true;
        Drop += OnDrop;
        DragOver += OnDragOver;
        DragEnter += OnDragEnter;
        DragLeave += OnDragLeave;

        if (IsWebTarget)
        {
            ToolTip = "Phone (Web)";
            if (GlowEffect != null)
            {
                GlowEffect.Color = System.Windows.Media.Color.FromRgb(16, 185, 129);
            }
        }
        else if (Device != null)
        {
            ToolTip = $"{Device.Name} ({Device.Ip})";
        }

        Visibility = Visibility.Collapsed;
        Opacity = 0.0;

        RegisterEdgeWindow(this);

        UpdateThemeColors(ThemeManager.AccentColor);
        ThemeManager.AccentColorChanged += OnAccentColorChanged;
        Closing += (_, _) =>
        {
            UnregisterEdgeWindow(this);
            ThemeManager.AccentColorChanged -= OnAccentColorChanged;
        };
        UpdatePosition();
    }

    private void OnAccentColorChanged(System.Windows.Media.Color accent)
    {
        Dispatcher.Invoke(() => UpdateThemeColors(accent));
    }

    private void UpdateThemeColors(System.Windows.Media.Color accent)
    {
        if (IsWebTarget)
        {
            _currentHighlightBrush = WebHighlightBrush;
            _currentRevealBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(85, 16, 185, 129));
            if (GlowEffect != null) GlowEffect.Color = System.Windows.Media.Color.FromRgb(16, 185, 129);
        }
        else
        {
            _currentHighlightBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(230, accent.R, accent.G, accent.B));
            _currentRevealBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(85, accent.R, accent.G, accent.B));
            if (GlowEffect != null) GlowEffect.Color = accent;
        }

        if (_isHighlighted)
        {
            DropBorder.Background = _currentHighlightBrush;
            if (GlowEffect != null) GlowEffect.Opacity = 0.92;
        }
        else if (_isStripRevealed)
        {
            DropBorder.Background = _currentRevealBrush;
            if (GlowEffect != null) GlowEffect.Opacity = 0.45;
        }
        else
        {
            DropBorder.Background = IdleBrush;
            if (GlowEffect != null) GlowEffect.Opacity = 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_APPWINDOW = 0x00040000;
    private const int VK_LBUTTON = 0x01;

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    [DllImport("user32.dll")]
    private static extern bool GetCursorPos(out POINT lpPoint);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr GetWindowLongPtr64(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW", SetLastError = true)]
    private static extern int GetWindowLong32(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)]
    private static extern IntPtr SetWindowLongPtr64(IntPtr hWnd, int nIndex, IntPtr dwNewLong);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongW", SetLastError = true)]
    private static extern int SetWindowLong32(IntPtr hWnd, int nIndex, int dwNewLong);

    private static IntPtr GetWindowLongPtr(IntPtr hWnd, int nIndex)
    {
        return IntPtr.Size == 8 ? GetWindowLongPtr64(hWnd, nIndex) : (IntPtr)GetWindowLong32(hWnd, nIndex);
    }

    private static IntPtr SetWindowLongPtr(IntPtr hWnd, int nIndex, IntPtr dwNewLong)
    {
        return IntPtr.Size == 8 ? SetWindowLongPtr64(hWnd, nIndex, dwNewLong) : (IntPtr)SetWindowLong32(hWnd, nIndex, (int)dwNewLong);
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        try
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd != IntPtr.Zero)
            {
                long exStyle = GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64();
                exStyle = (exStyle | WS_EX_TOOLWINDOW) & ~WS_EX_APPWINDOW;
                SetWindowLongPtr(hwnd, GWL_EXSTYLE, (IntPtr)exStyle);
            }
        }
        catch { }
    }

    [ThreadStatic]
    private static StringBuilder? _cachedClassNameSb;

    private static bool IsForegroundFullscreen()
    {
        try
        {
            IntPtr fg = GetForegroundWindow();
            if (fg == IntPtr.Zero) return false;

            if (GetWindowRect(fg, out RECT rect))
            {
                var screen = System.Windows.Forms.Screen.FromHandle(fg);
                if (rect.Left <= screen.Bounds.Left && rect.Top <= screen.Bounds.Top &&
                    rect.Right >= screen.Bounds.Right && rect.Bottom >= screen.Bounds.Bottom)
                {
                    _cachedClassNameSb ??= new StringBuilder(256);
                    _cachedClassNameSb.Clear();
                    GetClassName(fg, _cachedClassNameSb, 256);
                    string cls = _cachedClassNameSb.ToString();
                    if (cls != "Progman" && cls != "WorkerW" && cls != "Shell_TrayWnd")
                    {
                        return true;
                    }
                }
            }
        }
        catch { }
        return false;
    }

    private bool IsCursorNearEdge(POINT pt, System.Windows.Forms.Screen screen)
    {
        var wa = screen.WorkingArea;
        const int threshold = 36;

        return DropEdge switch
        {
            ScreenEdge.Left => pt.X >= wa.Left && pt.X <= wa.Left + threshold && pt.Y >= wa.Top && pt.Y <= wa.Bottom,
            ScreenEdge.Right => pt.X <= wa.Right && pt.X >= wa.Right - threshold && pt.Y >= wa.Top && pt.Y <= wa.Bottom,
            ScreenEdge.Top => pt.Y >= wa.Top && pt.Y <= wa.Top + threshold && pt.X >= wa.Left && pt.X <= wa.Right,
            ScreenEdge.Bottom => pt.Y <= wa.Bottom && pt.Y >= wa.Bottom - threshold && pt.X >= wa.Left && pt.X <= wa.Right,
            _ => false
        };
    }

    private static void RegisterEdgeWindow(EdgeDropWindow window)
    {
        if (!_activeEdgeWindows.Contains(window))
        {
            _activeEdgeWindows.Add(window);
        }
        if (_sharedDwellTimer == null)
        {
            _sharedDwellTimer = new DispatcherTimer(DispatcherPriority.Background)
            {
                Interval = TimeSpan.FromMilliseconds(40)
            };
            _sharedDwellTimer.Tick += OnSharedDwellTimerTick;
            _sharedDwellTimer.Start();
        }
    }

    private static void UnregisterEdgeWindow(EdgeDropWindow window)
    {
        _activeEdgeWindows.Remove(window);
        if (_activeEdgeWindows.Count == 0 && _sharedDwellTimer != null)
        {
            _sharedDwellTimer.Stop();
            _sharedDwellTimer = null;
        }
    }

    private static void OnSharedDwellTimerTick(object? sender, EventArgs e)
    {
        if (_activeEdgeWindows.Count == 0) return;

        bool isMouseDown = (GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0;
        if (!isMouseDown)
        {
            for (int i = 0; i < _activeEdgeWindows.Count; i++)
            {
                var w = _activeEdgeWindows[i];
                if (!w._isHighlighted) w.HideStrip();
            }
            return;
        }

        if (!GetCursorPos(out POINT pt)) return;

        var screen = System.Windows.Forms.Screen.FromPoint(new System.Drawing.Point(pt.X, pt.Y));
        if (screen == null) return;

        bool anyNear = false;
        for (int i = 0; i < _activeEdgeWindows.Count; i++)
        {
            if (_activeEdgeWindows[i].IsCursorNearEdge(pt, screen))
            {
                anyNear = true;
                break;
            }
        }

        if (!anyNear)
        {
            for (int i = 0; i < _activeEdgeWindows.Count; i++)
            {
                var w = _activeEdgeWindows[i];
                if (!w._isHighlighted) w.HideStrip();
            }
            return;
        }

        if (IsForegroundFullscreen())
        {
            for (int i = 0; i < _activeEdgeWindows.Count; i++)
            {
                _activeEdgeWindows[i].HideStrip();
            }
            return;
        }

        for (int i = 0; i < _activeEdgeWindows.Count; i++)
        {
            var w = _activeEdgeWindows[i];
            if (w.IsCursorNearEdge(pt, screen))
            {
                w.RevealStrip(screen);
            }
            else if (!w._isHighlighted)
            {
                w.HideStrip();
            }
        }
    }

    private void RevealStrip(System.Windows.Forms.Screen? screen = null)
    {
        if (_isStripRevealed) return;
        _isStripRevealed = true;
        UpdatePosition(screen);
        if (!_isHighlighted)
        {
            DropBorder.Background = _currentRevealBrush;
            if (GlowEffect != null) GlowEffect.Opacity = 0.45;
        }
        Visibility = Visibility.Visible;
        Opacity = 1.0;
    }

    private void HideStrip()
    {
        if (!_isStripRevealed && Visibility == Visibility.Collapsed) return;
        if (_isHighlighted) return;

        _isStripRevealed = false;
        _isHighlighted = false;
        DropBorder.Background = IdleBrush;
        if (GlowEffect != null) GlowEffect.Opacity = 0;
        Opacity = 0.0;
        Visibility = Visibility.Collapsed;
    }

    public void UpdatePosition(System.Windows.Forms.Screen? targetScreen = null)
    {
        var screen = targetScreen ?? System.Windows.Forms.Screen.PrimaryScreen ?? System.Windows.Forms.Screen.AllScreens.FirstOrDefault();
        if (screen == null) return;

        var wa = screen.WorkingArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double scaleX = dpi.DpiScaleX > 0 ? dpi.DpiScaleX : 1.0;
        double scaleY = dpi.DpiScaleY > 0 ? dpi.DpiScaleY : 1.0;

        double waLeft = wa.Left / scaleX;
        double waTop = wa.Top / scaleY;
        double waWidth = wa.Width / scaleX;
        double waHeight = wa.Height / scaleY;

        const double thickness = 14.0;

        switch (DropEdge)
        {
            case ScreenEdge.Left:
                Width = thickness;
                Height = waHeight;
                Left = waLeft;
                Top = waTop;
                break;
            case ScreenEdge.Top:
                Width = waWidth;
                Height = thickness;
                Left = waLeft;
                Top = waTop;
                break;
            case ScreenEdge.Bottom:
                Width = waWidth;
                Height = thickness;
                Left = waLeft;
                Top = waTop + waHeight - thickness;
                break;
            case ScreenEdge.Right:
            default:
                Width = thickness;
                Height = waHeight;
                Left = waLeft + waWidth - thickness;
                Top = waTop;
                break;
        }
    }

    private void SetHighlight(bool enable)
    {
        if (_isHighlighted == enable) return;
        _isHighlighted = enable;

        if (enable)
        {
            DropBorder.Background = _currentHighlightBrush;
            if (GlowEffect != null) GlowEffect.Opacity = 0.92;
        }
        else
        {
            DropBorder.Background = _isStripRevealed ? _currentRevealBrush : IdleBrush;
            if (GlowEffect != null) GlowEffect.Opacity = _isStripRevealed ? 0.45 : 0;
        }
    }

    private void OnDragEnter(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            SetHighlight(true);
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragOver(object sender, System.Windows.DragEventArgs e)
    {
        if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
        {
            if (!_isHighlighted) SetHighlight(true);
            e.Effects = System.Windows.DragDropEffects.Copy;
        }
        else
        {
            e.Effects = System.Windows.DragDropEffects.None;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, System.Windows.DragEventArgs e)
    {
        SetHighlight(false);
        HideStrip();
    }

    public void EnqueueFiles(IEnumerable<string> files)
    {
        _transferService.SendFiles(Device, files, IsWebTarget);
    }

    private void OnDrop(object sender, System.Windows.DragEventArgs e)
    {
        SetHighlight(false);
        HideStrip();

        if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;

        var files = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!;
        _transferService.SendFiles(Device, files, IsWebTarget);
    }
}

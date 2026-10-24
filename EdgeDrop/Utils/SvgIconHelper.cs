using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Pen = System.Windows.Media.Pen;
using SharpVectors.Converters;
using SharpVectors.Renderers.Wpf;

namespace EdgeDrop;

public static class SvgIconHelper
{
    public static string IconsDirectory => Path.Combine(AppContext.BaseDirectory, "icons");
    private static readonly ConcurrentDictionary<string, DrawingImage> _cache = new(StringComparer.OrdinalIgnoreCase);

    static SvgIconHelper()
    {
        ThemeManager.AccentColorChanged += _ => InvalidateCache();
        ThemeManager.BackgroundColorChanged += _ => InvalidateCache();
    }

    public static void InvalidateCache()
    {
        _cache.Clear();
    }

    public static DrawingImage? LoadSvgIcon(string iconFileName, Color? tintColor = null)
    {
        if (string.IsNullOrWhiteSpace(iconFileName)) return null;

        if (!iconFileName.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
        {
            iconFileName += ".svg";
        }

        string cacheKey = $"{iconFileName}_{tintColor?.ToString() ?? "null"}";

        if (_cache.TryGetValue(cacheKey, out var cached))
        {
            return cached;
        }

        try
        {
            var image = LoadSvgInternal(iconFileName, tintColor);

            if (image != null)
            {
                _cache[cacheKey] = image;

                return image;
            }
        }

        catch

        { }

        return null;
    }

    private static DrawingImage? LoadSvgInternal(string iconFileName, Color? tintColor)
    {
        try
        {
            string fullPath = Path.Combine(IconsDirectory, iconFileName);

            if (!File.Exists(fullPath))
            {
                fullPath = Path.Combine(Directory.GetCurrentDirectory(), "icons", iconFileName);

                if (!File.Exists(fullPath))
                {
                    return null;
                }
            }

            string svgText = File.ReadAllText(fullPath);

            if (tintColor.HasValue)
            {
                string hex = $"#{tintColor.Value.R:X2}{tintColor.Value.G:X2}{tintColor.Value.B:X2}";

                svgText = svgText.Replace("currentColor", hex);
            }

            var settings = new WpfDrawingSettings
            {
                IncludeRuntime = false,

                TextAsGeometry = true
            };

            var reader = new FileSvgReader(settings);

            DrawingGroup? drawing;

            using (var ms = new MemoryStream(Encoding.UTF8.GetBytes(svgText)))
            {
                drawing = reader.Read(ms);
            }

            if (drawing != null)
            {
                if (tintColor.HasValue)
                {
                    if (drawing.IsFrozen)
                    {
                        drawing = drawing.Clone();
                    }

                    var tintBrush = new SolidColorBrush(tintColor.Value);

                    tintBrush.Freeze();

                    TintDrawing(drawing, tintBrush);
                }

                drawing.Freeze();

                var image = new DrawingImage(drawing);

                image.Freeze();

                return image;
            }
        }

        catch

        { }

        return null;
    }

    private static void TintDrawing(Drawing drawing, Brush brush)
    {
        if (drawing is DrawingGroup group)
        {
            foreach (var child in group.Children)
            {
                TintDrawing(child, brush);
            }
        }

        else if (drawing is GeometryDrawing geom)
        {
            if (geom.Pen != null)
            {
                geom.Pen = new Pen(brush, geom.Pen.Thickness)
                {
                    StartLineCap = geom.Pen.StartLineCap,
                    EndLineCap = geom.Pen.EndLineCap,
                    DashCap = geom.Pen.DashCap,
                    LineJoin = geom.Pen.LineJoin,

                    MiterLimit = geom.Pen.MiterLimit
                };
            }

            if (geom.Brush != null && !Equals(geom.Brush, Brushes.Transparent))
            {
                geom.Brush = brush;
            }
        }
    }
}

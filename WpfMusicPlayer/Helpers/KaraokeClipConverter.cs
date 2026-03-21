using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace WpfMusicPlayer.Helpers;

/// <summary>
/// Converts karaoke progress into a Clip <see cref="Geometry"/> that reveals the
/// already-played portion of the lyric text, including sub-character partial fill.
/// <para/>
/// Expected bindings (in order):
/// <list type="number">
///   <item>Progress  (double, 0–1)</item>
///   <item>Text      (string)</item>
///   <item>ActualWidth  (double – from the overlay TextBlock itself)</item>
///   <item>FontSize     (double – effective value from the overlay TextBlock)</item>
///   <item>FontWeight   (FontWeight – effective value from the overlay TextBlock)</item>
///   <item>Self         (Visual – the overlay TextBlock, used for DPI)</item>
/// </list>
/// </summary>
public sealed class KaraokeClipConverter : IMultiValueConverter
{
    // Cache key: avoid redundant geometry rebuilds when only ActualWidth triggers re-evaluation
    // but progress/text/font haven't actually changed.
    private double _lastProgress;
    private string? _lastText;
    private double _lastFontSize;
    private FontWeight _lastFontWeight;
    private double _lastActualWidth;
    private Geometry? _cachedResult;

    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture)
    {
        if (values.Length < 6
            || values[0] is not double progress
            || values[1] is not string text
            || values[2] is not double actualWidth
            || values[3] is not double fontSize
            || values[4] is not FontWeight fontWeight
            || values[5] is not Visual visual
            || progress <= 0 || string.IsNullOrEmpty(text) || actualWidth <= 0)
            return Geometry.Empty;

        // Widen cache tolerance to avoid expensive recomputation during window resize
        if (_cachedResult != null
            && Math.Abs(progress - _lastProgress) < 0.0001
            && text == _lastText
            && Math.Abs(fontSize - _lastFontSize) < 0.01
            && fontWeight == _lastFontWeight
            && Math.Abs(actualWidth - _lastActualWidth) < 5.0)
        {
            return _cachedResult;
        }

        var pixelsPerDip = VisualTreeHelper.GetDpi(visual).PixelsPerDip;
        var typeface = new Typeface(
            SystemFonts.MessageFontFamily,
            FontStyles.Normal,
            fontWeight,
            FontStretches.Normal);

        var ft = new FormattedText(
            text,
            culture,
            FlowDirection.LeftToRight,
            typeface,
            fontSize,
            Brushes.White,
            pixelsPerDip)
        {
            MaxTextWidth = Math.Max(1, actualWidth),
            TextAlignment = TextAlignment.Center
        };

        Geometry result;

        if (progress >= 1.0)
        {
            var full = ft.BuildHighlightGeometry(new Point(0, 0), 0, text.Length);
            result = full ?? Geometry.Empty;
        }
        else
        {
            var charProgress = progress * text.Length;
            var fullChars = (int)charProgress;
            var subProgress = charProgress - fullChars;

            var group = new GeometryGroup { FillRule = FillRule.Nonzero };

            if (fullChars > 0)
            {
                var fullGeo = ft.BuildHighlightGeometry(new Point(0, 0), 0, fullChars);
                if (fullGeo != null)
                    group.Children.Add(fullGeo);
            }

            if (fullChars < text.Length && subProgress > 0.001)
            {
                var charGeo = ft.BuildHighlightGeometry(new Point(0, 0), fullChars, 1);
                if (charGeo != null)
                {
                    var bounds = charGeo.Bounds;
                    if (!bounds.IsEmpty && bounds.Width > 0)
                    {
                        var partialRect = new RectangleGeometry(
                            new Rect(bounds.Left, bounds.Top,
                                bounds.Width * subProgress, bounds.Height));
                        var clipped = Geometry.Combine(
                            charGeo, partialRect,
                            GeometryCombineMode.Intersect, null);
                        group.Children.Add(clipped);
                    }
                }
            }

            result = group;
        }

        result.Freeze();

        // Update cache
        _lastProgress = progress;
        _lastText = text;
        _lastFontSize = fontSize;
        _lastFontWeight = fontWeight;
        _lastActualWidth = actualWidth;
        _cachedResult = result;

        return result;
    }

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}


using System.Globalization;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Brushes = System.Windows.Media.Brushes;
using FlowDirection = System.Windows.FlowDirection;
using FontFamily = System.Windows.Media.FontFamily;
using Point = System.Windows.Point;
using Size = System.Windows.Size;

namespace TermLock.UI;

/// <summary>
/// A small matrix-rain renderer built the way a terminal emulator works:
///
///   glyph atlas (one bitmap with every character)
///        + per-cell brightness array
///        + a single WriteableBitmap frame buffer
///
/// No TextBlock per character, no layout pass per frame, no GC churn.
/// The rain is decorative: it never touches authentication.
/// </summary>
public sealed class MatrixRain : FrameworkElement
{
    private const string Glyphs = "0123456789";
    private const double FontSizeDip = 18.0;
    private const double Decay = 0.86;
    private const double TickSeconds = 0.033;
    private const float MinBrightness = 0.05f;
    private const int WarmupSteps = 120;
    private const int DropsPerColumn = 1;

    private readonly DispatcherTimer _timer;
    private readonly Random _random = new();

    private WriteableBitmap? _bitmap;
    private byte[]? _frame;
    private byte[]? _atlas;

    private int _width;
    private int _height;
    private int _cellW;
    private int _cellH;
    private int _cols;
    private int _rows;
    private int _atlasStride;

    private float[] _brightness = Array.Empty<float>();
    private byte[] _glyph = Array.Empty<byte>();
    private float[] _dropY = Array.Empty<float>();
    private float[] _speed = Array.Empty<float>();
    private int[] _tailLength = Array.Empty<int>();

    public MatrixRain()
    {
        IsHitTestVisible = false;
        Focusable = false;
        SnapsToDevicePixels = true;
        RenderOptions.SetBitmapScalingMode(this, BitmapScalingMode.NearestNeighbor);

        _timer = new DispatcherTimer(DispatcherPriority.Render)
        {
            Interval = TimeSpan.FromMilliseconds(33)
        };
        _timer.Tick += (_, _) => Step();

        Loaded += (_, _) =>
        {
            Rebuild();
            UpdateTimer();
        };
        Unloaded += (_, _) => _timer.Stop();
        SizeChanged += (_, _) => Rebuild();
        IsVisibleChanged += (_, _) => UpdateTimer();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return new Size(
            double.IsInfinity(availableSize.Width) ? 0 : availableSize.Width,
            double.IsInfinity(availableSize.Height) ? 0 : availableSize.Height);
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        drawingContext.DrawRectangle(
            Brushes.Black,
            null,
            new Rect(0, 0, ActualWidth, ActualHeight));

        if (_bitmap is not null)
        {
            drawingContext.DrawImage(
                _bitmap,
                new Rect(0, 0, ActualWidth, ActualHeight));
        }
    }

    private void UpdateTimer()
    {
        if (IsVisible && _bitmap is not null)
        {
            _timer.Start();
        }
        else
        {
            _timer.Stop();
        }
    }

    private void Rebuild()
    {
        if (ActualWidth < 4 || ActualHeight < 4)
        {
            return;
        }

        var dpi = VisualTreeHelper.GetDpi(this);
        _width = Math.Max(1, (int)Math.Ceiling(ActualWidth * dpi.DpiScaleX));
        _height = Math.Max(1, (int)Math.Ceiling(ActualHeight * dpi.DpiScaleY));

        BuildAtlas(dpi);

        _cols = Math.Max(1, _width / _cellW);
        _rows = Math.Max(1, _height / _cellH);

        _brightness = new float[_cols * _rows];
        _glyph = new byte[_cols * _rows];

        var dropCount = _cols * DropsPerColumn;
        _dropY = new float[dropCount];
        _speed = new float[dropCount];
        _tailLength = new int[dropCount];

        for (var drop = 0; drop < dropCount; drop++)
        {
            ResetDrop(drop, initial: true);
        }

        _frame = new byte[_width * _height * 4];
        _bitmap = new WriteableBitmap(
            _width,
            _height,
            96,
            96,
            PixelFormats.Bgra32,
            null);

        // Fill the panel immediately: a few hundred quiet steps build tails,
        // then the first visible frame is already a full rain field.
        for (var i = 0; i < WarmupSteps; i++)
        {
            Step(render: false);
        }

        Step();
        UpdateTimer();
    }

    private void BuildAtlas(DpiScale dpi)
    {
        var typeface = new Typeface(
            new FontFamily("Consolas"),
            FontStyles.Normal,
            FontWeights.Normal,
            FontStretches.Normal);

        var fontSize = FontSizeDip * dpi.DpiScaleY;

        double maxWidth = 0;
        double maxHeight = 0;

        foreach (var glyph in Glyphs)
        {
            var text = new FormattedText(
                glyph.ToString(),
                CultureInfo.InvariantCulture,
                FlowDirection.LeftToRight,
                typeface,
                fontSize,
                Brushes.White,
                dpi.PixelsPerDip);

            maxWidth = Math.Max(maxWidth, text.Width);
            maxHeight = Math.Max(maxHeight, text.Height);
        }

        _cellW = Math.Max(1, (int)Math.Ceiling(maxWidth) + 3);
        _cellH = Math.Max(1, (int)Math.Ceiling(maxHeight) + 4);
        _atlasStride = _cellW * Glyphs.Length * 4;

        var atlasWidth = _cellW * Glyphs.Length;
        var atlasBitmap = new RenderTargetBitmap(
            atlasWidth,
            _cellH,
            96,
            96,
            PixelFormats.Pbgra32);

        var visual = new DrawingVisual();
        using (var dc = visual.RenderOpen())
        {
            for (var i = 0; i < Glyphs.Length; i++)
            {
                var text = new FormattedText(
                    Glyphs[i].ToString(),
                    CultureInfo.InvariantCulture,
                    FlowDirection.LeftToRight,
                    typeface,
                    fontSize,
                    Brushes.White,
                    dpi.PixelsPerDip);

                dc.DrawText(text, new Point(i * _cellW + 1.5, 2));
            }
        }

        atlasBitmap.Render(visual);

        _atlas = new byte[atlasWidth * _cellH * 4];
        atlasBitmap.CopyPixels(_atlas, _atlasStride, 0);
    }

    private void ResetDrop(int drop, bool initial)
    {
        _dropY[drop] = initial
            ? (float)(_random.NextDouble() * _rows)
            : -_random.Next(3, 12);

        _speed[drop] = 5f + (float)_random.NextDouble() * 5f;
        _tailLength[drop] = 8 + _random.Next(0, 7);
    }

    private void Step(bool render = true)
    {
        if (_bitmap is null || _frame is null || _atlas is null)
        {
            return;
        }

        // Fade the tail.
        for (var i = 0; i < _brightness.Length; i++)
        {
            _brightness[i] *= (float)Decay;
        }

        // Move drops and stamp a bright head. Two drops per column keep the
        // panel alive even when one of them is above or below the viewport.
        for (var drop = 0; drop < _dropY.Length; drop++)
        {
            var col = drop % _cols;
            _dropY[drop] += _speed[drop] * (float)TickSeconds;
            var row = (int)_dropY[drop];

            if (row >= 0 && row < _rows)
            {
                SetCell(col, row, 1f, head: true);

                var tail = _tailLength[drop];
                for (var k = 1; k < tail; k++)
                {
                    var tailRow = row - k;
                    if (tailRow < 0)
                    {
                        break;
                    }

                    var fade = 1f - k / (float)tail;
                    var brightness = fade * fade * 0.65f;

                    if (brightness < MinBrightness)
                    {
                        break;
                    }

                    SetCell(col, tailRow, brightness, head: false);
                }
            }

            if (row >= _rows)
            {
                ResetDrop(drop, initial: false);
            }
        }

        if (!render)
        {
            return;
        }

        Array.Clear(_frame, 0, _frame.Length);

        for (var row = 0; row < _rows; row++)
        {
            for (var col = 0; col < _cols; col++)
            {
                var index = row * _cols + col;
                var brightness = _brightness[index];

                if (brightness > MinBrightness)
                {
                    DrawGlyph(col, row, brightness, _glyph[index]);
                }
            }
        }

        _bitmap.WritePixels(
            new Int32Rect(0, 0, _width, _height),
            _frame,
            _width * 4,
            0);

        InvalidateVisual();
    }

    private void SetCell(int col, int row, float brightness, bool head)
    {
        var index = row * _cols + col;
        var wasDim = _brightness[index] < MinBrightness;

        if (brightness > _brightness[index])
        {
            _brightness[index] = brightness;
        }

        if (_glyph[index] == 0 || wasDim || (head && _random.NextDouble() < 0.25))
        {
            _glyph[index] = (byte)_random.Next(Glyphs.Length);
        }
    }

    private void DrawGlyph(int col, int row, float brightness, int glyph)
    {
        if (_frame is null || _atlas is null)
        {
            return;
        }

        var destX = col * _cellW;
        var destY = row * _cellH;
        var srcX = glyph * _cellW;

        // The leading glyph is almost white; the tail is pure green.
        var head = Math.Clamp((brightness - 0.70f) / 0.30f, 0f, 1f);
        var redBlue = 0.08f + 0.92f * head;
        var green = 0.85f + 0.15f * head;

        for (var y = 0; y < _cellH; y++)
        {
            var dy = destY + y;
            if (dy < 0 || dy >= _height)
            {
                continue;
            }

            var srcRow = y * _atlasStride + srcX * 4;
            var dstRow = (dy * _width + destX) * 4;

            for (var x = 0; x < _cellW; x++)
            {
                var dx = destX + x;
                if (dx < 0 || dx >= _width)
                {
                    continue;
                }

                var alpha = _atlas[srcRow + x * 4 + 3];
                if (alpha == 0)
                {
                    continue;
                }

                var value = alpha * brightness;

                _frame[dstRow + x * 4 + 0] = (byte)(value * redBlue); // B
                _frame[dstRow + x * 4 + 1] = (byte)(value * green);   // G
                _frame[dstRow + x * 4 + 2] = (byte)(value * redBlue); // R
                _frame[dstRow + x * 4 + 3] = 255;                     // A
            }
        }
    }
}

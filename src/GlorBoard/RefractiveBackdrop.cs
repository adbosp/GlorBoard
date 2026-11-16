using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GlorBoard;

public sealed class RefractiveBackdrop : FrameworkElement
{
    private const int MaximumSampleWidth = 568;
    private const int Overscan = 54;
    private static readonly SolidColorBrush WhiteText = FrozenColor(0xFFFAFDFF);
    private static readonly SolidColorBrush WhiteSecondary = FrozenColor(0xFFF0F7FC);
    private static readonly SolidColorBrush DarkText = FrozenColor(0xFF14252E);
    private static readonly SolidColorBrush DarkSecondary = FrozenColor(0xFF213943);

    public static readonly DependencyProperty IsRegionProperty = DependencyProperty.RegisterAttached(
        "IsRegion", typeof(bool), typeof(RefractiveBackdrop), new PropertyMetadata(false));
    public static readonly DependencyProperty OpticsEnabledProperty = DependencyProperty.RegisterAttached(
        "OpticsEnabled", typeof(bool), typeof(RefractiveBackdrop),
        new FrameworkPropertyMetadata(false, FrameworkPropertyMetadataOptions.Inherits));
    public static readonly DependencyProperty FallbackFillProperty = DependencyProperty.Register(
        nameof(FallbackFill), typeof(Brush), typeof(RefractiveBackdrop),
        new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    public static void SetIsRegion(DependencyObject element, bool value) => element.SetValue(IsRegionProperty, value);
    public static bool GetIsRegion(DependencyObject element) => (bool)element.GetValue(IsRegionProperty);
    public static void SetOpticsEnabled(DependencyObject element, bool value) => element.SetValue(OpticsEnabledProperty, value);
    public static bool GetOpticsEnabled(DependencyObject element) => (bool)element.GetValue(OpticsEnabledProperty);
    public Brush FallbackFill
    {
        get => (Brush)GetValue(FallbackFillProperty);
        set => SetValue(FallbackFillProperty, value);
    }

    private readonly DispatcherTimer timer;
    private readonly List<Pane> zones = [];
    private readonly BackdropBlur blur = new();
    private readonly List<Pane> currentZones = [];
    private readonly Dictionary<Border, bool> darkLabels = [];
    private Window? owner;
    private nint handle;
    private DesktopPatch? capture;
    private WriteableBitmap? image;
    private PixelSample[] map = [];
    private int[] output = [];
    private SampleViewport lastViewport;
    private int lastFingerprint;
    private int unchangedFrames;
    private long retryAfter;

    public RefractiveBackdrop()
    {
        IsHitTestVisible = false;
        timer = new(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(83) };
        timer.Tick += (_, _) => UpdateFrame();
        Loaded += Attach;
        Unloaded += Detach;
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        Rect box = new(RenderSize);
        if (image == null) drawingContext.DrawRectangle(FallbackFill, null, box);
        else drawingContext.DrawImage(image, box);
    }

    private void Attach(object sender, RoutedEventArgs e)
    {
        owner = Window.GetWindow(this);
        if (owner == null) return;
        handle = new WindowInteropHelper(owner).Handle;
        owner.Activated += Continue;
        owner.Deactivated += Suspend;
        owner.StateChanged += StateChanged;
        owner.IsVisibleChanged += VisibilityChanged;
        UpdateActivity();
    }

    private void Detach(object sender, RoutedEventArgs e)
    {
        if (owner != null)
        {
            owner.Activated -= Continue;
            owner.Deactivated -= Suspend;
            owner.StateChanged -= StateChanged;
            owner.IsVisibleChanged -= VisibilityChanged;
        }
        Suspend(sender, e);
        owner = null;
    }

    private void Continue(object? sender, EventArgs e) => UpdateActivity();
    private void StateChanged(object? sender, EventArgs e) => UpdateActivity();
    private void VisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) => UpdateActivity();

    private void UpdateActivity()
    {
        if (owner is { IsActive: true, IsVisible: true, WindowState: not WindowState.Minimized } &&
            RenderCapability.Tier != 0 && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041) && GlassTheme.TransparencyAllowed)
        {
            timer.Start();
        }
        else Suspend(this, EventArgs.Empty);
    }

    private void Suspend(object? sender, EventArgs e)
    {
        timer.Stop();
        capture?.Dispose();
        capture = null;
        blur.Release();
        image = null;
        map = [];
        output = [];
        zones.Clear();
        ResetAppearance();
        InvalidateVisual();
    }

    private void ResetAppearance()
    {
        if (owner != null) SetOpticsEnabled(owner, false);
        foreach (Border pane in darkLabels.Keys)
        {
            pane.Resources.Remove("Text");
            pane.Resources.Remove("SecondaryText");
            pane.ClearValue(TextElement.ForegroundProperty);
        }
        darkLabels.Clear();
    }

    private unsafe void UpdateFrame()
    {
        if (owner == null || !owner.IsActive || ActualWidth < 4 || ActualHeight < 4 ||
            Environment.TickCount64 < retryAfter) return;

        if (SystemParameters.HighContrast)
        {
            Suspend(this, EventArgs.Empty);
            return;
        }

        currentZones.Clear();
        Point origin = TransformToAncestor(owner).Transform(new Point());
        if (ReadPanes(owner, origin)) return; // Leave the last clean frame beneath an open dropdown.

        Point topLeft = PointToScreen(new Point());
        Point bottomRight = PointToScreen(new Point(ActualWidth, ActualHeight));
        double scaleX = (bottomRight.X - topLeft.X) / ActualWidth;
        double scaleY = (bottomRight.Y - topLeft.Y) / ActualHeight;
        int overscanX = (int)Math.Ceiling(Overscan * scaleX);
        int overscanY = (int)Math.Ceiling(Overscan * scaleY);
        int displayLeft = GetSystemMetrics(76), displayTop = GetSystemMetrics(77);
        int displayRight = displayLeft + GetSystemMetrics(78), displayBottom = displayTop + GetSystemMetrics(79);
        int left = Math.Max(displayLeft, (int)Math.Floor(topLeft.X) - overscanX);
        int top = Math.Max(displayTop, (int)Math.Floor(topLeft.Y) - overscanY);
        int right = Math.Min(displayRight, (int)Math.Ceiling(bottomRight.X) + overscanX);
        int bottom = Math.Min(displayBottom, (int)Math.Ceiling(bottomRight.Y) + overscanY);
        int width = right - left, height = bottom - top;
        if (width is < 2 or > 4096 || height is < 2 or > 3072) return;

        if (capture == null || capture.Width != width || capture.Height != height)
        {
            capture?.Dispose();
            capture = new DesktopPatch(width, height);
        }
        if (!capture.ReadBehind(handle, left, top))
        {
            retryAfter = Environment.TickCount64 + 1000;
            ResetAppearance();
            if (image != null) { image = null; InvalidateVisual(); }
            return;
        }

        var (sampleColumns, sampleRows) = BackdropBlur.SampleSize(width, height);
        double reduceX = sampleColumns / (double)width, reduceY = sampleRows / (double)height;
        var viewport = new SampleViewport(sampleColumns, sampleRows,
            (topLeft.X - left) * reduceX, (topLeft.Y - top) * reduceY,
            scaleX * reduceX, scaleY * reduceY,
            ActualWidth, ActualHeight);
        bool rebuild = image == null || viewport != lastViewport || !PanesMatch();
        if (rebuild)
        {
            Rebuild(viewport);
            zones.Clear();
            zones.AddRange(currentZones);
            lastViewport = viewport;
        }
        int* pixels = (int*)capture.Pixels;
        int fingerprint = Fingerprint(pixels, width, height);
        if (!rebuild && lastFingerprint == fingerprint && ++unchangedFrames < 8) return;
        unchangedFrames = 0;
        lastFingerprint = fingerprint;
        int radius = Math.Clamp((int)Math.Round(10 * viewport.ScaleX), 4, 12);
        int[] opticalPixels = blur.Apply(pixels, width, height, sampleColumns, sampleRows, radius);
        ApplyContrast(opticalPixels, viewport);
        SetOpticsEnabled(owner, true);

        for (int i = 0; i < output.Length; i++)
        {
            PixelSample sample = map[i];
            int a = opticalPixels[sample.Index], b = opticalPixels[sample.Index + 1];
            int c = opticalPixels[sample.Index + sampleColumns], d = opticalPixels[sample.Index + sampleColumns + 1];
            int blue = Mix(Mix(a & 255, b & 255, sample.X), Mix(c & 255, d & 255, sample.X), sample.Y);
            int green = Mix(Mix((a >> 8) & 255, (b >> 8) & 255, sample.X),
                Mix((c >> 8) & 255, (d >> 8) & 255, sample.X), sample.Y);
            int red = Mix(Mix((a >> 16) & 255, (b >> 16) & 255, sample.X),
                Mix((c >> 16) & 255, (d >> 16) & 255, sample.X), sample.Y);
            output[i] = unchecked((int)0xED000000) | red << 16 | green << 8 | blue;
        }
        int sampleWidth = image!.PixelWidth, sampleHeight = image.PixelHeight;
        image.WritePixels(new Int32Rect(0, 0, sampleWidth, sampleHeight), output, sampleWidth * 4, 0);
        if (rebuild) InvalidateVisual();
    }

    private bool ReadPanes(DependencyObject parent, Point origin)
    {
        if (parent is UIElement { IsVisible: false }) return false;
        if (parent is ComboBox { IsDropDownOpen: true }) return true;
        if (parent is Border { ActualWidth: > 1, ActualHeight: > 1 } border && GetIsRegion(border))
        {
            Rect bounds = border.TransformToAncestor(owner!).TransformBounds(new Rect(border.RenderSize));
            bounds.Offset(-origin.X, -origin.Y);
            if (bounds.IntersectsWith(new Rect(RenderSize)))
                currentZones.Add(new(border, bounds, border.CornerRadius.TopLeft));
        }
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            if (ReadPanes(VisualTreeHelper.GetChild(parent, i), origin)) return true;
        return false;
    }

    private bool PanesMatch()
    {
        if (zones.Count != currentZones.Count) return false;
        for (int i = 0; i < zones.Count; i++)
            if (zones[i] != currentZones[i]) return false;
        return true;
    }

    private void Rebuild(SampleViewport viewport)
    {
        int columns = Math.Max(2, Math.Min(MaximumSampleWidth, (int)Math.Ceiling(viewport.Width)));
        int rows = Math.Max(2, (int)Math.Ceiling(viewport.Height * columns / viewport.Width));
        if (image == null || image.PixelWidth != columns || image.PixelHeight != rows)
        {
            image = new WriteableBitmap(columns, rows, 96, 96, PixelFormats.Bgra32, null);
            map = new PixelSample[columns * rows];
            output = new int[map.Length];
        }
        Rect window = new(0, 0, viewport.Width, viewport.Height);
        double stepX = viewport.Width / columns, stepY = viewport.Height / rows;
        for (int y = 0; y < rows; y++)
        for (int x = 0; x < columns; x++)
        {
            Point position = new((x + .5) * stepX, (y + .5) * stepY);
            Point bent = LensGeometry.Refract(position, window, WindowGlass.Radius, 39);
            foreach (Pane pane in currentZones)
            {
                if (!pane.Bounds.Contains(position)) continue;
                Point paneRay = LensGeometry.Refract(position, pane.Bounds, pane.Radius, 30);
                bent.Offset(paneRay.X - position.X, paneRay.Y - position.Y);
                break;
            }

            double sx = Math.Clamp(viewport.OffsetX + viewport.ScaleX * bent.X, 0, viewport.SourceWidth - 1.001);
            double sy = Math.Clamp(viewport.OffsetY + viewport.ScaleY * bent.Y, 0, viewport.SourceHeight - 1.001);
            int x0 = (int)sx, y0 = (int)sy;
            map[y * columns + x] = new(y0 * viewport.SourceWidth + x0,
                (byte)((sx - x0) * 256), (byte)((sy - y0) * 256));
        }
    }

    private void ApplyContrast(int[] pixels, SampleViewport viewport)
    {
        bool tintedLight = owner?.FindResource("Text") is SolidColorBrush { Color.R: < 120 };
        foreach (Pane zone in currentZones)
        {
            int average = 0;
            for (int x = 1; x <= 4; x++)
            for (int y = 1; y <= 3; y++)
            {
                double localX = zone.Bounds.X + zone.Bounds.Width * x / 5;
                double localY = zone.Bounds.Y + zone.Bounds.Height * y / 4;
                int sx = Math.Clamp((int)(viewport.OffsetX + localX * viewport.ScaleX), 0, viewport.SourceWidth - 1);
                int sy = Math.Clamp((int)(viewport.OffsetY + localY * viewport.ScaleY), 0, viewport.SourceHeight - 1);
                int value = pixels[sy * viewport.SourceWidth + sx];
                average += ((value >> 16 & 255) * 54 + (value >> 8 & 255) * 183 + (value & 255) * 19) >> 8;
            }
            bool seen = darkLabels.TryGetValue(zone.Element, out bool previous);
            int threshold = tintedLight ? 116 : 164;
            if (seen) threshold += previous ? -10 : 10;
            bool dark = average / 12 >= threshold;
            if (seen && dark == previous) continue;
            darkLabels[zone.Element] = dark;
            zone.Element.Resources["Text"] = dark ? DarkText : WhiteText;
            zone.Element.Resources["SecondaryText"] = dark ? DarkSecondary : WhiteSecondary;
            zone.Element.SetResourceReference(TextElement.ForegroundProperty, "Text");
        }
    }

    private static unsafe int Fingerprint(int* pixels, int width, int height)
    {
        int hash = 17;
        for (int y = 11; y < height; y += 19)
        for (int x = 9; x < width; x += 23)
            hash = unchecked(hash * 31 + (pixels[y * width + x] & 0x00FFFFFF));
        return hash;
    }

    private static SolidColorBrush FrozenColor(uint color)
    {
        var brush = new SolidColorBrush(Color.FromArgb((byte)(color >> 24), (byte)(color >> 16), (byte)(color >> 8), (byte)color));
        brush.Freeze();
        return brush;
    }

    private static int Mix(int a, int b, int fraction) => (a * (256 - fraction) + b * fraction) >> 8;

    private readonly record struct PixelSample(int Index, byte X, byte Y);
    private readonly record struct Pane(Border Element, Rect Bounds, double Radius);
    private readonly record struct SampleViewport(int SourceWidth, int SourceHeight, double OffsetX, double OffsetY,
        double ScaleX, double ScaleY, double Width, double Height);

    [DllImport("user32")] private static extern int GetSystemMetrics(int metric);
}

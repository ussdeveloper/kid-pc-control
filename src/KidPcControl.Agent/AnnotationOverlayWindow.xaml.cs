using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using KidPcControl.Shared.Models;
using Size = System.Windows.Size;

namespace KidPcControl.Agent;

public partial class AnnotationOverlayWindow : Window
{
    private readonly DispatcherTimer _closeTimer = new();

    public AnnotationOverlayWindow()
    {
        InitializeComponent();
        _closeTimer.Tick += (_, _) => Close();
    }

    public void ShowAnnotation(ScreenAnnotation ann, Rect screenBounds)
    {
        Left = screenBounds.Left;
        Top = screenBounds.Top;
        Width = screenBounds.Width;
        Height = screenBounds.Height;

        var x = ann.X * screenBounds.Width;
        var y = ann.Y * screenBounds.Height;
        var w = Math.Max(24, ann.Width * screenBounds.Width);
        var h = Math.Max(24, ann.Height * screenBounds.Height);

        Canvas.SetLeft(Highlight, x);
        Canvas.SetTop(Highlight, y);
        Highlight.Width = w;
        Highlight.Height = h;

        CaptionText.Text = string.IsNullOrWhiteSpace(ann.Text)
            ? "Uwaga od rodzica"
            : ann.Text;

        Caption.Measure(new Size(double.PositiveInfinity, double.PositiveInfinity));
        var cw = Math.Min(420, Math.Max(120, Caption.DesiredSize.Width + 24));
        var ch = Caption.DesiredSize.Height + 16;
        Caption.Width = cw;

        var cx = x;
        var cy = y - ch - 8;
        if (cy < 8) cy = y + h + 8;
        if (cx + cw > screenBounds.Width - 8) cx = screenBounds.Width - cw - 8;
        if (cx < 8) cx = 8;

        Canvas.SetLeft(Caption, cx);
        Canvas.SetTop(Caption, cy);

        var remaining = ann.ExpiresAt - DateTimeOffset.UtcNow;
        if (remaining < TimeSpan.FromSeconds(1))
            remaining = TimeSpan.FromSeconds(ann.DurationSeconds);
        _closeTimer.Stop();
        _closeTimer.Interval = remaining;
        _closeTimer.Start();

        if (!IsVisible) Show();
        else Activate();
    }
}

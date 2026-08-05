using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using KidPcControl.Shared.Control;
using KidPcControl.Shared.Models;
using MessageBox = System.Windows.MessageBox;
using Point = System.Windows.Point;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace KidPcControl.Admin;

public partial class ScreenPreviewWindow : Window
{
    private readonly KidApiClient _client;
    private readonly DispatcherTimer _refreshTimer = new() { Interval = TimeSpan.FromSeconds(3) };
    private Point _selectStart;
    private bool _selecting;
    private bool _panning;
    private Point _panStart;
    private double _panOriginX;
    private double _panOriginY;
    private Rect? _selectionNorm;
    private double _zoom = 1;

    public ScreenPreviewWindow(KidApiClient client, BitmapSource? initial)
    {
        InitializeComponent();
        DarkTitleBar.Apply(this);
        _client = client;
        if (initial is not null)
            PreviewImage.Source = initial;

        _refreshTimer.Tick += async (_, _) => await RefreshAsync();
        _refreshTimer.Start();
        Closed += (_, _) => _refreshTimer.Stop();
        Loaded += async (_, _) =>
        {
            if (PreviewImage.Source is null)
                await RefreshAsync();
            FitLayout();
        };
    }

    private async Task RefreshAsync()
    {
        var bytes = await _client.GetScreenJpegAsync();
        if (bytes is null || bytes.Length == 0) return;
        try
        {
            using var ms = new MemoryStream(bytes);
            var img = new BitmapImage();
            img.BeginInit();
            img.CacheOption = BitmapCacheOption.OnLoad;
            img.StreamSource = ms;
            img.EndInit();
            img.Freeze();
            PreviewImage.Source = img;
            FitLayout();
        }
        catch { /* ignore */ }
    }

    private void FitLayout()
    {
        if (PreviewImage.Source is not BitmapSource bmp) return;
        ImageHost.Width = Math.Max(bmp.PixelWidth * _zoom + 40, Scroller.ViewportWidth);
        ImageHost.Height = Math.Max(bmp.PixelHeight * _zoom + 40, Scroller.ViewportHeight);
        PreviewImage.Width = bmp.PixelWidth;
        PreviewImage.Height = bmp.PixelHeight;
        ZoomScale.ScaleX = _zoom;
        ZoomScale.ScaleY = _zoom;
        RedrawSelection();
    }

    private void Scroller_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (Keyboard.Modifiers == ModifierKeys.None || Keyboard.Modifiers == ModifierKeys.Control)
        {
            var factor = e.Delta > 0 ? 1.15 : 1 / 1.15;
            _zoom = Math.Clamp(_zoom * factor, 0.25, 6);
            FitLayout();
            e.Handled = true;
        }
    }

    private void Scroller_OnPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        var pos = e.GetPosition(ImageHost);

        if (e.ChangedButton == MouseButton.Middle ||
            (e.ChangedButton == MouseButton.Left && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift)))
        {
            _panning = true;
            _panStart = e.GetPosition(this);
            _panOriginX = PanOffset.X;
            _panOriginY = PanOffset.Y;
            CaptureMouse();
            e.Handled = true;
            return;
        }

        if (e.ChangedButton != MouseButton.Left) return;
        if (!TryMapToImage(pos, out var imgPt)) return;

        _selecting = true;
        _selectStart = imgPt;
        _selectionNorm = null;
        SelectionRect.Visibility = Visibility.Visible;
        CaptureMouse();
        UpdateSelectionVisual(_selectStart, _selectStart);
        e.Handled = true;
    }

    private void Scroller_OnPreviewMouseMove(object sender, MouseEventArgs e)
    {
        if (_panning &&
            (e.MiddleButton == MouseButtonState.Pressed ||
             (e.LeftButton == MouseButtonState.Pressed && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))))
        {
            var now = e.GetPosition(this);
            PanOffset.X = _panOriginX + (now.X - _panStart.X);
            PanOffset.Y = _panOriginY + (now.Y - _panStart.Y);
            return;
        }

        if (!_selecting || e.LeftButton != MouseButtonState.Pressed) return;
        var pos = e.GetPosition(ImageHost);
        if (!TryMapToImage(pos, out var imgPt)) return;
        UpdateSelectionVisual(_selectStart, imgPt);
    }

    private void Scroller_OnPreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        if (_panning)
        {
            _panning = false;
            ReleaseMouseCapture();
            e.Handled = true;
            return;
        }

        if (!_selecting) return;
        _selecting = false;
        ReleaseMouseCapture();

        var pos = e.GetPosition(ImageHost);
        if (TryMapToImage(pos, out var imgPt) && PreviewImage.Source is BitmapSource bmp)
        {
            var x1 = Math.Min(_selectStart.X, imgPt.X);
            var y1 = Math.Min(_selectStart.Y, imgPt.Y);
            var x2 = Math.Max(_selectStart.X, imgPt.X);
            var y2 = Math.Max(_selectStart.Y, imgPt.Y);
            var w = x2 - x1;
            var h = y2 - y1;
            if (w < 8 || h < 8)
            {
                ClearSelection();
                StatusText.Text = "Zaznaczenie za małe — przeciągnij większy prostokąt.";
            }
            else
            {
                _selectionNorm = new Rect(
                    x1 / bmp.PixelWidth,
                    y1 / bmp.PixelHeight,
                    w / bmp.PixelWidth,
                    h / bmp.PixelHeight);
                StatusText.Text = "Obszar gotowy — wpisz tekst i wyślij.";
                RedrawSelection();
            }
        }

        e.Handled = true;
    }

    private bool TryMapToImage(Point hostPoint, out Point imagePoint)
    {
        imagePoint = default;
        if (PreviewImage.Source is not BitmapSource bmp) return false;

        // Image is at (0,0) of ImageHost with Scale + Translate on the Image itself
        var x = (hostPoint.X - PanOffset.X) / _zoom;
        var y = (hostPoint.Y - PanOffset.Y) / _zoom;
        if (x < 0 || y < 0 || x > bmp.PixelWidth || y > bmp.PixelHeight)
            return false;
        imagePoint = new Point(x, y);
        return true;
    }

    private void UpdateSelectionVisual(Point a, Point b)
    {
        var x = Math.Min(a.X, b.X) * _zoom + PanOffset.X;
        var y = Math.Min(a.Y, b.Y) * _zoom + PanOffset.Y;
        var w = Math.Abs(a.X - b.X) * _zoom;
        var h = Math.Abs(a.Y - b.Y) * _zoom;
        Canvas.SetLeft(SelectionRect, x);
        Canvas.SetTop(SelectionRect, y);
        SelectionRect.Width = w;
        SelectionRect.Height = h;
        SelectionRect.Visibility = Visibility.Visible;
    }

    private void RedrawSelection()
    {
        if (_selectionNorm is null || PreviewImage.Source is not BitmapSource bmp)
            return;
        var r = _selectionNorm.Value;
        var a = new Point(r.X * bmp.PixelWidth, r.Y * bmp.PixelHeight);
        var b = new Point((r.X + r.Width) * bmp.PixelWidth, (r.Y + r.Height) * bmp.PixelHeight);
        UpdateSelectionVisual(a, b);
    }

    private void ClearSelection()
    {
        _selectionNorm = null;
        SelectionRect.Visibility = Visibility.Collapsed;
    }

    private void Clear_Click(object sender, RoutedEventArgs e)
    {
        ClearSelection();
        NoteBox.Text = "";
        StatusText.Text = "Zaznacz obszar, wpisz tekst i wyślij.";
    }

    private async void Send_Click(object sender, RoutedEventArgs e)
    {
        if (_selectionNorm is null)
        {
            StatusText.Text = "Najpierw zaznacz prostokąt na obrazie.";
            return;
        }

        if (!int.TryParse(SecondsBox.Text.Trim(), out var seconds) || seconds < 3)
            seconds = 15;
        seconds = Math.Clamp(seconds, 3, 120);

        var r = _selectionNorm.Value;
        var ann = new ScreenAnnotation
        {
            X = r.X,
            Y = r.Y,
            Width = r.Width,
            Height = r.Height,
            Text = NoteBox.Text.Trim(),
            DurationSeconds = seconds
        };

        try
        {
            await _client.SendAnnotationAsync(ann);
            StatusText.Text = $"Wysłano — widoczne na Kidzie przez {seconds} s.";
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"Nie wysłano adnotacji: {ex.Message}", "Kid PC Control");
        }
    }
}

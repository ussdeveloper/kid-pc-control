using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using KidPcControl.Shared;
using KidPcControl.Shared.Models;
using KidPcControl.Shared.Storage;
using Microsoft.Win32;
using MessageBox = System.Windows.MessageBox;

namespace KidPcControl.Agent;

public partial class LockWindow : Window
{
    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromSeconds(1.5) };
    private readonly DispatcherTimer _screenTimer = new() { Interval = TimeSpan.FromSeconds(2) };
    private AnnotationOverlayWindow? _annotationOverlay;
    private string? _lastAnnotationJson;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    public LockWindow()
    {
        InitializeComponent();
        _timer.Tick += (_, _) => Evaluate();
        _screenTimer.Tick += (_, _) => CaptureScreen();
        _timer.Start();
        _screenTimer.Start();
        Loaded += (_, _) =>
        {
            ApplyProxyFromPolicy();
            Evaluate();
            CaptureScreen();
        };
        Closed += (_, _) =>
        {
            try { _annotationOverlay?.Close(); } catch { /* ignore */ }
        };
    }

    private void Refresh_Click(object sender, RoutedEventArgs e) => Evaluate();

    private void Evaluate()
    {
        ApplyProxyFromPolicy();
        ShowUrlBlockBannerIfAny();
        ShowAnnotationIfAny();

        var status = StatusStore.Load();
        var policy = PolicyStore.LoadOrCreate();
        MessageText.Text = string.IsNullOrWhiteSpace(policy.LockMessage)
            ? "Czas na przerwę."
            : policy.LockMessage;

        var locked = status?.Locked
                     ?? (!KidPcControl.Shared.Policy.AccessEvaluator.IsWithinAllowedHours(policy, DateTime.Now) || policy.DeviceBlocked);

        if (status is not null && !string.IsNullOrWhiteSpace(status.LockReason) && status.Locked)
            MessageText.Text = $"{policy.LockMessage}\n\n({status.LockReason})";

        if (locked)
        {
            if (!IsVisible)
            {
                Show();
                WindowState = WindowState.Maximized;
            }
            Topmost = true;
            Activate();
        }
        else
        {
            Hide();
        }
    }

    private void ShowAnnotationIfAny()
    {
        try
        {
            if (!File.Exists(AppConstants.AnnotationPath))
            {
                CloseAnnotationOverlay();
                return;
            }

            var json = File.ReadAllText(AppConstants.AnnotationPath);
            var ann = JsonSerializer.Deserialize<ScreenAnnotation>(json, JsonOptions);
            if (ann is null || ann.ExpiresAt <= DateTimeOffset.UtcNow)
            {
                try { File.Delete(AppConstants.AnnotationPath); } catch { /* ignore */ }
                CloseAnnotationOverlay();
                return;
            }

            if (json == _lastAnnotationJson && _annotationOverlay is { IsVisible: true })
                return;

            _lastAnnotationJson = json;
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                         ?? new Rectangle(0, 0, 1280, 720);
            var rect = new Rect(bounds.Left, bounds.Top, bounds.Width, bounds.Height);
            _annotationOverlay ??= new AnnotationOverlayWindow();
            _annotationOverlay.ShowAnnotation(ann, rect);
        }
        catch
        {
            // ignore
        }
    }

    private void CloseAnnotationOverlay()
    {
        _lastAnnotationJson = null;
        if (_annotationOverlay is null) return;
        try { _annotationOverlay.Close(); } catch { /* ignore */ }
        _annotationOverlay = null;
    }

    private void ShowUrlBlockBannerIfAny()
    {
        try
        {
            if (!File.Exists(AppConstants.BlockBannerPath)) return;
            var msg = File.ReadAllText(AppConstants.BlockBannerPath);
            File.Delete(AppConstants.BlockBannerPath);
            if (string.IsNullOrWhiteSpace(msg)) return;
            MessageBox.Show(msg, "Kid PC Control", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch { /* ignore */ }
    }

    private void CaptureScreen()
    {
        try
        {
            Directory.CreateDirectory(AppConstants.ProgramDataDir);
            var bounds = System.Windows.Forms.Screen.PrimaryScreen?.Bounds
                         ?? new Rectangle(0, 0, 1280, 720);
            using var bmp = new Bitmap(bounds.Width, bounds.Height);
            using (var g = Graphics.FromImage(bmp))
            {
                g.CopyFromScreen(bounds.Location, System.Drawing.Point.Empty, bounds.Size);
            }

            var w = Math.Min(1280, bmp.Width);
            var h = (int)(bmp.Height * (w / (double)bmp.Width));
            using var scaled = new Bitmap(bmp, new System.Drawing.Size(w, h));
            var encoder = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
            var encParams = new EncoderParameters(1);
            encParams.Param[0] = new EncoderParameter(Encoder.Quality, 55L);
            scaled.Save(AppConstants.ScreenPath, encoder, encParams);
        }
        catch
        {
            // Session / permission issues — ignore
        }
    }

    private static void ApplyProxyFromPolicy()
    {
        try
        {
            var policy = PolicyStore.LoadOrCreate();
            var enable = policy.BlockedUrlRegex.Count > 0;
            using var key = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Internet Settings");
            if (enable)
            {
                key.SetValue("ProxyEnable", 1);
                key.SetValue("ProxyServer", $"127.0.0.1:{AppConstants.UrlProxyPort}");
            }
        }
        catch { /* ignore */ }
    }
}

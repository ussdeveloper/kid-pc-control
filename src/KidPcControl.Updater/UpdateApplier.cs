using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Principal;
using KidPcControl.Shared;

namespace KidPcControl.Updater;

public sealed class UpdateApplyResult
{
    public bool Started { get; init; }
    public string Message { get; init; } = string.Empty;
    public string? SetupPath { get; init; }
}

/// <summary>
/// Downloads Setup.exe from a public GitHub Release asset and launches a silent Inno install.
/// </summary>
public static class UpdateApplier
{
    private static readonly object Gate = new();
    private static bool _inProgress;

    public static bool IsInProgress
    {
        get { lock (Gate) return _inProgress; }
    }

    public static async Task<UpdateApplyResult> DownloadAndApplyAsync(
        UpdateCheckResult check,
        HttpClient? http = null,
        CancellationToken ct = default)
    {
        if (check is null || !check.UpdateAvailable)
            return new UpdateApplyResult { Message = "Brak nowej wersji." };

        if (string.IsNullOrWhiteSpace(check.DownloadUrl))
            return new UpdateApplyResult { Message = "Release nie ma pliku Setup.exe." };

        lock (Gate)
        {
            if (_inProgress)
                return new UpdateApplyResult { Message = "Aktualizacja już trwa." };
            _inProgress = true;
        }

        try
        {
            Directory.CreateDirectory(AppConstants.ProgramDataDir);
            var setupPath = Path.Combine(
                AppConstants.ProgramDataDir,
                $"KidPcControl-Setup-v{check.LatestVersion}.exe");

            using var client = http ?? CreateHttp();
            using var response = await client.GetAsync(check.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            await using (var fs = new FileStream(setupPath, FileMode.Create, FileAccess.Write, FileShare.None))
            {
                await response.Content.CopyToAsync(fs, ct);
            }

            var args = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /CLOSEAPPLICATIONS /RESTARTAPPLICATIONS";
            var elevated = IsAdministrator();
            var psi = new ProcessStartInfo
            {
                FileName = setupPath,
                Arguments = args,
                UseShellExecute = true
            };
            if (!elevated)
                psi.Verb = "runas";

            var proc = Process.Start(psi);
            if (proc is null)
                return new UpdateApplyResult { Message = "Nie uruchomiono instalatora (UAC anulowane?)." };

            return new UpdateApplyResult
            {
                Started = true,
                SetupPath = setupPath,
                Message = $"Uruchomiono instalator v{check.LatestVersion}."
            };
        }
        catch (Exception ex)
        {
            return new UpdateApplyResult { Message = $"Nie udało się zaktualizować: {ex.Message}" };
        }
        finally
        {
            lock (Gate) _inProgress = false;
        }
    }

    private static bool IsAdministrator()
    {
        try
        {
            using var id = WindowsIdentity.GetCurrent();
            return new WindowsPrincipal(id).IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    private static HttpClient CreateHttp()
    {
        var http = new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        http.DefaultRequestHeaders.UserAgent.ParseAdd("KidPcControl-Updater/0.2");
        http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/octet-stream"));
        return http;
    }
}

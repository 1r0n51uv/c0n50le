using C0n50le.Core.Translation;

namespace C0n50le.Services;

/// <summary>
/// The offline English → Italian translator: downloads the model once into the data folder, then translates on this
/// PC with no service. One translation at a time, off the UI thread.
/// </summary>
public sealed class TranslationService : IDisposable
{
    private readonly string _folder;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private OfflineTranslator? _translator;
    private Task<bool>? _download;

    public TranslationService(string dataFolder) => _folder = Path.Combine(dataFolder, "models", "opus-mt-en-it");

    public bool IsInstalled => TranslationModel.IsInstalled(_folder);
    public bool IsDownloading => _download is { IsCompleted: false };

    /// <summary>0 to 1 while downloading.</summary>
    public double Progress { get; private set; }

    public event Action? Changed;

    /// <summary>Downloads the model if it isn't there yet; true once it is installed. Safe to call again while it runs.</summary>
    public Task<bool> EnsureInstalledAsync(CancellationToken token = default)
    {
        if (IsInstalled) return Task.FromResult(true);
        return _download is { IsCompleted: false } running ? running : _download = DownloadAsync(token);
    }

    private async Task<bool> DownloadAsync(CancellationToken token)
    {
        try
        {
            Directory.CreateDirectory(_folder);
            using var http = new HttpClient { Timeout = TimeSpan.FromMinutes(20) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("c0n50le/" + Updater.CurrentVersion);
            long done = 0;
            foreach (var (remote, local) in TranslationModel.Files)
            {
                var target = Path.Combine(_folder, local);
                if (File.Exists(target)) continue;
                var part = target + ".part";
                using var response = await http.GetAsync(TranslationModel.Source + remote, HttpCompletionOption.ResponseHeadersRead, token);
                response.EnsureSuccessStatusCode();
                await using (var input = await response.Content.ReadAsStreamAsync(token))
                await using (var output = File.Create(part))
                {
                    var buffer = new byte[1 << 16];
                    int read;
                    while ((read = await input.ReadAsync(buffer, token)) > 0)
                    {
                        await output.WriteAsync(buffer.AsMemory(0, read), token);
                        done += read;
                        var progress = Math.Min(0.99, done / (double)TranslationModel.ApproximateBytes);
                        if (progress - Progress >= 0.01)
                        {
                            Progress = progress;
                            Changed?.Invoke();
                        }
                    }
                }
                File.Move(part, target, overwrite: true);
            }
            AppLog.Write($"Traduttore offline installato ({TranslationModel.Name})");
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"Traduttore offline: download non riuscito: {ex.Message}");
            return false;
        }
        finally
        {
            Progress = IsInstalled ? 1 : 0;
            Changed?.Invoke();
        }
    }

    /// <summary>The Italian text, or null when the model isn't installed or the translation failed.</summary>
    public async Task<string?> TranslateAsync(string english, CancellationToken token)
    {
        if (!IsInstalled || string.IsNullOrWhiteSpace(english)) return null;
        await _gate.WaitAsync(token);
        try
        {
            return await Task.Run(() =>
            {
                _translator ??= new OfflineTranslator(_folder);
                return _translator.Translate(english, token);
            }, token);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"Traduttore offline: {ex.Message}");
            return null;
        }
        finally { _gate.Release(); }
    }

    /// <summary>Removes the model (it can be downloaded again).</summary>
    public void Remove()
    {
        _translator?.Dispose();
        _translator = null;
        try { if (Directory.Exists(_folder)) Directory.Delete(_folder, true); }
        catch (IOException ex) { AppLog.Write($"Traduttore offline: rimozione: {ex.Message}"); }
        Changed?.Invoke();
    }

    public void Dispose()
    {
        _translator?.Dispose();
        _gate.Dispose();
    }
}

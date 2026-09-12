using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Windows.Threading;

namespace v2rayN;

/// <summary>
/// Exposes the existing in-process profile activation operation to same-user local clients.
/// The protocol deliberately contains no subscription URLs or authentication fields.
/// </summary>
public sealed class ArenaControlServer : IDisposable
{
    private const int ProtocolVersion = 1;
    private readonly Config _config;
    private readonly MainWindowViewModel _viewModel;
    private readonly Dispatcher _dispatcher;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly string _pipeName;
    private readonly string _endpointPath;

    public ArenaControlServer(Config config, MainWindowViewModel viewModel, Dispatcher dispatcher)
    {
        _config = config;
        _viewModel = viewModel;
        _dispatcher = dispatcher;
        var pid = Environment.ProcessId;
        _pipeName = $"ArenaV2rayN.{pid}.{Guid.NewGuid():N}";
        var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ArenaV2rayNBridge");
        Directory.CreateDirectory(directory);
        _endpointPath = Path.Combine(directory, $"{pid}.json");
        WriteEndpoint();
        _ = Task.Run(ServeAsync);
    }

    private void WriteEndpoint()
    {
        var payload = new
        {
            protocol = ProtocolVersion,
            pipe = _pipeName,
            pid = Environment.ProcessId,
            executable = Environment.ProcessPath,
            version = Utils.GetVersion()
        };
        var temporary = _endpointPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(payload), new UTF8Encoding(false));
        File.Move(temporary, _endpointPath, true);
    }

    private async Task ServeAsync()
    {
        while (!_cancellation.IsCancellationRequested)
        {
            try
            {
                await using var pipe = new NamedPipeServerStream(
                    _pipeName,
                    PipeDirection.InOut,
                    1,
                    PipeTransmissionMode.Byte,
                    PipeOptions.Asynchronous | PipeOptions.CurrentUserOnly);
                await pipe.WaitForConnectionAsync(_cancellation.Token);
                using var reader = new StreamReader(pipe, Encoding.UTF8, false, 4096, true);
                using var writer = new StreamWriter(pipe, new UTF8Encoding(false), 4096, true) { AutoFlush = true };
                var line = await reader.ReadLineAsync(_cancellation.Token);
                if (line is null)
                {
                    continue;
                }
                object response;
                try
                {
                    response = await DispatchAsync(JsonSerializer.Deserialize<ControlRequest>(line));
                }
                catch (Exception ex)
                {
                    response = new { ok = false, error = ex.Message };
                }
                await writer.WriteLineAsync(JsonSerializer.Serialize(response));
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception ex)
            {
                Logging.SaveLog("ArenaControlServer", ex);
                try
                {
                    await Task.Delay(250, _cancellation.Token);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }

    private async Task<object> DispatchAsync(ControlRequest? request)
    {
        if (request?.Command == "profiles")
        {
            return await ProfilesAsync();
        }
        if (request?.Command == "activate")
        {
            if (request.IndexId.IsNullOrEmpty())
            {
                throw new InvalidOperationException("Missing profile indexId");
            }
            return await ActivateAsync(request.IndexId);
        }
        if (request?.Command == "status")
        {
            return new { ok = true, protocol = ProtocolVersion, currentIndexId = _config.IndexId };
        }
        throw new InvalidOperationException("Unsupported command");
    }

    private async Task<object> ProfilesAsync()
    {
        var profiles = await AppHandler.Instance.ProfileItems(string.Empty) ?? [];
        var measurements = await ProfileExHandler.Instance.GetProfileExs();
        var nodes = profiles
            .Where(item => item.ConfigType != EConfigType.Custom && item.Port > 0 && item.Address.IsNotEmpty())
            .Select(item =>
            {
                var measurement = measurements.FirstOrDefault(value => value.IndexId == item.IndexId);
                return new
                {
                    indexId = item.IndexId,
                    name = item.Remarks,
                    address = item.Address,
                    port = item.Port,
                    type = item.ConfigType.ToString(),
                    delay = measurement?.Delay ?? 0,
                    current = item.IndexId == _config.IndexId
                };
            })
            .ToList();
        return new { ok = true, protocol = ProtocolVersion, currentIndexId = _config.IndexId, nodes };
    }

    private async Task<object> ActivateAsync(string indexId)
    {
        var item = await AppHandler.Instance.GetProfileItem(indexId);
        if (item is null || item.ConfigType == EConfigType.Custom || item.Port <= 0 || item.Address.IsNullOrEmpty())
        {
            throw new InvalidOperationException("Profile is missing or cannot be activated by the IP cycle");
        }
        if (indexId != _config.IndexId)
        {
            await _dispatcher.InvokeAsync(async () =>
            {
                if (await ConfigHandler.SetDefaultServerIndex(_config, indexId) != 0)
                {
                    throw new InvalidOperationException("v2rayN could not save the active profile");
                }
                // The local proxy listener does not change when only the active profile changes.
                // Reload the core directly so this control path never writes Windows system-proxy settings.
                await CoreHandler.Instance.LoadCore(item);
                _viewModel.ReloadResult();
            }).Task.Unwrap();
        }
        return new
        {
            ok = true,
            currentIndexId = _config.IndexId,
            node = new { indexId = item.IndexId, name = item.Remarks, address = item.Address, port = item.Port }
        };
    }

    public void Dispose()
    {
        _cancellation.Cancel();
        try
        {
            if (File.Exists(_endpointPath))
            {
                using var document = JsonDocument.Parse(File.ReadAllText(_endpointPath));
                if (document.RootElement.TryGetProperty("pipe", out var pipe) && pipe.GetString() == _pipeName)
                {
                    File.Delete(_endpointPath);
                }
            }
        }
        catch (IOException)
        {
        }
        _cancellation.Dispose();
    }

    private sealed class ControlRequest
    {
        public string? Command { get; set; }
        public string? IndexId { get; set; }
    }
}

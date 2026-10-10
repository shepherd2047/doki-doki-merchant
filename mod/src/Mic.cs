using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Godot;
using MegaCrit.Sts2.Core.Logging;

namespace DokiDokiMerchant;

/// <summary>Push-to-talk recording. Start when the key goes down, Stop returns a WAV clip when it comes up.</summary>
internal interface IMic : IDisposable
{
    /// <summary>Why the mic can't be used yet, or null when it's ready.</summary>
    string? Problem { get; }
    void Start();
    Task<byte[]?> Stop();
    void Poll();

    static IMic Create(Node parent) =>
        OperatingSystem.IsMacOS() ? new HelperMic() : new GodotMic(parent);
}

/// <summary>No Fish Audio key: nothing to transcribe with, so the mic stays off and the player types.</summary>
internal sealed class NoMic : IMic
{
    public string? Problem => Loc.T("Voice is off: add fish_api_key to DokiDokiMerchant.cfg to talk out loud. Typing works.",
        "语音未开启：在 DokiDokiMerchant.cfg 里填入 fish_api_key 才能语音对话。打字可以正常使用。");
    public void Start() { }
    public Task<byte[]?> Stop() => Task.FromResult<byte[]?>(null);
    public void Poll() { }
    public void Dispose() { }
}

/// <summary>
/// Windows/Linux: Godot's own capture. The game ships with audio input switched off, but the engine checks the
/// setting when a microphone stream starts, so turning it on at runtime is enough.
/// </summary>
internal sealed class GodotMic : IMic
{
    private const string BusName = "DokiMic";
    private readonly AudioStreamPlayer _player;
    private readonly AudioEffectCapture _capture;
    private readonly List<short> _samples = new();
    private bool _recording;

    public string? Problem => null;

    public GodotMic(Node parent)
    {
        ProjectSettings.SetSetting("audio/driver/enable_input", true);
        var bus = AudioServer.GetBusIndex(BusName);
        if (bus < 0)
        {
            AudioServer.AddBus();
            bus = AudioServer.BusCount - 1;
            AudioServer.SetBusName(bus, BusName);
            AudioServer.AddBusEffect(bus, new AudioEffectCapture());
            AudioServer.SetBusMute(bus, true); // record it, don't play your own voice back
        }
        _capture = (AudioEffectCapture)AudioServer.GetBusEffect(bus, 0);
        _player = new AudioStreamPlayer { Stream = new AudioStreamMicrophone(), Bus = BusName, Name = "DokiMicPlayer" };
        parent.AddChild(_player);
    }

    public void Start()
    {
        _samples.Clear();
        _capture.ClearBuffer();
        _player.Play();
        _recording = true;
    }

    public void Poll()
    {
        if (!_recording) return;
        var n = _capture.GetFramesAvailable();
        if (n <= 0) return;
        foreach (var f in _capture.GetBuffer(n))
            _samples.Add((short)Math.Clamp((f.X + f.Y) * 0.5f * 32767f, short.MinValue, short.MaxValue));
    }

    public Task<byte[]?> Stop()
    {
        Poll();
        _recording = false;
        _player.Stop();
        var rate = (int)AudioServer.GetMixRate();
        return Task.FromResult<byte[]?>(_samples.Count < rate / 5 ? null : Fish.Wav(_samples.ToArray(), rate));
    }

    public void Dispose()
    {
        if (GodotObject.IsInstanceValid(_player)) _player.QueueFree();
    }
}

/// <summary>
/// macOS: the game app has no microphone permission, so a tiny helper app that ships with the mod
/// (DokiMicHelper.app, with its own permission prompt) records instead. Protocol over a localhost socket:
/// we send 'S' to start and 'E' to end; the helper answers 'E' with a 4-byte little-endian length and a WAV clip.
/// </summary>
internal sealed class HelperMic : IMic
{
    private readonly TcpListener _listener;
    private TcpClient? _client;
    private NetworkStream? _stream;
    /// <summary>The Problem while the helper is still starting up (not a real failure).</summary>
    public static readonly string Starting = "Starting the microphone helper…";
    private string? _problem = Starting;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public string? Problem => _problem;

    private static string Disconnected => Loc.T("The microphone helper disconnected. Typing still works.", "麦克风助手断开了。仍然可以打字。");

    public HelperMic()
    {
        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start();
        var port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        var app = Path.Combine(Config.ModDir, "DokiMicHelper.app");
        if (!Directory.Exists(app))
        {
            _problem = Loc.T("Microphone helper is missing (DokiMicHelper.app). Typing still works.", "缺少麦克风助手（DokiMicHelper.app）。仍然可以打字。");
            return;
        }
        try
        {
            // -g: don't steal focus from the game. -n: a fresh instance that connects to this port.
            Process.Start(new ProcessStartInfo("/usr/bin/open", $"-g -n \"{app}\" --args {port}") { UseShellExecute = false });
        }
        catch (Exception e)
        {
            _problem = Loc.T("Could not start the microphone helper. Typing still works.", "无法启动麦克风助手。仍然可以打字。");
            Log.Warn($"[Doki] open DokiMicHelper.app failed: {e.Message}");
            return;
        }
        _ = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            var accept = _listener.AcceptTcpClientAsync();
            if (await Task.WhenAny(accept, Task.Delay(TimeSpan.FromSeconds(30))) != accept)
            {
                _problem = Loc.T("The microphone helper didn't answer. Typing still works.", "麦克风助手没有响应。仍然可以打字。");
                return;
            }
            _client = accept.Result;
            _client.NoDelay = true;
            _stream = _client.GetStream();
            // The helper sends one byte once it has (or was refused) microphone access: 'Y' or 'N'.
            var b = new byte[1];
            var n = await _stream.ReadAsync(b);
            _problem = n == 1 && b[0] == (byte)'Y'
                ? null
                : Loc.T("Microphone access was refused. Allow DokiMicHelper in System Settings › Privacy & Security › Microphone. Typing still works.",
                    "麦克风权限被拒绝。请在 系统设置 › 隐私与安全性 › 麦克风 中允许 DokiMicHelper。仍然可以打字。");
        }
        catch (Exception e)
        {
            _problem = Disconnected;
            Log.Warn($"[Doki] Mic helper connection failed: {e.Message}");
        }
    }

    public void Start()
    {
        if (_problem != null || _stream == null) return;
        try { _stream.WriteByte((byte)'S'); }
        catch { _problem = Disconnected; }
    }

    public async Task<byte[]?> Stop()
    {
        if (_problem != null || _stream == null) return null;
        await _lock.WaitAsync();
        try
        {
            _stream.WriteByte((byte)'E');
            var head = new byte[4];
            await _stream.ReadExactlyAsync(head);
            var len = BitConverter.ToInt32(head);
            if (len <= 44) return null;
            var wav = new byte[len];
            await _stream.ReadExactlyAsync(wav);
            return wav;
        }
        catch (Exception e)
        {
            _problem = Disconnected;
            Log.Warn($"[Doki] Mic helper read failed: {e.Message}");
            return null;
        }
        finally
        {
            _lock.Release();
        }
    }

    public void Poll() { }

    public void Dispose()
    {
        // Closing the socket tells the helper to quit.
        try { _client?.Close(); } catch { }
        try { _listener.Stop(); } catch { }
    }
}

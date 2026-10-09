using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Logging;

namespace DokiDokiMerchant;

/// <summary>Fish Audio: speech to text (/v1/asr) and the designed voices (/v1/tts).</summary>
internal static class Fish
{
    public const int SampleRate = 44100;
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(60) };

    /// <summary>Speak a line in one of the designed voices. Returns 16-bit mono PCM at <see cref="SampleRate"/>.</summary>
    public static async Task<byte[]> Speak(Config cfg, string voiceId, string text)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.fish.audio/v1/tts");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.FishKey);
        req.Headers.Add("model", cfg.FishTtsModel);
        var body = new JsonObject
        {
            ["text"] = text,
            ["reference_id"] = voiceId,
            ["format"] = "pcm",
            ["sample_rate"] = SampleRate,
            ["latency"] = "balanced",
        };
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req);
        var bytes = await res.Content.ReadAsByteArrayAsync();
        if (!res.IsSuccessStatusCode)
        {
            var msg = Encoding.UTF8.GetString(bytes, 0, Math.Min(300, bytes.Length));
            Log.Warn($"[Doki] Fish TTS answered {(int)res.StatusCode}: {msg}");
            throw new InvalidOperationException($"Fish TTS answered {(int)res.StatusCode}.");
        }
        return bytes;
    }

    /// <summary>Transcribe one push-to-talk clip (a WAV file).</summary>
    public static async Task<string> Transcribe(Config cfg, byte[] wav)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, "https://api.fish.audio/v1/asr");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.FishKey);
        req.Headers.Add("model", "transcribe-1");
        var form = new MultipartFormDataContent();
        var audio = new ByteArrayContent(wav);
        audio.Headers.ContentType = new MediaTypeHeaderValue("audio/wav");
        form.Add(audio, "audio", "clip.wav");
        form.Add(new StringContent(cfg.Language), "language");
        form.Add(new StringContent("false"), "tag_audio_events");
        req.Content = form;
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            Log.Warn($"[Doki] Fish ASR answered {(int)res.StatusCode}: {text[..Math.Min(300, text.Length)]}");
            return "";
        }
        var t = JsonNode.Parse(text)?["text"]?.GetValue<string>() ?? "";
        return System.Text.RegularExpressions.Regex.Replace(t, @"<\|speaker:\d+\|>", "").Trim();
    }

    /// <summary>Wrap 16-bit mono PCM in a WAV header.</summary>
    public static byte[] Wav(ReadOnlySpan<short> samples, int rate)
    {
        var data = samples.Length * 2;
        var ms = new MemoryStream(44 + data);
        var w = new BinaryWriter(ms);
        w.Write("RIFF"u8); w.Write(36 + data); w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(rate); w.Write(rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8); w.Write(data);
        foreach (var s in samples) w.Write(s);
        return ms.ToArray();
    }
}

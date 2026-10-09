using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Logging;

namespace DokiDokiMerchant;

/// <summary>
/// The Merchant's brain: an OpenAI-compatible chat model with the web game's tools (check_offer, react,
/// show_item). The game executes the tools locally and feeds results back until the model has a line to say.
/// </summary>
internal sealed class Brain
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(45) };
    private readonly Config _cfg;
    private readonly List<JsonObject> _history = new();
    private readonly Func<string, JsonObject, Task<JsonObject>> _runTool;

    public Brain(Config cfg, Func<string, JsonObject, Task<JsonObject>> runTool)
    {
        _cfg = cfg;
        _runTool = runTool;
    }

    private static readonly JsonArray Tools = new()
    {
        Fn("check_offer",
            "Ask the shop ledger what you think of a price the customer offered for one item. Call this EVERY time the customer names a price. Returns your private verdict and a counter-offer to use.",
            new JsonObject
            {
                ["item"] = new JsonObject { ["type"] = "string", ["description"] = "The item's shelf number or its name." },
                ["price"] = new JsonObject { ["type"] = "number", ["description"] = "The gold amount the customer offered." },
            }, "item", "price"),
        Fn("react",
            "Tell the game how the customer's last words made you feel. Call it whenever the customer says something personal: praise for you or your goods, flirting, friendly chat about you, or an insult. Returns your current affection level and how to act.",
            new JsonObject
            {
                ["kind"] = new JsonObject
                {
                    ["type"] = "string",
                    ["enum"] = new JsonArray("compliment", "flirt", "small_talk", "insult"),
                },
            }, "kind"),
        Fn("show_item",
            "Point at an item on the rug so the customer can see which one you mean. Use whenever you start talking about a specific item.",
            new JsonObject
            {
                ["item"] = new JsonObject { ["type"] = "string", ["description"] = "The item's shelf number or its name." },
            }, "item"),
    };

    private static JsonObject Fn(string name, string desc, JsonObject props, params string[] required) => new()
    {
        ["type"] = "function",
        ["function"] = new JsonObject
        {
            ["name"] = name,
            ["description"] = desc,
            ["parameters"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = props,
                ["required"] = new JsonArray(required.Select(r => (JsonNode)JsonValue.Create(r)!).ToArray()),
            },
        },
    };

    /// <summary>
    /// One turn: the customer's words (or a [GAME] event) in, the Merchant's spoken line out.
    /// The system prompt is rebuilt every turn so prices, gold and mood are always current.
    /// </summary>
    public async Task<string> Turn(string systemPrompt, string userText)
    {
        _history.Add(new JsonObject { ["role"] = "user", ["content"] = userText });
        for (var step = 0; step < 5; step++)
        {
            var messages = new JsonArray { new JsonObject { ["role"] = "system", ["content"] = systemPrompt } };
            foreach (var m in _history.TakeLast(40)) messages.Add(m.DeepClone());
            // Never start the window on an orphaned tool result.
            while (messages.Count > 1 && messages[1]?["role"]?.GetValue<string>() == "tool") messages.RemoveAt(1);

            var reply = await Chat(new JsonObject
            {
                ["model"] = _cfg.OpenAiModel,
                ["messages"] = messages,
                ["tools"] = Tools.DeepClone(),
            });
            var msg = reply?["choices"]?[0]?["message"]?.AsObject();
            if (msg == null) return "";
            var calls = msg["tool_calls"]?.AsArray();
            _history.Add((JsonObject)msg.DeepClone());
            if (calls == null || calls.Count == 0) return msg["content"]?.GetValue<string>() ?? "";

            foreach (var call in calls)
            {
                var name = call?["function"]?["name"]?.GetValue<string>() ?? "";
                JsonObject args;
                try { args = JsonNode.Parse(call?["function"]?["arguments"]?.GetValue<string>() ?? "{}")?.AsObject() ?? new(); }
                catch { args = new JsonObject(); }
                JsonObject result;
                try { result = await _runTool(name, args); }
                catch (Exception e) { result = new JsonObject { ["error"] = e.Message }; }
                _history.Add(new JsonObject
                {
                    ["role"] = "tool",
                    ["tool_call_id"] = call?["id"]?.GetValue<string>(),
                    ["content"] = result.ToJsonString(),
                });
            }
        }
        return "";
    }

    /// <summary>A single, tool-free completion, for the bestie's one-liners.</summary>
    public static async Task<string> OneShot(Config cfg, string system, string user)
    {
        var reply = await ChatWith(cfg, new JsonObject
        {
            ["model"] = cfg.OpenAiModel,
            ["messages"] = new JsonArray
            {
                new JsonObject { ["role"] = "system", ["content"] = system },
                new JsonObject { ["role"] = "user", ["content"] = user },
            },
        });
        return reply?["choices"]?[0]?["message"]?["content"]?.GetValue<string>() ?? "";
    }

    private Task<JsonNode?> Chat(JsonObject body) => ChatWith(_cfg, body);

    private static async Task<JsonNode?> ChatWith(Config cfg, JsonObject body)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, cfg.OpenAiBaseUrl + "/chat/completions");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", cfg.OpenAiKey);
        req.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        using var res = await Http.SendAsync(req);
        var text = await res.Content.ReadAsStringAsync();
        if (!res.IsSuccessStatusCode)
        {
            Log.Warn($"[Doki] Chat model answered {(int)res.StatusCode}: {text[..Math.Min(300, text.Length)]}");
            throw new InvalidOperationException($"The chat model answered {(int)res.StatusCode}.");
        }
        return JsonNode.Parse(text);
    }
}

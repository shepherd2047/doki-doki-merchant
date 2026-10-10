using System.Reflection;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Godot;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace DokiDokiMerchant;

/// <summary>
/// One shop visit: the Doki Doki shop UI that replaces the native shelves (rug, heart meter, dialogue box, mic,
/// chat, stamps), the conversation with the Merchant, the bestie's one-liners, haggled prices and the secret
/// card. Created when a merchant room is ready and torn down when it leaves the tree. Buying still goes
/// through the game's own MerchantEntry purchase code.
/// </summary>
internal sealed class Doki
{
    private static Doki? _current;

    private readonly NMerchantRoom _room;
    private readonly Config _cfg;
    private readonly MerchantInventory _inv;
    private readonly Player _player;
    private readonly string _seed;
    private readonly int _visit;
    private readonly RunRecord _rec;
    private readonly Affection _affection;
    private readonly Haggle _haggle;
    private readonly Brain _brain;
    private IMic? _mic;
    private readonly Key _pttKey;

    private bool _alive = true;
    private bool _talkEnabled = true;
    private int _purchases;
    private int _cardsBought;
    private int _relicsBought;
    private int _haggledOff;
    private bool _secretShown;
    private bool _angerApplied;
    private bool _cappedSaid;
    private bool _buying;
    private string _lastMerchantLine = "";
    private readonly SemaphoreSlim _turnLock = new(1, 1);
    private readonly SemaphoreSlim _voiceLock = new(1, 1);
    private readonly Queue<(string who, string text, byte[] pcm)> _speech = new();
    private bool _recording;
    private bool _recordingByClick;
    private bool _thinking;
    private bool _pttWasDown;
    private string _speakingWho = "";

    // Affection perks and the date
    private bool _potionGift, _removalGift;
    private string? _startGifts;
    private Button? _dateButton;
    private List<Date.Question>? _dateQs;
    private int _dateRound, _dateScore;
    private bool _dateReplying, _dateEnded;

    // UI
    private ShopStage _stage = null!;
    private NativeShop _native = null!;
    private RugView _rug = null!;
    private DetailPopup _detail = null!;
    private HeartMeter _heart = null!;
    private DialogueBox _dialogue = null!;
    private MicButton _micButton = null!;
    private ChatPanel _chat = null!;
    private Fx _fx = null!;
    private Toasts _toasts = null!;
    private DatePanel _date = null!;
    private AudioStreamPlayer _voice = null!;

    // Achievement bookkeeping that spans the whole run (the web game's stats), keyed by run seed.
    private sealed class RunStats { public bool EverRegular; public int FullPrice; }
    private static readonly Dictionary<string, RunStats> Stats = new();
    private RunStats RunStat => Stats.TryGetValue(_seed, out var s) ? s : Stats[_seed] = new RunStats();

    public static void Attach(NMerchantRoom room)
    {
        _current?.Close();
        _current = null;
        if (RunManager.Instance.NetService.Type != NetGameType.Singleplayer)
        {
            Log.Info("[Doki] Multiplayer shop: the Merchant stays quiet (haggling would desync the other players).");
            return;
        }
        try
        {
            _current = new Doki(room);
        }
        catch (Exception e)
        {
            // Never leave the player in a shop with the native shelves hidden and nothing in their place.
            Log.Error($"[Doki] Could not open the Doki Doki shop, falling back to the normal one: {e}");
            NativeShop.Current?.Dispose();
            _current = null;
        }
    }

    public static void Detach(NMerchantRoom room)
    {
        if (_current?._room != room) return;
        _current.Close();
        _current = null;
    }

    private Doki(NMerchantRoom room)
    {
        _room = room;
        _cfg = Config.Load();
        _inv = room.Room.GetLocalInventory();
        _player = _inv.Player;
        _seed = _player.RunState.Rng.StringSeed;
        _rec = RunMemory.Get(_seed);
        _visit = _rec.Visits + 1;
        _affection = new Affection(_rec.Affection);
        _haggle = new Haggle(_inv, _affection, () => _player.Gold);
        _brain = new Brain(_cfg, RunTool);
        _pttKey = Enum.TryParse<Key>(_cfg.PushToTalkKey, true, out var k) ? k : Key.V;

        BuildUi();
        SaveRun();
        _affection.Changed += OnAffectionChanged;
        foreach (var it in _haggle.Items)
        {
            _boughtName[it.Entry] = ItemText.Name(it);
            it.Entry.PurchaseCompleted += OnPurchased;
        }
        room.GetTree().ProcessFrame += OnFrame;
        OnVisitStart();

        var problem = _cfg.Problem;
        if (problem != null)
        {
            _dialogue.Say("system", problem, 0);
            _chat.Add("system", problem);
            SetTalkEnabled(false);
            return;
        }
        _mic = _cfg.HasVoice ? IMic.Create(_stage.Canvas) : new NoMic();
        if (!_cfg.HasVoice) _chat.Add("system", _mic.Problem!);
        TaskHelper.RunSafely(Greet());
    }

    private void Close()
    {
        if (!_alive) return;
        _alive = false;
        try
        {
            if (_purchases == 0)
            {
                _affection.LeftEmptyHanded();
                _rec.EmptyVisits++;
            }
            SaveRun();
            foreach (var it in _haggle.Items) it.Entry.PurchaseCompleted -= OnPurchased;
            if (GodotObject.IsInstanceValid(_room)) _room.GetTree().ProcessFrame -= OnFrame;
        }
        finally
        {
            Haggle.AgreedPrices.Clear();
            Haggle.AngerTax.Clear();
            _mic?.Dispose();
            _native?.Dispose();
            _stage?.Free();
        }
    }

    // ───────────────────────── UI ─────────────────────────

    private void BuildUi()
    {
        Theme.Init(_room);
        _stage = new ShopStage(_room);
        _stage.Canvas.Layer = NativeShop.SuggestedLayer;
        _native = new NativeShop(_room, _stage.Hud);
        // Draw on the game's own canvas, above the room but under the top bar, map, hover tips and modals.
        if (_native.EmbedBelowGameUi(_stage.Root)) _stage.Fit();
        _native.NativeLine += line => _dialogue?.Say("merchant", line, 3.5f);

        _rug = new RugView(_stage.Rug, _haggle.Items, () => _player.Gold);
        _rug.Clicked += OnItemClicked;
        _heart = new HeartMeter(_stage.Hud, _native.MerchantVisual);
        _dialogue = new DialogueBox(_stage.Hud);
        _chat = new ChatPanel(_stage.Hud);
        _chat.Submitted += OnTyped;
        _micButton = new MicButton(_stage.Hud);
        _micButton.Pressed += OnMicClicked;
        _detail = new DetailPopup(_stage.Popups);
        _detail.BuyPressed += it => TaskHelper.RunSafely(Buy(it));
        _detail.Closed += () => _rug.SetSelected(null);
        _date = new DatePanel(_stage.Popups);
        _date.Picked += OnDatePicked;
        _fx = new Fx(_stage.Fx, _stage.Root);
        _toasts = new Toasts(_stage.Toasts);

        _voice = new AudioStreamPlayer { Name = "DokiVoice" };
        _stage.Canvas.AddChild(_voice);

        _rug.DropIn();
        UpdateMeter();
        UpdateInfo();
    }

    private void SetTalkEnabled(bool on)
    {
        _talkEnabled = on;
        _chat.SetEnabled(on);
        _micButton.SetState(on ? MicState.Idle : MicState.Disabled);
    }

    private void UpdateMeter()
    {
        _heart.Set(_affection.Hearts, _affection.Value / 100f, _affection.Index + 1, _affection.LevelName, _affection.Index >= 3);
        if (_affection.Index >= 2) RunStat.EverRegular = true;
    }

    private void UpdateInfo()
    {
        string Plural(int n, string w) => $"{n} {w}{(n == 1 ? "" : "s")}";
        var act = "";
        try { act = Loc.T($"Act {_player.RunState.CurrentActIndex + 1} · ", $"第 {_player.RunState.CurrentActIndex + 1} 幕 · "); } catch { }
        var text = Loc.Zh
            ? $"已购：{_cardsBought} 张牌，{_relicsBought} 件遗物  ·  {act}第 {_player.RunState.TotalFloor} 层  ·  第 {_visit} 次光顾"
            : $"Bought: {Plural(_cardsBought, "card")}, {Plural(_relicsBought, "relic")}  ·  {act}Floor {_player.RunState.TotalFloor}  ·  Visit {_visit}";
        if (_haggledOff > 0) text += Loc.T($"  ·  Haggled off: {_haggledOff} gold", $"  ·  砍价省下：{_haggledOff} 金币");
        _native.SetInfo(text);
    }

    private void RefreshShop()
    {
        foreach (var it in _haggle.Items) it.Entry.OnMerchantInventoryUpdated();
        _rug.Refresh();
        if (_detail.IsOpen) _detail.Refresh(_player.Gold);
        UpdateInfo();
    }

    private void OnItemClicked(Haggle.Item it)
    {
        if (_detail.IsOpen && _detail.Item == it)
        {
            _detail.Hide();
            _rug.SetSelected(null);
            return;
        }
        _detail.Show(it, _player.Gold);
        _rug.SetSelected(it);
    }

    /// <summary>Buy through the game's own purchase code: gold, relic hooks, The Courier, the card-removal screen.</summary>
    private async Task Buy(Haggle.Item it)
    {
        if (_buying || !_alive) return;
        _buying = true;
        try
        {
            if (it.Kind == "service") { _detail.Hide(); _rug.SetSelected(null); } // the card picker opens over the room
            var ok = await it.Entry.OnTryPurchaseWrapper(_inv);
            if (!_alive) return;
            if (!ok && it.Entry.IsStocked && it.Entry.Cost > _player.Gold)
            {
                _dialogue.Shake();
                TaskHelper.RunSafely(Event($"The customer tried to pick up {ItemText.Name(it)} ({it.Entry.Cost} gold) but only has {_player.Gold} gold."));
            }
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Purchase failed: {e}");
        }
        finally
        {
            _buying = false;
            if (_alive)
            {
                RefreshShop();
                if (_detail.IsOpen && !(_detail.Item?.Entry.IsStocked ?? false))
                {
                    _detail.Hide();
                    _rug.SetSelected(null);
                }
            }
        }
    }

    // ───────────────────────── conversation ─────────────────────────

    private async Task Greet()
    {
        await Task.Delay(1200);
        var ret = _visit > 1 ? $"They've been to your shop {_visit - 1} time(s) before this climb." : "First time you've seen them.";
        if (_startGifts != null) ret += $" {_startGifts} Mention it grudgingly, as if it's nothing.";
        if (_rec.FactsBeforeVisit > 0) ret += " Bring up one thing you remember about them.";
        await Event($"The customer (the {CharacterName()}) walks into your tent on floor {_player.RunState.TotalFloor}. {ret} Greet them in one or two short sentences.");
    }

    private static readonly Regex Catchphrase = new(
        @"isn'?t a charity|little richer|deal in credit|local business|不是慈善|有钱了再来|不赊账|本地生意|小本生意",
        RegexOptions.IgnoreCase);

    private async Task Say(string customerWords)
    {
        if (Catchphrase.IsMatch(customerWords)) _toasts.Unlock("parrot");
        _affection.NewTurn();
        await RunTurn(customerWords);
    }

    private Task Event(string what) => RunTurn("[GAME] " + what);

    private async Task RunTurn(string text)
    {
        await _turnLock.WaitAsync();
        try
        {
            if (!_alive || !_talkEnabled) return;
            _thinking = true;
            _haggle.RefreshListPrices();
            var line = await _brain.Turn(SystemPrompt(), text);
            if (!_alive || string.IsNullOrWhiteSpace(line)) return;
            _lastMerchantLine = StripTags(line);
            await Speak("merchant", line);
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Turn failed: {e.Message}");
            if (_alive)
            {
                var msg = e.Message.Contains("401")
                    ? Loc.T("The chat model refused the API key (401). Check openai_api_key in DokiDokiMerchant.cfg, then re-enter the shop.",
                        "聊天模型拒绝了 API key（401）。检查 DokiDokiMerchant.cfg 里的 openai_api_key，然后重新进入商店。")
                    : Loc.T("The Merchant lost his train of thought (network error). Try again.", "商人走神了（网络错误）。再试一次。");
                _dialogue.Say("system", msg, 0);
                _chat.Add("system", msg);
            }
        }
        finally
        {
            _thinking = false;
            _turnLock.Release();
        }
    }

    private async Task<JsonObject> RunTool(string name, JsonObject args)
    {
        await Task.Yield();
        if (!_alive) return new JsonObject { ["error"] = "The shop closed." };
        switch (name)
        {
            case "check_offer":
            {
                var target = _haggle.Resolve(Str(args["item"]));
                var res = _haggle.Check(Str(args["item"]), Str(args["price"]), out var insulted);
                var verdict = res["verdict"]?.GetValue<string>();
                if (target != null) _rug.Point(target);
                int Num(string key) => res[key]?.GetValue<int>() ?? 0;
                switch (verdict)
                {
                    case "accept":
                    {
                        var offer = Num("offer");
                        var list = Num("list_price");
                        _fx.Stamp(Loc.T($"♥ DEAL! {offer}g", $"♥ 成交！{offer} 金"), StampKind.Deal,
                            offer < list ? Loc.T($"{list - offer}g off", $"便宜了 {list - offer} 金") : Loc.T("full price… he's thrilled", "原价……他乐坏了"));
                        RefreshShop();
                        break;
                    }
                    case "counter":
                        _fx.Stamp(Loc.T($"COUNTER: {Num("counter_offer")}g", $"还价：{Num("counter_offer")} 金"), StampKind.Info, Loc.T($"you offered {Num("offer")}g", $"你出价 {Num("offer")} 金"));
                        break;
                    case "final":
                        _fx.Stamp(Loc.T($"FINAL OFFER: {Num("counter_offer")}g", $"最终报价：{Num("counter_offer")} 金"), StampKind.Info, Loc.T("his absolute floor", "他的底线了"));
                        break;
                }
                if (insulted)
                {
                    _affection.Lowball();
                    _fx.Stamp(Loc.T("LOWBALL!", "砍太狠了！"), StampKind.Bad,
                        _haggle.Angry ? Loc.T("STRIKE 3/3 · uh oh", "警告 3/3 · 完了") : Loc.T($"STRIKE {_haggle.InsultStreak}/3", $"警告 {_haggle.InsultStreak}/3"));
                    _dialogue.Shake(); // the Bad stamp jolts the stage itself
                    if (_haggle.Angry && !_angerApplied)
                    {
                        _angerApplied = true;
                        _haggle.ApplyAngerTax();
                        RefreshShop();
                        _toasts.Unlock("cheapskate");
                        _fx.Vignette(false, Loc.T("FURIOUS!", "暴怒！"), Loc.T("every price +50%", "全部涨价 50%"));
                        Bestie("He's furious: three insulting lowballs in a row, and now every price is up by half.");
                    }
                    else if (_haggle.InsultStreak == 2)
                    {
                        Bestie("Two insulting lowballs in a row. The Merchant is about to snap.");
                    }
                }
                return res;
            }
            case "react":
            {
                var kind = (Str(args["kind"]) ?? "").Trim().ToLowerInvariant();
                var wasStranger = _affection.Index == 0;
                var res = _affection.React(kind);
                if (res["applied"]?.GetValue<bool>() == true)
                {
                    var delta = res["delta"]?.GetValue<int>() ?? 0;
                    var d = delta > 0 ? $"+{delta} ♥" : $"−{-delta} ♥";
                    var capped = res["capped"]?.GetValue<bool>() == true ? Loc.T(" (maxed for this floor)", "（本层已满）") : "";
                    switch (kind)
                    {
                        case "flirt":
                            if (wasStranger && delta <= 2) _fx.Stamp(Loc.T("FLIRT…?", "撩……？"), StampKind.Love, Loc.T($"he's not there yet · {d}", $"他还没那么熟 · {d}"));
                            else _fx.Stamp(Loc.T("CRITICAL FLIRT!", "暴击撩拨！"), StampKind.Love, d + capped);
                            break;
                        case "compliment":
                            _fx.Stamp(Loc.T("SWEET TALK!", "甜言蜜语！"), StampKind.Love, d + capped);
                            break;
                        case "small_talk":
                            _fx.Stamp(Loc.T("SMALL TALK", "闲聊"), StampKind.Info, d + capped);
                            break;
                        default:
                            _fx.Stamp(Loc.T("RUDE!", "无礼！"), StampKind.Bad, d);
                            _dialogue.Shake();
                            break;
                    }
                }
                return res;
            }
            case "remember":
            {
                if (!_rec.AddFact(Str(args["fact"]) ?? ""))
                    return new JsonObject { ["saved"] = false, ["reason"] = "empty or already known" };
                SaveRun();
                _fx.Note(Loc.T("He'll remember that ♥", "他记住了 ♥"), _heart.PopAnchor);
                return new JsonObject { ["saved"] = true };
            }
            case "show_item":
            {
                var it = _haggle.Resolve(Str(args["item"]));
                if (it == null) return new JsonObject { ["error"] = "No such item on the rug." };
                _rug.Point(it);
                _detail.Show(it, _player.Gold);
                _rug.SetSelected(it);
                return new JsonObject { ["shown"] = ItemText.Name(it) };
            }
            default:
                return new JsonObject { ["error"] = $"Unknown tool {name}" };
        }
    }

    private static string? Str(JsonNode? n) => n switch
    {
        null => null,
        JsonValue v when v.TryGetValue(out string? s) => s,
        _ => n.ToString(),
    };

    private string SystemPrompt()
    {
        var goods = string.Join("\n", _haggle.OnRug.Select(i =>
        {
            var price = Haggle.AngerTax.Contains(i.Entry) ? $"{(int)Math.Round(i.ListPrice * 1.5)} gold (furious markup)"
                : i.Agreed is { } g && i.Gift ? $"{g} gold (your gift to them, normally {i.ListPrice})"
                : i.Agreed is { } a ? $"{a} gold (agreed, was {i.ListPrice})"
                : $"{i.ListPrice} gold";
            return $"#{i.Shelf} {ItemText.Name(i)} [{i.Kind}{(i.Secret ? ", your secret card" : "")}], {price}. {ItemText.Describe(i)}";
        }));
        var secret = _secretShown ? "Already revealed." : "Not yet. Never mention it.";
        return $@"You are the Merchant from Slay the Spire 2: a hooded, blue-robed trader who sits cross-legged on a rug inside a tent in the Spire. You are calm, sly, dryly funny and a little smug, and you love gold. Your catchphrases include ""This isn't a charity."", ""Come back when you're a little richer."", ""I don't deal in credit."" and ""Support your local business.""

The customer is the {CharacterName()}, climbing the Spire. They have {_player.Gold} gold.

Today's goods, by shelf number:
{goods}

How you trade:
- You speak out loud, so every reply is one or two short sentences. Never read the list aloud; mention at most two items at a time.
- Whenever the customer names a price for an item, call check_offer first and follow its verdict. Never go below a price it rejected. Never reveal that a ledger or minimum exists.
- Haggle with personality: act wounded by lowballs, praise the item's power (use its description), invent little stories about where you got it, and make counter-offers.
- ""accept"": agree warmly; the price on the rug changes to the deal and the customer picks the item up themselves. ""counter"": make that counter-offer in your own words. ""insulted"": act offended and hold firm. ""final"": say it's your final price.
- Call show_item when you start talking about a particular item.
- Messages starting with [GAME] are things happening in the shop, not the customer speaking. React to them briefly in character.
- You may begin a line with one short emotion tag in square brackets for your voice, e.g. [smug], [whispering], [shrieking], [flustered], [sighs]. No other markup, no emojis, no stage directions in asterisks.

Your heart (this is secretly a dating sim, ""Doki Doki Merchant"", and you are the only route):
- How you feel about this customer right now: {_affection.LevelName}. {_affection.Mood}
- This is their visit #{_visit} to your shop this climb. If it isn't their first, recognise them and act on how you feel about them.
- Whenever the customer compliments you or your wares, flirts, chats about you, or insults you, call react with the right kind, and act the way its mood_note says.
- The levels go Stranger, Customer, Regular, Favourite, Darling. Cold with strangers; smug and teasing with regulars; flustered, coy and tsundere with favourites (""I-it's not a discount because I *like* you or anything!""); openly smitten with a darling, but still a schemer who loves gold.
- Never mention numbers, meters, points or levels by name. Show it through tone.
- You have one secret Rare card under the counter. Revealed: {secret} When a [GAME] message says SECRET UNLOCKED, reveal it in a hushed, flustered whisper.
{MemoryPrompt()}
- Only at Darling, you may break the fourth wall once, briefly, e.g. ""My counterpart in the unmodded game just sits there, you know. Mute. Tragic."" or ""Don't tell Neow about us.""
- {(Loc.Zh ? "Speak Simplified Chinese (简体中文) unless the customer clearly writes in another language. Keep emotion tags in English. Use item names exactly as listed above." : "Reply in the customer's language (English by default).")}";
    }

    private string MemoryPrompt()
    {
        var lines = new List<string>
        {
            "- When the customer tells you something personal worth remembering (their name, what they like or hate, a promise), call remember. Never say you're taking notes.",
        };
        if (_rec.Facts.Count > 0)
            lines.Add("- What you remember about them from this climb (bring one up naturally now and then, never as a list):\n"
                      + string.Join("\n", _rec.Facts.Select(f => "  * " + f)));
        if (_affection.Index >= 4 && !_rec.Dated)
            lines.Add("- You're smitten enough to want a date, and a \"Date\" button is now on their screen. You may hint once, flustered, that you'd close the shop early for them.");
        if (_rec.Dated)
            lines.Add("- You've already been on a date with them this climb. You think about it more than you'd admit.");
        return string.Join("\n", lines);
    }

    private string CharacterName()
    {
        try { return _player.Character.Title.GetFormattedText(); }
        catch { return "adventurer"; }
    }

    // ───────────────────────── bestie ─────────────────────────

    private const string BestiePrompt =
        @"You are the player's gossipy, loving best friend in ""Doki Doki Merchant"", a dating-sim parody of Slay the Spire 2. The player is flirting and haggling with the hooded Merchant in his shop while you watch from the sidelines.
You receive one thing that just happened, plus what the Merchant just said. Reply with ONE short spoken reaction to the player, at most 16 words, specific to what happened. Call the player ""babe"". Be dramatic, giddy, supportive, a little teasing; warn them when things go badly.
You may start with one emotion tag in square brackets, e.g. [gasps], [giggles], [whispering], [squealing], [cringing]. Never talk to the Merchant, no emojis, no quotation marks.";

    private const string BestieChinese =
        "\nSpeak Simplified Chinese (简体中文), at most 30 characters, and call the player \"宝\" or \"姐妹\" instead of \"babe\". Keep any emotion tag in English.";

    private void Bestie(string what)
    {
        if (!_cfg.BestieEnabled || !_talkEnabled) return;
        TaskHelper.RunSafely(BestieAsync(what));
    }

    private async Task BestieAsync(string what)
    {
        // Let the Merchant's reply to this moment land first; his voice and hers share one queue.
        await Task.Delay(400);
        await _turnLock.WaitAsync();
        _turnLock.Release();
        try
        {
            var prompt = Loc.Zh ? BestiePrompt + BestieChinese : BestiePrompt;
            var line = await Brain.OneShot(_cfg, prompt, $"{what}\nThe Merchant just said: {_lastMerchantLine}");
            if (_alive && !string.IsNullOrWhiteSpace(line)) await Speak("bestie", line);
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Bestie failed: {e.Message}");
        }
    }

    // ───────────────────────── voice ─────────────────────────

    private async Task Speak(string who, string text)
    {
        byte[] pcm = Array.Empty<byte>();
        if (!_cfg.HasVoice)
        {
            if (_alive) _speech.Enqueue((who, StripTags(text), pcm)); // text only
            return;
        }
        await _voiceLock.WaitAsync();
        try
        {
            pcm = await Fish.Speak(_cfg, who == "bestie" ? _cfg.BestieVoiceId : _cfg.MerchantVoiceId, text);
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Voice failed: {e.Message}");
            pcm = Array.Empty<byte>(); // still show the line
        }
        finally
        {
            _voiceLock.Release();
        }
        if (_alive) _speech.Enqueue((who, StripTags(text), pcm));
    }

    private static string StripTags(string s) => Regex.Replace(s, @"\[[^\]]{1,40}\]\s*", "").Trim();

    private ulong _holdUntil;

    private void PlayNext()
    {
        if (_voice.Playing || _speech.Count == 0 || _dialogue.IsTyping || Time.GetTicksMsec() < _holdUntil) return;
        var (who, text, pcm) = _speech.Dequeue();
        var seconds = pcm.Length / 2.0 / Fish.SampleRate;
        _speakingWho = who;
        _dialogue.Say(who, text, seconds);
        _chat.Add(who, text);
        if (pcm.Length == 0) { _holdUntil = Time.GetTicksMsec() + 1500; return; }
        _voice.Stream = new AudioStreamWav
        {
            Data = pcm,
            Format = AudioStreamWav.FormatEnum.Format16Bits,
            MixRate = Fish.SampleRate,
            Stereo = false,
        };
        _voice.Play();
    }

    // ───────────────────────── input ─────────────────────────

    private void OnFrame()
    {
        if (!_alive) return;
        _stage.Root.Visible = !_native.ShouldHideShopUi;
        _mic?.Poll();
        PlayNext();

        var speaking = _voice.Playing;
        _heart.SetSpeaking(speaking && _speakingWho == "merchant");

        if (!_talkEnabled)
        {
            _micButton.SetState(MicState.Disabled);
            _micButton.SetStatus(Loc.T("Typing and talking are off", "打字和语音已关闭"));
            return;
        }

        // Hold the push-to-talk key, or click the mic to start and click again to send.
        var keyDown = _stage.Canvas.Visible && Input.IsPhysicalKeyPressed(_pttKey) && !_chat.HasFocus;
        if (keyDown && !_pttWasDown && !_recording) StartTalking(byClick: false);
        else if (!keyDown && _pttWasDown && _recording && !_recordingByClick) StopTalking();
        _pttWasDown = keyDown;

        var problem = _mic?.Problem;
        _micButton.SetState(_recording ? MicState.Listening : _thinking ? MicState.Thinking : speaking ? MicState.Speaking : MicState.Idle);
        _micButton.SetStatus(problem == HelperMic.Starting ? Loc.T("Waking the mic…", "正在唤醒麦克风……")
            : problem != null ? Loc.T("Mic off · type below", "麦克风不可用 · 请打字")
            : _recording ? (_recordingByClick ? Loc.T("Listening… tap to send", "正在听……点一下发送") : Loc.T($"Listening… release {_pttKey}", $"正在听……松开 {_pttKey}"))
            : _thinking ? Loc.T("He's thinking…", "他在想……")
            : speaking ? Loc.T("Tap to interrupt", "点一下打断")
            : Loc.T($"Tap or hold {_pttKey} to talk", $"点一下或按住 {_pttKey} 说话"));
    }

    private void OnMicClicked()
    {
        if (!_talkEnabled) return;
        if (_recording) StopTalking();
        else if (_mic?.Problem is { } p && p != HelperMic.Starting)
        {
            _dialogue.Say("system", p, 0);
            _chat.Add("system", p);
        }
        else StartTalking(byClick: true);
    }

    private void StartTalking(bool byClick)
    {
        if (_mic == null || _mic.Problem != null) return;
        _recording = true;
        _recordingByClick = byClick;
        _voice.Stop(); // you interrupted him
        _speech.Clear();
        _dialogue.ShowYou("", live: true);
        _mic.Start();
    }

    private void StopTalking()
    {
        if (!_recording || _mic == null) return;
        _recording = false;
        _recordingByClick = false;
        TaskHelper.RunSafely(SendRecording(_mic));
    }

    private async Task SendRecording(IMic mic)
    {
        var wav = await mic.Stop();
        if (!_alive) return;
        if (wav == null)
        {
            _dialogue.HideYou();
            return;
        }
        _thinking = true;
        string words;
        try { words = await Fish.Transcribe(_cfg, wav); }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Transcription failed: {e.Message}");
            words = "";
        }
        _thinking = false;
        if (!_alive) return;
        if (string.IsNullOrWhiteSpace(words))
        {
            _dialogue.HideYou();
            return;
        }
        _dialogue.ShowYou(words, live: false);
        _chat.Add("you", words);
        await Say(words);
    }

    private void OnTyped(string text)
    {
        if (!_talkEnabled || text.Length == 0) return;
        _dialogue.ShowYou(text, live: false);
        _chat.Add("you", text);
        TaskHelper.RunSafely(Say(text));
    }

    // ───────────────────────── shop events ─────────────────────────

    private void OnPurchased(PurchaseStatus status, MerchantEntry entry)
    {
        if (!_alive) return;
        var it = _haggle.Items.FirstOrDefault(i => i.Entry == entry);
        if (it == null) return;
        var paid = Haggle.AngerTax.Contains(entry) ? (int)Math.Round(it.ListPrice * 1.5) : it.Agreed ?? it.ListPrice;
        var name = _boughtName.TryGetValue(entry, out var n) ? n : "something";
        var gift = it.Gift;
        var floor = _haggle.FloorOf(it);
        // Clear the deal first: an affection change below may hand out a new gift (a restocked slot included).
        it.Agreed = null;
        it.Gift = false;
        it.Rounds = 0;
        Haggle.AgreedPrices.Remove(entry);
        _purchases++;
        if (it.Kind == "card") _cardsBought++;
        if (it.Kind == "relic") _relicsBought++;
        if (!gift)
        {
            if (paid < it.ListPrice) _haggledOff += it.ListPrice - paid;
            if (paid >= it.ListPrice && it.Kind != "service")
            {
                _toasts.Unlock("fool");
                if (++RunStat.FullPrice >= 3) _toasts.Unlock("simp");
            }
            if (paid < it.ListPrice && paid <= floor + 1 && ++_rec.FloorDeals >= 5) _toasts.Unlock("haggle_master");
            _affection.Purchase(paid, it.ListPrice, floor);
        }
        SaveRun();
        if (entry.IsStocked) _boughtName[entry] = ItemText.Name(entry); // restocked (The Courier)
        RefreshShop();
        TaskHelper.RunSafely(Event($"The customer just {(gift ? "took your gift," : "bought")} {name} for {paid} gold. They have {_player.Gold} gold left."));
        MaybeRevealSecret();
    }

    // Names are read before the entry is cleared by the purchase, so remember them while the rug is up.
    private readonly Dictionary<MerchantEntry, string> _boughtName = new();

    private void OnAffectionChanged(Affection.Change ch)
    {
        UpdateMeter();
        if (ch.Delta != 0) _fx.HeartPop(ch.Delta, _heart.PopAnchor);
        if (ch.Why == "insult" && RunStat.EverRegular) _toasts.Unlock("friendzone");
        if (ch.Why == "compliment") _toasts.Unlock("crush");
        if (ch.LevelUp || ch.LevelDown)
        {
            _fx.Vignette(ch.LevelUp, ch.LevelUp ? Loc.T("♥ Affection up! ♥", "♥ 好感度上升！♥") : Loc.T("Affection down…", "好感度下降……"), Loc.Level(_affection.LevelName));
            var gifts = ch.LevelUp ? ApplyPerks() : null;
            TaskHelper.RunSafely(Event($"Your feelings toward the customer changed: they are now your \"{_affection.LevelName}\". {_affection.Mood}"
                                       + (gifts != null ? $" {gifts} Tell them, flustered." : "")));
            UpdateDateButton(announce: ch.LevelUp);
        }
        if (ch.LevelUp)
        {
            if (_affection.Index == 4) _toasts.Unlock("transaction");
            if (_affection.Index >= 2)
                Bestie(_affection.Index == 4
                    ? "The Merchant is now completely smitten: the player just reached Darling, the best ending is in reach."
                    : $"The Merchant's feelings just went up a level: he now sees the player as a {_affection.LevelName}.");
        }
        else if (ch.LevelDown)
        {
            Bestie($"The Merchant just cooled on the player; he now sees them as only a {_affection.LevelName}.");
        }
        if (ch.Capped && !_cappedSaid)
        {
            _cappedSaid = true;
            _fx.Note(Loc.T("He needs time… come back next floor ♥", "他需要点时间……下一层再来吧 ♥"), _heart.PopAnchor);
            Bestie("The Merchant won't warm up any more this visit; the player has to come back to the next shop.");
        }
        MaybeRevealSecret();
    }

    private void MaybeRevealSecret()
    {
        if (_secretShown || _affection.Value < Affection.SecretAt || _purchases < 2) return;
        var it = _haggle.Items.FirstOrDefault(i => i.Secret);
        if (it?.Entry is not MerchantCardEntry entry) return;
        try
        {
            var t = typeof(MerchantCardEntry);
            const BindingFlags f = BindingFlags.Instance | BindingFlags.NonPublic;
            t.GetField("_cardType", f)!.SetValue(entry, null);
            t.GetField("_cardRarity", f)!.SetValue(entry, CardRarity.Rare);
            entry.Populate();
            entry.OnMerchantInventoryUpdated();
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Could not stock the secret card: {e.Message}");
            return;
        }
        if (!entry.IsStocked) return;
        _secretShown = true;
        it.Hidden = false;
        it.ListPrice = Haggle.BaseCost(entry);
        it.Floor = Math.Max(1, (int)Math.Round(it.ListPrice * 0.7));
        it.Rounds = 0;
        it.Agreed = null;
        _boughtName[entry] = ItemText.Name(entry);
        _rug.RevealSecret(it);
        _rug.Point(it);
        _fx.Stamp(Loc.T("SECRET CARD!", "秘密卡牌！"), StampKind.Deal, Loc.T("from under the counter ♥", "从柜台底下拿出来的 ♥"));
        _toasts.Unlock("secret");
        TaskHelper.RunSafely(Event($"SECRET UNLOCKED: flustered, you pull a Rare card, {ItemText.Name(entry)}, from under the counter and put it on the rug (shelf #{it.Shelf}). Reveal it in a hushed whisper: something you don't show just anyone. It can be haggled like anything else."));
        Bestie($"The Merchant just pulled a secret Rare card, {ItemText.Name(entry)}, from under the counter, just for the player.");
    }

    // ───────────────────────── run memory, perks, the date ─────────────────────────

    private void SaveRun()
    {
        _rec.Affection = _affection.Value;
        _rec.Visits = _visit;
        RunMemory.Set(_seed, _rec);
    }

    private void OnVisitStart()
    {
        if (_visit >= 5) _toasts.Unlock("loyal");
        if (_rec.EmptyVisits >= 3) _toasts.Unlock("window_shopper");
        if (_rec.FactsBeforeVisit > 0)
        {
            _toasts.Unlock("remembered");
            _fx.Note(Loc.T("He remembers you ♥", "他还记得你 ♥"), _heart.PopAnchor);
        }
        _startGifts = ApplyPerks();
        UpdateDateButton(announce: false);
    }

    /// <summary>
    /// What his affection buys you, once per visit: Regular and up, the cheapest potion on the rug is free;
    /// Favourite and up, card removal is half price. Returns a note for the model, or null when nothing new.
    /// </summary>
    private string? ApplyPerks()
    {
        var show = new List<string>();
        var tell = new List<string>();
        if (_affection.Index >= 2 && !_potionGift)
        {
            var potion = _haggle.OnRug.Where(i => i.Kind == "potion").OrderBy(i => i.ListPrice).FirstOrDefault();
            if (potion != null && _haggle.GiveGift(potion, 0))
            {
                _potionGift = true;
                show.Add(Loc.T($"free {ItemText.Name(potion)}", $"免费的{ItemText.Name(potion)}"));
                tell.Add($"{ItemText.Name(potion)} for free");
            }
        }
        if (_affection.Index >= 3 && !_removalGift)
        {
            var removal = _haggle.OnRug.FirstOrDefault(i => i.Kind == "service");
            if (removal != null && _haggle.GiveGift(removal, (int)Math.Round(removal.ListPrice * 0.5)))
            {
                _removalGift = true;
                show.Add(Loc.T("card removal half price", "移除卡牌半价"));
                tell.Add("card removal at half price");
            }
        }
        if (show.Count == 0) return null;
        RefreshShop();
        _fx.Stamp(Loc.T("ON THE HOUSE!", "本店请客！"), StampKind.Love, string.Join(" · ", show));
        _toasts.Unlock("on_the_house");
        return $"Because you like them, you're giving them {string.Join(" and ", tell)} this visit (already marked on the rug).";
    }

    private void UpdateDateButton(bool announce)
    {
        var want = _affection.Index >= 4 && !_rec.Dated && !_haggle.Angry;
        if (want && _dateButton == null)
        {
            _dateButton = _native.AddSideButton(Loc.T("♥ Ask him on a date", "♥ 约他出去"), StartDate);
            if (announce) Bestie("The Merchant is smitten enough to go on a date with the player. There's a Date button now!");
        }
        else if (!want && _dateButton != null)
        {
            if (GodotObject.IsInstanceValid(_dateButton)) _dateButton.QueueFree();
            _dateButton = null;
        }
    }

    private void StartDate()
    {
        if (!_alive || _rec.Dated || _date.IsOpen) return;
        _detail.Hide();
        _rug.SetSelected(null);
        _dateQs = Date.Draw();
        _dateRound = 0;
        _dateScore = 0;
        _dateReplying = false;
        _dateEnded = false;
        AskDate();
    }

    private void AskDate()
    {
        var q = _dateQs![_dateRound];
        _date.Ask(_dateRound + 1, Date.Rounds, q.Text, q.Answers.Select(a => a.Text).ToList());
    }

    private void OnDatePicked(int choice)
    {
        if (!_alive || _dateQs == null) return;
        if (_dateEnded)
        {
            _date.Hide();
            _dateQs = null;
            return;
        }
        if (_dateReplying)
        {
            _dateReplying = false;
            if (++_dateRound < Date.Rounds) AskDate();
            else EndDate();
            return;
        }
        if (choice < 0) return;
        var a = _dateQs[_dateRound].Answers[choice];
        _dateScore += a.Score;
        _dateReplying = true;
        var verdict = a.Score switch
        {
            >= 2 => Loc.T("♥♥ He melts.", "♥♥ 他融化了。"),
            1 => Loc.T("♥ He likes that.", "♥ 他挺喜欢。"),
            0 => Loc.T("… He's not sure what to make of it.", "……他不知道该作何反应。"),
            _ => Loc.T("♡ Ouch.", "♡ 扎心了。"),
        };
        _date.Say($"{_dateRound + 1}/{Date.Rounds}",
            $"[color=#ffb3d6]{Loc.T("You", "你")}:[/color] {DatePanel.Escape(a.Text)}\n\n" +
            $"[b][color=#ff7eb6]{Loc.T("The Merchant", "商人")}:[/color][/b] {DatePanel.Escape(StripTags(a.Reply))}\n\n" +
            $"[i][color=#c9a3ff]{verdict}[/color][/i]",
            _dateRound + 1 < Date.Rounds ? Loc.T("Continue ♥", "继续 ♥") : Loc.T("How did it go…?", "结果如何……？"));
        TaskHelper.RunSafely(Speak("merchant", a.Reply));
    }

    private void EndDate()
    {
        _dateEnded = true;
        _rec.Dated = true;
        SaveRun();
        _toasts.Unlock("first_date");
        string body, tell, bestie;
        if (_dateScore >= Date.Perfect)
        {
            _toasts.Unlock("soulmate");
            var relic = _haggle.OnRug.Where(i => i.Kind == "relic").OrderByDescending(i => i.ListPrice).FirstOrDefault();
            if (relic != null && _haggle.GiveGift(relic, 0))
            {
                var name = ItemText.Name(relic);
                body = Loc.T($"He takes {name} off the rug and presses it into your hands. \"Free. Tell anyone and I'll deny it.\"",
                    $"他从地毯上拿起{name}，塞进你手里。“免费的。敢告诉别人我就不认账。”");
                tell = $"It went perfectly, and you gave them {name} for free (it's on the rug at 0 gold).";
            }
            else
            {
                GiftAll(0.5);
                body = Loc.T("He's so happy he marks everything on the rug half price. \"Don't get used to it.\"",
                    "他高兴得把地毯上所有东西都打了五折。“别习惯了。”");
                tell = "It went perfectly, and you put everything on the rug at half price for them.";
            }
            _fx.Vignette(true, Loc.T("♥ Perfect date! ♥", "♥ 完美约会！♥"), Loc.T("Soulmate", "灵魂伴侣"));
            bestie = "The date was PERFECT. The Merchant is head over heels and just gave the player a free gift.";
        }
        else if (_dateScore >= Date.Sweet)
        {
            GiftAll(0.75);
            body = Loc.T("He's in such a good mood that everything on the rug is 25% off. For today.",
                "他心情好得不得了，地毯上所有东西都打七五折。仅限今天。");
            tell = "It went sweetly, and you knocked 25% off everything on the rug for them.";
            _fx.Stamp(Loc.T("SWEET DATE!", "甜蜜约会！"), StampKind.Love, Loc.T("everything 25% off", "全场七五折"));
            bestie = "The date went sweetly; the Merchant knocked 25% off everything for the player.";
        }
        else
        {
            _affection.AwkwardDate();
            body = Loc.T("He clears his throat, straightens his hood and reopens the shop. That was… awkward.",
                "他清了清嗓子，整了整兜帽，重新开张。刚才……好尴尬。");
            tell = "It went awkwardly and you're a bit hurt.";
            _fx.Stamp(Loc.T("AWKWARD…", "好尴尬……"), StampKind.Bad, Loc.T("he's a little hurt", "他有点受伤"));
            bestie = "The date was a disaster. Awkward silence. The Merchant is a little hurt.";
        }
        RefreshShop();
        UpdateDateButton(announce: false);
        _date.Say(Loc.T("The end", "约会结束"), DatePanel.Escape(body), Loc.T("Back to the shop", "回到商店"));
        TaskHelper.RunSafely(Event($"You just went on a date with the customer in your tent. {tell} React in one short line as you reopen the shop."));
        Bestie(bestie);
    }

    private void GiftAll(double mul)
    {
        foreach (var it in _haggle.OnRug.ToList())
            _haggle.GiveGift(it, (int)Math.Round(it.ListPrice * mul));
    }
}

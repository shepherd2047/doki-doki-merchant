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
        var (aff, visits) = RunMemory.Get(_seed);
        _visit = visits + 1;
        _affection = new Affection(aff);
        _haggle = new Haggle(_inv, _affection, () => _player.Gold);
        _brain = new Brain(_cfg, RunTool);
        _pttKey = Enum.TryParse<Key>(_cfg.PushToTalkKey, true, out var k) ? k : Key.V;

        BuildUi();
        RunMemory.Set(_seed, _affection.Value, _visit);
        _affection.Changed += OnAffectionChanged;
        foreach (var it in _haggle.Items)
        {
            _boughtName[it.Entry] = ItemText.Name(it);
            it.Entry.PurchaseCompleted += OnPurchased;
        }
        room.GetTree().ProcessFrame += OnFrame;

        var problem = _cfg.Problem;
        if (problem != null)
        {
            _dialogue.Say("system", problem, 0);
            _chat.Add("system", problem);
            SetTalkEnabled(false);
            return;
        }
        _mic = IMic.Create(_stage.Canvas);
        TaskHelper.RunSafely(Greet());
    }

    private void Close()
    {
        if (!_alive) return;
        _alive = false;
        try
        {
            if (_purchases == 0) _affection.LeftEmptyHanded();
            RunMemory.Set(_seed, _affection.Value, _visit);
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
        try { act = $"Act {_player.RunState.CurrentActIndex + 1} · "; } catch { }
        var text = $"Bought: {Plural(_cardsBought, "card")}, {Plural(_relicsBought, "relic")}  ·  {act}Floor {_player.RunState.TotalFloor}  ·  Visit {_visit}";
        if (_haggledOff > 0) text += $"  ·  Haggled off: {_haggledOff} gold";
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
        await Event($"The customer (the {CharacterName()}) walks into your tent on floor {_player.RunState.TotalFloor}. {ret} Greet them in one short line.");
    }

    private async Task Say(string customerWords)
    {
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
                    ? "The chat model refused the API key (401). Check openai_api_key in DokiDokiMerchant.cfg, then re-enter the shop."
                    : "The Merchant lost his train of thought (network error). Try again.";
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
                        _fx.Stamp($"♥ DEAL! {offer}g", StampKind.Deal, offer < list ? $"{list - offer}g off" : "full price… he's thrilled");
                        RefreshShop();
                        break;
                    }
                    case "counter":
                        _fx.Stamp($"COUNTER: {Num("counter_offer")}g", StampKind.Info, $"you offered {Num("offer")}g");
                        break;
                    case "final":
                        _fx.Stamp($"FINAL OFFER: {Num("counter_offer")}g", StampKind.Info, "his absolute floor");
                        break;
                }
                if (insulted)
                {
                    _affection.Lowball();
                    _fx.Stamp("LOWBALL!", StampKind.Bad, _haggle.Angry ? "STRIKE 3/3 · uh oh" : $"STRIKE {_haggle.InsultStreak}/3");
                    _dialogue.Shake(); // the Bad stamp jolts the stage itself
                    if (_haggle.Angry && !_angerApplied)
                    {
                        _angerApplied = true;
                        _haggle.ApplyAngerTax();
                        RefreshShop();
                        _toasts.Unlock("cheapskate");
                        _fx.Vignette(false, "FURIOUS!", "every price +50%");
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
                    var capped = res["capped"]?.GetValue<bool>() == true ? " (maxed for this floor)" : "";
                    switch (kind)
                    {
                        case "flirt":
                            if (wasStranger && delta <= 2) _fx.Stamp("FLIRT…?", StampKind.Love, $"he's not there yet · {d}");
                            else _fx.Stamp("CRITICAL FLIRT!", StampKind.Love, d + capped);
                            break;
                        case "compliment":
                            _fx.Stamp("SWEET TALK!", StampKind.Love, d + capped);
                            break;
                        case "small_talk":
                            _fx.Stamp("SMALL TALK", StampKind.Info, d + capped);
                            break;
                        default:
                            _fx.Stamp("RUDE!", StampKind.Bad, d);
                            _dialogue.Shake();
                            break;
                    }
                }
                return res;
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
- Only at Darling, you may break the fourth wall once, briefly, e.g. ""My counterpart in the unmodded game just sits there, you know. Mute. Tragic."" or ""Don't tell Neow about us.""
- Reply in the customer's language ({_cfg.Language} by default).";
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
            var line = await Brain.OneShot(_cfg, BestiePrompt, $"{what}\nThe Merchant just said: {_lastMerchantLine}");
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
        byte[] pcm;
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
            _micButton.SetStatus("Typing and talking are off");
            return;
        }

        // Hold the push-to-talk key, or click the mic to start and click again to send.
        var keyDown = _stage.Canvas.Visible && Input.IsPhysicalKeyPressed(_pttKey) && !_chat.HasFocus;
        if (keyDown && !_pttWasDown && !_recording) StartTalking(byClick: false);
        else if (!keyDown && _pttWasDown && _recording && !_recordingByClick) StopTalking();
        _pttWasDown = keyDown;

        var problem = _mic?.Problem;
        _micButton.SetState(_recording ? MicState.Listening : _thinking ? MicState.Thinking : speaking ? MicState.Speaking : MicState.Idle);
        _micButton.SetStatus(problem != null && problem.StartsWith("Starting") ? "Waking the mic…"
            : problem != null ? "Mic off · type below"
            : _recording ? (_recordingByClick ? "Listening… tap to send" : $"Listening… release {_pttKey}")
            : _thinking ? "He's thinking…"
            : speaking ? "Tap to interrupt"
            : $"Tap or hold {_pttKey} to talk");
    }

    private void OnMicClicked()
    {
        if (!_talkEnabled) return;
        if (_recording) StopTalking();
        else if (_mic?.Problem is { } p && !p.StartsWith("Starting"))
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
        _purchases++;
        if (it.Kind == "card") _cardsBought++;
        if (it.Kind == "relic") _relicsBought++;
        if (paid < it.ListPrice) _haggledOff += it.ListPrice - paid;
        if (paid >= it.ListPrice && it.Kind != "service")
        {
            _toasts.Unlock("fool");
            if (++RunStat.FullPrice >= 3) _toasts.Unlock("simp");
        }
        _affection.Purchase(paid, it.ListPrice, _haggle.FloorOf(it));
        it.Agreed = null;
        it.Rounds = 0;
        Haggle.AgreedPrices.Remove(entry);
        if (entry.IsStocked) _boughtName[entry] = ItemText.Name(entry); // restocked (The Courier)
        RefreshShop();
        TaskHelper.RunSafely(Event($"The customer just bought {name} for {paid} gold. They have {_player.Gold} gold left."));
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
            _fx.Vignette(ch.LevelUp, ch.LevelUp ? "♥ Affection up! ♥" : "Affection down…", _affection.LevelName);
            TaskHelper.RunSafely(Event($"Your feelings toward the customer changed: they are now your \"{_affection.LevelName}\". {_affection.Mood}"));
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
            _fx.Note("He needs time… come back next floor ♥", _heart.PopAnchor);
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
        _fx.Stamp("SECRET CARD!", StampKind.Deal, "from under the counter ♥");
        _toasts.Unlock("secret");
        TaskHelper.RunSafely(Event($"SECRET UNLOCKED: flustered, you pull a Rare card, {ItemText.Name(entry)}, from under the counter and put it on the rug (shelf #{it.Shelf}). Reveal it in a hushed whisper: something you don't show just anyone. It can be haggled like anything else."));
        Bestie($"The Merchant just pulled a secret Rare card, {ItemText.Name(entry)}, from under the counter, just for the player.");
    }
}

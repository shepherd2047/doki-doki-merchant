using System.Text.Json.Nodes;
using MegaCrit.Sts2.Core.Logging;

namespace DokiDokiMerchant;

/// <summary>
/// How much the Merchant likes you. The game owns the number: the model only reports what kind of thing the
/// customer said (the react tool), and this table decides how far the heart meter moves. Ported from the web
/// game's affection.js.
/// </summary>
internal sealed class Affection
{
    public record Level(string Name, int Min, double FloorMul);

    public static readonly Level[] Levels =
    {
        new("Stranger", 0, 1.08),
        new("Customer", 20, 1),
        new("Regular", 40, 1),
        new("Favourite", 60, 0.95),
        new("Darling", 80, 0.9),
    };

    private static readonly string[] Moods =
    {
        "You barely know this one. Be cold, curt and strictly business.",
        "Just another customer. Smug and sly, the usual.",
        "A regular! Be friendlier, tease them a little, maybe share a bit of gossip about the Spire.",
        "You're warming to this one and it flusters you. Be coy and tsundere: deny you like them while obviously liking them.",
        "You adore this customer and can barely hide it. Be flustered, sweet and a bit dramatic, still a schemer at heart.",
    };

    public const int VisitGainCap = 20; // he warms up slowly: at most +20 per visit, so it takes several shops
    public const int SecretAt = 80;

    public record Change(int Delta, bool Capped, string Why, bool LevelUp, bool LevelDown);

    public int Value { get; private set; }
    private int _visitGain;
    private bool _reactedThisTurn;

    public event Action<Change>? Changed;

    public Affection(int value) => Value = Math.Clamp(value, 0, 100);

    public int Index => Levels.Select((l, i) => (l, i)).Last(t => Value >= t.l.Min).i;
    public string LevelName => Levels[Index].Name;
    public string Mood => Moods[Index];
    public double FloorMul => Levels[Index].FloorMul;
    public bool CappedThisVisit => _visitGain >= VisitGainCap;
    public string Hearts => new string('♥', Index + 1) + new string('♡', Levels.Length - 1 - Index);

    /// <summary>Progress through the current level, 0..1, for the meter.</summary>
    public float LevelProgress
    {
        get
        {
            if (Index == Levels.Length - 1) return 1f;
            var lo = Levels[Index].Min;
            var hi = Levels[Index + 1].Min;
            return (Value - lo) / (float)(hi - lo);
        }
    }

    public void NewTurn() => _reactedThisTurn = false;

    public JsonObject React(string kind)
    {
        if (_reactedThisTurn) return new JsonObject { ["applied"] = false, ["reason"] = "already counted this turn" };
        int? delta = kind switch
        {
            "compliment" => 6,
            "flirt" => Index >= 1 ? 8 : 2,
            "small_talk" => 3,
            "insult" => -12,
            _ => null,
        };
        if (delta == null) return new JsonObject { ["applied"] = false, ["reason"] = $"unknown kind \"{kind}\"" };
        _reactedThisTurn = true;
        var ch = Add(delta.Value, kind);
        return new JsonObject
        {
            ["applied"] = true,
            ["affection_level"] = LevelName,
            ["mood_note"] = Mood,
            ["capped_this_visit"] = ch.Capped,
        };
    }

    /// <summary>Paying near list price pleases him; grinding him down to his floor makes him sulk.</summary>
    public Change Purchase(int paid, int list, int floor)
    {
        var t = list > floor ? Math.Clamp((paid - floor) / (double)(list - floor), 0, 1) : 1;
        var delta = 4 + (int)Math.Round(8 * t);
        if (paid <= floor + 1 && paid < list) delta -= 7; // net -3: he "misses out"
        return Add(delta, delta < 0 ? "squeezed" : "purchase");
    }

    public Change Lowball() => Add(-8, "lowball");
    public Change LeftEmptyHanded() => Add(-5, "left");

    private Change Add(int delta, string why)
    {
        var before = Index;
        var old = Value;
        var capped = false;
        if (delta > 0)
        {
            var room = Math.Max(0, VisitGainCap - _visitGain);
            if (delta > room)
            {
                delta = room;
                capped = true;
            }
            _visitGain += delta;
        }
        Value = Math.Clamp(Value + delta, 0, 100);
        var ch = new Change(Value - old, capped, why, Index > before, Index < before);
        Changed?.Invoke(ch);
        return ch;
    }
}

/// <summary>
/// What the Merchant remembers about the current run (affection and how many times you've visited), keyed by
/// the run seed so Continue picks it back up. Kept in the mod folder, never in the game's own save files.
/// </summary>
internal static class RunMemory
{
    private static string FilePath => Path.Combine(Config.ModDir, "runs.dat");

    public static (int affection, int visits) Get(string seed)
    {
        var j = Read();
        if (j[seed] is JsonObject o)
            return (o["affection"]?.GetValue<int>() ?? 20, o["visits"]?.GetValue<int>() ?? 0);
        return (20, 0);
    }

    public static void Set(string seed, int affection, int visits)
    {
        try
        {
            var j = Read();
            j[seed] = new JsonObject { ["affection"] = affection, ["visits"] = visits, ["at"] = DateTime.UtcNow.ToString("o") };
            // Keep only the 20 most recent runs.
            foreach (var old in j.OrderByDescending(kv => kv.Value?["at"]?.GetValue<string>()).Skip(20).Select(kv => kv.Key).ToList())
                j.Remove(old);
            File.WriteAllText(FilePath, j.ToJsonString());
        }
        catch (Exception e)
        {
            Log.Warn($"[Doki] Could not save run memory: {e.Message}");
        }
    }

    private static JsonObject Read()
    {
        try
        {
            if (File.Exists(FilePath)) return JsonNode.Parse(File.ReadAllText(FilePath))?.AsObject() ?? new JsonObject();
        }
        catch
        {
            // A corrupt file just means he forgets you.
        }
        return new JsonObject();
    }
}

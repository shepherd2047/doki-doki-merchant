using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Merchant;

namespace DokiDokiMerchant;

/// <summary>
/// The haggling rules, ported from the web game's haggle.js. The game, not the model, owns the numbers: every
/// item has a hidden floor price and the Merchant asks check_offer about each offer. An accepted offer becomes
/// that item's price on the rug (see <see cref="CostPatch"/>); the player still buys it by clicking it.
/// </summary>
internal sealed class Haggle
{
    internal sealed class Item
    {
        public required int Shelf;
        public required MerchantEntry Entry;
        public required string Kind; // card, relic, potion, service
        public int ListPrice;
        public int Floor;
        public int Rounds;
        public int? Agreed;
        /// <summary>The under-the-counter card (shelf 10): not on the rug until the Merchant reveals it.</summary>
        public bool Secret;
        /// <summary>True while the secret card is still under the counter.</summary>
        public bool Hidden;
    }

    public readonly List<Item> Items = new();
    private readonly Affection _affection;
    private readonly Func<int> _gold;
    public int InsultStreak { get; private set; }
    public bool Angry => InsultStreak >= 3;

    /// <summary>Prices agreed this visit, read by the Cost getter patch.</summary>
    public static readonly Dictionary<MerchantEntry, int> AgreedPrices = new();
    /// <summary>Entries whose price is marked up because the Merchant is furious.</summary>
    public static readonly HashSet<MerchantEntry> AngerTax = new();

    public Haggle(MerchantInventory inv, Affection affection, Func<int> gold)
    {
        _affection = affection;
        _gold = gold;
        AgreedPrices.Clear();
        AngerTax.Clear();
        var rng = new Random();
        void Add(MerchantEntry e, string kind)
        {
            var it = new Item { Shelf = Items.Count + 1, Entry = e, Kind = kind };
            it.ListPrice = SafeCost(e);
            it.Floor = Math.Max(1, (int)Math.Round(it.ListPrice * (0.6 + rng.NextDouble() * 0.18)));
            Items.Add(it);
        }
        // The web game's smaller stall, so the Merchant stays in view: shelves 1-4 the Attack, Attack, Skill and
        // Power cards, 5-6 the rolled relic and the Shop relic, 7-8 two potions, 9 card removal. The second Skill
        // slot becomes the secret card (shelf 10). Colorless cards, the other relic and potion stay off the rug.
        var cards = inv.CharacterCardEntries;
        foreach (var i in new[] { 0, 1, 2, 4 })
            if (i < cards.Count) Add(cards[i], "card");
        var relics = inv.RelicEntries;
        foreach (var i in new[] { 0, relics.Count - 1 })
            if (i >= 0 && i < relics.Count && Items.All(x => x.Entry != relics[i])) Add(relics[i], "relic");
        foreach (var e in inv.PotionEntries.Take(2)) Add(e, "potion");
        if (inv.CardRemovalEntry != null) Add(inv.CardRemovalEntry, "service");
        if (cards.Count > 3)
        {
            Add(cards[3], "card");
            Items[^1].Shelf = 10;
            Items[^1].Secret = true;
            Items[^1].Hidden = true;
        }
    }

    /// <summary>Items the customer can see and buy right now.</summary>
    public IEnumerable<Item> OnRug => Items.Where(i => !i.Hidden && i.Entry.IsStocked);

    private static int SafeCost(MerchantEntry e)
    {
        try { return e.IsStocked ? e.Cost : 0; } catch { return 0; }
    }

    /// <summary>The price the rug would show without our haggling.</summary>
    public static int BaseCost(MerchantEntry e)
    {
        CostPatch.Bypass = true;
        try { return SafeCost(e); } finally { CostPatch.Bypass = false; }
    }

    /// <summary>Re-read list prices (relics, sales and the like can change them).</summary>
    public void RefreshListPrices()
    {
        foreach (var it in Items)
        {
            var p = BaseCost(it.Entry);
            if (p > 0 && p != it.ListPrice)
            {
                it.Floor = Math.Max(1, (int)Math.Round(it.Floor * (p / (double)Math.Max(1, it.ListPrice))));
                it.ListPrice = p;
            }
        }
    }

    public int FloorOf(Item it)
    {
        if (Angry) return it.ListPrice;
        return Math.Min(it.ListPrice, Math.Max(1, (int)Math.Round(it.Floor * _affection.FloorMul)));
    }

    public Item? Resolve(string? reference)
    {
        var s = (reference ?? "").Trim().ToLowerInvariant().TrimStart('#');
        var stocked = OnRug.ToList();
        if (int.TryParse(s, out var n)) return stocked.FirstOrDefault(i => i.Shelf == n);
        static string Norm(string x) => Regex.Replace(x.ToLowerInvariant(), "[^a-z0-9]", "");
        var ns = Norm(s);
        if (ns.Length == 0) return null;
        return stocked.FirstOrDefault(i => Norm(ItemText.Name(i)) == ns)
               ?? stocked.FirstOrDefault(i => Norm(ItemText.Name(i)).Contains(ns) || ns.Contains(Norm(ItemText.Name(i))));
    }

    /// <summary>The Merchant's private verdict on an offer.</summary>
    public JsonObject Check(string? reference, string? priceArg, out bool insulted)
    {
        insulted = false;
        var it = Resolve(reference);
        if (it == null) return Error($"No item matches \"{reference}\". Ask the customer which shelf number they mean.");
        var digits = Regex.Replace(priceArg ?? "", @"[^\d.]", "");
        if (!double.TryParse(digits, System.Globalization.CultureInfo.InvariantCulture, out var pd) || pd <= 0)
            return Error("The offer must be a positive number of gold.");
        var p = (int)Math.Round(pd);
        var list = it.ListPrice;
        var floor = FloorOf(it);
        var gold = _gold();
        it.Rounds++;
        string verdict;
        int? counter = null;
        if (p >= list) verdict = "accept";
        else if (p >= floor)
        {
            // Above the floor: he takes it after a bit of back and forth, or if it's close to list.
            if (it.Rounds >= 2 || p >= floor + (list - floor) * 0.5) verdict = "accept";
            else
            {
                verdict = "counter";
                counter = Math.Max(p + 1, (int)Math.Round(p + (list - p) * 0.5));
            }
        }
        else if (p < floor * 0.65)
        {
            InsultStreak++;
            verdict = "insulted";
            counter = list;
            insulted = true;
        }
        else
        {
            // Below the floor: counters slide toward the floor round by round.
            counter = (int)Math.Round(floor + (list - floor) * Math.Pow(0.6, it.Rounds));
            verdict = counter - floor <= 3 ? "final" : "counter";
            if (verdict == "final") counter = floor;
        }
        if (verdict != "insulted") InsultStreak = 0;

        var res = new JsonObject
        {
            ["item"] = ItemText.Name(it),
            ["offer"] = p,
            ["list_price"] = list,
            ["customer_gold"] = gold,
            ["verdict"] = verdict,
        };
        if (counter != null) res["counter_offer"] = counter;
        if (verdict == "accept")
        {
            it.Agreed = Math.Min(p, list);
            AgreedPrices[it.Entry] = it.Agreed.Value;
            res["note"] = "Deal. The price on the rug now shows the agreed price; the customer buys it by picking it up.";
        }
        if (verdict == "insulted")
            res["note"] = Angry
                ? "That's the third insult in a row. You snap: every price in your shop goes up by half for this greedy customer, and you won't haggle with them any more."
                : InsultStreak == 2
                    ? "Second insulting lowball in a row. Warn them, menacingly: one more and they'll regret it."
                    : "An insulting lowball. Act deeply offended.";
        if (gold < (counter ?? p)) res["note"] = $"{res["note"]?.GetValue<string>()} The customer only has {gold} gold.".Trim();
        return res;
    }

    /// <summary>Three insults: every price goes up 50% and all deals are off.</summary>
    public void ApplyAngerTax()
    {
        AgreedPrices.Clear();
        foreach (var it in Items)
        {
            it.Agreed = null;
            AngerTax.Add(it.Entry);
        }
    }

    private static JsonObject Error(string msg) => new() { ["error"] = msg };
}

/// <summary>Names and descriptions of shop entries, as plain text for the model.</summary>
internal static class ItemText
{
    public static string Name(Haggle.Item it) => Name(it.Entry);

    public static string Name(MerchantEntry e)
    {
        try
        {
            return e switch
            {
                MerchantCardEntry c when c.CreationResult != null => c.CreationResult.Card.Title,
                MerchantRelicEntry r when r.Model != null => r.Model.Title.GetFormattedText(),
                MerchantPotionEntry p when p.Model != null => p.Model.Title.GetFormattedText(),
                MerchantCardRemovalEntry => "Card Removal (removes one card from your deck)",
                _ => "(sold)",
            };
        }
        catch
        {
            return "(unknown)";
        }
    }

    public static string Describe(Haggle.Item it)
    {
        try
        {
            var text = it.Entry switch
            {
                MerchantCardEntry c when c.CreationResult != null =>
                    $"{c.CreationResult.Card.Rarity} {c.CreationResult.Card.Type} card. {c.CreationResult.Card.GetDescriptionForPile(PileType.None)}",
                MerchantRelicEntry r when r.Model != null => $"{r.Model.Rarity} relic. {r.Model.DynamicDescription.GetFormattedText()}",
                MerchantPotionEntry p when p.Model != null => $"{p.Model.Rarity} potion. {p.Model.DynamicDescription.GetFormattedText()}",
                MerchantCardRemovalEntry => "A service: you remove one card of the customer's choice from their deck.",
                _ => "",
            };
            return Clean(text);
        }
        catch
        {
            return "";
        }
    }

    /// <summary>Strip the game's BBCode-style markup ([gold], [/b], {Var}) so the model reads plain text.</summary>
    public static string Clean(string s) =>
        Regex.Replace(Regex.Replace(s, @"\[/?[a-zA-Z_]+[^\]]*\]", ""), @"\s+", " ").Trim();
}

// The haggling rules. The game, not the LLM, owns the numbers: every item has a hidden floor price,
// the merchant asks check_offer about each offer, and sell_item refuses anything under the floor.

export class Haggle {
  constructor(items, player, rng, affection) {
    this.items = items;
    this.player = player; // { gold, potionSlots, potions: [], deck: [] }
    this.rng = rng;
    this.affection = affection; // optional: floors follow how much he likes you
    this.insultStreak = 0; // insulting lowballs in a row; three and he attacks
    this.saved = 0;
    this.onBuy = null;
    for (const it of items) this.stock(it);
  }

  stock(it) {
    it.floor = Math.max(1, Math.round(it.price * this.rng.range(0.6, 0.78)));
    it.rounds = 0;
  }

  // The floor he'll actually accept right now: stingier with strangers, softer with darlings.
  floorOf(it) {
    if (this.angry) return it.price;
    const mul = this.affection ? this.affection.floorMul() : 1;
    return Math.min(it.price, Math.max(1, Math.round(it.floor * mul)));
  }

  get angry() {
    return this.insultStreak >= 3;
  }

  resolve(ref) {
    const s = String(ref ?? "").trim().toLowerCase().replace(/^#/, "");
    const n = parseInt(s, 10);
    const items = this.items.filter((it) => !it.hidden);
    if (String(n) === s) return items.find((it) => it.slot === n);
    const norm = (x) => x.toLowerCase().replace(/[^a-z0-9]/g, "");
    return (
      items.find((it) => norm(it.data.name) === norm(s)) ??
      items.find((it) => norm(it.data.name).includes(norm(s)) || norm(s).includes(norm(it.data.name)))
    );
  }

  // Merchant's private verdict on an offer.
  check(ref, priceArg) {
    const it = this.resolve(ref);
    if (!it) return { error: `No item matches "${ref}". Ask the customer which shelf number they mean.` };
    if (it.sold) return { error: `${it.data.name} is already sold.` };
    const p = Math.round(Number(String(priceArg).replace(/[^\d.]/g, "")));
    if (!Number.isFinite(p) || p <= 0) return { error: "The offer must be a positive number of gold." };

    const list = it.price;
    const floor = this.floorOf(it);
    const res = { item: it.data.name, offer: p, list_price: list, customer_gold: this.player.gold };
    it.rounds++;
    let verdict, counter;
    if (p >= list) verdict = "accept";
    else if (p >= floor) {
      // Above the floor: he takes it after a bit of back and forth, or if it's close to list.
      if (it.rounds >= 2 || p >= floor + (list - floor) * 0.5) verdict = "accept";
      else [verdict, counter] = ["counter", Math.max(p + 1, Math.round(p + (list - p) * 0.5))];
    } else if (p < floor * 0.65) {
      this.insultStreak++;
      [verdict, counter] = ["insulted", list];
    } else {
      // Below the floor: counters slide toward the floor round by round.
      counter = Math.round(floor + (list - floor) * Math.pow(0.6, it.rounds));
      verdict = counter - floor <= 3 ? "final" : "counter";
      if (verdict === "final") counter = floor;
    }
    if (verdict !== "insulted") this.insultStreak = 0;
    Object.assign(res, { verdict });
    if (counter !== undefined) res.counter_offer = counter;
    if (verdict === "accept") it.agreed = p;
    if (verdict === "insulted")
      res.note = this.angry
        ? "That's the third insult in a row. You snap: you are going to FIGHT this customer."
        : this.insultStreak === 2
          ? "Second insulting lowball in a row. Warn them, menacingly: one more and they'll regret it."
          : "An insulting lowball. Act deeply offended.";
    if (this.player.gold < (counter ?? p)) res.note = `${res.note ?? ""} The customer only has ${this.player.gold} gold.`.trim();
    return res;
  }

  // Hand over the goods. Returns { ok, ... } or { ok: false, reason }.
  sell(ref, priceArg) {
    const it = this.resolve(ref);
    if (!it) return { ok: false, reason: `No item matches "${ref}".` };
    if (it.sold) return { ok: false, reason: `${it.data.name} is already sold.` };
    const p = Math.round(Number(String(priceArg).replace(/[^\d.]/g, "")));
    if (!Number.isFinite(p) || p <= 0) return { ok: false, reason: "Price must be a positive number." };
    const floor = this.floorOf(it);
    if (p < floor) return { ok: false, reason: "That's below what you'd ever accept. Make a counter-offer instead." };
    if (p > this.player.gold) return { ok: false, reason: `The customer only has ${this.player.gold} gold.` };
    if (it.kind === "potion" && this.player.potions.length >= this.player.potionSlots)
      return { ok: false, reason: "The customer's potion belt is full." };
    this.buy(it, p);
    return { ok: true, item: it.data.name, paid: p, customer_gold_left: this.player.gold };
  }

  buy(it, p) {
    it.sold = true;
    it.paid = p;
    this.player.gold -= p;
    this.saved += Math.max(0, it.price - p);
    this.onBuy?.(it, p, this.floorOf(it));
    if (it.kind === "potion") this.player.potions.push(it.data);
    else if (it.kind === "card") this.player.deck.push(it.data);
    else if (it.kind === "relic") this.player.relics.push(it.data);
  }
}

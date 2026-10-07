// The merchant's stock, rolled the way the original MerchantInventory.CreateForNormalMerchant does
// (decompiled MegaCrit.Sts2.Core.Entities.Merchant), trimmed to a smaller stall so the Merchant stays in
// view: 4 Ironclad cards (Attack, Attack, Skill, Power, one on sale at half price), 1 relic of rolled
// rarity + 1 Shop relic, 2 distinct potions, and the card removal service.

export function makeRng(seed) {
  let a = seed >>> 0;
  const next = () => {
    a = (a + 0x6d2b79f5) >>> 0;
    let t = a;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
  return {
    next,
    int: (n) => Math.floor(next() * n),
    range: (lo, hi) => lo + next() * (hi - lo),
    item: (arr) => arr[Math.floor(next() * arr.length)],
  };
}

// C# Math.Round: round half to even.
const roundEven = (x) => {
  const r = Math.round(x);
  return Math.abs(x % 1) === 0.5 && r % 2 !== 0 ? r - 1 : r;
};

const CARD_COST = { Common: 50, Uncommon: 75, Rare: 150 };
const RELIC_COST = { Common: 175, Uncommon: 225, Rare: 275, Shop: 200 };
const POTION_COST = { Common: 50, Uncommon: 75, Rare: 100 };
const NEXT_RARITY = { Common: "Uncommon", Uncommon: "Rare", Rare: "Common" };

export function rollShop(catalog, seed = Date.now()) {
  const rng = makeRng(seed);
  const items = [];
  const taken = new Set();
  const cards = catalog.cards.filter((c) => c.artOk !== false);

  // MerchantCardEntry: rare 9% + rarity offset (starts at -5%), uncommon 37%; fall through to the
  // next rarity that has a card of this type; never a card already on the shelf.
  const rollCard = (pool, type, fixedRarity) => {
    let rarity = fixedRarity;
    if (!rarity) {
      const roll = rng.next();
      const rare = 0.09 - 0.05;
      rarity = roll < rare ? "Rare" : roll < 0.37 + rare ? "Uncommon" : "Common";
    }
    for (let k = 0; k < 3; k++) {
      const ids = cards.filter((c) => c.character === pool && c.rarity === rarity && (!type || c.type === type) && !taken.has(c.id));
      if (ids.length) {
        const card = rng.item(ids);
        taken.add(card.id);
        return card;
      }
      if (fixedRarity) return null;
      rarity = NEXT_RARITY[rarity];
    }
    return null;
  };

  const sale = rng.int(4);
  ["Attack", "Attack", "Skill", "Power"].forEach((type, i) => {
    const card = rollCard("ironclad", type);
    if (!card) return;
    let price = roundEven(CARD_COST[card.rarity] * rng.range(0.95, 1.05));
    if (i === sale) price = Math.floor(price / 2);
    items.push({ kind: "card", data: card, price, onSale: i === sale });
  });

  // RelicFactory.RollRarity: 50% Common, 33% Uncommon, 17% Rare; the third slot is a Shop relic.
  const relicPool = catalog.relics.filter((r) => r.allowedInShops);
  for (const rarity of [rollRelicRarity(rng), "Shop"]) {
    const options = relicPool.filter((r) => r.rarity === rarity && !taken.has(r.id));
    if (!options.length) continue;
    const relic = rng.item(options);
    taken.add(relic.id);
    items.push({ kind: "relic", data: relic, price: roundEven(RELIC_COST[relic.rarity] * rng.range(0.85, 1.15)) });
  }

  // PotionFactory.CreateRandomPotionsOutOfCombat: rare 10%, uncommon 25%, three distinct.
  for (let i = 0; i < 2; i++) {
    const roll = rng.next();
    const rarity = roll <= 0.1 ? "Rare" : roll <= 0.35 ? "Uncommon" : "Common";
    const options = catalog.potions.filter((p) => p.rarity === rarity && !taken.has(p.id));
    if (!options.length) continue;
    const potion = rng.item(options);
    taken.add(potion.id);
    items.push({ kind: "potion", data: potion, price: roundEven(POTION_COST[potion.rarity] * rng.range(0.95, 1.05)) });
  }

  items.push({ kind: "removal", data: { name: "Card Removal Service", description: "Remove a card from your deck." }, price: 75 });
  items.forEach((it, i) => Object.assign(it, { slot: i + 1, sold: false }));
  return items;
}

function rollRelicRarity(rng) {
  const f = rng.next();
  return f < 0.5 ? "Common" : f < 0.83 ? "Uncommon" : "Rare";
}

export const plain = (s) =>
  String(s ?? "")
    .replace(/(\[icon:energy\])+/g, (m) => ` ${m.length / 13} energy`)
    .replace(/\[icon:star\]/g, "star")
    .replace(/\[\/?[a-z]+\]/g, "")
    .replace(/\s*\n\s*/g, " ")
    .trim();

export function describeForAgent(it) {
  const d = it.data;
  let what;
  if (it.kind === "card") {
    const cost = d.cost === "X" ? "X" : `${d.cost}`;
    what = `${it.colorless ? "colorless" : "Ironclad"} ${d.rarity} ${d.type} card, costs ${cost} energy: "${plain(d.description)}"`;
  } else if (it.kind === "relic") what = `${d.rarity} relic: "${plain(d.description)}"`;
  else if (it.kind === "potion") what = `${d.rarity} potion: "${plain(d.description)}"`;
  else what = `service: remove one card from the customer's deck`;
  return `#${it.slot} ${d.name} — ${what} — ${it.price} gold${it.onSale ? " (on sale)" : ""}`;
}

// The Merchant's under-the-counter stock: one Ironclad Rare that isn't already on the rug, at the original
// Rare card price (150 x 0.95-1.05). Only shown to customers he's fond of.
export function rollSecretCard(catalog, items, seed) {
  const rng = makeRng(seed ^ 0x10fe);
  const onRug = new Set(items.map((it) => it.data.id));
  const rares = catalog.cards.filter((c) => c.character === "ironclad" && c.rarity === "Rare" && c.artOk !== false && !onRug.has(c.id));
  const card = rng.item(rares);
  return { kind: "card", data: card, price: roundEven(CARD_COST.Rare * rng.range(0.95, 1.05)), secret: true, slot: items.length + 1, sold: false };
}

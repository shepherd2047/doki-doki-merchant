// How much the Merchant likes you. Like prices, the game owns the number: the LLM only reports what kind of
// thing the customer said (react tool), and this table decides how far the heart meter moves.

export const LEVELS = [
  { name: "Stranger", min: 0, floorMul: 1.08 },
  { name: "Customer", min: 20, floorMul: 1 },
  { name: "Regular", min: 40, floorMul: 1 },
  { name: "Favourite", min: 60, floorMul: 0.95 },
  { name: "Darling", min: 80, floorMul: 0.9 },
];

const MOOD = [
  "You barely know this one. Be cold, curt and strictly business.",
  "Just another customer. Smug and sly, the usual.",
  "A regular! Be friendlier, tease them a little, maybe share a bit of gossip about the Spire.",
  "You're warming to this one and it flusters you. Be coy and tsundere: deny you like them while obviously liking them.",
  "You adore this customer and can barely hide it. Be flustered, sweet and a bit dramatic, still a schemer at heart.",
];

export const MAX_LEVEL = LEVELS.length; // 5: Darling, the goal of a run
export const VISIT_GAIN_CAP = 20; // he warms up slowly: at most +20 per visit, so it takes several floors
export const SECRET_AT = 80; // and at least two purchases
export const SECRET_LOST_BELOW = 60;

export class Affection {
  constructor(value = 20) {
    this.value = clamp(value);
    this.listeners = [];
    this.reactedThisTurn = false;
    this.visitGain = 0;
  }

  // A new shop visit: he's ready to warm up a little more.
  newVisit() { this.visitGain = 0; }
  get cappedThisVisit() { return this.visitGain >= VISIT_GAIN_CAP; }

  get index() {
    let i = 0;
    LEVELS.forEach((l, k) => { if (this.value >= l.min) i = k; });
    return i;
  }
  get level() { return LEVELS[this.index].name; }
  get levelNo() { return this.index + 1; }
  get hearts() { return "♥".repeat(this.index + 1) + "♡".repeat(LEVELS.length - 1 - this.index); }
  get mood() { return MOOD[this.index]; }
  floorMul() { return LEVELS[this.index].floorMul; }

  onChange(fn) { this.listeners.push(fn); }

  // The customer started a new spoken turn: they may move the meter by talking once more.
  newTurn() { this.reactedThisTurn = false; }

  // Things the customer said, as classified by the merchant agent.
  react(kind) {
    if (this.reactedThisTurn) return { applied: false, reason: "already counted this turn" };
    const delta = { compliment: 6, flirt: this.index >= 1 ? 8 : 2, small_talk: 3, insult: -12 }[kind];
    if (delta === undefined) return { applied: false, reason: `unknown kind "${kind}"` };
    this.reactedThisTurn = true;
    return this.add(delta, kind);
  }

  // Paying near list price pleases him; grinding him down to his floor makes him sulk.
  purchase(paid, list, floor) {
    const t = list > floor ? Math.min(1, Math.max(0, (paid - floor) / (list - floor))) : 1;
    let delta = 4 + Math.round(8 * t);
    if (paid <= floor + 1 && paid < list) delta -= 3 + 4; // net -3: he "misses out"
    return this.add(delta, delta < 0 ? "squeezed" : "purchase");
  }

  lowball() { return this.add(-8, "lowball"); }
  leftEmptyHanded() { return this.add(-5, "left"); }

  add(delta, why) {
    const before = this.index;
    const old = this.value;
    let capped = false;
    if (delta > 0) {
      const room = Math.max(0, VISIT_GAIN_CAP - this.visitGain);
      if (delta > room) { delta = room; capped = true; }
      this.visitGain += delta;
    }
    this.value = clamp(this.value + delta);
    const ev = { delta: this.value - old, capped, why, value: this.value, level: this.level, levelUp: this.index > before, levelDown: this.index < before };
    for (const fn of this.listeners) fn(ev);
    return { applied: true, ...ev, affection_level: this.level, mood_note: this.mood };
  }
}

const clamp = (v) => Math.max(0, Math.min(100, Math.round(v)));

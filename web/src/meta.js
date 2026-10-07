// "Doki Doki Merchant": the dating-sim parody dressing. Title screen, achievement toasts, CG cut-ins,
// endings. Pure presentation; the numbers live in affection.js.

const $ = (id) => document.getElementById(id);
const stage = () => $("stage");
const store = {
  get(k, d) { try { return JSON.parse(localStorage.getItem(`hts_${k}`)) ?? d; } catch { return d; } },
  set(k, v) { try { localStorage.setItem(`hts_${k}`, JSON.stringify(v)); } catch {} },
};
export { store };

function el(html) {
  const t = document.createElement("template");
  t.innerHTML = html.trim();
  return t.content.firstElementChild;
}

export function petals(container, n = 28) {
  for (let i = 0; i < n; i++) {
    const p = document.createElement("span");
    p.className = "petal";
    p.style.left = `${Math.random() * 100}%`;
    p.style.animationDuration = `${6 + Math.random() * 6}s`;
    p.style.animationDelay = `${-Math.random() * 10}s`;
    p.style.setProperty("--drift", `${(Math.random() - 0.5) * 300}px`);
    p.style.transform = `scale(${0.6 + Math.random() * 0.8})`;
    container.appendChild(p);
  }
}

// ---------------------------------------------------------------- title screen
export function titleScreen(onStart) {
  const t = el(`<div id="title">
    ${keyVisual()}
    <div class="petals"></div>
    <div class="logo">
      <div class="logo-top">Doki<span class="heart">♥</span>Doki</div>
      <div class="logo-main">Merchant</div>
    </div>
    <div class="route">Merchant Route <small>(the only route)</small></div>
    <nav>
      <button data-a="new">New Game</button>
      <button data-a="cont" class="fake" title="you have no save, coward">Continue</button>
      <button data-a="gallery">Gallery</button>
      <button data-a="config">Config</button>
    </nav>
    <div class="quip"></div>
    <div class="copyright">© 2026 Not Mega Crit · Not affiliated · Please do not sue the Merchant</div>
  </div>`);
  petals(t.querySelector(".petals"), 40);
  const quip = t.querySelector(".quip");
  const nope = (b, text) => { quip.textContent = text; b.classList.remove("wobble"); void b.offsetWidth; b.classList.add("wobble"); };
  t.querySelector("nav").onclick = (e) => {
    const b = e.target.closest("button");
    if (!b) return;
    if (b.dataset.a === "new") { t.classList.add("leaving"); setTimeout(() => t.remove(), 700); onStart(); }
    if (b.dataset.a === "cont") nope(b, "You have no save, coward.");
    if (b.dataset.a === "config") nope(b, "No.");
    if (b.dataset.a === "gallery") gallery();
  };
  stage().appendChild(t);
}

// The title's key visual: the Merchant as a dating-sim love interest. Original art, plus blush, sparkles,
// a big heart and floating gold drawn on top (coordinates over the sprite are in its own 522x658 pixels).
function keyVisual() {
  const coins = [[1040, 170, 0.9, 0], [1760, 120, 0.7, 1.2], [1830, 560, 1, 0.5], [1090, 760, 0.8, 1.8], [1500, 60, 0.6, 2.4]]
    .map(([x, y, k, d]) => `<img class="kv-coin" src="/assets/ui/gold_coin_price.png" style="left:${x}px;top:${y}px;--k:${k};animation-delay:-${d}s" alt="">`).join("");
  return `<div class="kv">
    <div class="kv-rays"></div><div class="kv-dots"></div>
    <svg class="kv-heart" viewBox="0 0 100 92"><path d="M50 88 C20 66 2 48 2 28 C2 12 14 2 28 2 C38 2 46 8 50 16 C54 8 62 2 72 2 C86 2 98 12 98 28 C98 48 80 66 50 88Z"/></svg>
    ${coins}
    <div class="kv-m">
      <img src="/assets/merchant_2x.png" alt="">
      <svg viewBox="0 0 522 658">
        <g class="kv-blush">
          <ellipse cx="226" cy="152" rx="20" ry="11"/><ellipse cx="292" cy="146" rx="20" ry="11"/>
          <path d="M214 146 l-6 12 M224 145 l-6 12 M234 145 l-6 12 M280 140 l-6 12 M290 139 l-6 12 M300 139 l-6 12"/>
        </g>
        <g class="kv-doki"><path d="M392 52 q14 -16 30 -10 M400 72 q18 -8 30 4 M396 32 q6 -20 24 -24"/></g>
        <g class="kv-spark">
          <path d="M150 40 l6 18 l18 6 l-18 6 l-6 18 l-6 -18 l-18 -6 l18 -6z"/>
          <path d="M430 150 l4 12 l12 4 l-12 4 l-4 12 l-4 -12 l-12 -4 l12 -4z"/>
          <path d="M110 230 l3 9 l9 3 l-9 3 l-3 9 l-3 -9 l-9 -3 l9 -3z"/>
        </g>
        <text class="kv-heartlet" x="330" y="30">♥</text>
      </svg>
    </div>
    <div class="kv-bubble">I-it's not like I gave you a discount because I <i>like</i> you or anything…</div>
  </div>`;
}

const CGS = [
  { id: "favourite", title: "He Blushed (Probably)", caption: "It's hard to tell under the hood." },
  { id: "darling", title: "Under the Hood", caption: "No, you still can't see his face." },
];

function gallery() {
  const got = store.get("cgs", []);
  const g = el(`<div class="overlay gallery"><h2>Gallery</h2><div class="cgs">${CGS.map((c, i) =>
    got.includes(c.id)
      ? `<figure><div class="cg-thumb"><img src="/assets/merchant_2x.png"></div><figcaption>CG ${i + 1}: ${c.title}</figcaption></figure>`
      : `<figure class="locked"><div class="cg-thumb">?</div><figcaption>CG ${i + 1}: ???</figcaption></figure>`).join("")}
    </div><p>${got.length}/${CGS.length} unlocked · tap to close</p></div>`);
  g.onclick = () => g.remove();
  stage().appendChild(g);
}

// ---------------------------------------------------------------- CG cut-in
export function cgUnlock(id) {
  const i = CGS.findIndex((c) => c.id === id);
  if (i < 0) return;
  const got = store.get("cgs", []);
  if (!got.includes(id)) store.set("cgs", [...got, id]);
  const c = CGS[i];
  const o = el(`<div class="overlay cg"><div class="petals"></div>
    <div class="cg-frame"><img src="/assets/merchant_2x.png"><div class="sparkles"></div></div>
    <div class="cg-text"><div class="cg-tag">CG ${i + 1}/${CGS.length} UNLOCKED</div><h2>“${c.title}”</h2><p>${c.caption}</p></div></div>`);
  petals(o.querySelector(".petals"), 30);
  o.onclick = () => o.remove();
  stage().appendChild(o);
  setTimeout(() => o.classList.add("out"), 3800);
  setTimeout(() => o.remove(), 4400);
}

// ---------------------------------------------------------------- achievements
const ACH = {
  crush: ["Hooded Crush", "Complimented a man whose face you've never seen."],
  fool: ["Paid Full Price Like a Fool", "He's laughing all the way to the bank."],
  friendzone: ["Friend-Zoned by a Hooded Man", "You were a Regular. You blew it."],
  transaction: ["Love Is a Transaction", "Reached Darling. Your wallet weeps."],
  cheapskate: ["Cheapskate", "He refuses to haggle with you anymore."],
  simp: ["Simp", "Bought 3 things at list price. For him."],
  secret: ["Under the Counter", "He showed you the good stuff."],
};
const unlocked = new Set();
export function achieve(key) {
  if (unlocked.has(key) || !ACH[key]) return;
  unlocked.add(key);
  const [name, desc] = ACH[key];
  const t = el(`<div class="toast"><div class="trophy">🏆</div><div><b>Achievement unlocked</b><div class="ach-name">${name}</div><small>${desc}</small></div></div>`);
  $("toasts").appendChild(t);
  setTimeout(() => t.classList.add("out"), 4200);
  setTimeout(() => t.remove(), 4800);
}

// ---------------------------------------------------------------- heart popups + level-up flash
export function heartPop(delta) {
  if (!delta) return;
  const text = typeof delta === "string" ? delta : delta > 0 ? `+${delta} ♥` : `${delta} 💔`;
  const p = el(`<div class="heart-pop ${typeof delta === "string" ? "note" : delta > 0 ? "up" : "down"}">${text}</div>`);
  p.style.left = `${1300 + (Math.random() - 0.5) * 120}px`;
  stage().appendChild(p);
  setTimeout(() => p.remove(), 1600);
}

export function levelFlash(up, level) {
  const v = el(`<div class="vignette ${up ? "up" : "down"}"><div>${up ? "♥ Affection up! ♥" : "Affection down…"}<small>${level}</small></div></div>`);
  stage().appendChild(v);
  setTimeout(() => v.remove(), 1900);
}

// ---------------------------------------------------------------- endings
export function ending(kind, stats) {
  if (kind !== "good") narrate(kind, 900); // the good ending's line plays over the walk-out
  const E = {
    good: ["TRUE LOVE END", "Out of the Spire, Together", "You never reached the top. You didn't need to. Somewhere below, a tent stands empty, with a sign: \"Closed. Gone on a date.\""],
    friends: ["BAD END", "Just Friends", "He waves you off at the top of the Spire. \"You're a good customer,\" he says. Customer. The word echoes for a thousand years."],
    fight: ["BAD END", "Killed Over a Discount", "You tried to haggle with a man who sells weapons. He gave you a 100% discount on your remaining HP."],
  }[kind];
  const roll = [
    ["The Merchant", "Himself"],
    ["The Customer", "You ♥"],
    ["Haggling", "A random number generator"],
    ["Voice", "Fish Audio"],
    ["Hidden floor prices", "The game, not the AI"],
    ["Love", stats.affection],
    ["Visits", `${stats.visits ?? 1}`],
    ["Gold spent", `${stats.spent}`],
    ["Gold haggled off", `${stats.saved}`],
    ["Original game", "Slay the Spire 2 by Mega Crit"],
    ["Special thanks", "Neow (please don't tell her)"],
  ];
  const o = el(`<div class="overlay ending end-${kind}"><div class="petals"></div>
    <div class="end-card"><div class="end-kind">${E[0]}</div><h1>“${E[1]}”</h1><p>${E[2]}</p></div>
    <div class="staff"><div class="crawl">${roll.map(([a, b]) => `<div><span>${a}</span><b>${b}</b></div>`).join("")}
      <div class="fin">Thank you for shopping ♥</div></div></div>
    <button class="again">New Game+</button></div>`);
  if (kind === "good") petals(o.querySelector(".petals"), 36);
  o.querySelector(".again").onclick = () => location.assign(location.pathname);
  stage().appendChild(o);
}

// ---------------------------------------------------------------- intro: the real trailer, then the twist
// A few seconds of the official STS2 reveal trailer (Mega Crit), played straight, then a record-scratch
// freeze into the pink parody title. Click/any key or the Skip button jumps straight to the title.
export function intro(onDone, laugh, onFreeze) {
  const o = el(`<div id="intro">
    <div class="intro-start"><div class="intro-press">▶ Click to begin</div><small>sound on 🔊</small></div>
    <video src="/assets/trailer.mp4" playsinline preload="auto"></video>
    <div class="intro-caption"></div>
    <button class="intro-skip">Skip ▸▸</button>
  </div>`);
  const v = o.querySelector("video");
  const cap = o.querySelector(".intro-caption");
  let done = false, started = false;
  const timers = [];
  const finish = () => {
    if (done) return;
    done = true;
    timers.forEach(clearTimeout);
    v.pause();
    o.classList.add("pink-wipe");
    setTimeout(() => o.remove(), 900);
    onDone();
  };
  const twist = () => {
    if (done) return;
    o.classList.add("frozen");
    onFreeze?.();
    const lines = [[0, "Civilization waited 1,000 years for the Spire to reopen…"], [1900, "…so you could finally"], [3100, "<b>date the Merchant.</b>"]];
    lines.forEach(([t, text]) => timers.push(setTimeout(() => { cap.innerHTML = text; cap.classList.remove("pop"); void cap.offsetWidth; cap.classList.add("pop"); if (t === 3100) laugh?.(); }, t)));
    timers.push(setTimeout(finish, 5000));
  };
  const start = () => {
    if (started) return;
    started = true;
    o.classList.add("playing");
    v.play().catch(twist);
  };
  v.addEventListener("ended", twist);
  v.addEventListener("error", finish); // no trailer file: go straight to the title
  o.querySelector(".intro-start").onclick = start;
  o.querySelector(".intro-skip").onclick = (e) => { e.stopPropagation(); finish(); };
  const onKey = (e) => {
    if (done) return removeEventListener("keydown", onKey);
    if (!started) start();
    else if (e.key === "Escape" || e.key === " " || e.key === "Enter") finish();
  };
  addEventListener("keydown", onKey);
  stage().appendChild(o);
}

// ---------------------------------------------------------------- between visits
// ---------------------------------------------------------------- judgement stamps: the AI's calls, made visible
// kind: love | bad | deal | info. Slams a big dating-sim reaction onto the screen for ~1.6s.
export function stamp(text, kind = "info", sub = "") {
  document.querySelectorAll(".stamp").forEach((x) => x.remove());
  const o = el(`<div class="stamp st-${kind}"><div class="st-burst"></div><div class="st-text">${text}</div>${sub ? `<div class="st-sub">${sub}</div>` : ""}</div>`);
  stage().appendChild(o);
  if (kind === "bad") { stage().classList.remove("jolt"); void stage().offsetWidth; stage().classList.add("jolt"); }
  if (kind === "love" || kind === "deal") {
    for (let i = 0; i < 14; i++) {
      const h = el(`<i class="st-heart">${kind === "deal" ? "✦" : "♥"}</i>`);
      h.style.setProperty("--a", `${(i / 14) * 360}deg`);
      h.style.setProperty("--r", `${220 + Math.random() * 160}px`);
      o.appendChild(h);
    }
  }
  setTimeout(() => o.remove(), 1900);
}

// ---------------------------------------------------------------- the narrator: your bestie (scripts/narrate.mjs)
const narrLines = fetch("/assets/narration/lines.json").then((r) => r.json()).catch(() => ({}));
let narrAudio;
let onNarrate;
export const setNarrateListener = (fn) => { onNarrate = fn; };

// Cuts the bestie off mid-line (the Merchant always has the floor).
export function stopNarration() {
  narrAudio?.pause();
  const n = document.querySelector(".narr");
  if (n) { n.classList.add("out"); setTimeout(() => n.remove(), 500); }
}

// Plays a pre-rendered narrator line with a subtitle; resolves to its length in ms (0 if missing).
export async function narrate(key, delay = 0) {
  const line = (await narrLines)[key];
  if (!line) return 0;
  setTimeout(() => onNarrate?.(line.text), delay);
  setTimeout(() => {
    narrAudio?.pause();
    narrAudio = new Audio(`/assets/narration/${key}.mp3`);
    narrAudio.volume = 1;
    narrAudio.play().catch(() => {});
    document.querySelector(".narr")?.remove();
    const n = el(`<div class="narr"><div class="narr-name">Your Bestie ♥</div><div class="narr-text">${line.text}</div></div>`);
    stage().appendChild(n);
    setTimeout(() => { n.classList.add("out"); setTimeout(() => n.remove(), 500); }, line.ms + 400);
  }, delay);
  return line.ms;
}

// Subtitle for a live bestie line (her own Fish agent speaks it); returns a function that hides it.
export function narrSubtitle(text) {
  onNarrate?.(text);
  document.querySelector(".narr")?.remove();
  const n = el(`<div class="narr"><div class="narr-name">Your Bestie ♥</div><div class="narr-text">${text}</div></div>`);
  stage().appendChild(n);
  return () => { n.classList.add("out"); setTimeout(() => n.remove(), 500); };
}

// The climb between shops, as a tiny cartoon: the Ironclad bumps into a monster, a dust-cloud brawl, the monster
// blasts off into the sky, gold rains down, and the Merchant's tent is just ahead. onMid swaps the shop in
// underneath before the pink wipe.
export async function floorCard({ from, to, act, foe, gold, bonus, narr }, onMid) {
  const coins = gold ? Array.from({ length: 9 }, (_, i) =>
    `<img class="ec-coin" src="/assets/ui/gold_coin_price.png" style="--dx:${(i - 4) * 46 + (Math.random() - 0.5) * 30}px;--h:${170 + Math.random() * 140}px;animation-delay:${3.05 + i * 0.04}s" alt="">`).join("") : "";
  const o = el(`<div class="overlay encounter">
    <img class="ec-bg" src="/assets/combat/bg.png" alt="">
    <div class="ec-floor">${act ? `Act ${act} · ` : ""}Floor ${from ? `${from} <span>▸</span> ` : ""}${to}</div>
    ${bonus ? `<div class="ec-bonus">Wait… another tent?!<small>BONUS SHOP: one last chance</small></div>` : ""}
    <div class="ec-hero"><img src="/assets/combat/ironclad.png" alt=""><b class="ec-bang">!</b></div>
    <div class="ec-foe"><img src="/assets/monsters/${foe.id}.png" alt=""><b class="ec-bang">!</b></div>
    <div class="ec-cloud"><img class="peek ph" src="/assets/combat/ironclad.png" alt=""><img class="peek pf" src="/assets/monsters/${foe.id}.png" alt=""><i></i><i></i><i></i><i></i><i></i><i></i><span class="ec-pow p1">POW!</span><span class="ec-pow p2">BONK!</span><span class="ec-pow p3">★</span></div>
    <img class="ec-fly" src="/assets/monsters/${foe.id}.png" alt=""><div class="ec-twinkle">✦</div>
    ${coins}
    <div class="ec-cap">${gold ? `You beat ${foe.name} and found <b>+${gold} gold</b>!` : `${foe.name[0].toUpperCase() + foe.name.slice(1)} stood between you and the shop. Not anymore.`}</div>
  </div>`);
  o.insertAdjacentHTML("beforeend", `<div class="petals"></div>`);
  petals(o.querySelector(".petals"), 18);
  stage().appendChild(o);
  const hold = bonus ? 1400 : 0;
  if (bonus) { o.classList.add("wait"); setTimeout(() => o.classList.remove("wait"), hold); }
  // Stay until the bestie has finished her line.
  const said = narr ? await narrate(narr, 300) : 0;
  const end = Math.max(4800, said ? 300 + said + 300 - hold : 0);
  setTimeout(onMid, end - 100 + hold);
  setTimeout(() => o.classList.add("out"), end + hold);
  setTimeout(() => o.remove(), end + 700 + hold);
}

// ---------------------------------------------------------------- the good ending: walking out, hand in hand
export function walkOut(onDone) {
  narrate("good", 1200);
  const o = el(`<div class="overlay walkout"><img class="wo-bg" src="/assets/combat/spire_dusk.jpg" alt="">
    <div class="petals"></div>
    <div class="wo-pair">
      <img class="wo-ironclad" src="/assets/combat/ironclad.png" alt="">
      <div class="wo-heart">♥</div>
      <div class="wo-merchant"><img class="wo-rug" src="/assets/ui/shop_rug_full.png" alt=""><img class="wo-m" src="/assets/merchant_2x.png" alt=""></div>
    </div>
    <div class="wo-cap"></div></div>`);
  petals(o.querySelector(".petals"), 40);
  stage().appendChild(o);
  const cap = o.querySelector(".wo-cap");
  [[600, "Hand in hand, you walk out of the Spire."], [3400, "He refused to stand up. He brought the rug."], [6200, "Civilization waited 1,000 years for this."]]
    .forEach(([t, text]) => setTimeout(() => { cap.textContent = text; cap.classList.remove("pop"); void cap.offsetWidth; cap.classList.add("pop"); }, t));
  o.onclick = () => { o.remove(); onDone(); };
  setTimeout(() => { if (o.isConnected) { o.remove(); onDone(); } }, 9000);
}

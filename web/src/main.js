// Haggle the Spire: the STS2 merchant room, with the merchant as a Fish voice agent you can haggle with.
// Shop generation lives in shop.js, haggling rules in haggle.js; this file is rendering + voice plumbing.
// Web SDK docs: https://docs.fish.audio/agents/deploy/web-sdk.md
import { AgentSession } from "@fishaudio/agent-client";
import { rollShop, describeForAgent, makeRng, rollSecretCard } from "./shop.js";
import { Haggle } from "./haggle.js";
import { Affection, SECRET_AT, SECRET_LOST_BELOW, MAX_LEVEL } from "./affection.js";
import { startMusic, stopMusic } from "./music.js";
import * as meta from "./meta.js";
import { startFight } from "./fight.js";

const $ = (id) => document.getElementById(id);
const A = "/assets";

// ---------------------------------------------------------------- stage scaling
const stage = $("stage");
const fit = () => {
  const k = Math.min(innerWidth / 1920, innerHeight / 1080);
  stage.style.transform = `scale(${k}) translate(-50%, -50%)`;
};
addEventListener("resize", fit);
fit();

// ---------------------------------------------------------------- data
const [catalog, cardIndex] = await Promise.all([
  fetch("/data/catalog.json").then((r) => r.json()),
  fetch(`${A}/cards/index.json`).then((r) => r.json()),
]);
for (const c of catalog.cards) c.artOk = c.locKey in cardIndex;

const params = new URLSearchParams(location.search);
const seed = Number(params.get("seed")) || Math.floor(Math.random() * 1e9);
const player = { gold: 300, potionSlots: 3, potions: [], deck: [], relics: [] };
let items = rollShop(catalog, seed);
// A reload is a new run: his feelings start from scratch (they carry over between the shops of a run). ?affection=75 is the demo shortcut.
const affection = new Affection(params.has("affection") ? Number(params.get("affection")) : 20);
let haggle = new Haggle(items, player, makeRng(seed ^ 0x5eed), affection);
const stats = { bought: 0, fullPrice: 0, spent: 0, everRegular: false, saved: 0, boughtThisVisit: 0 };
// A run is the Merchant's shops on the way up the Spire, as in the original: two in Act 1, one in Act 2,
// one in Act 3, and maybe (50%) a bonus one near the top. Win his heart (level 5) by the last shop.
// Same gold, deck and feelings throughout; new stock each floor; everything between shops is off-screen.
const SHOPS = [{ act: 1, floor: 6 }, { act: 1, floor: 13 }, { act: 2, floor: 24 }, { act: 3, floor: 40 }];
const BONUS_SHOP = { act: 3, floor: 46, bonus: true };
const run = { visit: 1, ...SHOPS[0] };
let secret = null;
let selected = null;
let session;

// The Merchant's real voice level. The agent's "speaking" mode ends when his reply is generated, not when it
// has finished playing, so we tap the incoming WebRTC audio and measure it: he's "audible" while there's sound.
let voiceAt = 0; // last time his voice was audible
let bestieVoiceAt = 0; // same for the bestie's agent
let tapping = "merchant"; // whose session is connecting, so each WebRTC track is credited to the right voice
let voiceCtx;
const NativeRTC = window.RTCPeerConnection;
if (NativeRTC) {
  window.RTCPeerConnection = class extends NativeRTC {
    constructor(...args) {
      super(...args);
      const who = tapping;
      this.addEventListener("track", (e) => { if (e.track.kind === "audio") tapVoice(e.track, who); });
    }
  };
}
function tapVoice(track, who) {
  voiceCtx ??= new AudioContext();
  voiceCtx.resume().catch(() => {});
  const an = voiceCtx.createAnalyser();
  an.fftSize = 512;
  voiceCtx.createMediaStreamSource(new MediaStream([track])).connect(an);
  const buf = new Float32Array(an.fftSize);
  const tick = setInterval(() => {
    if (track.readyState === "ended") return clearInterval(tick);
    an.getFloatTimeDomainData(buf);
    let sum = 0;
    for (const v of buf) sum += v * v;
    if (Math.sqrt(sum / buf.length) > 0.003) {
      if (who === "bestie") bestieVoiceAt = performance.now();
      else voiceAt = performance.now();
    }
  }, 50);
}
const merchantAudible = () => performance.now() - voiceAt < 1200; // whispers and "..." pauses count as talking
let replies = 0; // Merchant replies so far, so a line can wait for the reply to a game event
let bestieMuted = false; // mic muted while the bestie talks, so the Merchant doesn't hear her
// The bestie, live: her own Fish agent (scripts/create-bestie.mjs) with the designed narrator voice. One agent at
// a time: she waits for the Merchant to answer whatever just happened and fall silent, then gets the event plus his
// last line. While she has the floor, game events and typed lines are held and the mic is muted, so he can't
// start; she hands it back once she has finished speaking.
const BESTIE_EVENTS = {
  up3: "Affection just rose to Regular: the Merchant is warming up to the player.",
  up4: "Affection just rose to Favourite: the Merchant is clearly flustered by the player.",
  up5: "Affection just hit Darling, the max: the Merchant is head over heels.",
  down: "The player just said something that hurt the Merchant's feelings; affection dropped.",
  lowball2: "The player lowballed the Merchant twice in a row. One more and he snaps and attacks.",
  secret: "The Merchant just revealed a secret rare card he hides from everyone else, only for the player.",
  capped: "The player keeps flirting but the Merchant won't warm up any more this visit; it has to wait until the next floor.",
};
let bestieSession;
let bestieMode = "listening";
let bestieReplies = 0;
let bestieQueued = null; // the event waiting for her turn; a newer one replaces it (the secret beats the rest)
let bestieAt = 0;
let bestieSpeaking = false; // she has the floor
let lastMerchantLine = "";
const heldTyped = [];
const sleep = (ms) => new Promise((r) => setTimeout(r, ms));

async function connectBestie() {
  if (bestieSession) return bestieSession;
  const res = await fetch("/api/session", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ agent: "bestie", dynamicVariables: {} }),
  });
  if (!res.ok) return undefined;
  tapping = "bestie";
  try {
    bestieSession = await AgentSession.start({
      sessionToken: await res.json(),
      microphone: false,
      callbacks: {
        onAgentResponse: ({ text }) => { bestieReplies++; bestieHide?.(); bestieHide = meta.narrSubtitle(text.replace(/\[[^\]]*\]\s*/g, "").trim()); },
        onModeChange: (mode) => { bestieMode = mode; },
        onError: (err) => console.warn("[bestie]", err),
        onDisconnect: () => { bestieSession = undefined; },
      },
    });
  } catch (err) { console.warn("[bestie]", err); }
  tapping = "merchant";
  return bestieSession;
}
let bestieHide;

async function bestie(key) {
  if (!key || !session || bestieSpeaking) return;
  if (bestieQueued) { if (bestieQueued !== "secret") bestieQueued = key; return; }
  if (Date.now() - bestieAt < 8000) return;
  bestieQueued = key;
  // His turn first: the event reaches him, he replies, and he goes quiet.
  const since = replies;
  let quiet = 0;
  for (let waited = 0; quiet < 1200; waited += 150) {
    if (waited > 30000 || !session) { bestieQueued = null; return; }
    await sleep(150);
    const busy = agentMode !== "listening" || merchantAudible() || pendingGame.length || (replies === since && waited < 10000);
    quiet = busy ? 0 : quiet + 150;
  }
  key = bestieQueued;
  bestieQueued = null;
  bestieAt = Date.now();
  bestieSpeaking = true; // her turn: he gets nothing until she's done
  const muted = !session.micMuted;
  if (muted) { bestieMuted = true; session.setMicMuted(true).catch(() => {}); }
  const s = await connectBestie();
  if (s) {
    const before = bestieReplies;
    s.sendUserMessage(`[EVENT] ${BESTIE_EVENTS[key]}\nThe Merchant just said: ${lastMerchantLine || "(nothing yet)"}`, { audio: true });
    // Her turn ends when she has replied and her voice has been silent for a moment.
    for (let waited = 0; waited < 15000; waited += 150) {
      await sleep(150);
      const heard = performance.now() - bestieVoiceAt < 900;
      if (bestieReplies > before && bestieMode === "listening" && !heard && waited > 1500) break;
    }
    bestieHide?.();
  }
  bestieSpeaking = false;
  if (muted && session && bestieMuted) session.setMicMuted(false).catch(() => {});
  bestieMuted = false;
  for (const text of heldTyped.splice(0)) session?.sendUserMessage(text, { audio: true });
  flushGame();
}

// ---------------------------------------------------------------- audio (original merchant SFX/music)
const sfx = (name, vol = 0.6) => {
  const a = new Audio(`${A}/audio/${name}.mp3`);
  a.volume = vol;
  a.play().catch(() => {});
};
const pick = (arr) => arr[Math.floor(Math.random() * arr.length)];
const VO = {
  welcome: ["welcome_v1_rr1", "welcome_v1_rr2", "welcome_v1_rr3", "welcome_v1_rr4"],
  thanks: ["thank_yous_v1_rr1", "thank_yous_v1_rr2", "thank_yous_v1_rr3"],
  sad: ["dissapointment_v1_rr1", "dissapointment_v1_rr2", "dissapointment_v1_rr3"],
  laugh: ["laughter_v1_rr1", "laughter_v1_rr2"],
};
const vo = (k) => sfx(`sts2_sfx_vo_merchant_${pick(VO[k])}`, 0.45);
const music = new Audio(`${A}/audio/sts2_merchant_act1_v2.mp3`);
music.loop = true;
music.volume = 0.12;

// ---------------------------------------------------------------- text markup
const fmt = (s) =>
  String(s ?? "")
    .replace(/&/g, "&amp;").replace(/</g, "&lt;")
    .replace(/\[icon:energy\]/g, `<img class="icon-energy" src="${A}/frame/energy_ironclad.png">`)
    .replace(/\[icon:star\]/g, "★")
    .replace(/\[gold\]/g, '<span class="g">').replace(/\[blue\]/g, '<span class="b">')
    .replace(/\[green\]/g, '<span class="gr">').replace(/\[red\]/g, '<span class="r">')
    .replace(/\[purple\]/g, '<span class="p">')
    .replace(/\[\/(gold|blue|green|red|purple)\]/g, "</span>")
    .replace(/\[\/?[a-z]+\]/g, "")
    .replace(/\n/g, "<br>");

// ---------------------------------------------------------------- rendering
function cardEl(card, colorless, scale) {
  const pool = colorless ? "colorless" : "ironclad";
  const kind = card.type.toLowerCase();
  const rar = card.rarity.toLowerCase();
  const wrap = document.createElement("div");
  wrap.style.width = `${598 * scale}px`;
  wrap.style.height = `${844 * scale}px`;
  wrap.innerHTML = `<div class="card" style="transform:scale(${scale})">
    <img class="portrait" src="${A}/cards/${card.art}.png">
    <img class="frame" src="${A}/frame/frame_${kind}_${pool}.png">
    <img class="border" src="${A}/frame/border_${kind}_${rar}.png">
    <img class="banner" src="${A}/frame/banner_${rar}.png">
    <div class="title">${card.name}</div>
    <img class="plaque" src="${A}/frame/plaque_${rar}.png">
    <div class="type">${card.type}</div>
    <img class="orb" src="${A}/frame/energy_${pool}.png">
    <div class="cost">${card.cost}</div>
    <div class="desc"><div>${fmt(card.description)}</div></div>
  </div>`;
  return wrap;
}

function iconSrc(it) {
  if (it.kind === "relic") return `${A}/relics/${it.data.art}.png`;
  if (it.kind === "potion") return `${A}/potions/${it.data.art}.png`;
  return `${A}/ui/card_removal.png`;
}

function priceEl(it) {
  const el = document.createElement("div");
  el.className = "price" + (it.price > player.gold ? " unaffordable" : "");
  const shown = it.sold ? it.paid : it.agreed ?? it.price;
  el.innerHTML = `<img src="${A}/ui/gold_coin_price.png"><b>${it.price}</b>`;
  if (!it.sold && it.agreed && it.agreed < it.price)
    el.innerHTML = `<img src="${A}/ui/gold_coin_price.png"><s>${it.price}</s> <b class="deal">${shown}</b>`;
  return el;
}

// A smaller stall on the left half so the Merchant stays visible: the original rug texture, squeezed to
// 1160x930, with the original card spacing (263px) and slot scale (0.65).
const CARD_S = 0.5 * 0.65;
const RUG = [20, 100];
const SLOTS = {
  card: [[185, 230], [448, 230], [711, 230], [974, 230]],
  colorless: [],
  relic: [[150, 640], [300, 640]],
  potion: [[450, 640], [600, 640]],
  removal: [[790, 650]],
  secret: [[1010, 625]],
};
function layout() {
  const used = { card: 0, colorless: 0, relic: 0, potion: 0, removal: 0, secret: 0 };
  return items.map((it) => {
    const k = it.secret ? "secret" : it.kind === "card" ? (it.colorless ? "colorless" : "card") : it.kind;
    const [x, y] = SLOTS[k][used[k]++];
    return [RUG[0] + x, RUG[1] + y];
  });
}

function renderGoods() {
  const goods = $("goods");
  goods.innerHTML = "";
  const pos = layout();
  items.forEach((it, i) => {
    if (it.hidden) return;
    const el = document.createElement("div");
    el.className = `good kind-${it.kind}` + (it.sold ? " sold" : "") + (selected === it ? " selected" : "") + (it.secret ? " secret" : "");
    el.dataset.slot = it.slot;
    el.style.left = `${pos[i][0]}px`;
    el.style.top = `${pos[i][1]}px`;
    el.dataset.cx = pos[i][0];
    el.dataset.cy = pos[i][1];
    if (it.kind === "card") el.appendChild(cardEl(it.data, it.colorless, CARD_S));
    else {
      el.classList.add("icon-good");
      if (it.kind === "removal") el.classList.add("removal");
      el.innerHTML = `<img class="icon" src="${iconSrc(it)}" alt="${it.data.name}">`;
    }
    if (it.onSale && !it.sold) el.insertAdjacentHTML("beforeend", `<img class="sale" src="${A}/ui/sale_tag.png">`);
    if (it.secret) el.insertAdjacentHTML("beforeend", `<div class="secret-ribbon">♥ SECRET ♥</div>`);
    el.insertAdjacentHTML("beforeend", `<div class="slotno">${it.slot}</div>`);
    el.appendChild(priceEl(it));
    el.onclick = (e) => { e.stopPropagation(); selected === it ? deselect() : select(it, true); };
    goods.appendChild(el);
  });
}

function renderHud() {
  $("gold").textContent = player.gold;
  $("potion-belt").innerHTML = Array.from({ length: player.potionSlots }, (_, i) =>
    `<span class="slot">${player.potions[i] ? `<img src="${A}/potions/${player.potions[i].art}.png">` : ""}</span>`).join("");
  $("deck-count").textContent = `Bought: ${player.deck.length} cards, ${player.relics.length} relics`;
  $("visit").textContent = `Act ${run.act} · Floor ${run.floor} · ${run.bonus ? "Bonus shop!" : `Shop ${run.visit}/${SHOPS.length}`}`;
  $("climb").innerHTML = run.visit >= SHOPS.length ? "▲ Climb to the top <small>(the run ends)</small>" : "▲ Climb on <small>(next shop)</small>";
  $("saved").textContent = haggle.saved ? `Haggled off: ${haggle.saved} gold` : "";
  const a = $("affection");
  a.querySelector(".hearts").textContent = affection.hearts;
  a.querySelector(".aff-bar i").style.width = `${affection.value}%`;
  a.querySelector(".aff-level").textContent = `Lv ${affection.levelNo} · ${affection.level}`;
  $("merchant").classList.toggle("blush", affection.index >= 3);
}

function renderDetail() {
  const d = $("detail");
  if (!selected) return d.classList.add("hidden");
  const it = selected;
  d.classList.remove("hidden");
  d.innerHTML = "";
  const art = document.createElement("div");
  art.className = "art";
  if (it.kind === "card") art.appendChild(cardEl(it.data, it.colorless, 0.3));
  else art.innerHTML = `<img src="${iconSrc(it)}" style="width:${it.kind === "removal" ? 120 : 130}px">`;
  const info = document.createElement("div");
  const kindText = it.kind === "card" ? `${it.secret ? "♥ Secret · " : ""}${it.colorless ? "Colorless" : "Ironclad"} · ${it.data.rarity} ${it.data.type}` : it.kind === "removal" ? "Service" : `${it.data.rarity} ${it.kind}`;
  const price = it.agreed ?? it.price;
  info.innerHTML = `<h2>#${it.slot} ${it.data.name}</h2><div class="meta">${kindText}</div><div class="text">${fmt(it.data.description)}</div>`;
  if (!it.sold && it.kind !== "removal") {
    const btn = document.createElement("button");
    btn.textContent = `Buy for ${price} gold`;
    btn.disabled = price > player.gold;
    btn.onclick = () => buyDirect(it, price);
    info.appendChild(btn);
  }
  const close = document.createElement("button");
  close.className = "close";
  close.textContent = "×";
  close.title = "Close";
  d.append(art, info, close);
  const el = document.querySelector(`.good[data-slot="${it.slot}"]`);
  if (el) {
    const cx = +el.dataset.cx, cy = +el.dataset.cy;
    const w = 620, right = cx + 130 + w < 1900;
    d.style.left = `${right ? cx + 120 : cx - 120 - w}px`;
    d.style.top = `${Math.max(90, Math.min(cy - 150, 1080 - 340))}px`;
  }
}

function renderAll() {
  renderGoods();
  renderHud();
  renderDetail();
}

// ---------------------------------------------------------------- player actions
function deselect() {
  if (!selected) return;
  selected = null;
  renderAll();
}

// The item popup closes with its ×, a tap anywhere else, Esc, or tapping the item again.
$("detail").addEventListener("click", (e) => { e.stopPropagation(); if (e.target.closest(".close")) deselect(); });
$("stage").addEventListener("click", (e) => { if (!e.target.closest("button, .good, #mic, #call")) deselect(); });
addEventListener("keydown", (e) => { if (e.key === "Escape") deselect(); });

function select(it, byPlayer) {
  selected = it;
  renderAll();
  if (byPlayer && session && !it.sold)
    gameMsg(`The customer picks up #${it.slot} ${it.data.name} (${it.price} gold) and looks at it.`);
}

function buyDirect(it, price) {
  if (it.kind === "potion" && player.potions.length >= player.potionSlots) return say(pick(catalog.merchantLines.purchaseFailureSpace));
  if (price > player.gold) return say(pick(catalog.merchantLines.purchaseFailureGold));
  haggle.buy(it, price);
  vo("thanks");
  say(pick(catalog.merchantLines.purchaseSuccess));
  gameMsg(`The customer just paid ${price} gold for #${it.slot} ${it.data.name} without haggling. They have ${player.gold} gold left.`);
  renderAll();
}

function point(it) {
  const el = document.querySelector(`.good[data-slot="${it.slot}"]`);
  if (!el) return;
  el.classList.remove("pointed");
  void el.offsetWidth;
  el.classList.add("pointed");
}

// ---------------------------------------------------------------- merchant speech + log
// Visual-novel text box: types the line out; a streamed continuation keeps typing from where it was.
let typing = { full: "", shown: 0, timer: 0 };
function say(text) {
  const t = $("bubble-text");
  const cont = text.startsWith(typing.full.slice(0, typing.shown));
  typing.full = text;
  if (!cont) typing.shown = 0;
  clearInterval(typing.timer);
  typing.timer = setInterval(() => {
    typing.shown = Math.min(typing.full.length, typing.shown + 2);
    t.innerHTML = fmt(typing.full.slice(0, typing.shown)) + (typing.shown < typing.full.length ? "" : '<span class="caret">▼</span>');
    if (typing.shown >= typing.full.length) clearInterval(typing.timer);
  }, 22);
}
// ---------------------------------------------------------------- chat log (retractable, above the type bar)
// Everything said in the shop, kept across reloads (localStorage) and savable as a text file.
const chat = []; // this run's conversation (a reload is a new run)
function record(who, text) {
  text = String(text ?? "").trim();
  if (!text) return;
  chat.push({ who, text, at: Date.now() });
  if (chat.length > 400) chat.splice(0, chat.length - 400);
  addChatRow(chat.at(-1));
}
function addChatRow({ who, text }) {
  const list = $("chatlog-list");
  const row = document.createElement("div");
  row.className = `cl-row cl-${who.toLowerCase()}`;
  row.innerHTML = `<b></b><span></span>`;
  row.firstChild.textContent = who === "Bestie" ? "Bestie ♥" : who;
  row.lastChild.textContent = text;
  list.appendChild(row);
  $("chatlog-count").textContent = chat.length;
  list.scrollTop = list.scrollHeight;
}
chat.forEach(addChatRow);
const setChatOpen = (open) => { $("chatlog").classList.toggle("closed", !open); meta.store.set("chatlogOpen", open); if (open) $("chatlog-list").scrollTop = 1e9; };
setChatOpen(!!meta.store.get("chatlogOpen"));
$("chatlog-tab").onclick = (e) => { e.stopPropagation(); setChatOpen($("chatlog").classList.contains("closed")); };
$("chatlog").addEventListener("click", (e) => e.stopPropagation());
$("chatlog-save").onclick = () => {
  const txt = chat.map((c) => `[${new Date(c.at).toLocaleTimeString()}] ${c.who}: ${c.text}`).join("\n");
  const a = document.createElement("a");
  a.href = URL.createObjectURL(new Blob([txt], { type: "text/plain" }));
  a.download = `doki-doki-merchant-log-${new Date().toISOString().slice(0, 16).replace(/[:T]/g, "-")}.txt`;
  a.click();
};
meta.setNarrateListener((text) => record("Bestie", text));

function log(text, cls = "") {
  const div = document.createElement("div");
  div.className = cls;
  div.textContent = text;
  $("log").appendChild(div);
  while ($("log").children.length > 12) $("log").firstChild.remove();
}

// ---------------------------------------------------------------- affection
// Game events are queued and sent once the merchant stops talking: injecting a message in the middle of a
// tool call or a spoken reply can cut the agent off.
const pendingGame = [];
let agentMode = "listening";
function gameMsg(text) {
  if (!session) return;
  pendingGame.push(`[GAME] ${text}`);
  flushGame();
}
function flushGame() {
  if (!session || agentMode === "speaking" || bestieSpeaking || !pendingGame.length) return;
  setTimeout(() => {
    if (!session || agentMode === "speaking" || bestieSpeaking || !pendingGame.length) return;
    session.sendUserMessage(pendingGame.splice(0).join("\n"), { audio: true }); // voiced even in a typed session
  }, 400);
}

affection.onChange((ev) => {
  meta.heartPop(ev.delta);
  if (ev.levelUp || ev.levelDown) {
    meta.levelFlash(ev.levelUp, ev.level);
    gameMsg(`Your feelings toward the customer changed: they are now your "${ev.level}". ${affection.mood}`);
  }
  if (affection.index >= 2) stats.everRegular = true;
  if (ev.why === "insult" && stats.everRegular) meta.achieve("friendzone");
  if (ev.why === "compliment") meta.achieve("crush");
  if (ev.levelUp) bestie({ Regular: "up3", Favourite: "up4", Darling: "up5" }[ev.level]);
  if (ev.levelDown) bestie("down");
  if (ev.levelUp && ev.level === "Favourite") meta.cgUnlock("favourite");
  if (ev.levelUp && ev.level === "Darling") { meta.cgUnlock("darling"); meta.achieve("transaction"); }
  checkSecret();
  renderAll();
  if (ev.capped && !cappedNoted) {
    cappedNoted = true;
    meta.heartPop("He needs time… come back next floor ♥");
    bestie("capped");
  }
});

let cappedNoted = false;

// Piss him off badly enough and the dating sim turns into Slay the Spire.
let fighting = false;
function provoke(why) {
  if (fighting) return;
  fighting = true;
  selected = null;
  renderDetail();
  gameMsg(`${why} You have had it with this customer. Drop the act completely: this is now a FIGHT. Shriek a furious villain battle cry and taunt them, one or two short lines.`);
  music.pause();
  stopMusic(0.2);
  vo("sad");
  const flash = document.createElement("div");
  flash.className = "rage-flash";
  stage.appendChild(flash);
  stage.style.setProperty("--fit", stage.style.transform);
  stage.classList.add("rage");
  setTimeout(() => { stage.classList.remove("rage"); flash.remove(); }, 900);
  setTimeout(() => startFight({
    deck: player.deck.filter((c) => c.type),
    cardEl,
    sfx: () => vo("laugh"),
    onEnd: () => {
      session?.end();
      meta.ending("fight", { affection: "💢 Hostile", spent: stats.spent, saved: stats.saved + haggle.saved, visits: run.visit });
    },
  }), 500);
}

// The under-the-counter Rare: shown once he really likes you and you've bought a couple of things.
function checkSecret() {
  if (!secret && affection.value >= SECRET_AT && stats.bought >= 2) {
    secret = rollSecretCard(catalog, items, seed);
    haggle.stock(secret);
    items.push(secret);
    revealSecret();
  } else if (secret && !secret.sold && !secret.hidden && affection.value < SECRET_LOST_BELOW) {
    secret.hidden = true;
    if (selected === secret) selected = null;
    gameMsg(`You are no longer fond enough of this customer: you quietly slip #${secret.slot} ${secret.data.name} back under the counter. Say so, sulkily.`);
  } else if (secret && secret.hidden && affection.value >= SECRET_AT) {
    secret.hidden = false;
    revealSecret();
  }
}
function revealSecret() {
  meta.achieve("secret");
  bestie("secret");
  renderAll();
  point(secret);
  gameMsg(`SECRET UNLOCKED. You like this customer so much that you pull something from under the counter: #${secret.slot} ${describeForAgent(secret).replace(/^#\d+ /, "")}. Reveal it in a hushed, flustered whisper, as something you don't show just anyone. It can be haggled like anything else.`);
}

const onBuy = (it, paid, floor) => {
  stats.bought++;
  stats.boughtThisVisit++;
  stats.spent += paid;
  if (paid >= it.price) {
    stats.fullPrice++;
    meta.achieve("fool");
    if (stats.fullPrice >= 3) meta.achieve("simp");
  }
  affection.purchase(paid, it.price, floor);
};
haggle.onBuy = onBuy;

// "Climb on": an off-screen floor or two of fighting, some gold, then the Merchant again with new stock.
// Monsters for the between-floor cartoon (art rendered from the game: tools/extract_monsters.py).
const FOES = [
  ["nibbit", "a Nibbit"], ["leaf_slime_s", "a Leaf Slime"], ["leaf_slime_m", "a slightly bigger Leaf Slime"], ["toadpole", "a Toadpole"],
  ["fuzzy_wurm_crawler", "a Fuzzy Wurm Crawler"], ["shrinker_beetle", "a Shrinker Beetle"], ["flying_mushrooms", "some Flying Mushrooms"],
  ["byrdpip", "a Byrdpip"], ["bowlbug", "a Bowlbug"], ["myte", "a Myte"], ["sneaky_gremlin", "a Sneaky Gremlin"],
  ["fat_gremlin", "a Fat Gremlin"], ["cultists", "a Cultist"], ["chomper", "a Chomper"],
].map(([id, name]) => ({ id, name }));
function nextVisit() {
  if (stats.boughtThisVisit === 0) affection.leftEmptyHanded();
  stats.saved += haggle.saved;
  session?.end();
  const rng = makeRng(seed + run.visit * 7919);
  const gold = 90 + rng.int(51);
  const from = run.floor;
  // The last guaranteed shop is the check: his heart is won, or maybe fate grants one more shop.
  let next = SHOPS[run.visit];
  if (!next) {
    if (affection.levelNo >= MAX_LEVEL || run.bonus) return finish();
    if (Math.random() >= 0.5 && !params.has("bonus")) return finish();
    next = BONUS_SHOP;
  }
  run.visit++;
  Object.assign(run, { act: next.act, floor: next.floor, bonus: !!next.bonus });
  meta.floorCard({ from, to: run.floor, act: run.act, foe: rng.item(FOES), gold, bonus: run.bonus, narr: run.bonus ? "bonus" : `shop${run.visit}` }, () => {
    affection.newVisit();
    cappedNoted = false;
    player.gold += gold;
    items = rollShop(catalog, seed + run.visit * 104729);
    haggle = new Haggle(items, player, makeRng(seed ^ (0x5eed + run.visit)), affection);
    haggle.onBuy = onBuy;
    secret = null;
    selected = null;
    stats.boughtThisVisit = 0;
    checkSecret();
    renderAll();
    vo("welcome");
    say(affection.index >= 3 ? "You came back! I-I mean... oh. It's you. Hmph." : affection.index >= 2 ? "Ah, my favourite regular climbs on. New stock, just for you." : "You again? Fine. Fresh stock. Same prices.");
  });
}

// The run is over: level 5 means you leave the Spire together; anything less and you're just friends.
let finished = false;
function finish() {
  if (finished) return; // one ending per run
  finished = true;
  session?.end();
  music.pause();
  const won = affection.levelNo >= MAX_LEVEL;
  const end = () => meta.ending(won ? "good" : "friends", {
    affection: `${affection.hearts} Lv ${affection.levelNo} ${affection.level}`,
    spent: stats.spent, saved: stats.saved + haggle.saved, visits: run.visit,
  });
  if (won) { startMusic(0.4, "end"); meta.walkOut(end); } else end();
}
// Leave opens a small choice, so a stray click mid-conversation doesn't roll the credits.
$("leave").onclick = () => $("leave-menu").classList.toggle("hidden");
$("climb").onclick = () => { $("leave-menu").classList.add("hidden"); nextVisit(); };
$("endrun").onclick = () => { $("leave-menu").classList.add("hidden"); finish(); };

// ---------------------------------------------------------------- voice agent
const clientTools = {
  check_offer: ({ item, price }) => {
    const res = haggle.check(item, price);
    const it = haggle.resolve(item);
    if (it) { select(it, false); point(it); }
    if (res.verdict === "insulted") {
      vo("sad");
      affection.lowball();
      if (haggle.angry) { meta.achieve("cheapskate"); provoke("The customer kept insulting you with lowball offers."); }
      else if (haggle.insultStreak === 2) bestie("lowball2");
    }
    if (res.verdict === "accept") vo("laugh");
    if (res.verdict === "insulted") meta.stamp("LOWBALL!", "bad", haggle.angry ? "STRIKE 3/3 · uh oh" : `STRIKE ${haggle.insultStreak}/3`);
    if (res.verdict === "counter") meta.stamp(`COUNTER: ${res.counter_offer}g`, "info", `you offered ${res.offer}g`);
    if (res.verdict === "final") meta.stamp(`FINAL OFFER: ${res.counter_offer}g`, "info", "his absolute floor");
    if (res.verdict === "accept") meta.stamp(`♥ DEAL! ${res.offer}g`, "deal", res.offer < res.list_price ? `${res.list_price - res.offer}g off` : "full price… he's thrilled");
    renderAll();
    return { ...res, affection_level: affection.level, mood_note: affection.mood };
  },
  sell_item: ({ item, price }) => {
    const res = haggle.sell(item, price);
    if (res.ok) vo("thanks");
    renderAll();
    return { ...res, affection_level: affection.level, mood_note: affection.mood };
  },
  show_item: ({ item }) => {
    const it = haggle.resolve(item);
    if (it) { select(it, false); point(it); }
    return { shown: !!it };
  },
  react: ({ kind }) => {
    const k = String(kind ?? "").trim().toLowerCase();
    const res = affection.react(k);
    if (res.applied) {
      const d = res.delta > 0 ? `+${res.delta} ♥` : `${res.delta} ♥`;
      const capped = res.capped ? " (maxed for this floor)" : "";
      if (k === "flirt") meta.stamp(affection.index === 0 && res.delta <= 2 ? "FLIRT…?" : "CRITICAL FLIRT!", "love", affection.index === 0 && res.delta <= 2 ? `he's not there yet · ${d}` : d + capped);
      if (k === "compliment") meta.stamp("SWEET TALK!", "love", d + capped);
      if (k === "small_talk") meta.stamp("SMALL TALK", "info", d + capped);
      if (k === "insult") meta.stamp("RUDE!", "bad", d);
    }
    return { ...res, affection_level: affection.level, mood_note: affection.mood };
  },
};

function dynamicVariables() {
  return {
    gold: String(player.gold),
    potion_slots: String(player.potionSlots - player.potions.length),
    inventory: items.filter((it) => !it.sold && !it.hidden).map(describeForAgent).join("\n"),
    affection_level: affection.level,
    mood: affection.mood,
    secret_available: secret && !secret.hidden && !secret.sold ? "yes" : "no",
    visit: String(run.visit),
    floor: String(run.floor),
  };
}

// Mic button states: idle -> connecting -> listening (you) / speaking (him) -> idle.
const MIC_LABEL = { idle: "Tap to talk", connecting: "Calling him…", listening: "Listening… (tap to hang up)", thinking: "Thinking…", speaking: "He's talking…" };
function setMic(state, label) {
  $("mic").className = state;
  $("status").textContent = label ?? MIC_LABEL[state] ?? state;
}

// What the player just said, shown above the Merchant's dialogue box: live while talking, fading after.
let youTimer;
function showYou(text, final) {
  if (!text?.trim()) return;
  const y = $("you");
  $("you-text").textContent = text;
  y.classList.remove("hidden", "fade");
  y.classList.toggle("live", !final);
  clearTimeout(youTimer);
  if (final) youTimer = setTimeout(() => y.classList.add("fade"), 7000);
}

// Mic button: call him (or unmute a typed-only call); tap again to hang up.
$("talk").onclick = async () => {
  if (session?.micMuted && !bestieMuted) {
    try { await session.setMicMuted(false); setMic("listening"); } catch (err) { setMic("idle", `Mic error: ${err.message ?? err}`); }
    return;
  }
  if (session) return session.end();
  connect({ mic: true });
};

// Type-to-talk: works without a microphone (and is the demo's backup if the mic or room noise misbehaves).
$("type-bar").onsubmit = async (e) => {
  e.preventDefault();
  const input = $("type-input");
  const text = input.value.trim();
  if (!text) return;
  input.value = "";
  if (!session) await connect({ mic: false });
  if (!session) return;
  affection.newTurn();
  showYou(text, true);
  log(`You: ${text}`, "you");
  record("You", text);
  lastTyped = text;
  if (bestieSpeaking) heldTyped.push(text); // she has the floor: he hears it right after her
  else session.sendUserMessage(text, { audio: true });
};
$("type-input").addEventListener("keydown", (e) => e.stopPropagation());

let lastTyped = "";
const typedEcho = (text) => text?.trim() === lastTyped;

async function connect({ mic }) {
  const btn = $("talk");
  btn.disabled = true;
  music.play().catch(() => {});
  setMic("connecting");
  const res = await fetch("/api/session", {
    method: "POST",
    headers: { "Content-Type": "application/json" },
    body: JSON.stringify({ dynamicVariables: dynamicVariables() }),
  });
  if (!res.ok) {
    setMic("idle", `Couldn't reach him: ${(await res.json().catch(() => ({}))).error ?? res.status}`);
    btn.disabled = false;
    return;
  }
  const sessionToken = await res.json();
  try {
    session = await AgentSession.start({
      sessionToken,
      clientTools,
      microphone: mic,
      callbacks: {
        onUserTranscript: ({ text, final }) => {
          if (text?.startsWith("[GAME]") || typedEcho(text)) return; // game events and typed lines are shown elsewhere
          showYou(text, final);
          if (final) { affection.newTurn(); log(`You: ${text}`, "you"); record("You", text); }
        },
        onAgentResponse: ({ text }) => { replies++; lastMerchantLine = text; say(text); log(`Merchant: ${text}`); record("Merchant", text); },
        onModeChange: (mode) => {
          agentMode = mode;
          flushGame();
          if (mode === "listening" && session?.micMuted && !bestieMuted) setMic("idle", "Typing · tap to use your voice");
          else setMic(mode === "speaking" ? "speaking" : mode === "listening" ? "listening" : "thinking");
          $("merchant").classList.toggle("speaking", mode === "speaking");
        },
        onToolCallStarted: ({ toolName, input }) => log(`→ ${toolName} ${input ?? ""}`, "tool"),
        onToolCallCompleted: ({ toolName, output }) => log(`← ${toolName} ${output ?? ""}`, "tool"),
        onError: (err) => { console.error("[agent]", err); log(`[error] ${err.code} ${err.message ?? ""}`, "tool"); },
        onDisconnect: ({ reason }) => {
          console.warn("[agent] disconnected:", reason);
          session = undefined;
          pendingGame.length = 0;
          setMic("idle", reason === "user_hangup" ? "Tap to talk" : `Call ended (${reason}) · tap to call back`);
          if (reason !== "user_hangup") say(`*The Merchant's voice fades out.* (connection ended: ${reason}). Tap the mic to call him back.`);
          $("merchant").classList.remove("speaking");
          btn.disabled = false;
        },
      },
    });
    vo("welcome");
    connectBestie(); // ready before her first line (she never speaks unprompted)
    setMic(mic ? "listening" : "idle", mic ? undefined : "Typing · tap to use your voice");
  } catch (err) {
    setMic("idle", `Mic error: ${err.message ?? err}`);
  }
  btn.disabled = false;
}

function openRug(open) {
  document.body.classList.toggle("rug-open", open);
  if (!open) { selected = null; renderDetail(); }
}

renderAll();
say("...");
// New Game: the climb to the first shop (one quick fight on the way), then the Merchant.
const showTitle = () => { startMusic(0.9); meta.titleScreen(() => {
  stopMusic(1.2);
  meta.floorCard({ to: run.floor, act: run.act, foe: FOES[makeRng(seed).int(FOES.length)], gold: 0, narr: "shop1" }, () => {
    music.play().catch(() => {});
    setTimeout(() => openRug(true), 300);
    vo("welcome");
    say(affection.index >= 3 ? "Oh! It's you again... n-not that I was waiting." : "Ah, a customer! Tap an item, or tap the mic and haggle.");
  });
}); };
// ?nointro skips the trailer cold open.
if (params.has("fight")) provoke("Demo.");
else if (params.has("nointro")) showTitle();
// The music crashes in the instant the trailer freezes and carries on over the title screen.
else meta.intro(showTitle, () => vo("laugh"), () => startMusic(0.9));

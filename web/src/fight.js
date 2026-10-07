// Pushed too far, the Merchant drops the act and the dating sim snaps into Slay the Spire combat. It's a
// facade: the scene, the intent, an opening hand drawn from your deck, and no combat system behind it.

const $ = (id) => document.getElementById(id);
const C = "/assets/combat";

const NOPE = [
  "Combat isn't implemented. We had four hours.",
  "The Merchant is immune to Strikes. And to love.",
  "Error 402: Payment Required.",
  "You can't afford to play that.",
  "This card is for display purposes only.",
];

export async function startFight({ deck, cardEl, onEnd, sfx }) {
  const starter = await fetch(`${C}/starter.json`).then((r) => r.json()).catch(() => ({ deck: [], cards: {} }));
  const pile = shuffle([...starter.deck.map((id) => starter.cards[id]), ...deck]);
  const hand = pile.slice(0, 5);

  const o = document.createElement("div");
  o.id = "fight";
  o.innerHTML = `
    <img class="f-bg" src="${C}/bg.png" alt="">
    <div class="f-unit f-player">
      <img class="f-ironclad" src="${C}/ironclad.png" alt="The Ironclad">
      <div class="f-hp"><div class="f-hpfill" style="width:100%"></div><span>80/80</span></div>
    </div>
    <div class="f-unit f-enemy">
      <div class="f-intent"><img src="${C}/intent_attack.png" alt=""><b>99</b></div>
      <img class="f-merchant" src="/assets/merchant_2x.png" alt="The Merchant">
      <div class="f-name">The Merchant <small>(furious)</small></div>
      <div class="f-hp"><div class="f-hpfill" style="width:100%"></div><span>999/999</span></div>
    </div>
    <div class="f-orb"><img src="${C}/energy_orb.png" alt=""><b>3/3</b></div>
    <div class="f-pile f-draw"><span>${pile.length - hand.length}</span>Draw</div>
    <div class="f-pile f-discard"><span>0</span>Discard</div>
    <button class="f-end"><img src="${C}/end_turn.png" alt=""><b>End Turn</b></button>
    <div class="f-hand"></div>
    <div class="f-tip"></div>
    <div class="f-banner"><div>The Merchant is <b>hostile!</b></div><small>Price negotiations have broken down.</small></div>`;
  $("stage").appendChild(o);
  requestAnimationFrame(() => o.classList.add("in"));

  // Deal the opening hand from the draw pile into the usual fan.
  const handEl = o.querySelector(".f-hand");
  const n = hand.length;
  hand.forEach((card, i) => {
    const c = cardEl(card, false, 0.42);
    c.className = "f-card";
    const off = i - (n - 1) / 2;
    c.style.setProperty("--x", `${960 + off * 205}px`);
    c.style.setProperty("--y", `${Math.abs(off) ** 2 * 10}px`);
    c.style.setProperty("--r", `${off * 5}deg`);
    c.style.animationDelay = `${2.0 + i * 0.13}s`;
    c.onclick = () => nope(c);
    handEl.appendChild(c);
  });
  setTimeout(() => sfx?.(), 2000);

  const tip = o.querySelector(".f-tip");
  let k = 0;
  function nope(c) {
    c.classList.remove("nope");
    void c.offsetWidth;
    c.classList.add("nope");
    tip.textContent = NOPE[k++ % NOPE.length];
    tip.classList.remove("show");
    void tip.offsetWidth;
    tip.classList.add("show");
  }

  // End Turn: he collects, in full.
  o.querySelector(".f-end").onclick = () => {
    o.querySelector(".f-end").disabled = true;
    o.classList.add("enemy-turn");
    setTimeout(() => {
      o.classList.add("hit");
      o.querySelector(".f-player .f-hpfill").style.width = "0%";
      o.querySelector(".f-player .f-hp span").textContent = "0/80";
      const dmg = document.createElement("div");
      dmg.className = "f-dmg";
      dmg.textContent = "99";
      o.appendChild(dmg);
    }, 650);
    setTimeout(() => onEnd(), 2600);
  };
  return o;
}

function shuffle(a) {
  for (let i = a.length - 1; i > 0; i--) {
    const j = Math.floor(Math.random() * (i + 1));
    [a[i], a[j]] = [a[j], a[i]];
  }
  return a;
}

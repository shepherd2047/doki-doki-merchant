# Doki Doki Merchant ♥

*You climbed the Spire to kill the Architect. You stayed for the man on the rug.*

![Title screen](docs/screenshots/title.jpg)

**Doki Doki Merchant** is a dating sim about the most emotionally unavailable man in Slay the Spire 2: the
Merchant. He sells you cards. He sells you relics. He refuses, on principle, to stand up. And now, thanks to a
live [Fish Audio](https://fish.audio) voice agent, he also talks back.

Flirt with him. Haggle with him. Lowball him if you hate being alive.

Built in an afternoon at SF Tech Week's **Conversational AI x Gaming Hackathon** by one Ironclad who should have
been fighting Act 1 bosses.

**Two ways to play:**

- **In the browser:** the hackathon demo, a self-contained dating sim with its own climb, fights and endings.
  Fish hosted voice agents, runs in Chrome. See [Run it](#run-it-browser).
- **In Slay the Spire 2:** a real mod. Every shop in your actual run gets the rug, the haggling, the affection
  meter and the secret card, on top of the game's own shop. OpenAI brain, Fish voice. See
  [Play it in the real game](#play-it-in-the-real-game-sts2-mod).

---

## How to win a man who lives on a rug

![The shop](docs/screenshots/shop.jpg)

Talk into the mic or type into the box, and he answers out loud in his own designed voice. He judges you
immediately and loudly: every line gets a stamp. **CRITICAL FLIRT!** **SWEET TALK!** **RUDE!** **LOWBALL!**

- **Affection** runs from *Stranger* to *Darling* (level 5). Compliments help. Paying full price helps more,
  because he's a merchant and that is his love language.
- **Hidden price floors:** every item has a secret minimum. Haggle above it and he caves, blushing. Go below it
  and he takes it personally.
- **Three lowballs in a row** and he drops the act. See: *the fight*.

### The climb

![Encounter](docs/screenshots/encounter.jpg)

Four shops on the way up the Spire (Act 1 floors 6 and 13, Act 2 floor 24, Act 3 floor 40), plus a 50% chance
he sets up a **bonus tent** near the top because he couldn't let you go. Monsters stand between you and him.
They do not stand there for long.

Your gold, your deck and his feelings carry over between shops. You have four tents to make him yours.

### Your bestie is watching

There's a second voice agent: your gossipy best friend, watching from the sidelines and screaming about it.
She reacts live to whatever he just said ("Babe, he is totally using fancy card talk just to impress you!") and
politely waits for him to finish first. Nobody talks over the Merchant. We tested it. Twice.

### Under the counter

![Secret card](docs/screenshots/secret.jpg)

Get him to *Darling*, buy a couple of things, and he gets flustered and pulls a **secret Rare card** out from
under the counter, "something I don't show just anyone". The bestie loses her mind. You still have to pay for
it. He's in love, not stupid.

---

## The endings

| | |
|---|---|
| ![True Love End](docs/screenshots/walkout.jpg) | **TRUE LOVE END: "Out of the Spire, Together"**<br>Reach *Darling* by the last shop and you walk out hand in hand. He brings the rug. |
| ![Ending card](docs/screenshots/ending.jpg) | Somewhere below, a tent stands empty with a sign: *"Closed. Gone on a date."* |
| ![The fight](docs/screenshots/fight.jpg) | **BAD END: "Killed Over a Discount"**<br>Lowball him three times and he attacks for **99**. You have 80 HP. You may press End Turn. That's about it. |
| 💔 | **BAD END: "Just Friends"**<br>Not Darling by the top? "You're a good customer," he says. *Customer.* The word echoes for a thousand years. |

---

## How it actually works (for the judges)

- **The game owns the numbers.** Affection, hidden price floors and offer verdicts all live in plain JavaScript.
  The LLM never decides a price; it calls client tools (`check_offer`, `react`, `show_item`) and acts out the
  verdict. You cannot sweet-talk him past the math. Many have tried.
- **Two agents, one floor.** The Merchant and the bestie are separate Fish hosted agents. The game taps both
  WebRTC audio streams to know who is *actually* audible (an agent's "speaking" state ends before its audio
  does), then passes the floor like a talking stick: game events, typed lines and your mic are held while she
  talks.
- **No cloned voices.** The Merchant's shriek and the bestie's voice were both made from text descriptions with
  Fish voice design. No real person was harmed or sampled.
- **The API key never reaches the browser.** A tiny Express server mints short-lived session tokens.

## Run it (browser)

```bash
npm install
cp .env.example .env            # fill in FISH_API_KEY
npm run create-agent            # prints MERCHANT_AGENT_ID -> .env
node scripts/create-bestie.mjs  # prints BESTIE_AGENT_ID -> .env
npm run server                  # token server on :8787
npm run dev                     # the game, on http://localhost:5173
```

Use Chrome for voice (mic permission). Typing works everywhere, for the shy.

**Cheat codes (demo presets):**

| Preset | What it does |
|---|---|
| `?nointro` | Skip the trailer. |
| `?affection=75` | He already likes you; the secret card unlocks after two purchases. |
| `?affection=100` | He already loves you. Coward. |
| `?bonus` | Force the bonus tent. |
| `?fight` | Skip the romance, go straight to the violence. |

## Play it in the real game (STS2 mod)

```bash
cd mod
scripts/build.sh     # needs the .NET 9 SDK and Slay the Spire 2 installed
scripts/install.sh   # copies it into the game's mods folder
```

Put your OpenAI and Fish keys in `DokiDokiMerchant.cfg` in the installed mod folder, restart the game and walk
into a shop (or type `room shop` in the dev console). Hold **V** to talk. Full instructions, config and
troubleshooting: [mod/README.md](mod/README.md).

## Where things live

- `web/src/main.js`: the shop, the run, both voice agents and their turn-taking.
- `web/src/affection.js` / `haggle.js`: the cold, hard numbers behind the warm feelings.
- `web/src/meta.js`: the dating-sim dressing (title, encounters, stamps, endings).
- `server/server.mjs`: mints Fish agent session tokens.
- `scripts/`: agent setup, voice design, bestie narration.
- `tools/`: extract the demo's assets from your own STS2 install.
- `mod/`: the Slay the Spire 2 mod (C#, Godot 4.5 / .NET 9).

## Credits

- Slay the Spire 2 art, audio and card data © Mega Crit. This is a non-commercial fan parody. Not affiliated.
  Please do not sue the Merchant; he has a rug to look after.
- Music: "Shining Smile Melody" by ZTMusic and "Pink Candy" by AI-SEVEN-BGM (Pixabay Content License).
- Voices and agents: [Fish Audio](https://fish.audio).

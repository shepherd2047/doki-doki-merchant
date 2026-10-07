# Doki Doki Merchant ♥

*Haggle your way into the Slay the Spire 2 Merchant's heart.*

A dating-sim parody built at SF Tech Week's **Conversational AI x Gaming Hackathon**. The Merchant is a live
[Fish Audio](https://fish.audio) voice agent: talk or type to flirt and haggle, and he answers in his own
designed voice. Your gossipy bestie (a second Fish agent) reacts from the sidelines.

- **The climb:** 4 shops up the Spire (Act 1 floors 6 and 13, Act 2 floor 24, Act 3 floor 40), plus a 50% chance of a bonus shop near the top. Gold, deck and his feelings carry over.
- **The goal:** reach affection level 5, *Darling*, by the last shop.
  - **True Love End:** walk out of the Spire hand in hand.
  - **Just Friends:** "You're a good customer." Customer.
  - **Killed Over a Discount:** lowball him three times in a row and he fights you.
- **Secret Rare card:** once he's fond enough of you and you've bought two things, he pulls a rare card from under the counter.
- **The game owns the numbers:** affection, hidden price floors and verdicts all live in the game. The LLM only classifies what you said through client tools (`check_offer`, `react`, `show_item`), so he can't be sweet-talked past the rules.
- **No cloned voices:** both voices were made with Fish voice design.

## Run it

```bash
npm install
cp .env.example .env          # fill in FISH_API_KEY
npm run create-agent          # prints MERCHANT_AGENT_ID -> .env
node scripts/create-bestie.mjs  # prints BESTIE_AGENT_ID -> .env
npm run server                # token server on :8787 (the browser never sees the API key)
npm run dev                   # game on http://localhost:5173
```

Use Chrome for voice (mic permission). Typing works everywhere.

**Demo presets:** `?nointro` to skip the trailer, `?affection=75` for the secret card after two purchases, `?affection=100` for Darling, `?bonus` to force the bonus shop, `?fight` to go straight to the fight.

## Layout

- `web/src/main.js`: the shop, the run, both voice agents and their turn-taking.
- `affection.js` / `haggle.js`: the numbers.
- `meta.js`: dating-sim dressing (title, encounters, stamps, endings).
- `server/server.mjs`: mints Fish agent session tokens.
- `scripts/`: agent setup, voice design, narration rendering.
- `tools/`: extract the demo's assets from your own STS2 install.

## Credits

- Slay the Spire 2 art, audio and card data © Mega Crit, used for this non-commercial fan demo.
- Music: "Shining Smile Melody" by ZTMusic and "Pink Candy" by AI-SEVEN-BGM (Pixabay Content License).
- Voices and agents: Fish Audio.

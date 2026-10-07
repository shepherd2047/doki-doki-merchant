#!/usr/bin/env node
// The narrator: your gossipy bestie, voiced by a designed Fish voice (voices/bestie_*.wav, no real person's
// voice cloned). Her lines are pre-rendered to mp3 so the demo never waits on the network. Usage:
//   node scripts/narrate.mjs save bestie_2   -> saves that candidate as a voice model, prints NARRATOR_VOICE_ID
//   node scripts/narrate.mjs                 -> renders every line to web/public/assets/narration/<key>.mp3
//                                               plus lines.json (subtitles + durations for the game)
import { execFileSync } from "node:child_process";
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { loadEnv } from "../server/env.mjs";

loadEnv();
const KEY = process.env.FISH_API_KEY;
const auth = { Authorization: `Bearer ${KEY}` };
const OUT = "web/public/assets/narration";

// [bracket] tags steer delivery on S2 models and are stripped from the subtitles.
export const LINES = {
  shop1: "[excited] Okay babe, this is it. His tent is right past this little monster. [giggles] Go get your man!",
  shop2: "Floor thirteen already? [teasing] Don't pretend you're climbing for the relics. We both know who you're climbing for.",
  shop3: "[gasps] Act two, and he moved his tent up here too? [whispering] Girl... that is not a coincidence.",
  shop4: "[serious] Okay, listen. This is his last shop before the top. If you don't make him yours now... you never will.",
  bonus: "[shocked] Wait. He set up another tent?! [squealing] He couldn't let you go! One more chance, babe. Don't blow it.",
  good: "[emotional] Oh my god. They're leaving the Spire together. Hand in hand. [sniffles] I'm not crying. You're crying.",
  friends: "[sighs] He called you a good customer. Customer. [softly] Oh babe... I'm so sorry. Ice cream?",
  fight: "[gasps] You lowballed him three times?! [laughing] Babe, that's not flirting. That's a declaration of war.",
  // In the shop: short reactions to the romance (kept brief so they don't talk over him).
  up3: "[excited] Ooh, he called you a regular! That's basically a pet name for him.",
  up4: "[squealing] He blushed. Babe, I saw it! Under the hood. He blushed!",
  up5: "[screaming excitedly] Darling?! [gasps] He is so in love with you!",
  down: "[cringing] Oof. Babe. Why would you say that to him?",
  lowball2: "[nervous] Okay, that's two lowballs in a row. One more and he's gonna snap. I'm serious.",
  secret: "[gasps] Wait. He's hiding a secret card just for you? That is so romantic.",
  capped: "[giggles] Slow down, Romeo. He needs time. Try again next floor.",
};

if (process.argv[2] === "save") {
  const n = process.argv[3] || "bestie_1";
  const form = new FormData();
  form.append("type", "tts");
  form.append("title", "Doki Doki Merchant narrator (designed)");
  form.append("train_mode", "fast");
  form.append("visibility", "private");
  form.append("voices", new Blob([readFileSync(`voices/${n}.wav`)], { type: "audio/wav" }), `${n}.wav`);
  const res = await fetch("https://api.fish.audio/model", { method: "POST", headers: auth, body: form });
  const body = await res.json();
  if (!res.ok) throw new Error(`${res.status} ${JSON.stringify(body)}`);
  console.log(`NARRATOR_VOICE_ID=${body._id}`);
} else {
  const voice = process.env.NARRATOR_VOICE_ID;
  if (!voice) throw new Error("NARRATOR_VOICE_ID is not set (run: node scripts/narrate.mjs save bestie_N).");
  mkdirSync(OUT, { recursive: true });
  const only = process.argv.slice(2);
  const manifest = {};
  for (const [key, text] of Object.entries(LINES)) {
    const file = `${OUT}/${key}.mp3`;
    if (!only.length || only.includes(key)) {
      const res = await fetch("https://api.fish.audio/v1/tts", {
        method: "POST",
        headers: { ...auth, "Content-Type": "application/json", model: "s2.1-pro" },
        body: JSON.stringify({ text, reference_id: voice, format: "mp3", mp3_bitrate: 128, latency: "normal" }),
      });
      if (!res.ok) throw new Error(`${key}: ${res.status} ${await res.text()}`);
      writeFileSync(file, Buffer.from(await res.arrayBuffer()));
    }
    const secs = +execFileSync("ffprobe", ["-v", "error", "-show_entries", "format=duration", "-of", "csv=p=0", file]).toString();
    manifest[key] = { text: text.replace(/\[[^\]]*\]\s*/g, "").trim(), ms: Math.round(secs * 1000) };
    console.log(`${key}.mp3 ${secs.toFixed(1)}s`);
  }
  writeFileSync(`${OUT}/lines.json`, JSON.stringify(manifest, null, 2) + "\n");
}

#!/usr/bin/env node
// Designs a brand-new voice for the Merchant from a text description (Fish voice design), so no real
// person's voice is cloned. Usage:
//   node scripts/design-voice.mjs villain    -> writes voices/villain_1..4.wav to listen to
//   node scripts/design-voice.mjs save villain_2  -> saves voices/villain_2.wav as a voice model, prints FISH_VOICE_ID
// Docs: https://docs.fish.audio/features/voice-design.md
import { mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { loadEnv } from "../server/env.mjs";

loadEnv();
const KEY = process.env.FISH_API_KEY;
const auth = { Authorization: `Bearer ${KEY}` };

const DESCRIPTION =
  "A slippery, scheming little merchant villain with an extremely high-pitched, shrill, shrieking voice, " +
  "almost squeaky, piercing and nasal like an imp or a goblin. Oily, slimy and wheedling, sing-song and " +
  "fast-talking like a con man, voice cracking up into shrieks when excited, with sneaky high snickering giggles.";
const SAMPLE =
  "Ehehehe... a customer! Look, look, everything's for sale. This isn't a charity, friend... but for you? Maybe a tiny discount. Hehe.";

if (process.argv[2] === "save") {
  const n = process.argv[3] || "v_1";
  const form = new FormData();
  form.append("type", "tts");
  form.append("title", "STS2 Merchant (designed)");
  form.append("train_mode", "fast");
  form.append("visibility", "private");
  form.append("voices", new Blob([readFileSync(`voices/${n}.wav`)], { type: "audio/wav" }), `${n}.wav`);
  const res = await fetch("https://api.fish.audio/model", { method: "POST", headers: auth, body: form });
  const body = await res.json();
  if (!res.ok) throw new Error(`${res.status} ${JSON.stringify(body)}`);
  console.log(`FISH_VOICE_ID=${body._id}`);
} else {
  const res = await fetch("https://api.fish.audio/v1/voice-design", {
    method: "POST",
    headers: { ...auth, "Content-Type": "application/json", model: "voice-design-1" },
    body: JSON.stringify({ instruction: DESCRIPTION, reference_text: SAMPLE, language: "en", n: 4 }),
  });
  if (!res.ok) throw new Error(`${res.status} ${await res.text()}`);
  const { candidates = [] } = await res.json();
  mkdirSync("voices", { recursive: true });
  const tag = process.argv[2] || "v";
  candidates.forEach((c, i) => {
    writeFileSync(`voices/${tag}_${i + 1}.wav`, Buffer.from(c.audio_base64, "base64"));
    console.log(`voices/${tag}_${i + 1}.wav (${((c.duration_ms ?? 0) / 1000).toFixed(1)}s)`);
  });
}

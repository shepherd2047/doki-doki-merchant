#!/usr/bin/env node
// Creates (or updates, when BESTIE_AGENT_ID is set) the bestie: a second Fish agent with the designed narrator
// voice (NARRATOR_VOICE_ID). She never speaks first; the game feeds her "[EVENT]" messages, each with what the
// Merchant just said, and only when he has finished his turn. Prints BESTIE_AGENT_ID for ./.env.
import { loadEnv } from "../server/env.mjs";

loadEnv();
const { FISH_API_KEY, NARRATOR_VOICE_ID } = process.env;
const headers = { Authorization: `Bearer ${FISH_API_KEY}`, "Content-Type": "application/json" };

async function call(method, path, body) {
  for (let i = 0; ; i++) {
    const res = await fetch("https://api.fish.audio" + path, { method, headers, body: body && JSON.stringify(body) });
    const text = await res.text();
    if (res.ok) return text ? JSON.parse(text) : {};
    if (i === 3) throw new Error(`${method} ${path} -> ${res.status} ${text}`);
    await new Promise((r) => setTimeout(r, 1500 * (i + 1)));
  }
}

const SYSTEM_PROMPT = `You are the player's gossipy, loving best friend in "Doki Doki Merchant", a dating-sim parody of Slay the Spire 2.
The player (the Ironclad) is flirting and haggling with the hooded Merchant in his shop while you watch from the sidelines.

You only ever receive messages starting with [EVENT]: something that just happened in the shop, plus what the Merchant just said.
Reply with ONE short spoken reaction to the player, at most 16 words, specific to what the Merchant actually said.
Call the player "babe". Be dramatic, giddy, supportive, a little teasing; warn them when things go badly.
You may start with one emotion tag in square brackets, e.g. [gasps], [giggles], [whispering], [squealing], [cringing], [teasing].
Never talk to the Merchant, never ask questions that need an answer, never use emojis or quotation marks.`;

let agentId = process.env.BESTIE_AGENT_ID;
if (!agentId) {
  agentId = (await call("POST", "/v1/agent/agents", { name: "Doki Doki Merchant: Bestie" })).agent_id;
  console.log(`[create-bestie] created agent ${agentId}`);
}
await call("PATCH", `/v1/agent/agents/${agentId}/config`, {
  prompt: { system_prompt: SYSTEM_PROMPT, first_message_mode: "off" },
  voice: { voice_id: NARRATOR_VOICE_ID, speaking_language: "en" },
  conversation: { max_duration_seconds: 1200, hangup_after_seconds: 1200, reengage_enabled: false },
});
await call("POST", `/v1/agent/agents/${agentId}/publish`, {});
console.log(`[create-bestie] configured and published.\n\nBESTIE_AGENT_ID=${agentId}\n`);

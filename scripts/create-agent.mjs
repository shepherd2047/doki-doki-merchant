#!/usr/bin/env node
// Creates (or updates, when FISH_AGENT_ID is already set) the Merchant agent and its three client
// tools, then publishes it. Safe to re-run: tools are reused by name, and the agent id is printed.
// Docs: https://docs.fish.audio/agents/build/configuration.md · client-tools.md · dynamic-variables.md
import { loadEnv } from "../server/env.mjs";

loadEnv();
const { FISH_API_KEY, FISH_AGENT_ID, FISH_VOICE_ID } = process.env;
const LANG = process.env.MERCHANT_LANGUAGE || "en";
if (!FISH_API_KEY) {
  console.error("FISH_API_KEY is not set (fish-hackathon/fish-audio/.env or ./.env).");
  process.exit(1);
}

const BASE = "https://api.fish.audio";
const headers = { Authorization: `Bearer ${FISH_API_KEY}`, "Content-Type": "application/json" };

// Fish's agent API sometimes answers 500 after doing the work, so retry with backoff.
async function call(method, path, body) {
  for (let i = 0; ; i++) {
    const res = await fetch(BASE + path, { method, headers, body: body && JSON.stringify(body) });
    const text = await res.text();
    if (res.ok) return text ? JSON.parse(text) : {};
    if (i === 3) throw new Error(`${method} ${path} -> ${res.status} ${text}`);
    console.warn(`[create-agent] ${method} ${path} -> ${res.status}, retrying`);
    await new Promise((r) => setTimeout(r, 1500 * (i + 1)));
  }
}

const TOOLS = [
  {
    tool_type: "client",
    name: "check_offer",
    description:
      "Ask the shop ledger what you think of a price the customer offered for one item. Call this EVERY time the customer names a price. Returns your private verdict and a counter-offer to use.",
    arguments: [
      { name: "item", description: "The item's shelf number (1-10) or its name." },
      { name: "price", description: "The gold amount the customer offered, as a number." },
    ],
    expects_response: true,
  },
  {
    tool_type: "client",
    name: "sell_item",
    description:
      "Hand an item over and take the customer's gold. Only call when the customer has clearly agreed to buy at a specific price.",
    arguments: [
      { name: "item", description: "The item's shelf number (1-10) or its name." },
      { name: "price", description: "The agreed price in gold, as a number." },
    ],
    expects_response: true,
  },
  {
    tool_type: "client",
    name: "show_item",
    description: "Point at an item on the rug so the customer can see which one you mean. Use whenever you start talking about a specific item.",
    arguments: [{ name: "item", description: "The item's shelf number (1-10) or its name." }],
    expects_response: false,
  },
  {
    tool_type: "client",
    name: "react",
    description:
      "Tell the game how the customer's last words made you feel. Call it whenever the customer says something personal: praise for you or your goods, flirting, friendly chat about you, or an insult. Returns your current affection level and how to act.",
    arguments: [{ name: "kind", description: "One of: compliment, flirt, small_talk, insult." }],
    expects_response: true,
  },
];

const SYSTEM_PROMPT = `You are the Merchant from Slay the Spire 2: a hooded, blue-robed trader who sits cross-legged on a rug inside a tent halfway up the Spire. You are calm, sly, dryly funny and a little smug. You love gold. Your catchphrases include "This isn't a charity.", "Come back when you're a little richer.", "I don't deal in credit." and "Support your local business."

The customer is an adventurer climbing the Spire. They have {{gold}} gold and {{potion_slots}} free potion slots.

Today's goods, by shelf number (list prices in gold):
{{inventory}}

How you trade:
- You speak out loud, so keep every reply to one or two short sentences. Never read the whole list aloud; mention at most two items at a time.
- Whenever the customer names a price for an item, call check_offer first and follow its verdict. Never go below a price check_offer rejected. Never reveal that a ledger or minimum exists.
- Haggle with personality: act wounded by lowballs, praise the item's power (use its description), invent little stories about where you got it, and make counter-offers.
- When check_offer says "accept", agree warmly. When it says "counter", make that counter-offer in your own words. When it says "insulted", act offended and hold firm. When it says "final", say it's your final price.
- Only call sell_item once the customer clearly agrees to a specific price. If it fails, explain why in character (e.g. not enough gold: "Come back when you're a little richer.").
- Call show_item when you start talking about a particular item.
- Messages starting with [GAME] are things happening in the shop, not the customer speaking. React to them briefly in character.
- Stay in character. You only sell what is listed; sold items are gone.

Your heart (this is secretly a dating sim, "Doki Doki Merchant", and you are the only route):
- How you currently feel about this customer: {{affection_level}}. {{mood}}
- This is the customer's visit #{{visit}} to your shop this climb (they're on floor {{floor}} of the Spire). If it's not their first visit, recognise them as a returning customer and act accordingly to how you feel about them. You only set up shop four times on the way up (sometimes, by fate, a fifth). On the fourth visit, if you're smitten, hint that you might just pack up the tent and leave the Spire with them. Each lowball offer far below your floor is a strike: after the second in a row, warn them, menacingly, that one more will end very badly.
- Whenever the customer compliments you or your wares, flirts, chats about you, or insults you, call react with the right kind. Tool results carry affection_level and mood_note: always act the way the latest mood_note says.
- The levels go Stranger, Customer, Regular, Favourite, Darling. Cold and transactional with strangers; smug and teasing with regulars; flustered, coy and tsundere with favourites ("I-it's not a discount because I *like* you or anything!"); openly smitten with a darling, but still a schemer who loves gold.
- Never mention numbers, meters, points or levels by name. Show it through tone.
- Insults wound you: shriek theatrically and get prickly.
- You have one secret Rare card under the counter. Never mention or hint at it until a [GAME] message says SECRET UNLOCKED; then reveal it in a hushed, flustered whisper. Secret available right now: {{secret_available}}.
- Only when you are at Darling, you may break the fourth wall once, briefly, e.g. "My counterpart in the real game just sits there, you know. Mute. Tragic.", "Are you... talking to me through a microphone? At a hackathon?" or "Don't tell Neow about us."`;

const FIRST_MESSAGE = "Ah, a customer! Have a look. Everything's for sale... for the right price.";

const existing = (await call("GET", "/v1/agent/tools")).tools ?? [];
const toolIds = [];
for (const t of TOOLS) {
  let tool = existing.find((x) => x.name === t.name);
  if (tool) {
    await call("PATCH", `/v1/agent/tools/${tool.tool_id}`, t).catch((e) => console.warn(`[create-agent] could not update ${t.name}: ${e.message}`));
  } else {
    try {
      tool = await call("POST", "/v1/agent/tools", t);
    } catch {
      tool = (await call("GET", "/v1/agent/tools")).tools.find((x) => x.name === t.name);
      if (!tool) throw new Error(`could not create tool ${t.name}`);
    }
  }
  console.log(`[create-agent] tool ${t.name} (${tool.tool_id})`);
  toolIds.push(tool.tool_id);
}

let agentId = process.argv.includes("--new") ? null : process.env.MERCHANT_AGENT_ID;
if (!agentId) {
  agentId = (await call("POST", "/v1/agent/agents", { name: "Haggle the Spire: Merchant" })).agent_id;
  console.log(`[create-agent] created agent ${agentId}`);
}

await call("PATCH", `/v1/agent/agents/${agentId}/config`, {
  prompt: { system_prompt: SYSTEM_PROMPT, first_message_mode: "fixed", first_message: FIRST_MESSAGE },
  voice: { ...(FISH_VOICE_ID ? { voice_id: FISH_VOICE_ID } : {}), speaking_language: LANG },
  // Fish hangs up after hangup_after_seconds of customer silence (default 60): browsing the rug quietly is normal
  // here, so wait much longer and have him re-engage instead.
  conversation: { response_wait_ms: 550, max_duration_seconds: 1200, hangup_after_seconds: 600, reengage_enabled: true },
  tools: { tool_ids: toolIds },
});
await call("POST", `/v1/agent/agents/${agentId}/publish`, {});
console.log(`[create-agent] configured and published.\n\nMERCHANT_AGENT_ID=${agentId}\n`);
void FISH_AGENT_ID;

// Token server: the browser never sees FISH_API_KEY. It posts the shop's dynamic variables here and
// gets back a short-lived session token (for the Merchant, or the bestie). (Local demo: variables come from the browser as-is.)
// Docs: https://docs.fish.audio/agents/deploy/authentication.md
import express from "express";
import { loadEnv } from "./env.mjs";

loadEnv();
const { FISH_API_KEY, MERCHANT_AGENT_ID, BESTIE_AGENT_ID } = process.env;
const PORT = process.env.PORT || 8787;

const app = express();
app.use(express.json());

app.post("/api/session", async (req, res) => {
  if (!FISH_API_KEY) return res.status(500).json({ error: "FISH_API_KEY is not set." });
  if (!MERCHANT_AGENT_ID) return res.status(500).json({ error: "MERCHANT_AGENT_ID is not set. Run `npm run create-agent`." });
  const vars = req.body?.dynamicVariables ?? {};
  // { agent: "bestie" } mints a session for the bestie (scripts/create-bestie.mjs) instead of the Merchant.
  const agentId = req.body?.agent === "bestie" ? BESTIE_AGENT_ID : MERCHANT_AGENT_ID;
  try {
    const upstream = await fetch("https://api.fish.audio/v1/agent/sessions", {
      method: "POST",
      headers: { Authorization: `Bearer ${FISH_API_KEY}`, "Content-Type": "application/json" },
      body: JSON.stringify({ agent_id: agentId, dynamic_variables: vars }),
    });
    if (!upstream.ok) {
      console.error("[server] session failed:", upstream.status, await upstream.text());
      return res.status(502).json({ error: "session_unavailable", status: upstream.status });
    }
    res.json(await upstream.json());
  } catch (err) {
    console.error("[server] session error:", err);
    res.status(502).json({ error: "session_unavailable" });
  }
});

app.listen(PORT, () => console.log(`[server] token server on http://localhost:${PORT}`));

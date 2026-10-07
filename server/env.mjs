// Loads ./.env, then fish-hackathon/fish-audio/.env for anything still unset (that's where the key lives).
import dotenv from "dotenv";
import { existsSync } from "node:fs";
import { resolve, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const root = resolve(dirname(fileURLToPath(import.meta.url)), "..");

export function loadEnv() {
  for (const p of [resolve(root, ".env"), resolve(root, "fish-hackathon/fish-audio/.env")])
    if (existsSync(p)) dotenv.config({ path: p, quiet: true });
}

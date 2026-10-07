import { defineConfig } from "vite";

// The browser never holds FISH_API_KEY: /api goes to server/server.mjs, which mints session tokens.
export default defineConfig({
  root: "web",
  build: { target: "esnext" },
  server: { port: 5173, proxy: { "/api": "http://localhost:8787" } },
});

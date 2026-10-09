# Doki Doki Merchant: the Slay the Spire 2 mod

The web demo, but in your actual run. Walk into any shop and the native shelves are gone: the Merchant sits on
the web game's rug, haggles with you out loud, keeps score of your affection for the whole run, and may pull a
secret Rare card out from under the counter. Buying still goes through the game's own shop code, so gold,
relic effects (The Courier, Membership Card…) and card removal behave exactly as usual.

- **Brain:** any OpenAI-compatible chat model (default `gpt-4.1-mini`). The game owns the numbers: prices,
  floors and affection are computed by the mod; the model only acts them out through tool calls.
- **Voice:** Fish Audio for speech recognition and the two designed voices (the Merchant and your bestie).
- **Single-player only.** Tested on macOS (Apple Silicon). Windows should work but is untested.

## What you need

- Slay the Spire 2 (Steam), version 0.111.0 or later.
- An [OpenAI API key](https://platform.openai.com/api-keys) (or any OpenAI-compatible endpoint).
- A [Fish Audio API key](https://fish.audio) for voice. Without one he still haggles, in text.
- To build from source: the .NET 9 SDK (`brew install dotnet@9` on macOS) and, on macOS, the Xcode command line
  tools (`xcode-select --install`) for the microphone helper.

## Install (macOS)

```bash
cd mod
scripts/build.sh     # builds dist/DokiDokiMerchant (DLL, manifest, config template, mic helper)
scripts/install.sh   # copies it into the game's mods folder
```

The build finds the game at the default Steam location. If yours is elsewhere, point it at the game's
`Resources` folder first, e.g. `export STS2_RESOURCES_DIR=".../SlayTheSpire2.app/Contents/Resources"`; for the
install step set `STS2_MODS_DIR` to the game's `mods` folder.

Then add your keys to the config the installer created (it is never overwritten by a reinstall):

```
~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/MacOS/mods/DokiDokiMerchant/DokiDokiMerchant.cfg
```

```json
{
  "openai_api_key": "sk-...",
  "fish_api_key": "...",
  "openai_model": "gpt-4.1-mini"
}
```

**Restart the game after editing the config**: it is read once at startup.

### Install by hand (any OS)

Copy `dist/DokiDokiMerchant/` into the game's `mods/` folder (next to the game executable; on macOS inside
`SlayTheSpire2.app/Contents/MacOS/mods/`), copy `DokiDokiMerchant.cfg.example` to `DokiDokiMerchant.cfg` in the
same folder, and fill in your keys.

### Sharing with a friend on your keys

`scripts/make-key-installer.sh` writes `dist/key-installer/Doki-keys-windows.bat` and `Doki-keys-mac.command`,
which carry your `DokiDokiMerchant.cfg` and install it next to the mod wherever Steam put it (Workshop or the
game's `mods/` folder). Your friend subscribes on the Workshop, starts the game once, double-clicks the script
and restarts. The scripts contain your keys: send them privately; never commit or upload them. The Workshop
package itself (`scripts/build.sh`) never contains keys.

## Play

- Enter any shop. Hold **V** to talk (or click the mic), or type into the box and press Enter.
- Click an item on the rug for its details and the Buy button. Haggle first; he has a hidden floor per item.
- **Leave** (top right) opens the map, like the native Proceed button.
- Want to test without walking to a shop? Open the dev console with `` ` `` and type `room shop`.

**Microphone on macOS:** the game itself can't ask for mic access, so the mod launches a tiny helper,
`DokiMicHelper.app`, the first time you talk. Allow it when macOS asks (or later in System Settings › Privacy &
Security › Microphone). Typing always works.

## Config reference

| Key | Default | |
|---|---|---|
| `openai_api_key` | | Required for the Merchant's brain. |
| `openai_base_url` | `https://api.openai.com/v1` | Any OpenAI-compatible endpoint. |
| `openai_model` | `gpt-4.1-mini` | Needs tool calling. |
| `fish_api_key` | | Speech recognition and voices. Empty = text only. |
| `fish_tts_model` | `s2.1-pro` | |
| `merchant_voice_id` / `bestie_voice_id` | designed voices | Fish voice model ids. |
| `bestie_enabled` | `true` | Your gossipy friend commenting from the sidelines. |
| `language` | `en` | |
| `push_to_talk_key` | `V` | |

## Troubleshooting

- **"The chat model refused the API key (401)"**: the key is wrong, or you saved it after the game started.
  Fix the cfg and restart the game.
- **The shop looks native / nothing happens**: check the game's log for lines starting with `[Doki]`
  (macOS: `~/Library/Application Support/SlayTheSpire2/logs/`).
- **He talks but you can't hear him**: no Fish key, or the Fish request failed; the text still shows.

## Where things live

- `src/Doki.cs`: one shop visit: wires the UI, the brain, the voices and the game's purchase calls.
- `src/Haggle.cs` / `Affection.cs`: hidden floors, offer verdicts, affection (ported from the web game).
- `src/Brain.cs`, `src/Fish.cs`, `src/Mic.cs`: OpenAI chat with tools, Fish ASR/TTS, microphone capture.
- `src/Ui/`: the web layout rebuilt from plain Godot nodes (rug, heart meter, dialogue box, chat, stamps,
  achievements, item popup) and `NativeShop.cs`, which hides the native shelves and keeps the game's top bar.
- `mic-helper/`: the macOS microphone helper (Swift).

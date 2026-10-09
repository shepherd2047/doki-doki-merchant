# Doki Doki Merchant shop UI: brief for component agents

We are rebuilding the Slay the Spire 2 merchant room UI (a C# mod, Godot 4.5 / .NET 9) to look like the web
game in `/Users/m/hagglethespire/web`. The native shelves are hidden; the game's own purchase logic is kept.
Several agents work in parallel, one component each. Read this whole file first.

## Your job
- Implement **only your own file(s)** in `/Users/m/hagglethespire/mod/src/Ui/`. Its current content is a stub
  that defines the public API. Keep every public member's name and signature; you may add members (say so in
  your report). You may create ONE extra file named after your component (e.g. `NativeShopPatches.cs`).
- Do not edit `Theme.cs`, `src/Doki.cs`, `src/Haggle.cs`, other components, `stubs/`, the csproj or scripts.
  If you need a helper Theme lacks, write it privately in your file.
- Compile-check in isolation (other components are swapped for stubs, so their work can't break yours):
  `export PATH=/opt/homebrew/opt/dotnet@9/bin:$PATH; /Users/m/hagglethespire/mod/scripts/agent-build.sh YourFile.cs [YourExtra.cs]`
  It must print `Build succeeded.` with no errors before you finish.
- Do not run or install the game, do not touch git, never print API keys (DokiDokiMerchant.cfg).
- Final report (short): what you implemented, any public members you added, anything the integrator (who wires
  components together in Doki.cs) must know, and anything you couldn't verify.

## Hard technical rules
- **No custom Node subclasses**, no `[Export]`, no `partial` Godot classes: the mod builds without Godot's
  source generators, so a C# class deriving from Node will not work. Build everything from stock nodes
  (Control, Label, RichTextLabel, Button, LineEdit, TextureRect, Panel, ColorRect, Polygon2D, Line2D, ...).
- Animate with `Tween` (`node.CreateTween()`), and per-frame work via `SceneTree.ProcessFrame += handler`
  (unsubscribe in `node.TreeExiting`). Custom drawing: subscribe to the C# event `control.Draw += () => {
  control.DrawCircle(...); control.DrawPolygon(...); ... }` and call `control.QueueRedraw()` when it changes
  (each frame for animations). Runtime shaders are fine: `new ShaderMaterial { Shader = new Shader { Code = "shader_type canvas_item; ..." } }`.
  Use `Callable.From(...)` for tween callbacks. Godot API calls on the main thread only.
- Mouse: every container/decoration gets `MouseFilter = Ignore`; only real interactive controls `Stop`. The
  stage covers the whole screen, so a stray `Stop` blocks the game.
- Everything lives under one `CanvasLayer` that is freed when the shop closes, so nodes need no manual cleanup;
  but unsubscribe any SceneTree/static events you add.
- Text: use `Theme.MakeLabel(...)`, `Theme.Font` / `Theme.Bold` (the game's Kreon), the colours in `Theme`.
  Kreon has ♥ ▲ ▼ × but probably no emoji; draw icons instead of using emoji.

## Coordinates and layout
- `ShopStage` (in Theme.cs) gives a 1920x1080 root scaled to the window. **Its pixel coordinates are exactly
  the web game's**: `left: 1200px; top: 822px` in style.css is `Position = new Vector2(1200, 822)` here.
- Layers bottom to top: `stage.Rug`, `stage.Hud`, `stage.Popups`, `stage.Fx`, `stage.Toasts`.
- The game's own top bar (gold, HP, potions, map, deck) stays and covers roughly y < 80; keep your component
  out of that band (only the NativeShop agent works near it).
- Under our UI, the native room still renders: tent background and the animated Merchant, at the same spot as
  the web's `#merchant` (left 1204, top 470, 263x331).
- Target look: `/Users/m/hagglethespire/docs/screenshots/shop.jpg` and `secret.jpg` (read them as images).
  Web source of truth: `web/index.html`, `web/src/style.css` (layout, colours, animations),
  `web/src/main.js` (rendering and behaviour), `web/src/meta.js` (stamps, toasts, achievements, vignettes).

## Game data
- Shop items: `Haggle.Item` (src/Haggle.cs): `Shelf` (1..10), `Entry` (MerchantEntry), `Kind` ("card",
  "relic", "potion", "service"), `ListPrice`, `Agreed` (haggled price or null), `Secret`, `Hidden`.
  Web layout: shelves 1-4 character cards, 5-6 relics, 7-8 potions, 9 card removal, 10 the secret card.
  The price to show is `item.Entry.Cost` (our Harmony patch already applies deals and the +50% anger tax); show
  the strike-through list price when `Agreed != null` (green deal price) or `Haggle.AngerTax.Contains(entry)`.
  Sold = `!item.Entry.IsStocked`. `ItemText.Name(item)`, `ItemText.Describe(item)`, `ItemText.Clean(s)` exist.
- Entries: `MerchantCardEntry` (`CreationResult?.Card` is a CardModel, `IsOnSale`), `MerchantRelicEntry`
  (`Model`), `MerchantPotionEntry` (`Model`), `MerchantCardRemovalEntry`.
- Decompiled game source (slightly older build, check names before relying on them):
  `/Users/m/projects/sts2-3ds-work/sts2-decompiled/` e.g. `MegaCrit.Sts2.Core.Nodes.Screens.Shops/NMerchantCard.cs`
  (uses `NCard.Create(card)`), `NMerchantRelic.cs` (`NRelic.Create(model, size)`), `NMerchantPotion.cs`,
  `MegaCrit.Sts2.Core.Nodes.Rooms/NMerchantRoom.cs`. The installed build's API docs:
  `~/Library/Application Support/Steam/steamapps/common/Slay the Spire 2/SlayTheSpire2.app/Contents/Resources/data_sts2_macos_arm64/sts2.xml`
  (the authoritative compile target is `sts2.dll` in the same folder).
- Game textures: the web copied them from the game; each `web/public/assets/<dir>/index.json` maps a file to its
  `source` inside the game, loadable as `Theme.Tex("res://" + source)` (e.g. `ui/index.json` has the rug, coin,
  sale tag, card removal). `.tres` sources are AtlasTextures and load as Texture2D too.

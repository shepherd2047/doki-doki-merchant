#!/usr/bin/env python3
"""Export Slay the Spire 2 merchant-shop art from the user's own Steam install as web PNGs.

Reads the game's PCK through the sts2-3ds port's tools (read-only import, nothing modified there).
Output: web/public/assets/{merchant_room.png, merchant.png, merchant_bg.png, cards/, frame/,
potions/, relics/, ui/, audio/}. Usage: python3 tools/extract_assets.py [--skip audio,relics,...]
"""
import argparse
import glob
import json
import os
import re
import shutil
import subprocess
import sys
import tempfile
import time
import traceback

PORT = '/Users/m/projects/sts2-3ds-work/sts2-3ds'
sys.path.insert(0, os.path.join(PORT, 'tools'))
from PIL import Image  # noqa: E402
from gdpck import Game  # noqa: E402
import spine_render  # noqa: E402
import build_assets as ba  # noqa: E402  (module import only; nothing in it runs at import time)

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), 'web', 'public', 'assets')
UI = 'images/atlases/ui_atlas.sprites/'


def save(img, rel):
    p = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    img.save(p, optimize=False)
    return img.size


def write_json(rel, obj):
    p = os.path.join(OUT, rel)
    os.makedirs(os.path.dirname(p), exist_ok=True)
    with open(p, 'w') as f:
        json.dump(obj, f, indent=1, sort_keys=True)


def card_headers():
    """KEY -> dict(cost, type, rarity, target) from CARD_HEADER(Name, "KEY", Cost, Type, Rar, Tgt)."""
    out = {}
    for src in glob.glob(os.path.join(PORT, 'source', 'core', '*.[ch]*')):
        for m in re.finditer(r'CARD_HEADER\(\w+,\s*"([A-Z0-9_]+)",\s*(-?\d+),\s*(\w+),\s*(\w+),\s*(\w+)\)',
                             open(src, encoding='utf-8').read()):
            out[m.group(1)] = dict(cost=int(m.group(2)), type=m.group(3), rarity=m.group(4), target=m.group(5))
    return out


# ------------------------------------------------------------------ 1. merchant room
def do_merchant(g, a):
    S = 1.0  # native 1920x1080 game space
    W, H = 1920, 1080
    room = Image.new('RGBA', (W, H), (0, 0, 0, 255))
    layers = {}
    for name, tres, x, y, sc in (
            ('bg', 'animations/backgrounds/merchant_room/bottom/shop_merchant_bottom.tres', -10, 20, 0.5 * 1.01),
            ('merchant', 'animations/backgrounds/merchant_room/top/shop_merchant_top.tres',
             960 + 246 - 1122.7, 540 - 72 - 396.68, 0.470095)):
        skel, atlas, load = g.spine(tres)
        img, origin = spine_render.render(skel, atlas, load, scale=sc * S)
        pos = (round(x * S - origin[0]), round(y * S - origin[1]))
        layers[name] = (img, pos)
        room.alpha_composite(img, pos) if pos[0] >= 0 and pos[1] >= 0 else _paste(room, img, pos)
    save(room.convert('RGB'), 'merchant_room.png')
    # Tent alone, on the same 1920x1080 canvas.
    bg = Image.new('RGBA', (W, H), (0, 0, 0, 255))
    _paste(bg, *layers['bg'])
    save(bg.convert('RGB'), 'merchant_bg.png')
    # Merchant alone, cropped to his bounding box, plus where he sits in 1920x1080.
    m, pos = layers['merchant']
    bbox = m.getbbox()
    save(m.crop(bbox), 'merchant.png')
    write_json('merchant_layout.json', {
        'canvas': [W, H],
        'merchant': {'file': 'merchant.png', 'x': pos[0] + bbox[0], 'y': pos[1] + bbox[1],
                     'w': bbox[2] - bbox[0], 'h': bbox[3] - bbox[1]},
        'note': 'merchant.png placed at (x, y) on merchant_bg.png reproduces merchant_room.png (Spine setup pose, idle_loop frame 0)'})
    # Higher-res merchant (2x the in-game size; the atlas has the detail) for large web layouts.
    skel, atlas, load = g.spine('animations/backgrounds/merchant_room/top/shop_merchant_top.tres')
    hd, _ = spine_render.render(skel, atlas, load, scale=0.470095 * 2)
    save(hd.crop(hd.getbbox()), 'merchant_2x.png')
    # Merchant's hand (shown over the rug when the inventory opens).
    try:
        skel, atlas, load = g.spine('animations/backgrounds/merchant_room/hand/merchant_hand_skel_data.tres')
        img, _ = spine_render.render(skel, atlas, load, scale=0.5)
        if img is not None and img.getbbox():
            save(img.crop(img.getbbox()), 'ui/merchant_hand.png')
    except Exception as e:  # noqa: BLE001
        print('  merchant hand skipped:', e)
    return {'merchant_room.png': room.size, 'merchant.png': (bbox[2] - bbox[0], bbox[3] - bbox[1])}


def _paste(canvas, img, pos):
    """alpha_composite that tolerates negative / overflowing positions."""
    x, y = pos
    sx, sy = max(0, -x), max(0, -y)
    dx, dy = max(0, x), max(0, y)
    w = min(img.width - sx, canvas.width - dx)
    h = min(img.height - sy, canvas.height - dy)
    if w > 0 and h > 0:
        canvas.alpha_composite(img.crop((sx, sy, sx + w, sy + h)), (dx, dy))


# ------------------------------------------------------------------ 2. cards
POOLS = ('ironclad', 'colorless')


def card_pool_of(g, snake):
    """(pool, resource) where the game keeps this card's portrait, preferring the packed PNG."""
    for d in ba.Assets.PORTRAIT_DIRS:
        for sub in ('', 'beta/'):
            c = f'images/packed/card_portraits/{d}/{sub}{snake}.png'
            if c + '.import' in g.pck.files:
                return d, c, sub == 'beta/'
    for d in ba.Assets.PORTRAIT_DIRS:
        tres = f'images/atlases/card_atlas.sprites/{d}/{snake}.tres'
        if tres in g.pck.files:
            return d, tres, False
    return None, None, False


def do_cards(g, a):
    src_keys = set(ba.CARDS_FIXED) | set(ba.keys_from_source('CARD_HEADER'))
    headers = card_headers()
    # Also every portrait the game ships for these two pools (cards the port has not implemented yet).
    pck_keys = set()
    for f in g.pck.files:
        m = re.match(r'images/(?:packed/card_portraits|atlases/card_atlas\.sprites)/(ironclad|colorless)/(?:beta/)?([a-z0-9_]+)\.(?:png\.import|tres)$', f)
        if m:
            pck_keys.add(m.group(2).upper())
    index, meta = {}, {}
    for key in sorted(src_keys | pck_keys):
        snake = key.lower()
        pool, res, beta = card_pool_of(g, snake)
        if pool not in POOLS:
            continue
        img = g.image(res) if res.endswith('.png') else a.sprite(res)
        fn = f'{snake}.png'
        size = save(img, f'cards/{fn}')
        index[key] = fn
        meta[key] = dict(file=fn, pool=pool, w=size[0], h=size[1], in_port_source=key in src_keys,
                         beta_art=beta, source=res, **headers.get(key, {}))
    write_json('cards/index.json', index)
    write_json('cards/meta.json', meta)
    return len(index), sum(1 for m in meta.values() if m['pool'] == 'ironclad'), \
        sum(1 for m in meta.values() if m['pool'] == 'colorless')


# ------------------------------------------------------------------ 3. frames
def do_frames(g, a):
    idx = {}

    def put(img, fn, **desc):
        size = save(img, f'frame/{fn}')
        idx[fn] = dict(w=size[0], h=size[1], **desc)

    banner_hsv = {r: ba.material_hsv(g, f'materials/cards/banners/card_banner_{r}_mat.tres')
                  for r in ba.CARD_BANNER_RARITIES}
    pool_hsv = {p: ba.material_hsv(g, f'materials/cards/frames/card_frame_{ba.CARD_FRAME_MATS[p]}_mat.tres')
                for p in POOLS}
    for kind in ('attack', 'skill', 'power'):
        frame = a.sprite(f'{UI}card/card_frame_{kind}_s.tres')
        put(frame, f'frame_{kind}_raw.png', part='frame', kind=kind, pool=None,
            note='untinted atlas sprite; the game always applies a pool material')
        for p in POOLS:
            put(ba.hsv_shader(frame, *pool_hsv[p]), f'frame_{kind}_{p}.png', part='frame', kind=kind, pool=p,
                hsv=pool_hsv[p], note='card body frame, pre-tinted with the pool hsv material')
        border = a.sprite(f'{UI}card/card_portrait_border_{kind}_s.tres')
        put(border, f'border_{kind}_raw.png', part='portrait_border', kind=kind, rarity=None)
        for r in ba.CARD_BANNER_RARITIES:
            put(ba.hsv_shader(border, *banner_hsv[r]), f'border_{kind}_{r}.png', part='portrait_border',
                kind=kind, rarity=r, hsv=banner_hsv[r],
                note='frame around the portrait art, tinted by rarity (basic/token use common)')
    plaque = a.sprite(f'{UI}card/card_portrait_border_plaque_s.tres')
    put(plaque, 'plaque_raw.png', part='type_plaque', rarity=None)
    for r in ba.CARD_BANNER_RARITIES:
        put(ba.hsv_shader(plaque, *banner_hsv[r]), f'plaque_{r}.png', part='type_plaque', rarity=r,
            note='small plaque under the portrait holding the type text; horizontal 9-slice, margins 13/12 px')
    banner = a.sprite(f'{UI}card/card_banner.tres')
    put(banner, 'banner_raw.png', part='name_banner', rarity=None)
    for r in ba.CARD_BANNER_RARITIES:
        put(ba.hsv_shader(banner, *banner_hsv[r]), f'banner_{r}.png', part='name_banner', rarity=r,
            note='title ribbon across the top of the card, tinted by rarity')
    for p in POOLS:
        put(a.sprite(f'{UI}card/energy_{p}.tres'), f'energy_{p}.png', part='energy_orb', pool=p,
            note='cost orb at the top-left corner; draw the cost number on it')
    put(a.sprite(f'{UI}card/card_unplayable_icon.tres'), 'unplayable.png', part='unplayable_icon')
    put(a.sprite(f'{UI}card/card_frame_ancient_s.tres'), 'frame_ancient.png', part='frame', kind='ancient')
    put(a.sprite(f'{UI}card/ancient_banner.tres'), 'banner_ancient.png', part='name_banner', rarity='ancient')
    write_json('frame/index.json', idx)
    return len(idx)


# ------------------------------------------------------------------ 4/5. potions, relics
def do_potions(g, a):
    keys = set(ba.keys_from_source('POTION_HEADER'))
    for f in g.pck.files:  # every potion the game ships, not just the port's
        m = re.match(r'images/atlases/potion_atlas\.sprites/([a-z0-9_]+)\.tres$', f)
        if m:
            keys.add(m.group(1).upper())
    index = {}
    for key in sorted(keys):
        tres = f'images/atlases/potion_atlas.sprites/{key.lower()}.tres'
        if tres not in g.pck.files:
            print('  missing potion', key)
            continue
        save(a.sprite(tres), f'potions/{key.lower()}.png')
        index[key] = f'{key.lower()}.png'
    write_json('potions/index.json', index)
    return len(index)


def do_relics(g, a):
    keys = set(ba.keys_from_source('RELIC_HEADER'))
    for f in g.pck.files:
        m = re.match(r'images/relics/([a-z0-9_]+)\.png\.import$', f)
        if m:
            keys.add(m.group(1).upper())
    index = {}
    for key in sorted(keys):
        snake = key.lower()
        path = f'images/relics/{snake}.png'
        if path + '.import' not in g.pck.files:
            path = f'images/relics/{snake}_ironclad.png'  # per-character icons (Yummy Cookie)
        if path + '.import' not in g.pck.files:
            tres = f'images/atlases/relic_atlas.sprites/{snake}.tres'
            if tres not in g.pck.files:
                print('  missing relic', key)
                continue
            img = a.sprite(tres)
        else:
            img = g.image(path)
        save(img, f'relics/{snake}.png')
        index[key] = f'{snake}.png'
    write_json('relics/index.json', index)
    return len(index)


# ------------------------------------------------------------------ 6. ui
def do_ui(g, a):
    idx = {}

    def put(img, fn, src):
        size = save(img, f'ui/{fn}')
        idx[fn] = dict(w=size[0], h=size[1], source=src)

    R = 'images/rooms/merchant_room/'
    put(g.image(R + 'shop_sales_tag.png'), 'sale_tag.png', R + 'shop_sales_tag.png')
    # card_removal.png = the service's main Visual texture (merchant_card_removal.tscn); also every layer.
    t = g.pck.read('scenes/merchant/merchant_card_removal.tscn').decode()
    main = None
    m = re.search(r'texture = ExtResource\("([^"]+)"\)', t)
    if m:
        r = re.search(r'path="res://([^"]+)" id="' + re.escape(m.group(1)) + '"', t)
        main = r.group(1) if r else None
    for f in sorted(g.pck.files):
        if f.startswith(R + 'card_removal_') and f.endswith('.png.import'):
            res = f[:-7]
            put(g.image(res), os.path.basename(res), res)
    if main:
        put(g.image(main), 'card_removal.png', main)
    put(g.image(R + 'shop_rug.png'), 'shop_rug.png', R + 'shop_rug.png')
    put(g.image('images/packed/sprite_fonts/gold_icon.png'), 'gold_coin.png', 'images/packed/sprite_fonts/gold_icon.png')
    put(a.sprite(UI + 'top_bar/top_bar_gold.tres'), 'gold_coin_price.png',
        UI + 'top_bar/top_bar_gold.tres (the coin next to merchant prices)')
    for fn, tres in (('proceed_button.png', UI + 'proceed_button.tres'),
                     ('proceed_button_outline.png', 'images/atlases/compressed.sprites/proceed_button_outline.tres'),
                     ('back_button.png', UI + 'back_button.tres'),
                     ('back_button_outline.png', 'images/atlases/compressed.sprites/back_button_outline.tres'),
                     ('back_button_arrow.png', 'images/atlases/compressed.sprites/back_button_arrow.tres')):
        try:
            put(a.sprite(tres), fn, tres)
        except Exception as e:  # noqa: BLE001
            print('  ui skipped', fn, e)
    try:
        put(g.image('images/packed/vfx/speech_bubble3.png'), 'speech_bubble.png', 'images/packed/vfx/speech_bubble3.png')
    except Exception as e:  # noqa: BLE001
        print('  speech bubble skipped', e)
    write_json('ui/index.json', idx)
    return len(idx)


# ------------------------------------------------------------------ 7. audio
AUDIO_PREFIXES = ('sts2_sfx_vo_merchant', 'sts2_merchant_act1_v2')


def do_audio(g, a):
    import audio_extract as ae
    import struct  # noqa: F401
    if not (shutil.which('vgmstream-cli') and shutil.which('ffmpeg')):
        print('  audio skipped: needs vgmstream-cli and ffmpeg')
        return 0
    tmp = tempfile.mkdtemp(prefix='hts_audio')
    out = {}
    seen = set()
    for bn in sorted(n for n in g.pck.files if n.startswith('banks/desktop/') and n.endswith('.bank')):
        name = os.path.basename(bn)[:-5]
        if name.endswith('.strings'):
            continue
        d = g.pck.read(bn)
        off, ss = ae.parse_bank(name, d, {})
        hits = [(k, s) for k, s in enumerate(ss or []) if s['name'].lower().startswith(AUDIO_PREFIXES)]
        if not hits:
            continue
        fsb = os.path.join(tmp, name + '.fsb')
        with open(fsb, 'wb') as f:
            f.write(d[off:])
        for k, s in hits:
            nm = ae.slug(s['name'])
            if nm in seen:
                continue
            seen.add(nm)
            wav = os.path.join(tmp, nm + '.wav')
            subprocess.run(['vgmstream-cli', '-o', wav, '-s', str(k + 1), fsb], check=True,
                           stdout=subprocess.DEVNULL, stderr=subprocess.DEVNULL)
            dst = os.path.join(OUT, 'audio', nm + '.mp3')
            os.makedirs(os.path.dirname(dst), exist_ok=True)
            subprocess.run(['ffmpeg', '-v', 'error', '-y', '-i', wav, '-c:a', 'libmp3lame', '-q:a', '3', dst], check=True)
            out[nm] = dict(file=nm + '.mp3', bank=name, seconds=round(s['samples'] / s['freq'], 2),
                           loop=bool(s.get('loop')))
    shutil.rmtree(tmp, ignore_errors=True)
    write_json('audio/index.json', out)
    return len(out)


STEPS = [('merchant', do_merchant), ('cards', do_cards), ('potions', do_potions), ('frames', do_frames),
         ('ui', do_ui), ('relics', do_relics), ('audio', do_audio)]


def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--skip', default='')
    ap.add_argument('--only', default='')
    args = ap.parse_args()
    skip, only = set(filter(None, args.skip.split(','))), set(filter(None, args.only.split(',')))
    g = Game()
    a = ba.Assets(g)
    t0 = time.time()
    for name, fn in STEPS:
        if name in skip or (only and name not in only):
            continue
        t = time.time()
        try:
            print(f'{name}: {fn(g, a)}  ({time.time() - t:.1f} s)', flush=True)
        except Exception:  # noqa: BLE001 - one failing item must not block the rest
            print(f'{name}: FAILED')
            traceback.print_exc()
    print(f'total {time.time() - t0:.1f} s -> {OUT}')


if __name__ == '__main__':
    main()

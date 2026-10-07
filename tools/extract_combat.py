#!/usr/bin/env python3
"""Export Slay the Spire 2 combat art (Ironclad, act-1 room, combat UI, starter deck) as web PNGs.

Same approach as tools/extract_assets.py: reads the game's PCK through the sts2-3ds port's tools
(read-only import). Output: web/public/assets/combat/ (+ basic-rarity frame pieces in frame/).
"""
import json
import os
import shutil
import sys

PORT = '/Users/m/projects/sts2-3ds-work/sts2-3ds'
sys.path.insert(0, os.path.join(PORT, 'tools'))
from PIL import Image  # noqa: E402
from gdpck import Game  # noqa: E402
import spine_render  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
ASSETS = os.path.join(os.path.dirname(HERE), 'web', 'public', 'assets')
OUT = os.path.join(ASSETS, 'combat')


def save(img, fn):
    os.makedirs(OUT, exist_ok=True)
    img.save(os.path.join(OUT, fn))
    print(f'  {fn} {img.size}')


def crop(img):
    img = img.convert('RGBA')
    return img.crop(img.getbbox())


def do_ironclad(g):
    # scenes/creature_visuals/ironclad.tscn: SpineSprite scale 0.28 in 1920x1080 space; faces right.
    skel, atlas, load = g.spine('animations/characters/ironclad/ironclad_skel_data.tres')
    img, _ = spine_render.render(skel, atlas, load, scale=0.28 * 1.25)
    save(crop(img), 'ironclad.png')


def do_bg(g):
    # Overgrowth combat room: layers composited, then the camera view (1920 of the 2764.8x1296 scene).
    layers = [g.image(f'images/rooms/overgrowth/overgrowth_{n}.png') for n in ('00', '01_a', '02_a', '03_a', '04_a')]
    W, H = layers[0].size
    scene = Image.new('RGBA', (W, H), (0, 0, 0, 255))
    for im in layers:
        scene.alpha_composite(im.convert('RGBA').resize((W, H)))
    k = W / 2764.8
    cw, ch = 1920 * k, 1080 * k
    cx, cy = W / 2, H / 2
    view = scene.crop((round(cx - cw / 2), round(cy - ch / 2), round(cx + cw / 2), round(cy + ch / 2)))
    save(view.resize((1920, 1080), Image.LANCZOS).convert('RGB'), 'bg.png')


def do_ui(g):
    save(crop(g.image('images/packed/intents/attack/intent_attack_5.png')), 'intent_attack.png')
    save(g.image('images/packed/combat_ui/end_turn_button.png').convert('RGBA'), 'end_turn.png')
    save(g.image('images/packed/combat_ui/end_turn_button_glow.png').convert('RGBA'), 'end_turn_glow.png')
    for n in ('bg', 'fill', 'stroke'):
        save(g.image(f'images/ui/combat/health_bar_{n}.png').convert('RGBA'), f'health_bar_{n}.png')
    # scenes/combat/energy_counters/ironclad_energy_counter.tscn: 5 full-rect TextureRects, keep-aspect centred.
    layers = [g.image(f'images/ui/combat/energy_counters/ironclad/ironclad_orb_layer_{i}.png').convert('RGBA')
              for i in range(1, 6)]
    S = max(max(l.size) for l in layers)
    orb = Image.new('RGBA', (S, S), (0, 0, 0, 0))
    for l in layers:
        f = S / max(l.size)
        l = l.resize((round(l.width * f), round(l.height * f)), Image.LANCZOS)
        orb.alpha_composite(l, ((S - l.width) // 2, (S - l.height) // 2))
    save(orb, 'energy_orb.png')


def do_basic_frames():
    # CardRarity.Basic falls through to card_banner_common_mat (CrystalSphereCardReward / CardModel default).
    fr = os.path.join(ASSETS, 'frame')
    idx_p = os.path.join(fr, 'index.json')
    idx = json.load(open(idx_p))
    for src, dst in (('border_attack_common.png', 'border_attack_basic.png'),
                     ('border_skill_common.png', 'border_skill_basic.png'),
                     ('border_power_common.png', 'border_power_basic.png'),
                     ('banner_common.png', 'banner_basic.png'),
                     ('plaque_common.png', 'plaque_basic.png')):
        shutil.copyfile(os.path.join(fr, src), os.path.join(fr, dst))
        e = dict(idx[src])
        e['rarity'] = 'basic'
        e['note'] = f'copy of {src}: Basic rarity uses the common banner material in STS2'
        idx[dst] = e
        print(f'  frame/{dst}')
    with open(idx_p, 'w') as f:
        json.dump(idx, f, indent=1, sort_keys=True)


def do_starter():
    cards = {
        'StrikeIronclad': dict(id='StrikeIronclad', locKey='STRIKE_IRONCLAD', name='Strike', type='Attack', cost=1,
                               rarity='Basic', art='strike_ironclad', target='AnyEnemy',
                               description='Deal 6 damage.', vars={'Damage': 6}),
        'DefendIronclad': dict(id='DefendIronclad', locKey='DEFEND_IRONCLAD', name='Defend', type='Skill', cost=1,
                               rarity='Basic', art='defend_ironclad', target='Self',
                               description='Gain 5 [gold]Block[/gold].', vars={'Block': 5}),
        'Bash': dict(id='Bash', locKey='BASH', name='Bash', type='Attack', cost=2, rarity='Basic', art='bash',
                     target='AnyEnemy', description='Deal 8 damage.\nApply 2 [gold]Vulnerable[/gold].',
                     vars={'Damage': 8, 'Vulnerable': 2}),
    }
    deck = ['StrikeIronclad'] * 5 + ['DefendIronclad'] * 4 + ['Bash']
    os.makedirs(OUT, exist_ok=True)
    with open(os.path.join(OUT, 'starter.json'), 'w') as f:
        json.dump({'character': 'ironclad', 'cards': cards, 'deck': deck}, f, indent=1)
    print('  starter.json')


def main():
    g = Game()
    for fn in (do_ironclad, do_bg, do_ui):
        fn(g)
    do_basic_frames()
    do_starter()


if __name__ == '__main__':
    main()

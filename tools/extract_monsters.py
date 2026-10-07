#!/usr/bin/env python3
"""Export a few (cute) Slay the Spire 2 monsters as web PNGs for the between-floor encounter animation.

Same approach as tools/extract_combat.py: reads the game's PCK through the sts2-3ds port's tools (read-only
import) and renders each monster's Spine rig in its setup pose. Output: web/public/assets/monsters/.
"""
import os
import sys

PORT = '/Users/m/projects/sts2-3ds-work/sts2-3ds'
sys.path.insert(0, os.path.join(PORT, 'tools'))
from gdpck import Game  # noqa: E402
import spine_render  # noqa: E402

HERE = os.path.dirname(os.path.abspath(__file__))
OUT = os.path.join(os.path.dirname(HERE), 'web', 'public', 'assets', 'monsters')
MONSTERS = ['nibbit', 'leaf_slime_s', 'leaf_slime_m', 'toadpole', 'fuzzy_wurm_crawler', 'shrinker_beetle',
            'flying_mushrooms', 'byrdpip', 'bowlbug', 'myte', 'sneaky_gremlin', 'fat_gremlin', 'cultists', 'chomper']


def main():
    g = Game()
    os.makedirs(OUT, exist_ok=True)
    for m in MONSTERS:
        try:
            skel, atlas, load = g.spine(f'animations/monsters/{m}/{m}_skel_data.tres')
            img, _ = spine_render.render(skel, atlas, load, scale=0.5)
            img = img.convert('RGBA')
            img = img.crop(img.getbbox())
            img.save(os.path.join(OUT, f'{m}.png'))
            print(f'  {m}.png {img.size}')
        except Exception as e:  # some rigs need extra data; skip them
            print(f'  skip {m}: {e}')


if __name__ == '__main__':
    main()

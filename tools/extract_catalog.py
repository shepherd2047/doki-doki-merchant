#!/usr/bin/env python3
"""Merchant-shop catalog for the web version, taken from the ORIGINAL game:

  * names / description templates / merchant lines: the game's localization JSON in the PCK
    (read with sts2-3ds/tools/gdpck.py, used purely as a PCK reader);
  * type, rarity, cost, keywords and dynamic-var numbers: the decompiled C# models
    (sts2-decompiled/MegaCrit.Sts2.Core.Models.*), parsed with sts2-3ds/tools/balance_check.py's
    C# helpers (imported read-only);
  * pools: IroncladCardPool + ColorlessCardPool, IroncladPotionPool + SharedPotionPool,
    SharedRelicPool + IroncladRelicPool.

Description templates are SmartFormat; the common formatters are expanded here (out of combat,
no target), markup tags ([gold], [blue], ...) are kept, energy shows as [icon:energy].

  python3 tools/extract_catalog.py [--out web/public/data/catalog.json]
"""
import argparse
import json
import os
import re
import sys

HERE = os.path.dirname(os.path.abspath(__file__))
PROJECT = os.path.dirname(HERE)
WORK = '/Users/m/projects/sts2-3ds-work'
PORT_TOOLS = os.path.join(WORK, 'sts2-3ds', 'tools')
DEC = os.path.join(WORK, 'sts2-decompiled')
sys.path.insert(0, PORT_TOOLS)

from gdpck import Game  # noqa: E402
import balance_check as bc  # noqa: E402

MISSING = []  # (model, var) placeholders we could not fill


def slugify(name):
    """StringHelper.Slugify: ModelId.Entry of a class name (BodySlam -> BODY_SLAM)."""
    # C# regex "([A-Za-z0-9]|\G(?!^))([A-Z])" -> "$1_$2": an underscore before every capital that follows a char
    out = []
    i = 0
    while i < len(name):
        out.append(name[i])
        if i + 1 < len(name) and name[i + 1].isupper() and (name[i].isalnum()):
            out.append('_')
        i += 1
    s = ''.join(out).upper()
    s = re.sub(r'\s+', '_', s)
    return re.sub(r'[^A-Z0-9_]', '', s)


def pool(folder, cls, kind):
    text = open(os.path.join(DEC, 'MegaCrit.Sts2.Core.Models.' + folder, cls + '.cs'), encoding='utf-8').read()
    return re.findall(r'ModelDb\.%s<(\w+)>\(\)' % kind, text)


def fmt(v):
    if v is None:
        return None
    v = float(v)
    return str(int(v)) if v == int(v) else ('%g' % v)


# ------------------------------------------------------------------ SmartFormat subset

def split_top(s, sep='|'):
    out, d, start = [], 0, 0
    for k, ch in enumerate(s):
        if ch == '{':
            d += 1
        elif ch == '}':
            d -= 1
        elif ch == sep and d == 0:
            out.append(s[start:k])
            start = k + 1
    out.append(s[start:])
    return out


def energy_icons(n):
    if 0 < n < 4:
        return '[icon:energy]' * n
    return '%d[icon:energy]' % n


def expand(src, vars_, ctx):
    """vars_: name -> number (float) | str.  ctx: {'upgraded': bool, 'base': dict of unupgraded numbers, 'model': id}."""
    out = []
    i = 0
    while i < len(src):
        ch = src[i]
        if ch != '{':
            out.append(ch)
            i += 1
            continue
        d, j = 0, i
        while j < len(src):
            if src[j] == '{':
                d += 1
            elif src[j] == '}':
                d -= 1
                if d == 0:
                    break
            j += 1
        out.append(placeholder(src[i + 1:j], vars_, ctx))
        i = j + 1
    return ''.join(out)


def placeholder(body, vars_, ctx):
    colon = body.find(':')
    name = body if colon < 0 else body[:colon]
    rest = '' if colon < 0 else body[colon + 1:]
    val = vars_.get(name)
    num = val if isinstance(val, (int, float)) else None
    shown = fmt(num) if num is not None else (val if isinstance(val, str) else None)

    def sub_self(alt):
        alt = alt.replace('{}', shown if shown is not None else '?')
        alt = alt.replace('{:', '{' + name + ':')
        return expand(alt, vars_, ctx)

    if name == 'IfUpgraded' and rest.startswith('show:'):
        alts = split_top(rest[5:])
        return expand(alts[0] if ctx['upgraded'] else (alts[1] if len(alts) > 1 else ''), vars_, ctx)
    if name == 'energyPrefix' and rest.startswith('energyIcons'):
        m = re.search(r'energyIcons\((\d*)\)', rest)
        n = int(m.group(1)) if m and m.group(1) else 1
        return energy_icons(n)
    if name == 'singleStarIcon':
        return '[icon:star]'
    if rest.startswith('energyIcons'):
        return energy_icons(int(num)) if num is not None else miss(ctx, name)
    if rest.startswith('starIcons'):
        return (fmt(num) + '[icon:star]') if num is not None else miss(ctx, name)
    if rest.startswith('plural:'):
        alts = split_top(rest[7:])
        if num is None:
            return miss(ctx, name)
        pick = alts[0] if (num == 1 or len(alts) < 2) else alts[1]
        return sub_self(pick)
    if rest.startswith('choose('):
        close = rest.find(')')
        opts = split_top(rest[7:close])
        c2 = rest.find(':', close)
        alts = split_top(rest[c2 + 1:]) if c2 >= 0 else []
        cur = shown if shown is not None else ''
        for k, o in enumerate(opts):
            if (num is not None and o.lstrip('-').isdigit() and float(o) == num) or o == cur:
                return sub_self(alts[min(k, len(alts) - 1)]) if alts else ''
        return sub_self(alts[-1]) if len(alts) > len(opts) else ''
    if rest.startswith('cond:'):
        parts = split_top(rest[5:])
        v = num or 0
        tests = False
        for a in parts:
            m = re.match(r'(<=|>=|!=|==|<|>|=)(-?\d+)\?(.*)$', a, re.S)
            if not m:
                if not tests:
                    return sub_self(parts[0] if v != 0 else (parts[1] if len(parts) > 1 else ''))
                return sub_self(a)
            tests = True
            op, n, alt = m.group(1), int(m.group(2)), m.group(3)
            ok = {'<': v < n, '>': v > n, '<=': v <= n, '>=': v >= n, '!=': v != n}.get(op, v == n)
            if ok:
                return sub_self(alt)
        return ''
    if rest.startswith('abs'):
        return fmt(abs(num)) if num is not None else miss(ctx, name)
    if rest.startswith('percentMore'):
        return fmt(round((num - 1) * 100)) if num is not None else miss(ctx, name)
    if rest.startswith('percentLess'):
        return fmt(round((1 - num) * 100)) if num is not None else miss(ctx, name)
    if rest.startswith('diff') or rest.startswith('inverseDiff') or rest == '' or re.match(r'^[nN]\d*$', rest):
        if shown is None:
            return miss(ctx, name)
        base = ctx['base'].get(name)
        if rest.startswith('diff') and ctx['upgraded'] and num is not None and base is not None and base != num:
            return '[green]' + shown + '[/green]'
        return shown
    if '|' in rest:
        # a boolean flag ({InCombat:a|b}, {OnTable:..}, {IsTargeting:..}, {IsMultiplayer:..}): out of
        # combat, single player -> false unless we know the var
        alts = split_top(rest)
        truthy = bool(num) if num is not None else bool(val) if isinstance(val, str) else False
        return expand(alts[0] if truthy else (alts[1] if len(alts) > 1 else ''), vars_, ctx)
    if shown is not None:
        return shown
    return miss(ctx, name)


def miss(ctx, name):
    MISSING.append((ctx['model'], name))
    return '?'


STRVAR_TABLE = {'Enchantment': 'enchantments', 'Relic': 'relics', 'Card': 'cards', 'Potion': 'potions', 'Power': 'powers'}


def string_vars(text, loc):
    """StringVar("Name", ModelDb.<Kind><Cls>().Title...) -> that model's English title."""
    out = {}
    for m in re.finditer(r'new StringVar\("(\w+)",\s*ModelDb\.(\w+)<(\w+)>\(\)\.Title', text):
        table = loc.get(STRVAR_TABLE.get(m.group(2), ''), {})
        out[m.group(1)] = table.get(slugify(m.group(3)) + '.title', m.group(3))
    return out


def card_vars(vs):
    """CalculatedX vars show their CalculationBase out of combat (multiplier 0)."""
    out = {k: v for k, v in vs.items() if v is not None}
    base = vs.get('CalculationBase')
    if base is not None:
        for k in vs:
            if k.startswith('Calculated') and vs[k] is None:
                out[k] = base
        for k in ('CalculatedDamage', 'CalculatedBlock', 'CalculatedHits', 'CalculatedCards'):
            out.setdefault(k, base)
    return out


# ------------------------------------------------------------------ main

def main():
    ap = argparse.ArgumentParser()
    ap.add_argument('--out', default=os.path.join(PROJECT, 'web', 'public', 'data', 'catalog.json'))
    args = ap.parse_args()

    g = Game()
    files = set(g.pck.files)
    loc = {t: g.loc('eng', t) for t in ('cards', 'relics', 'potions', 'merchant_room', 'card_keywords', 'enchantments', 'powers')}
    kw = loc['card_keywords']
    period = kw.get('PERIOD', '.')

    def kw_text(k):
        return '[gold]' + kw.get(slugify(k) + '.title', k) + '[/gold]' + period

    # ---------------- cards
    cards = []
    for pool_cls, character, folder_title in (('IroncladCardPool', 'ironclad', 'ironclad'),
                                              ('ColorlessCardPool', 'colorless', 'colorless')):
        for cls in pool('CardPools', pool_cls, 'Card'):
            text = bc.class_text(bc.MODELS['card'], cls) or ''
            info = bc.csharp_card(cls)
            if not info or 'rarity' not in info:
                print('  !! cannot parse card', cls, file=sys.stderr)
                continue
            if info['rarity'] not in ('Common', 'Uncommon', 'Rare') or info['type'] not in ('Attack', 'Skill', 'Power'):
                continue
            if re.search(r'MultiplayerConstraint\s*=>\s*CardMultiplayerConstraint\.MultiplayerOnly', text):
                continue  # FilterForPlayerCount (single player)
            entry = slugify(cls)
            tmpl = loc['cards'].get(entry + '.description', '')
            title = loc['cards'].get(entry + '.title', cls)
            vbase, vup = card_vars(info['vars']), card_vars(info['upvars'])
            strs = {'TargetType': info.get('target', ''), 'CardType': info['type'], **string_vars(text, loc)}

            def describe(vs, upgraded, kws):
                ctx = {'upgraded': upgraded, 'base': vbase, 'model': cls}
                d = expand(tmpl, {**strs, **vs}, ctx)
                lines = [d] if d else []
                for k in ('Ethereal', 'Sly', 'Retain', 'Innate', 'Unplayable'):  # CardKeywordOrder.beforeDescription (Insert(0))
                    if k in kws:
                        lines.insert(0, kw_text(k))
                for k in ('Exhaust', 'Eternal'):
                    if k in kws:
                        lines.append(kw_text(k))
                return '\n'.join(lines)

            x = bool(info['x'])
            art = entry.lower()
            portrait = None
            for sub in ('', 'beta/'):
                p = f'images/packed/card_portraits/{folder_title}/{sub}{art}.png'
                if p + '.import' in files:
                    portrait = p
                    break
            cards.append({
                'id': cls, 'locKey': entry, 'art': art, 'artPath': portrait,
                'name': title, 'character': character, 'type': info['type'], 'rarity': info['rarity'],
                'cost': 'X' if x else info['cost'],
                'keywords': sorted(info['kw']),
                'description': describe(vbase, False, info['kw']),
                'vars': {k: v for k, v in vbase.items() if not isinstance(v, str)},
                'upgraded': {
                    'name': title + '+',
                    'description': describe(vup, True, info['upkw']),
                    'cost': 'X' if x else info['upcost'],
                },
            })

    # ---------------- potions
    potions = []
    seen = set()
    for cls in pool('PotionPools', 'IroncladPotionPool', 'Potion') + pool('PotionPools', 'SharedPotionPool', 'Potion'):
        if cls in seen:
            continue
        seen.add(cls)
        info = bc.csharp_simple('potion', cls)
        if not info:
            print('  !! cannot parse potion', cls, file=sys.stderr)
            continue
        if info.get('Rarity') not in ('Common', 'Uncommon', 'Rare'):
            continue
        entry = slugify(cls)
        vs = {k: v for k, v in info['vars'].items() if v is not None}
        vs.update(string_vars(bc.class_text(bc.MODELS['potion'], cls) or '', loc))
        d = expand(loc['potions'].get(entry + '.description', ''), vs, {'upgraded': False, 'base': vs, 'model': cls})
        art = entry.lower()
        potions.append({'id': cls, 'locKey': entry, 'art': art,
                        'artPath': f'images/atlases/potion_atlas.sprites/{art}.tres',
                        'name': loc['potions'].get(entry + '.title', cls), 'rarity': info['Rarity'],
                        'description': d})

    # ---------------- relics
    relics = []
    seen = set()
    for cls in pool('RelicPools', 'SharedRelicPool', 'Relic') + pool('RelicPools', 'IroncladRelicPool', 'Relic'):
        if cls in seen:
            continue
        seen.add(cls)
        info = bc.csharp_simple('relic', cls)
        if not info:
            print('  !! cannot parse relic', cls, file=sys.stderr)
            continue
        if info.get('Rarity') not in ('Common', 'Uncommon', 'Rare', 'Shop'):
            continue
        text = bc.class_text(bc.MODELS['relic'], cls) or ''
        allowed = not re.search(r'IsAllowedInShops\s*=>\s*false', text)
        entry = slugify(cls)
        vs = {k: v for k, v in info['vars'].items() if v is not None}
        vs.update(string_vars(text, loc))
        d = expand(loc['relics'].get(entry + '.description', ''), vs, {'upgraded': False, 'base': vs, 'model': cls})
        art = entry.lower()
        path = f'images/relics/{art}.png'
        if path + '.import' not in files and f'images/relics/beta/{art}.png.import' in files:
            path = f'images/relics/beta/{art}.png'
        relics.append({'id': cls, 'locKey': entry, 'art': art, 'artPath': path,
                       'name': loc['relics'].get(entry + '.title', cls), 'rarity': info['Rarity'],
                       'description': d, 'allowedInShops': allowed})

    # ---------------- merchant lines
    lines = {}
    for k, v in loc['merchant_room'].items():
        m = re.match(r'MERCHANT\.talk\.(\w+)\.line(\d+)$', k)
        if m:
            lines.setdefault(m.group(1), []).append((int(m.group(2)), v))
    merchant = {k: [t for _, t in sorted(v)] for k, v in sorted(lines.items())}

    out = {'cards': cards, 'potions': potions, 'relics': relics, 'merchantLines': merchant}
    os.makedirs(os.path.dirname(args.out), exist_ok=True)
    with open(args.out, 'w', encoding='utf-8') as f:
        json.dump(out, f, ensure_ascii=False, indent=1)

    # ---------------- report
    from collections import Counter
    print('wrote', args.out)
    for ch in ('ironclad', 'colorless'):
        cs = [c for c in cards if c['character'] == ch]
        print(f'{ch}: {len(cs)} cards', dict(Counter((c["rarity"], c["type"]) for c in cs)))
    print('potions:', len(potions), dict(Counter(p['rarity'] for p in potions)))
    print('relics:', len(relics), dict(Counter(r['rarity'] for r in relics)),
          'not allowed in shops:', [r['id'] for r in relics if not r['allowedInShops']])
    print('merchant line groups:', {k: len(v) for k, v in merchant.items()})
    nolocs = [c['id'] for c in cards + potions + relics if not c['description']]
    if nolocs:
        print('empty descriptions:', nolocs)
    noart = [c['id'] for c in cards if not c['artPath']]
    if noart:
        print('cards without packed portrait:', noart)
    if MISSING:
        print('unfilled placeholders (%d):' % len(MISSING), sorted(set(MISSING)))


if __name__ == '__main__':
    main()

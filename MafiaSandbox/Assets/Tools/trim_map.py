#!/usr/bin/env python3
"""Trim city.json to the reduced map: drops the cafe, pharmacy, pawn shop, jeweler, barber, bank, the SW warehouses and the SE scrapyard.
Usage: python3 trim_map.py <city.json>   (run once on the original city.json; the branch's git history keeps the untrimmed version)"""
import json, sys
path = sys.argv[1]; d = json.load(open(path))
REMOVE = {'cafe', 'pharmacy', 'pawn', 'jeweler', 'barber', 'bank'}
rects = []   # (xmin, zmin, xmax, zmax) areas whose props / colliders go
shell_names = set()
for b in d['buildings']:
    if b['kind'] in REMOVE:
        rects.append((b['min'][0] - 0.3, b['min'][1] - 0.3, b['max'][0] + 0.3, b['max'][1] + 0.3)); shell_names.add(b['name'])
rects.append((-46, -132, -4, -104))   # SW warehouses
rects.append((57, -132, 80, -104))    # SE scrapyard
def inside(x, z): return any(r[0] <= x <= r[2] and r[1] <= z <= r[3] for r in rects)
props0, cols0, b0 = len(d['props']), len(d['colliders']), len(d['buildings'])
d['props'] = [p for p in d['props'] if not (p['name'] in shell_names or (inside(p['pos'][0], p['pos'][2]) and not p['name'].startswith(('Sokak_', 'Trafik_', 'Tabela_', 'Posta_', 'Yangin_', 'Saksi', 'Agac_', 'Cop_', 'ATM', 'Bisiklet', 'Otobus'))))]
d['colliders'] = [c for c in d['colliders'] if not ((c.get('building') or True) and inside((c['min'][0] + c['max'][0]) / 2, (c['min'][2] + c['max'][2]) / 2) and (c['max'][0] - c['min'][0] > 0.9 or c['max'][2] - c['min'][2] > 0.9 or c['max'][1] > 2.0))]
d['buildings'] = [b for b in d['buildings'] if b['kind'] not in REMOVE]
d['locations'] = [l for l in d['locations'] if not l['name'].startswith(('Bank', 'Warehouses', 'Scrapyard'))]
json.dump(d, open(path, 'w'), separators=(',', ':'))
print('props', props0, '->', len(d['props']), '| colliders', cols0, '->', len(d['colliders']), '| buildings', b0, '->', len(d['buildings']))

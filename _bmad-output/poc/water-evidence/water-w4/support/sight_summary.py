import json, sys
for p in sys.argv[1:]:
    d = json.load(open(p))
    print(p, d.get('generated'))
    for group in ('runs', 'robustness'):
        for r in d.get(group, []):
            keys = {k: r[k] for k in r if not isinstance(r[k], (list, dict))}
            print(' ', group, {k: (round(v, 4) if isinstance(v, float) else v) for k, v in keys.items()})

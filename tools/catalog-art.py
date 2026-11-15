"""Read original game artwork without launching or modifying the game."""
import sys, json
from pathlib import Path
root = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(root / '.tools' / 'unitypy'))
import UnityPy
game = Path(r'A:\Steam\steamapps\common\rat game\Rat Game_Data')
out = root / 'analysis' / 'art'
out.mkdir(parents=True, exist_ok=True)
records = []
for path in game.glob('*.assets'):
    env = UnityPy.load(str(path))
    for obj in env.objects:
        if obj.type.name not in ('Texture2D', 'Sprite'):
            continue
        data = obj.read()
        name = data.m_Name
        entry = dict(file=path.name, id=obj.path_id, type=obj.type.name, name=name,
                     width=getattr(data, 'm_Width', None), height=getattr(data, 'm_Height', None))
        records.append(entry)
        if any(word in name.lower() for word in ('chef', 'rat', 'robot', 'coop', 'versus', 'logo')):
            print(json.dumps(entry), flush=True)
            try:
                data.image.save(out / (path.stem + '-' + str(obj.path_id) + '.png'))
            except Exception as exc:
                print(str(exc), flush=True)
    del env
(out / 'catalog.json').write_text(json.dumps(records, indent=2), encoding='utf-8')
print('TOTAL', len(records))

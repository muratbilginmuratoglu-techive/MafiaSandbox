# Tools

- `build_props.py` — Blender 4/5 `bpy` script that generates all low-poly props, vehicles and enterable building shells
  (`python -c "import bpy"` builds work with the `bpy` pip wheel too). Output: `out/props/*.glb` + `props.json` (footprints, building meta).
- `build_crowd.py` — generates the 32 rigged characters with 38 animation clips (`out/crowd/*.glb`).
- `bundle_props.py` — bundles the GLBs into `props.js` for the HTML prototype.
- `export_city_from_html.py` — Playwright snippet used against the HTML prototype (`Reference/html_game_index.html`) to dump
  `CityStatic.glb` (ground/roads/bridges via three.js GLTFExporter) and `city.json` (prop placements, colliders, buildings, graphs).
  The Unity `city.json` is a JsonUtility-friendly transform of that dump.

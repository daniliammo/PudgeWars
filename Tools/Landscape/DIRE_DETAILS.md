# Ordinary Dire ground details

Source: the installed Dota 2 `game/dota/pak01_dir.vpk`. Explicit allowlist in
`build_dire_details.py`: ordinary `badside_debris_rocks`, `badside_roots_path`,
`branches` and `badside_bones` resources. No cosmetic, winter, holiday, desert,
or other terrain-pack resources. This is an ordinary-resource selection, not
an assertion that every prop is placed on the current default Dota map.

Extract with Source2Viewer-CLI (substitute the installed game paths):

```bash
Source2Viewer-CLI -i "$DOTA/pak01_dir.vpk" --game "$DOTA/gameinfo.gi" \
  -f 'models/props_nature/badside_roots_path,models/props_nature/branches,models/props_rock/badside_debris_rocks,models/props_bones/badside_bones' \
  -d --gltf_export_format glb --gltf_export_materials --threads 8 \
  -o /tmp/dota-dire-details
~/.local/bin/blender -b --factory-startup -P Tools/Landscape/build_dire_details.py -- \
  /tmp/dota-dire-details Assets/Art/Landscape/DireDetails
```

The converter reuses tree scale/axis conversion and copies original colour
textures without winter tinting. The Unity builder prepares materials, meshes
and instanced detail prefabs before touching TerrainData. Details are visual;
they have no colliders and do not obstruct heroes or hooks.

Apply using **Pudge Wars → Build Landscape** in the open arena and
**Pudge Wars → Build Main Menu** for menu scenery. Existing scene/terrain assets
are intentionally left for the editor to rebuild. Graphics presets control
Terrain detail density/distance as for Radiant grass.

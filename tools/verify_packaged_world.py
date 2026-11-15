"""Verify the packaged Ember Knights .apworld loads through Archipelago's real world loader.

Run with the Archipelago source root as the working directory, with the archive installed in
custom_worlds. Fails if the world did not register or its data cannot be read from the zip.
"""

import sys
from pathlib import Path

sys.path.insert(0, str(Path.cwd()))

import worlds  # noqa: E402
from worlds.AutoWorld import AutoWorldRegister  # noqa: E402

if "Ember Knights" not in AutoWorldRegister.world_types:
    failed = getattr(worlds, "failed_world_loads", {})
    raise SystemExit(f"Ember Knights did not load. Failed loads: {failed}")

world = AutoWorldRegister.world_types["Ember Knights"]
source = Path(sys.modules[world.__module__].__file__ or "")
if ".apworld" not in str(source):
    raise SystemExit(f"Ember Knights loaded from {source}, not from the packaged .apworld archive.")

# The data package lists every name the world can create. That is the base game (228 locations, 85 items) plus the DLC\n# (67 locations, 22 items), whether or not a player turns the DLC option on.\nif len(world.location_name_to_id) != 295 or len(world.item_name_to_id) != 107:
    raise SystemExit(
        f"Unexpected data: {len(world.location_name_to_id)} locations, {len(world.item_name_to_id)} items."
    )

print(f"OK: loaded from {source} with {len(world.location_name_to_id)} locations "
      f"and {len(world.item_name_to_id)} items; manifest={world.manifest}")
